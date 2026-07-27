using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Avalonia <see cref="TopLevel.StorageProvider"/> tabanlı dosya seçici. Ana pencereye erişim
/// bir <see cref="Func{TopLevel}"/> accessor ile gecikmeli sağlanır (DI kompozisyon anında pencere henüz yok).</summary>
public sealed class AvaloniaAudioFilePicker : IAudioFilePicker
{
    private readonly Func<TopLevel?> _topLevelAccessor;

    public AvaloniaAudioFilePicker(Func<TopLevel?> topLevelAccessor)
    {
        _topLevelAccessor = topLevelAccessor;
    }

    public async Task<string?> PickAudioFileAsync()
    {
        var topLevel = _topLevelAccessor();
        if (topLevel is null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Ses dosyası seç",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Ses") { Patterns = new[] { "*.mp3", "*.m4a", "*.aac", "*.wav" } },
            },
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
