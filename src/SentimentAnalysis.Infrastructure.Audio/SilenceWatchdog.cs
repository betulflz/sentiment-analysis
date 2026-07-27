namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>
/// Akışın <b>tamamen ölü</b> olup olmadığını izler — macOS mikrofon izni reddedildiğinde
/// AudioQueue hata dönmez, normal hızda sessiz tampon gönderir. Bu sınıf o durumu yakalar.
/// </summary>
/// <remarks>
/// Eşik (<c>silenceCeiling</c>) DİJİTAL sessizliği ayırt etmek içindir, "konuşma var mı"
/// sorusunu yanıtlamaz — o iş VAD'ındır (<c>SileroVadGate</c>). Spike ölçümü (2026-07-27):
/// izin verilmiş sessiz odanın tabanı 0,0098-0,0168; konuşma 0,0805; izin reddinde ise
/// örnekler tam sıfırdır. Varsayılan 0,001 bu üçünü doğru ayırır.
/// Saf ve deterministik.
/// </remarks>
public sealed class SilenceWatchdog
{
    private readonly long _windowSamples;
    private readonly double _silenceCeiling;
    private long _seenSamples;

    public SilenceWatchdog(int sampleRate, TimeSpan window, double silenceCeiling = 0.001)
    {
        _windowSamples = (long)Math.Round(window.TotalSeconds * sampleRate);
        _silenceCeiling = silenceCeiling;
    }

    /// <summary>Gözlem penceresi doldu mu (karar vermek için yeterli veri geldi mi)?</summary>
    public bool WindowElapsed => _seenSamples >= _windowSamples;

    /// <summary>Görülen en büyük mutlak genlik.</summary>
    public double PeakSeen { get; private set; }

    /// <summary>Pencere doldu VE hiçbir zaman dijital sessizliğin üstüne çıkılmadı.</summary>
    public bool LooksDead => WindowElapsed && PeakSeen <= _silenceCeiling;

    public void Observe(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            double a = Math.Abs(s);
            if (a > PeakSeen) PeakSeen = a;
        }
        _seenSamples += samples.Length;
    }
}
