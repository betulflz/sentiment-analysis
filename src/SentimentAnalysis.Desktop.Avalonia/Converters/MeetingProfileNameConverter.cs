using System.Globalization;
using Avalonia.Data.Converters;
using SentimentAnalysis.Application.Decision;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

public sealed class MeetingProfileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is MeetingProfile profile ? DecisionText.Name(profile) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
