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
        // İt.5: kullanıcı ayarları (Ayarlar ekranında kaydedilen) açılışta uygulanır; dosya yoksa/bozuksa varsayılan.
        Services = CompositionRoot.BuildServiceProvider(
            UserSettingsFile.Apply(UserSettingsFile.DefaultPath(), SentimentAnalysis.Application.Configuration.AppConfig.Default));
        // Ses saklama politikası: süresi dolan (ya da "sesi sakla" kapalıysa tüm) ses açılışta arka planda silinir.
        var store = Services.GetRequiredService<SentimentAnalysis.Application.Abstractions.ISessionStore>();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try { await store.PurgeExpiredAudioAsync(System.Threading.CancellationToken.None); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"Ses temizliği başarısız: {ex.Message}"); }
        });
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
