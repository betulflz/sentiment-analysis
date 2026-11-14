using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SentimentAnalysis.Desktop;
using SentimentAnalysis.Application.Runtime;
using Avalonia.Controls;

namespace SentimentAnalysis.Desktop.Avalonia;

public partial class App : global::Avalonia.Application
{
    private bool _shutdownStarted;
    private bool _shutdownFinished;

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
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            mainWindow.Closing += (_, e) =>
            {
                if (_shutdownFinished) return;
                e.Cancel = true;
                BeginShutdown(desktop);
            };
            desktop.ShutdownRequested += (_, e) =>
            {
                if (_shutdownFinished) return;
                e.Cancel = true;
                BeginShutdown(desktop);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void BeginShutdown(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;
        try
        {
            await ModelWorkShutdown.RunAsync(
                Services.GetRequiredService<ModelWorkQueue>(),
                Services.GetRequiredService<ResourceManagedAnalysisRunner>(),
                Services.GetRequiredService<LiveViewModel>().WaitForIdleAsync,
                () => Services is IAsyncDisposable provider ? provider.DisposeAsync() : ValueTask.CompletedTask);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Application cleanup failed: {ex.GetType().Name}");
        }
        finally
        {
            _shutdownFinished = true;
            MainWindowAccessor.Current = null;
            desktop.Shutdown();
        }
    }
}
