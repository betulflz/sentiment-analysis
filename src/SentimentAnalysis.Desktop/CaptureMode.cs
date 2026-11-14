namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analizin kaynağı. Dosya modunda video uzantılı dosya seçilirse görüntü de analiz edilir.</summary>
public enum CaptureMode
{
    /// <summary>Diskten seçilen bir ses dosyası.</summary>
    Dosya,

    /// <summary>Canlı mikrofon.</summary>
    Mikrofon,

    /// <summary>Canlı mikrofon + kamera (karşı tarafın yüzü; spec 11).</summary>
    Kamera,
}

/// <summary>Seçili kaynak modunu ViewModel ile kompozisyon kökü arasında paylaşır
/// (kaynak fabrikası hangi <c>IAudioCaptureSource</c>'u üreteceğini buradan öğrenir).</summary>
public interface ICaptureModeState
{
    CaptureMode Mode { get; set; }
}

/// <summary><see cref="ICaptureModeState"/>'in DI'da tekil (singleton) olarak kaydedilen somut hâli.</summary>
public sealed class CaptureModeState : ICaptureModeState
{
    public CaptureMode Mode { get; set; } = CaptureMode.Dosya;
}
