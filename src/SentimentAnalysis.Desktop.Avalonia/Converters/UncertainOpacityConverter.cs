using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SentimentAnalysis.Desktop.Avalonia.Converters;

/// <summary>Belirsiz atamayı soluklaştırır (true → 0,6). Belirsizlik yalnızca rozetle değil
/// görsel ağırlıkla da anlatılır: kullanıcı rozeti kaçırsa bile satırın zayıf olduğunu görür.</summary>
public sealed class UncertainOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.6 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
