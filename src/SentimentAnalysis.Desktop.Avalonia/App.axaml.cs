using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Desktop;

namespace SentimentAnalysis.Desktop.Avalonia;

public partial class App : global::Avalonia.Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = CompositionRoot.BuildServiceProvider();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();

        // TrayIcon menü/tooltip bağlamaları Application.DataContext üzerinden çözülür (compiled binding yok — bkz. tasarım notu).
        DataContext = mainViewModel;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            desktop.MainWindow = mainWindow;
            MainWindowAccessor.Current = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
