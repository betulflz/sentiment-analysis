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
        if (_disposed)
            throw new ObjectDisposedException(nameof(MicrophoneCaptureSource));

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "Canlı mikrofon yakalama şu an yalnızca macOS'ta destekleniyor. " +
                "Windows (WASAPI) implementasyonu sonraki turda; dosya analizi her platformda çalışır.");

        // Yeniden giriş koruması: aynı örnek üzerinde bir kuyruk zaten canlıyken ikinci bir
        // CaptureAsync çağrısı, ilk kuyruğun tek GC kökü olan handle'ı çalar (aşağıdaki
        // realloc) → native taraf, artık kimsenin pinlemediği bir delege üzerinden çağrılır
        // ve çöker. Bunu en baştan reddet.
        if (_queue != IntPtr.Zero)
            throw new InvalidOperationException(
                "Bu kaynak üzerinde zaten bir yakalama sürüyor; önce mevcut akışı durdurun.");

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
            try
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
            }
            catch (Exception ex)
            {
                // Reverse P/Invoke sınırından yönetilmeyen bir istisna kaçarsa süreç çöker
                // (native AudioQueue çağrı yığını, .NET exception'ını tanımaz). Bunun yerine
                // istisnayı kanala taşıyıp tüketiciye dürüstçe fırlatıyoruz. TryComplete zaten
                // tamamlanmış bir kanalda güvenle false döner, tekrar fırlatmaz (doğrulandı).
                chunks.Writer.TryComplete(ex);
            }
            finally
            {
                // Tamponu HER durumda kuyruğa geri ver (hata olsa bile) — aksi halde
                // AudioQueue'nun elindeki tampon sayısı tükenir ve akış sessizce durur.
                // Durdurulmuş bir kuyruğa karşı hata dönebilir, önemsizdir.
                AudioQueueInterop.AudioQueueEnqueueBuffer(queue, bufferPtr, 0, IntPtr.Zero);
            }
        };

        // Önceki bir yakalamadan kalan pinlenmiş handle varsa serbest bırak (kaynak sızıntısı
        // olmasın). Yukarıdaki _queue kontrolü sayesinde burada asla CANLI bir kuyruğun
        // handle'ını çalmıyoruz.
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _callbackHandle = GCHandle.Alloc(_callback);

        try
        {
            // Start() try içinde: AudioQueueNewInput kuyruğu oluşturduktan SONRA
            // AudioQueueStart (veya tampon ayırma) başarısız olursa, oluşturulmuş kuyruk
            // finally'deki StopQueue() ile temizlenir — aksi halde yalnızca açık bir
            // Dispose() çağrısı kurtarabilirdi.
            Start();

            // "Durdur" (ct iptali) artık normal bir bitiştir, hata değil — kullanıcı konuştuğu
            // sürenin analiz EDİLMESİNİ ister (bkz. görev 5 düzeltme turu 2). Bunu sağlamak için
            // manuel enumerator kullanıyoruz: yield NOT olamayacağı için (CS1626: catch içeren
            // try'da yield yasak) MoveNextAsync'i AYRI bir try/catch içinde çağırıp chunk'ı o
            // try'ın DIŞINDA yield ediyoruz.
            var enumerator = chunks.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    AudioChunk? current = null;
                    bool hasCurrent = false, cancelled = false;
                    try
                    {
                        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            current = enumerator.Current;
                            hasCurrent = true;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // yield burada OLAMAZ (CS1626: catch içinde yasak) — yalnızca bayrak
                        // bırakıyoruz, asıl "kalanı ver" işi aşağıda, catch DIŞINDA yapılır.
                        cancelled = true;
                    }

                    if (cancelled)
                    {
                        // Durdur: kanalda zaten kuyruğa alınmış ama henüz verilmemiş chunk'lar
                        // varsa onları da kaybetmeden ver — yalnızca ct nedeniyle iptal edilen
                        // bir okuma, üretilmiş veriyi çöpe atmamalı.
                        while (chunks.Reader.TryRead(out var pending))
                            yield return pending;
                        break;
                    }

                    if (!hasCurrent) break; // kanal normal tamamlandı (iptal yok)

                    yield return current!;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // NOT: yield return burada OLAMAZ (finally içinde yield C#'ta derleme hatasıdır,
            // CS1625). Kuyruk durdurma/temizleme burada; kalan artığın verilmesi try/finally
            // dışında yapılır — hem normal bitişte hem de iptalden sonra (artık ikisi de aynı
            // yoldan geçer, çünkü iptal artık yukarıda yutulur).
            StopQueue();
        }

        // Buraya HEM normal bitişte (kaynak tükendi) HEM DE iptalden sonra ulaşılır — tamponda
        // henüz tam bir chunk'a ulaşmamış ama gerçek konuşma içeren bir artık varsa (ör. 10
        // saniyelik chunk uzunluğunda 6 saniyede "Durdur"a basılması) bunu kaybetmeden ver.
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
            // Sessizce yutulan bir hata burada özellikle tehlikeli: sıfır tampon kuyruğa
            // girerse hiç callback tetiklenmez, SilenceWatchdog bunu "izin reddi" sanıp
            // kullanıcıya yanlış güvenle mikrofon izni mesajı gösterir. Durumu adıyla fırlat.
            int allocStatus = AudioQueueInterop.AudioQueueAllocateBuffer(_queue, BufferBytes, out var b);
            if (allocStatus != 0)
                throw new InvalidOperationException(
                    $"Mikrofon tamponu ayrılamadı (AudioQueueAllocateBuffer → {allocStatus}).");

            int enqueueStatus = AudioQueueInterop.AudioQueueEnqueueBuffer(_queue, b, 0, IntPtr.Zero);
            if (enqueueStatus != 0)
                throw new InvalidOperationException(
                    $"Mikrofon tamponu kuyruğa eklenemedi (AudioQueueEnqueueBuffer → {enqueueStatus}).");
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
