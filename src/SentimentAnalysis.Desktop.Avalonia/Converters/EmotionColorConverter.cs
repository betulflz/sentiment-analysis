using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop.Avalonia.Controls;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Duygu haritası açıklamasındaki renk kutusu: şeritteki parçayla aynı renk.</summary>
public sealed class EmotionColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Emotion e ? EmotionColors.Brush(e) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
