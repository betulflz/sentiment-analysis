using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop.Avalonia.Converters;

namespace SentimentAnalysis.Desktop.Avalonia.Views;

public partial class LiveView : UserControl
{
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly VideoFrameToBitmapConverter _converter = new();
    private VideoFrame? _displayedFrame;
    private WriteableBitmap? _bitmap;

    public LiveView()
    {
        InitializeComponent();
        _previewTimer.Tick += (_, _) => RefreshPreview();
        DataContextChanged += (_, _) => ClearBitmap();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshPreview();
        _previewTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _previewTimer.Stop();
        ClearBitmap();
        base.OnDetachedFromVisualTree(e);
    }

    private void RefreshPreview()
    {
        if (DataContext is not LiveViewModel vm) return;
        vm.RefreshPreview();
        var frame = vm.PreviewFrame;
        if (ReferenceEquals(frame, _displayedFrame)) return;
        var next = (WriteableBitmap?)_converter.Convert(frame, typeof(WriteableBitmap), null, CultureInfo.InvariantCulture);
        var previous = _bitmap;
        _bitmap = next;
        _displayedFrame = frame;
        PreviewImage.Source = next;
        previous?.Dispose();
    }

    private void ClearBitmap()
    {
        PreviewImage.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
        _displayedFrame = null;
    }
}
