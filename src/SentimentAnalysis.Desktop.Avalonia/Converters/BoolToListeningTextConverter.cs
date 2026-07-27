using System.Globalization;
using Avalonia.Data.Converters;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Tepsi tooltip'i + "dinleniyor" göstergesi için: <c>bool IsCapturing</c> → Türkçe metin.</summary>
public sealed class BoolToListeningTextConverter : IValueConverter
{
    public static readonly BoolToListeningTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "SentimentAnalysis — dinleniyor" : "SentimentAnalysis — boşta";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
