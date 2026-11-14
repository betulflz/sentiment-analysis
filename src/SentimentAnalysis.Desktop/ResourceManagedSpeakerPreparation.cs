using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Application.Runtime;

namespace SentimentAnalysis.Desktop;

/// <summary>Dosya modunda analizden önceki konuşmacı ayrımı (karşı tarafa odak).</summary>
public interface ISpeakerPreparation
{
    Task<SpeakerPreparationResult> PrepareAsync(string path, int expectedSpeakers, CancellationToken ct);
}

/// <summary>Dosyayı bellekte çözer, ardından konuşmacı ayrımını ortak model kuyruğunda kendi model oturumuyla çalıştırır;
/// oturum başarı/hata/iptalde kapanır (analiz modelleriyle aynı anda yüklenmez).</summary>
public sealed class ResourceManagedSpeakerPreparation(
    IAudioFileDecoder decoder, ModelWorkQueue queue,
    Func<OwnedModelSession<SpeakerPreparation>> createSession, AppConfig config) : ISpeakerPreparation
{
    public async Task<SpeakerPreparationResult> PrepareAsync(string path, int expectedSpeakers, CancellationToken ct)
    {
        var max = TimeSpan.FromMinutes(config.Diarization.MaxRecordedMinutes);
        float[] audio;
        try
        {
            audio = await decoder.DecodeMono16kAsync(path, max, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return SpeakerPreparationResult.Failed(Array.Empty<float>(), false); }
        bool truncated = audio.Length >= (long)(max.TotalSeconds * 16000) - 16000 / 10;
        if (audio.Length == 0) return SpeakerPreparationResult.Failed(audio, false);

        var lease = await queue.AcquireAsync(ct).ConfigureAwait(false);
        OwnedModelSession<SpeakerPreparation> session;
        try { session = createSession(); }
        catch { lease.Dispose(); throw; }

        // sherpa'nın Process çağrısı senkron ve iptali dinlemez: Durdur'da çağıran hemen döner (UI dakikalarca
        // beklemez); model oturumu ve kuyruk yeri yerel iş GERÇEKTEN bitince bırakılır (iş sürerken model kapatılmaz).
        // Normal yolda oturum dönmeden ÖNCE kapanır — analiz modelleri ayrım modeliyle aynı anda bellekte olmasın.
        var work = Task.Run(() => session.Service.RunAsync(audio, expectedSpeakers, truncated, lease.Token));
        bool detached = false;
        try
        {
            await work.WaitAsync(lease.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!work.IsCompleted)
        {
            detached = true;
            _ = work.ContinueWith(_ => ReleaseAsync(session, lease), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            throw;
        }
        finally
        {
            if (!detached) await ReleaseAsync(session, lease).ConfigureAwait(false);
        }
        return await work.ConfigureAwait(false);
    }

    private static async Task ReleaseAsync(OwnedModelSession<SpeakerPreparation> session, ModelWorkQueue.Lease lease)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); }
        finally { lease.Dispose(); }
    }
}
