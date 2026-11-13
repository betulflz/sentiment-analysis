using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Karar destek kriterleri (spec 11 §6.3, K1..K6). Dizilerde <c>(int)Criterion</c> ile
/// indekslenir — sıra her yerde bu enum sırasıdır.</summary>
public enum Criterion { Gerilim, IlgiKaybi, OlumsuzDuygu, Uyumsuzluk, DusukEnerji, Kotulesme }

/// <summary>Görüşme sırasında önerilebilen eylemler. <see cref="DevamEt"/> kuralla seçilir;
/// diğerleri TOPSIS ile sıralanan müdahalelerdir.</summary>
public enum LiveAction
{
    DevamEt, KonuyuDegistir, SoruSor, EmpatiKur, AraVer, KapanisaGec,
    // 2026-11-09 genişletme. Yeni değerler SONA eklenir: eylem tam sayı olarak saklanır, eski kayıtlar bozulmasın.
    SakinOl, AciklayiciOl, OzetleDinle, TebessumEt, OrnekleAnlat, KisaTut, TereddudunuSor, YenidenAnlat,
    EnerjiYukselt, OrtakNoktaHatirlat,
    FaydayaDon,      // yalnız satış
    KolaySoruSor,    // yalnız mülakat
    SozVer,          // yalnız toplantı
    BirAdimIlerlet, OlumluyuPekistir,  // olumlu durum: TOPSIS müdahalesi değil, kuralla seçilir
    // 2026-11-12: duygu/tempo/hareket ipuçlarına bağlı eylemler (yine SONA eklenir).
    GuvenceVer, SusVeDinle, GozTemasiKur, DuyguyuAdlandir, YavaslaKonus, MizahlaGevset, ItiraziKabulEt
}

/// <summary>Bir pencerenin 6 göstergeye girmeyen bağlam ipuçları: baskın duygu, tempo, baş çevirme,
/// gülümseme. Öneri çeşitliliği için eylemleri duygu/hal/harekete göre öne çıkarır (saklanmaz).</summary>
public sealed record ContextCues(
    Emotion Primary,
    double Arousal,       // 0..1
    double Valence,       // −1..1 (konuşma)
    double WordsPerSecond,
    double LookAway,      // en çok başını çeviren yüz, 0..1
    double Smile,         // en çok gülümseyen güvenilir yüz, 0..1
    double FaceNegative,  // en olumsuz yüz ifadesi, 0..1
    bool HasFace)
{
    /// <summary>12: son dakikada konuşmanın yönetene düşen payı (0..1); ses tanıtılmadıysa ya da az konuşma varsa null.</summary>
    public double? HostShare { get; init; }

    /// <summary>12: yönetenin konuşma hızı (kelime/sn); bilinmiyorsa null. Bilinirken <see cref="WordsPerSecond"/>
    /// karşı tarafın hızıdır.</summary>
    public double? HostWordsPerSecond { get; init; }
}

/// <summary>Görüşme sonrası takip adımları (profile özgü alt kümeler). Mülakat seçenekleri SÜREÇ
/// adımlarıdır — sistem işe alım kararı vermez (spec §4).</summary>
public enum FollowUpAction
{
    TeklifGonder, IkinciGorusme, ItirazBilgisi, Beklet,
    DegerlendirmeyeGec, EkGorusme, GorusmeyiTekrarla,
    KararEpostasi, BireBirGorusme, TakipToplantisi,
    // 2026-11-13 kategorili takip: SQLite int saklar — yalnız SONA ekle, sıra değişmez.
    ReferansKontrolu, DemoOner, IkinciGorusAl, NetlestirmeGorusmesi, TeknikDemo, CalismaGrubu,
    YaziliGorus, AdaylarlaKarsilastir, BilgiListesi, KarariErtele, IliskiyiOnar
}

/// <summary>Takip seçeneğinin ekrandaki yön kategorisi.</summary>
public enum FollowUpCategory { Ilerletin, Netlestirin, Bekleyin, IliskiyiGozetin }

/// <summary>Görüşme profili: kriter ağırlıklarını ve seçenek listelerini belirler.</summary>
public enum MeetingProfile { Satis, Mulakat, Toplanti }

/// <summary>Bir yüzün bir analiz penceresindeki özeti (11b üretir, 11c tüketir).
/// <paramref name="ValenceShift"/> kişinin KENDİ taban çizgisine göredir (−1..1);
/// <paramref name="NegativeExpression"/> taban-düzeltilmiş olumsuz ifade payıdır (0..1);
/// <paramref name="Reliability"/> yüz boyutundan türeyen güvenilirliktir (0..1).</summary>
public sealed record FaceWindowSummary(
    int FaceIndex,
    double VisibleRatio,
    double LookAwayRatio,
    double ValenceShift,
    double NegativeExpression,
    double SmileRatio,
    double Reliability);

/// <summary>Bir penceredeki K1..K6 ihtiyaç değerleri (0..1). <paramref name="SourceFace"/> değeri
/// sürükleyen yüzdür; null = görüşme geneli (ses/metin veya yüzsüz).</summary>
public sealed record Indicators(
    IReadOnlyList<double> Values,
    IReadOnlyList<int?> SourceFace,
    double DataQuality)
{
    /// <summary>Görüşmenin ne kadar olumlu gittiği (0..1): konuşma valence'ı ve gülümseme. Sorun yokken
    /// "olumlu öneri" için kullanılır; saklanmaz (yalnız canlı öneri). Additive, varsayılan 0.</summary>
    public double Positivity { get; init; }

    /// <summary>Duygu/tempo/hareket bağlamı; null = bağlam yok (eylem sıralaması yalnız TOPSIS). Additive.</summary>
    public ContextCues? Cues { get; init; }
}

/// <summary>TOPSIS sıralamasındaki bir seçenek ve onu en çok destekleyen iki kriter.</summary>
public sealed record Ranked<T>(T Alternative, double Closeness, IReadOnlyList<Criterion> TopCriteria);

/// <summary>Canlı öneri: eylem, sıralama, gerekçe ve veri kalitesinden türeyen güven.</summary>
public sealed record Recommendation(
    LiveAction Action,
    double Closeness,
    IReadOnlyList<Ranked<LiveAction>> Ranking,
    IReadOnlyList<Criterion> TopCriteria,
    int? Face,
    Confidence Confidence,
    string Reason);

/// <summary>Bir analiz penceresinin karar kaydı (canlı ekran üretir, rapor/depolama tüketir).</summary>
public sealed record DecisionWindow(System.TimeSpan Start, System.TimeSpan End, Indicators Indicators, Recommendation Recommendation)
{
    /// <summary>Pencerede görülen yüzlerin özetleri (11e depolaması). Additive, varsayılan boş.</summary>
    public IReadOnlyList<FaceWindowSummary> Faces { get; init; } = System.Array.Empty<FaceWindowSummary>();
}

/// <summary>Bitmiş bir görüşmenin karar verisi (11e): rapor ve depolama bu anlık görüntüyle çalışır.
/// <paramref name="ProfileWeights"/> profilin AHP ağırlıkları, <paramref name="FinalWeights"/> görüşme
/// sonundaki kaydırıcı değerleridir.</summary>
public sealed record DecisionSession(
    MeetingProfile Profile,
    IReadOnlyList<double> ProfileWeights,
    IReadOnlyList<double> FinalWeights,
    IReadOnlyList<DecisionWindow> Windows);

/// <summary>Saklanan karar kaydı (11e): görüşmenin karar verisi + görüşme sonu ağırlıklarıyla verilen takip kararı.</summary>
public sealed record DecisionSessionRecord(DecisionSession Session, FollowUpAction FollowUp, string FollowUpReason);

/// <summary>Raporun kritik anı: ihtiyacın müdahale eşiğini aştığı ardışık pencereler. <paramref name="Peak"/>
/// ihtiyaçtır (değer × veri kalitesi); <paramref name="Face"/> tepe penceresinde o kriteri sürükleyen yüz;
/// <paramref name="Shown"/> tepe penceresinde gösterilen öneri.</summary>
public sealed record CriticalMoment(System.TimeSpan Start, System.TimeSpan End, Criterion Criterion, double Peak, int? Face, LiveAction Shown);

/// <summary>Görüşme sırasında gösterilen önerinin değiştiği an.</summary>
public sealed record RecommendationChange(System.TimeSpan At, LiveAction Action, string Reason, Confidence Confidence);

/// <summary>Görüşme sonrası takip kararı.</summary>
/// <param name="Direction">Önerilen yön: <paramref name="Best"/>'in kategorisi.</param>
/// <param name="Options">Ekranda gösterilecek 1–3 seçenek; ilki <paramref name="Best"/>.</param>
public sealed record FollowUpDecision(FollowUpAction Best, IReadOnlyList<Ranked<FollowUpAction>> Ranking, string Reason,
    FollowUpCategory Direction, IReadOnlyList<FollowUpAction> Options);

/// <summary>Duyarlılık analizi satırı: bir kriterin ağırlığı <paramref name="Delta"/> oranında
/// değişince en iyi seçenek.</summary>
public sealed record SensitivityRow<T>(Criterion Criterion, double Delta, T Best, bool Changed);
