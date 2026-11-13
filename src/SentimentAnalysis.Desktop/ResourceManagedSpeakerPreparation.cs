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

        using var lease = await queue.AcquireAsync(ct).ConfigureAwait(false);
        await using var session = createSession();
        return await Task.Run(() => session.Service.RunAsync(audio, expectedSpeakers, truncated, lease.Token), lease.Token)
            .ConfigureAwait(false);
    }
}
