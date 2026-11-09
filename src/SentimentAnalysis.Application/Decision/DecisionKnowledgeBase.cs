using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Decision;

/// <summary>Bir görüşme profilinin karar tanımı.</summary>
public sealed record ProfileDefinition(
    MeetingProfile Profile,
    double[,] Pairwise,
    IReadOnlyList<LiveAction> Interventions,
    IReadOnlyList<FollowUpAction> FollowUps,
    FollowUpAction PositiveFollowUp);

/// <summary>KDS bilgi tabanı: profil AHP karşılaştırmaları ve eylem etki tabloları.
/// <para><b>Uzman bilgisi, KALİBRE EDİLMEMİŞ.</b> Değerler plan 11c'de makul varsayımlarla
/// seçildi ve senaryo davranışı Python ile doğrulandı (tüm profillerde CR &lt; 0,02). Etki tablosu
/// "bu eylem bu sorunu ne kadar giderir" sorusunun 0..1 cevabıdır; kriter sırası
/// <see cref="Criterion"/> enum sırasıdır.</para></summary>
public static class DecisionKnowledgeBase
{
    // Her profilde ortak müdahaleler; her profile bir de kendine özel eylem eklenir (ilgi kaybında satışta
    // "faydaya dön", mülakatta "daha kolay soru", toplantıda "sessiz kişiye söz ver").
    private static readonly LiveAction[] Common =
    {
        LiveAction.KonuyuDegistir, LiveAction.SoruSor, LiveAction.EmpatiKur, LiveAction.AraVer, LiveAction.KapanisaGec,
        LiveAction.SakinOl, LiveAction.AciklayiciOl, LiveAction.OzetleDinle, LiveAction.TebessumEt,
        LiveAction.OrnekleAnlat, LiveAction.KisaTut, LiveAction.TereddudunuSor, LiveAction.YenidenAnlat,
        LiveAction.EnerjiYukselt, LiveAction.OrtakNoktaHatirlat,
    };

    private static LiveAction[] For(LiveAction profileSpecific) => Common.Append(profileSpecific).ToArray();

    //                                                           K1   K2   K3   K4   K5   K6
    public static IReadOnlyDictionary<LiveAction, double[]> LiveImpact { get; } = new Dictionary<LiveAction, double[]>
    {
        [LiveAction.KonuyuDegistir] = new[] { 0.3, 0.9, 0.3, 0.2, 0.7, 0.5 },
        [LiveAction.SoruSor]        = new[] { 0.2, 0.5, 0.3, 0.9, 0.4, 0.3 },
        [LiveAction.EmpatiKur]      = new[] { 0.9, 0.2, 0.8, 0.4, 0.1, 0.5 },
        [LiveAction.AraVer]         = new[] { 0.6, 0.4, 0.2, 0.1, 0.9, 0.2 },
        [LiveAction.KapanisaGec]    = new[] { 0.2, 0.3, 0.3, 0.1, 0.3, 0.9 },
        // 2026-11-09: yeni eylemler. Aynı varsayım: "bu eylem bu sorunu ne kadar giderir" (0..1), KALİBRE EDİLMEMİŞ.
        [LiveAction.SakinOl]             = new[] { 0.9, 0.1, 0.6, 0.3, 0.1, 0.4 },
        [LiveAction.AciklayiciOl]        = new[] { 0.4, 0.3, 0.5, 0.7, 0.2, 0.4 },
        [LiveAction.OzetleDinle]         = new[] { 0.6, 0.3, 0.6, 0.4, 0.1, 0.5 },
        [LiveAction.TebessumEt]          = new[] { 0.7, 0.3, 0.7, 0.2, 0.3, 0.4 },
        [LiveAction.OrnekleAnlat]        = new[] { 0.1, 0.7, 0.2, 0.5, 0.6, 0.3 },
        [LiveAction.KisaTut]             = new[] { 0.3, 0.7, 0.1, 0.1, 0.6, 0.3 },
        [LiveAction.TereddudunuSor]      = new[] { 0.2, 0.3, 0.4, 0.9, 0.2, 0.3 },
        [LiveAction.YenidenAnlat]        = new[] { 0.2, 0.4, 0.3, 0.8, 0.3, 0.4 },
        [LiveAction.EnerjiYukselt]       = new[] { 0.1, 0.5, 0.1, 0.1, 0.9, 0.3 },
        [LiveAction.OrtakNoktaHatirlat]  = new[] { 0.4, 0.3, 0.5, 0.4, 0.3, 0.9 },
        [LiveAction.FaydayaDon]          = new[] { 0.1, 0.9, 0.3, 0.4, 0.5, 0.4 },
        [LiveAction.KolaySoruSor]        = new[] { 0.5, 0.8, 0.4, 0.3, 0.4, 0.3 },
        [LiveAction.SozVer]              = new[] { 0.1, 0.9, 0.1, 0.2, 0.6, 0.4 },
    };

    public static IReadOnlyDictionary<FollowUpAction, double[]> FollowUpImpact { get; } = new Dictionary<FollowUpAction, double[]>
    {
        [FollowUpAction.IkinciGorusme]     = new[] { 0.5, 0.8, 0.4, 0.6, 0.7, 0.5 },
        [FollowUpAction.ItirazBilgisi]     = new[] { 0.4, 0.3, 0.8, 0.9, 0.2, 0.6 },
        [FollowUpAction.Beklet]            = new[] { 0.8, 0.5, 0.6, 0.3, 0.5, 0.9 },
        [FollowUpAction.EkGorusme]         = new[] { 0.3, 0.6, 0.4, 0.9, 0.5, 0.4 },
        [FollowUpAction.GorusmeyiTekrarla] = new[] { 0.9, 0.5, 0.5, 0.3, 0.6, 0.5 },
        [FollowUpAction.BireBirGorusme]    = new[] { 0.9, 0.3, 0.8, 0.7, 0.2, 0.6 },
        [FollowUpAction.TakipToplantisi]   = new[] { 0.3, 0.9, 0.4, 0.5, 0.8, 0.6 },
    };

    // AHP üst üçgeni, satır sırasıyla (1,2)(1,3)…(1,6)(2,3)…(5,6); alt üçgen = 1/a.
    // Satış: olumsuz duygu en önemli; Mülakat: gerilim (aday rahatlatılmalı); Toplantı: ilgi kaybı.
    private static readonly IReadOnlyDictionary<MeetingProfile, ProfileDefinition> Profiles =
        new Dictionary<MeetingProfile, ProfileDefinition>
        {
            [MeetingProfile.Satis] = new(MeetingProfile.Satis,
                Reciprocal(1, 1 / 2.0, 3, 3, 1, 1 / 2.0, 2, 3, 1, 3, 4, 2, 2, 1 / 2.0, 1 / 3.0),
                For(LiveAction.FaydayaDon),
                new[] { FollowUpAction.IkinciGorusme, FollowUpAction.ItirazBilgisi, FollowUpAction.Beklet },
                FollowUpAction.TeklifGonder),
            [MeetingProfile.Mulakat] = new(MeetingProfile.Mulakat,
                Reciprocal(3, 2, 2, 5, 3, 1 / 2.0, 1 / 2.0, 2, 1, 1, 3, 2, 3, 2, 1 / 2.0),
                For(LiveAction.KolaySoruSor),
                new[] { FollowUpAction.EkGorusme, FollowUpAction.GorusmeyiTekrarla },
                FollowUpAction.DegerlendirmeyeGec),
            [MeetingProfile.Toplanti] = new(MeetingProfile.Toplanti,
                Reciprocal(1 / 3.0, 1, 2, 1 / 2.0, 2, 3, 4, 2, 3, 2, 1 / 2.0, 1, 1 / 3.0, 1 / 2.0, 2),
                For(LiveAction.SozVer),
                new[] { FollowUpAction.BireBirGorusme, FollowUpAction.TakipToplantisi },
                FollowUpAction.KararEpostasi),
        };

    private static readonly IReadOnlyDictionary<MeetingProfile, IReadOnlyList<double>> Weights =
        Profiles.ToDictionary(p => p.Key, p => Ahp.Compute(p.Value.Pairwise).Weights);

    public static ProfileDefinition Get(MeetingProfile p) => Profiles[p];

    public static IReadOnlyList<double> DefaultWeights(MeetingProfile p) => Weights[p];

    private static double[,] Reciprocal(params double[] upper)
    {
        const int n = 6;
        var m = new double[n, n];
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            m[i, i] = 1;
            for (int j = i + 1; j < n; j++)
            {
                m[i, j] = upper[k];
                m[j, i] = 1 / upper[k];
                k++;
            }
        }
        return m;
    }
}
