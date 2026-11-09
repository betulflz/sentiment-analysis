using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SentimentAnalysis.Desktop.Avalonia.Controls;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Konuşmacı sırası → rengi. Parametre "soft" ise sohbet balonu için açık ton.</summary>
public sealed class SpeakerColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i ? SpeakerColors.Brush(i, parameter as string == "soft") : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
