using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>Mikrofon izni verilmemiş görünüyorsa fırlatılır. Sessiz reddi dürüstçe yüzeye çıkarır.</summary>
public sealed class MicrophonePermissionException : Exception
{
    public MicrophonePermissionException(string message) : base(message) { }
}

/// <summary>
/// macOS mikrofonundan canlı 16 kHz mono <see cref="AudioChunk"/> akışı üretir
/// (AudioToolbox / AudioQueue). Native tipler yalnızca burada ve
/// <see cref="AudioQueueInterop"/>'ta; chunk'lama, dönüştürme ve sessizlik tespiti saf
/// yardımcılarda ve ayrı ayrı test edilir.
/// </summary>
/// <remarks>
/// Spike ölçümü (2026-07-27): AudioQueue 16 kHz mono PCM16'yı DOĞRUDAN üretir — cihaz
/// 48 kHz olsa bile dönüşümü kendisi yapar, bu yüzden burada yeniden örnekleme yoktur.
/// Callback delegate'i <see cref="GCHandle"/> ile pinlenir; aksi halde GC onu toplar ve
/// native taraf çöker.
/// </remarks>
public sealed class MicrophoneCaptureSource : IAudioCaptureSource, IDisposable
{
    private const int TargetRate = 16000;
    private const uint BufferBytes = 8192;
    private const int BufferCount = 3;

    /// <summary>Ölü-akış kararı için gözlem penceresi. Bu süre boyunca dijital sessizlik
    /// gelirse izin reddi varsayılır.</summary>
    private static readonly TimeSpan DeadStreamWindow = TimeSpan.FromSeconds(3);

    private IntPtr _queue;
    private GCHandle _callbackHandle;
    private AudioQueueInterop.InputCallback? _callback;
    private bool _disposed;

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "Canlı mikrofon yakalama şu an yalnızca macOS'ta destekleniyor. " +
                "Windows (WASAPI) implementasyonu sonraki turda; dosya analizi her platformda çalışır.");

        var chunks = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var buffer = new StreamingChunkBuffer(TargetRate, options.ChunkLength, options.Overlap);
        var watchdog = new SilenceWatchdog(TargetRate, DeadStreamWindow);
        bool deadStreamReported = false;

        _callback = (userData, queue, bufferPtr, startTime, numPackets, packetDescs) =>
        {
            var native = Marshal.PtrToStructure<AudioQueueInterop.AudioQueueBuffer>(bufferPtr);
            int byteCount = (int)native.mAudioDataByteSize;

            if (byteCount > 0)
            {
                var raw = new byte[byteCount];
                Marshal.Copy(native.mAudioData, raw, 0, byteCount);

                var samples = new float[byteCount / 2];
                Pcm16Converter.ToFloat(raw, samples);

                watchdog.Observe(samples);
                if (watchdog.LooksDead && !deadStreamReported)
                {
                    deadStreamReported = true;
                    chunks.Writer.TryComplete(new MicrophonePermissionException(
                        "Mikrofondan yalnızca sessizlik geliyor. Büyük olasılıkla mikrofon izni " +
                        "verilmemiş: Sistem Ayarları → Gizlilik ve Güvenlik → Mikrofon bölümünden " +
                        "bu uygulamaya izin verin ve tekrar deneyin."));
                    return;
                }

                foreach (var chunk in buffer.Append(samples))
                    chunks.Writer.TryWrite(chunk);
            }

            // Tamponu kuyruğa geri ver (durdurulmuşsa hata döner, önemsizdir).
            AudioQueueInterop.AudioQueueEnqueueBuffer(queue, bufferPtr, 0, IntPtr.Zero);
        };

        // Önceki bir yakalamadan kalan pinlenmiş handle varsa serbest bırak (kaynak sızıntısı olmasın).
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _callbackHandle = GCHandle.Alloc(_callback);

        Start();

        try
        {
            await foreach (var chunk in chunks.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return chunk;
        }
        finally
        {
            // NOT: yield return burada OLAMAZ (finally içinde yield C#'ta derleme hatasıdır,
            // CS1625). Kuyruk durdurma/temizleme burada; kalan artığın verilmesi try/finally
            // dışında, yalnızca akış iptal olmadan normal bittiğinde yapılır.
            StopQueue();
        }

        // İptal edilmişse (ct tetiklendiyse) yukarıdaki await foreach zaten
        // OperationCanceledException fırlatıp buraya hiç ulaşmadan metottan çıkar —
        // yani iptalden sonra artık chunk asla verilmez.
        var tail = buffer.Flush();
        if (tail is not null)
            yield return tail;
    }

    private void Start()
    {
        var format = new AudioQueueInterop.AudioStreamBasicDescription
        {
            mSampleRate = TargetRate,
            mFormatID = AudioQueueInterop.FormatLinearPcm,
            mFormatFlags = AudioQueueInterop.FlagsSignedIntegerPacked,
            mBytesPerPacket = 2,
            mFramesPerPacket = 1,
            mBytesPerFrame = 2,
            mChannelsPerFrame = 1,
            mBitsPerChannel = 16,
            mReserved = 0,
        };

        int status = AudioQueueInterop.AudioQueueNewInput(
            ref format, _callback!, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out _queue);
        if (status != 0)
            throw new InvalidOperationException(
                $"Mikrofon kuyruğu oluşturulamadı (AudioQueueNewInput → {status}). " +
                "Ses giriş cihazı bulunamamış olabilir.");

        for (int i = 0; i < BufferCount; i++)
        {
            AudioQueueInterop.AudioQueueAllocateBuffer(_queue, BufferBytes, out var b);
            AudioQueueInterop.AudioQueueEnqueueBuffer(_queue, b, 0, IntPtr.Zero);
        }

        status = AudioQueueInterop.AudioQueueStart(_queue, IntPtr.Zero);
        if (status != 0)
            throw new InvalidOperationException($"Mikrofon başlatılamadı (AudioQueueStart → {status}).");
    }

    private void StopQueue()
    {
        if (_queue == IntPtr.Zero) return;
        AudioQueueInterop.AudioQueueStop(_queue, true);
        AudioQueueInterop.AudioQueueDispose(_queue, true);
        _queue = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopQueue();
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _callback = null;
    }
}
