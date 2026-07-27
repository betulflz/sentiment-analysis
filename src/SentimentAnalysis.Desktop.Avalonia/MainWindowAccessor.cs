using Avalonia.Controls;

namespace SentimentAnalysis.Desktop.Avalonia;

/// <summary>Ana pencereye gecikmeli erişim tutucusu: DI kompozisyonu (<see cref="CompositionRoot"/>)
/// pencere oluşmadan önce çalıştığından, <see cref="AvaloniaAudioFilePicker"/> bir <c>Func&lt;TopLevel?&gt;</c>
/// accessor alır; bu accessor burada tutulan referansı okur. <see cref="App"/> pencereyi oluşturunca doldurur.</summary>
public static class MainWindowAccessor
{
    public static TopLevel? Current { get; set; }
}
