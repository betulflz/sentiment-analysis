using Avalonia.Controls;
using Avalonia.Platform.Storage;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Infrastructure.Audio;

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
            // Desteklenen küme TranscodePlan tarafından belirlenir; burada elle ikinci bir liste
            // tutmak ikisinin sessizce ayrışmasına yol açardı.
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Ses / video")
                {
                    Patterns = TranscodePlan.NativeExtensions
                        .Concat(TranscodePlan.TranscodableExtensions)
                        .Select(e => "*" + e)
                        .ToArray(),
                },
                new FilePickerFileType("Tüm dosyalar") { Patterns = new[] { "*" } },
            },
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
