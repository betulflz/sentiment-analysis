namespace SentimentAnalysis.Desktop;

/// <summary>Canlı analizin ses kaynağı.</summary>
public enum CaptureMode
{
    /// <summary>Diskten seçilen bir ses dosyası.</summary>
    Dosya,

    /// <summary>Canlı mikrofon.</summary>
    Mikrofon,
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
