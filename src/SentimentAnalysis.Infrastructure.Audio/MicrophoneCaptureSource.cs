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

    private readonly IDiagnosticLog _log;

    /// <param name="log">Geliştirici teşhis kanalı; verilmezse sessizdir (üretim varsayılanı).
    /// <b>Gerçek zamanlı ses geri çağrımından ASLA çağrılmaz</b> — orada metin biçimleme ve I/O
    /// ses kopmasına yol açar. Callback yalnızca kilitsiz sayaç artırır; özet, akış durduktan
    /// sonra (gerçek zamanlı yolun dışında) yazılır.</param>
    public MicrophoneCaptureSource(IDiagnosticLog? log = null) => _log = log ?? NullDiagnosticLog.Instance;

    private void Diagnose(Func<string> message)
    {
        if (!_log.IsEnabled) return;
        try { _log.Write(message()); } catch { /* teşhis, yakalamayı bozamaz */ }
    }

    private PortAudioSharp.Stream? _paStream;
    private PortAudioSharp.Stream.Callback? _paCallback;

    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        CaptureOptions options, [EnumeratorCancellation] CancellationToken ct)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MicrophoneCaptureSource));

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            if (_paStream != null || _queue != IntPtr.Zero)
                throw new InvalidOperationException(
                    "Bu kaynak üzerinde zaten bir yakalama sürüyor; önce mevcut akışı durdurun.");

            var paChunks = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });

            var paBuffer = new StreamingChunkBuffer(TargetRate, options.ChunkLength, options.Overlap);
            var paWatchdog = new SilenceWatchdog(TargetRate, DeadStreamWindow);
            bool paDeadStreamReported = false;
            long totalSamplesReceived = 0;
            int chunksProduced = 0;

            _paCallback = (IntPtr input, IntPtr output, uint frameCount,
                ref PortAudioSharp.StreamCallbackTimeInfo timeInfo,
                PortAudioSharp.StreamCallbackFlags statusFlags,
                IntPtr userDataPtr) =>
            {
                try
                {
                    if (input != IntPtr.Zero && frameCount > 0)
                    {
                        var samples = new float[frameCount];
                        Marshal.Copy(input, samples, 0, (int)frameCount);
                        System.Threading.Interlocked.Add(ref totalSamplesReceived, frameCount);

                        paWatchdog.Observe(samples);
                        if (paWatchdog.LooksDead && !paDeadStreamReported)
                        {
                            paDeadStreamReported = true;
                            paChunks.Writer.TryComplete(new MicrophonePermissionException(
                                "Mikrofondan yalnızca sessizlik geliyor. Büyük olasılıkla mikrofon izni " +
                                "verilmemiş. Sistem ayarlarından mikrofon iznini kontrol edin ve tekrar deneyin."));
                            return PortAudioSharp.StreamCallbackResult.Complete;
                        }

                        // GERÇEK ZAMANLI YOL: burada yalnızca kilitsiz sayaç ve kanala yazma
                        // yapılır. Metin biçimleme/I/O (günlük) bu geri çağrımı geciktirir ve
                        // ses kopmasına yol açar; özet, akış durduktan sonra yazılır.
                        foreach (var chunk in paBuffer.Append(samples))
                        {
                            System.Threading.Interlocked.Increment(ref chunksProduced);
                            paChunks.Writer.TryWrite(chunk);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Hata zaten kanal üzerinden tüketiciye taşınıyor; burada günlük yazmak
                    // (gerçek zamanlı yol) bilgi eklemez, risk ekler.
                    paChunks.Writer.TryComplete(ex);
                    return PortAudioSharp.StreamCallbackResult.Abort;
                }
                return PortAudioSharp.StreamCallbackResult.Continue;
            };

            try
            {
                PortAudioSharp.PortAudio.Initialize();
                try
                {
                    var inputParameters = new PortAudioSharp.StreamParameters();
                    inputParameters.device = PortAudioSharp.PortAudio.DefaultInputDevice;
                    if (inputParameters.device == -1)
                    {
                        throw new InvalidOperationException("Varsayılan ses giriş cihazı (mikrofon) bulunamadı.");
                    }

                    inputParameters.channelCount = 1;
                    inputParameters.sampleFormat = PortAudioSharp.SampleFormat.Float32;

                    var deviceInfo = PortAudioSharp.PortAudio.GetDeviceInfo(inputParameters.device);
                    inputParameters.suggestedLatency = deviceInfo.defaultLowInputLatency;
                    inputParameters.hostApiSpecificStreamInfo = IntPtr.Zero;

                    _paStream = new PortAudioSharp.Stream(
                        inputParameters,
                        null,
                        TargetRate,
                        0,
                        PortAudioSharp.StreamFlags.NoFlag,
                        _paCallback,
                        null
                    );

                    _paStream.Start();
                    var startedDevice = inputParameters.device;
                    var startedName = deviceInfo.name;
                    Diagnose(() => $"[MIC] PortAudio başlatıldı. Cihaz ID: {startedDevice}, Ad: {startedName}");
                }
                catch
                {
                    // Hata çağırana olduğu gibi yükseliyor; ayrıca günlüğe yazmak gerekmez.
                    StopPaStream();
                    PortAudioSharp.PortAudio.Terminate();
                    throw;
                }

                var paEnumerator = paChunks.Reader.ReadAllAsync(ct).GetAsyncEnumerator(ct);
                try
                {
                    while (true)
                    {
                        AudioChunk? paCurrent = null;
                        bool paHasCurrent = false, paCancelled = false;
                        try
                        {
                            if (await paEnumerator.MoveNextAsync().ConfigureAwait(false))
                            {
                                paCurrent = paEnumerator.Current;
                                paHasCurrent = true;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            paCancelled = true;
                        }

                        if (paCancelled)
                        {
                            Diagnose(() => "[MIC] Okuma döngüsü iptal edildi. Kalan chunk'lar drene ediliyor.");
                            while (paChunks.Reader.TryRead(out var pending))
                                yield return pending;
                            break;
                        }

                        if (!paHasCurrent) break;

                        yield return paCurrent!;
                    }
                }
                finally
                {
                    await paEnumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                StopPaStream();
                try { PortAudioSharp.PortAudio.Terminate(); } catch { }
                // Gerçek zamanlı yol bitti: sayaç özetini yazmak artık güvenli.
                long receivedSamples = System.Threading.Interlocked.Read(ref totalSamplesReceived);
                int producedChunks = System.Threading.Volatile.Read(ref chunksProduced);
                Diagnose(() => $"[MIC] PortAudio sonlandı. Alınan örnek: {receivedSamples}, Üretilen chunk: {producedChunks}");
            }

            var paTail = paBuffer.Flush();
            if (paTail is not null && paTail.Samples.Length >= 8000)
            {
                Diagnose(() => $"[MIC] Tail chunk üretildi. Start: {paTail.Start.TotalSeconds}s, End: {paTail.End.TotalSeconds}s, Samples: {paTail.Samples.Length}");
                yield return paTail;
            }

            yield break;
        }

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException(
                "Canlı mikrofon yakalama şu an yalnızca macOS'ta destekleniyor. " +
                "Windows ve Linux için PortAudio desteği bu sınıfa dahil edilmiştir.");

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
        if (tail is not null && tail.Samples.Length >= 8000)
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

    private void StopPaStream()
    {
        if (_paStream != null)
        {
            try { _paStream.Stop(); } catch { }
            try { _paStream.Close(); } catch { }
            try { _paStream.Dispose(); } catch { }
            _paStream = null;
        }
        _paCallback = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopQueue();
        StopPaStream();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            try { PortAudioSharp.PortAudio.Terminate(); } catch { }
        }
        if (_callbackHandle.IsAllocated) _callbackHandle.Free();
        _callback = null;
    }
}
