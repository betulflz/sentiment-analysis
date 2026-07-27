namespace SentimentAnalysis.Desktop;

/// <summary>Kullanıcıdan bir ses dosyası seçmesini ister (Avalonia StorageProvider vb. tarafından implemente edilir).</summary>
public interface IAudioFilePicker
{
    Task<string?> PickAudioFileAsync();
}
