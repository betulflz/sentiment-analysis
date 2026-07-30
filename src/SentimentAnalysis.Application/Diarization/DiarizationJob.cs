using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Diarization;

/// <summary>Arka plan konuşmacı geçişinin sonucu. Kullanıcıya dürüstçe bildirilir —
/// sessiz başarısızlık yoktur (master §12.2).</summary>
public enum DiarizationOutcome
{
    /// <summary>Geçiş tamamlandı (konuşmacı bulunamamış olsa bile — o da bir sonuçtur).</summary>
    Completed,

    /// <summary>Oturumun saklanmış sesi yok; diarization çalıştırılamaz. Tipik nedeni:
    /// oturum, konuşmacı ayrımı kapalıyken kaydedildi.</summary>
    NoAudio,

    /// <summary>Model eksik veya motor hata verdi. Rapor konuşmacısız gösterilir.</summary>
    Failed,
}

/// <summary>Oturum bittikten SONRA bir kez çalışan arka plan geçişi: saklanmış sesi diarize
/// eder ve konuşmacı bölgelerini saklar. Canlı hatta çağrılmaz (spec 10 S2: sherpa'nın
/// diarization'ı offline'dır ve 10a'da RTF ≈ 0,44 ölçülmüştür).
/// <para><b>Uyarı — catch tek başına yetmez:</b> sherpa-onnx, model dosyası eksikse istisna
/// fırlatmaz, SÜRECİ SONLANDIRIR (10b Task 6'da ölçüldü). Aşağıdaki <c>catch</c> yalnızca
/// yönetilen hataları yakalar; asıl koruma <c>SherpaDiarizer</c>'ın motora girmeden önce
/// dosya varlığını doğrulamasıdır. O doğrulama kaldırılırsa burası kullanıcıyı koruyamaz.</para></summary>
public sealed class DiarizationJob
{
    private readonly IDiarizer _diarizer;
    private readonly IDiarizationStore _store;
    private readonly AppConfig _config;

    public DiarizationJob(IDiarizer diarizer, IDiarizationStore store, AppConfig config)
    {
        _diarizer = diarizer;
        _store = store;
        _config = config;
    }

    public async Task<DiarizationOutcome> RunAsync(Guid sessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var audio = await _store.GetSessionAudioAsync(sessionId, ct).ConfigureAwait(false);
        if (audio is null || audio.Length == 0) return DiarizationOutcome.NoAudio;

        IReadOnlyList<SpeakerSegment> speakers;
        try
        {
            speakers = await _diarizer.DiarizeAsync(audio, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // iptal bir hata değil, koşunun durumudur — Failed'a çevrilmez
        }
        catch
        {
            // Model eksik / motor hatası: kullanıcıya dürüstçe "başarısız" denir ve rapor
            // konuşmacısız gösterilir. Yarım veya uydurma sonuç YAZILMAZ.
            return DiarizationOutcome.Failed;
        }

        // Motorun keyfi küme kimlikleri KALICI veriye sızmasın: kanonik numaralar (ilk konuşan
        // A) burada, saklamadan önce verilir — böylece okuyan her yüzey aynı numarayı görür.
        var canonical = SpeakerRenumberer.ByFirstAppearance(speakers);

        // Boş liste de saklanır: "diarization çalıştı, konuşmacı ayırt edilemedi" ile
        // "diarization hiç çalışmadı" farklı durumlardır ve rapor bunları ayırt etmelidir.
        await _store.SaveSpeakerSegmentsAsync(sessionId, canonical, ct).ConfigureAwait(false);
        return DiarizationOutcome.Completed;
    }
}
