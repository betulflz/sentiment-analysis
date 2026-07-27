using System.Globalization;
using Avalonia.Data.Converters;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Canlı ekranda uyumsuzluk rozeti şeridi gibi metin tabanlı görünürlük anahtarları için:
/// boş olmayan <c>string</c> → <c>true</c>. Boş/null → <c>false</c> (şerit çöker, boş kutu kalmaz).</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
