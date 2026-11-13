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

    /// <param name="expectedSpeakers">Kullanıcının söylediği kişi sayısı; 0 = bilinmiyor.</param>
    /// <summary>Ön-hesaplı (zaten hayalet süzmeden geçmiş) bölgeleri saklar — dosya modu, karşı tarafa odak.</summary>
    public Task SaveSegmentsAsync(Guid sessionId, IReadOnlyList<SpeakerSegment> segments, CancellationToken ct) =>
        _store.SaveSpeakerSegmentsAsync(sessionId, segments, ct);

    public async Task<DiarizationOutcome> RunAsync(Guid sessionId, int expectedSpeakers, CancellationToken ct) =>
        (await RunWithAudioAsync(sessionId, expectedSpeakers, ct).ConfigureAwait(false)).Outcome;

    /// <summary>Aynı işi yapar (bkz. <see cref="RunAsync"/>) ama okuduğu ham sesi de dışarı verir.
    /// <para><b>Neden:</b> 10d'de konuşmacı prozodisi (<see cref="SpeakerProsodyAnalyzer"/>) aynı
    /// oturum sesine tekrar ihtiyaç duyar. <c>SqliteSessionStore.GetSessionAudioAsync</c> her
    /// çağrıda dosyayı yeniden şifre çözüp yeni bir <c>float[]</c> ayırır; 90 dk'lık üst sınırda
    /// bu ~172 MB + ~345 MB'lık bir tahsisi İKİNCİ KEZ yapmak demektir (M1 Air 8 GB hedef donanımda
    /// pahalı). Bu yüzden ses burada TEK sefer okunur ve çağırana da geçirilir; ikinci bir
    /// tüketicinin ayrıca <c>IDiarizationStore</c>'a gitmesine gerek kalmaz.</para>
    /// <para>Outcome <see cref="DiarizationOutcome.Completed"/> İSE Audio HER ZAMAN doludur —
    /// aşağıdaki mantık sesi yalnızca dolu olduğunda Completed döndürür. Diğer tüm durumlarda
    /// (NoAudio/Failed) Audio boş dizidir; çağıran ekstra bir null/boş kontrolüne gerek duymadan
    /// bu değişmeze güvenebilir.</para></summary>
    public async Task<(DiarizationOutcome Outcome, float[] Audio)> RunWithAudioAsync(Guid sessionId, int expectedSpeakers, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var audio = await _store.GetSessionAudioAsync(sessionId, ct).ConfigureAwait(false);
        if (audio is null || audio.Length == 0) return (DiarizationOutcome.NoAudio, Array.Empty<float>());

        IReadOnlyList<SpeakerSegment> speakers;
        try
        {
            speakers = await _diarizer.DiarizeAsync(audio, expectedSpeakers, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;   // iptal bir hata değil, koşunun durumudur — Failed'a çevrilmez
        }
        catch
        {
            // Model eksik / motor hatası: kullanıcıya dürüstçe "başarısız" denir ve rapor
            // konuşmacısız gösterilir. Yarım veya uydurma sonuç YAZILMAZ.
            return (DiarizationOutcome.Failed, Array.Empty<float>());
        }

        // Motorun keyfi küme kimlikleri KALICI veriye sızmasın: kanonik numaralar (ilk konuşan
        // A) burada, saklamadan önce verilir — böylece okuyan her yüzey aynı numarayı görür.
        // Hayalet kümeler (kırıntı) de saklamadan ÖNCE ayıklanır: kayıt, rapor ve ses tonu ölçümü tek
        // numara uzayını paylaşır (ayrı bir sunum-zamanı yeniden numaralama kimlik ayrışması yaratırdı).
        var canonical = GhostSpeakerFilter.Apply(speakers);

        // Boş liste de saklanır: "diarization çalıştı, konuşmacı ayırt edilemedi" ile
        // "diarization hiç çalışmadı" farklı durumlardır ve rapor bunları ayırt etmelidir.
        await _store.SaveSpeakerSegmentsAsync(sessionId, canonical, ct).ConfigureAwait(false);
        return (DiarizationOutcome.Completed, audio);
    }
}
