using System.Collections.Generic;

namespace SentimentAnalysis.Core;

/// <summary>Karar destek kriterleri (spec 11 §6.3, K1..K6). Dizilerde <c>(int)Criterion</c> ile
/// indekslenir — sıra her yerde bu enum sırasıdır.</summary>
public enum Criterion { Gerilim, IlgiKaybi, OlumsuzDuygu, Uyumsuzluk, DusukEnerji, Kotulesme }

/// <summary>Görüşme sırasında önerilebilen eylemler. <see cref="DevamEt"/> kuralla seçilir;
/// diğerleri TOPSIS ile sıralanan müdahalelerdir.</summary>
public enum LiveAction { DevamEt, KonuyuDegistir, SoruSor, EmpatiKur, AraVer, KapanisaGec }

/// <summary>Görüşme sonrası takip adımları (profile özgü alt kümeler). Mülakat seçenekleri SÜREÇ
/// adımlarıdır — sistem işe alım kararı vermez (spec §4).</summary>
public enum FollowUpAction
{
    TeklifGonder, IkinciGorusme, ItirazBilgisi, Beklet,
    DegerlendirmeyeGec, EkGorusme, GorusmeyiTekrarla,
    KararEpostasi, BireBirGorusme, TakipToplantisi
}

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
    double DataQuality);

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
public sealed record DecisionWindow(System.TimeSpan Start, System.TimeSpan End, Indicators Indicators, Recommendation Recommendation);

/// <summary>Görüşme sonrası takip kararı.</summary>
public sealed record FollowUpDecision(FollowUpAction Best, IReadOnlyList<Ranked<FollowUpAction>> Ranking, string Reason);

/// <summary>Duyarlılık analizi satırı: bir kriterin ağırlığı <paramref name="Delta"/> oranında
/// değişince en iyi seçenek.</summary>
public sealed record SensitivityRow<T>(Criterion Criterion, double Delta, T Best, bool Changed);
