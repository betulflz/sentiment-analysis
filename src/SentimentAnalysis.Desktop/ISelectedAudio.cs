namespace SentimentAnalysis.Desktop;

/// <summary>Seçili ses dosyasının yolunu tutan paylaşılan durum (LiveViewModel ile UI arasında).</summary>
public interface ISelectedAudio
{
    string? Path { get; set; }
}

public sealed class SelectedAudio : ISelectedAudio
{
    public string? Path { get; set; }
}
