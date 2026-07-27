using Avalonia.Controls;

namespace SentimentAnalysis.Desktop.Avalonia.Views;

/// <summary>Metin / Ses / Nihai sütunlarının ortak şablonu; DataContext bir
/// <see cref="SentimentAnalysis.Desktop.SourceAnalysisVm"/>'dir (spec 08d Dilim 2).</summary>
public partial class SourceAnalysisPanel : UserControl
{
    public SourceAnalysisPanel() => InitializeComponent();
}
