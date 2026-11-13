using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Application.Configuration;
using SentimentAnalysis.Application.Diarization;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Kök VM: alt görünümleri barındırır + basit navigasyon.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISpeakerAnalysis? _speakers;
    private readonly IFileRoleSelection? _fileRoles;
    private readonly AppConfig? _config;
    private readonly Action<Action> _dispatch;
    private readonly IDecisionStore? _decisions;
    private int _reportGeneration; // raporda gösterilen görüşmenin sırası — geç biten kayıt eski görüşmeye aitse uyarı yazılmaz

    public LiveViewModel Live { get; }
    public ReportViewModel Report { get; }
    public HistoryViewModel History { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Arka plan konuşmacı geçişinin görevi — testlerin bekleyebilmesi için açıkta.
    /// Üretimde kimse beklemez (rapor hemen gelir, konuşmacılar sonra düşer).</summary>
    public Task? SpeakerAnalysisTask { get; private set; }

    /// <summary>Karar kaydının (11e) arka plan görevi — testler bekler; üretimde kimse beklemez.</summary>
    public Task? DecisionSaveTask { get; private set; }

    [ObservableProperty] private object _currentView;

    /// <param name="speakers">Konuşmacı ayrımı servisi (10b). <c>null</c> ise özellik hiç
    /// denenmez ve rapor bugünküyle bire bir aynı davranır.</param>
    /// <param name="dispatch">UI güncellemelerini doğru iş parçacığına taşıyan eylem. Varsayılan
    /// satır-içi çalıştırmadır (testler); Avalonia katmanı <c>Dispatcher.UIThread.Post</c> geçer.
    /// Bu, 08e'de yaşanan "arka plan iş parçacığından UI koleksiyonuna yazma" hatasının
    /// tekrarlanmaması için EXPLICIT tutulur — <c>Desktop</c> projesi Avalonia'sız kalmalıdır.</param>
    /// <param name="decisions">Karar verisinin kalıcı deposu (11e). <c>null</c> ise rapor yine gösterilir, kayıt denenmez.</param>
    public MainViewModel(LiveViewModel live, ReportViewModel report, HistoryViewModel history,
                         SettingsViewModel settings, ISpeakerAnalysis? speakers = null,
                         AppConfig? config = null, Action<Action>? dispatch = null,
                         IDecisionStore? decisions = null, IFileRoleSelection? fileRoles = null)
    {
        Live = live; Report = report; History = history; Settings = settings;
        _fileRoles = fileRoles;
        _speakers = speakers;
        _config = config;
        _dispatch = dispatch ?? (a => a());
        _decisions = decisions;
        _currentView = live;

        Live.Completed += s =>
        {
            Report.LoadSummary(s);
            // Kopya alınır: rapor açıkken yeni koşu başlarsa canlı geçmiş temizlenir, rapor eskisini korur.
            var decision = Live.Decision.Snapshot();
            Report.LoadDecisions(decision);
            // Konuşmacı kartından yüz seçilebilsin: yüzsüz değerli parçalar + panelde yazılan adlar (kopya).
            Report.SetFaceContext(Live.RunChunks.ToArray(), Live.UserFaceNames);
            var generation = ++_reportGeneration;
            if (_decisions is not null) DecisionSaveTask = SaveDecisionsAsync(_decisions, decision, generation);
            History.RefreshCommand.Execute(null);
            CurrentView = Report;

            // Konuşmacı ayrımı raporu BEKLETMEZ: özet anında gösterilir, konuşmacılar
            // arka planda hesaplanıp sonradan düşer (spec 10 S2; 08e dersi: "Durdur"
            // sonrası kullanıcıyı bekletme).
            SpeakerAnalysisTask = StartSpeakerAnalysisAsync();
        };
    }

    private async Task SaveDecisionsAsync(IDecisionStore store, DecisionSession session, int generation)
    {
        // Takip kararı LoadDecisions'ta görüşme sonu ağırlıklarıyla hesaplandı; kullanıcının raporda
        // kaydırıcı oynatması ("ya şöyle olsaydı") saklanmaz — kayıt burada, beklemeden önce alınır.
        if (Live.LastSessionId is not Guid sessionId || Report.FollowUp.Current is not { } followUp) return;
        var record = new DecisionSessionRecord(session, followUp.Best, followUp.Reason);
        try
        {
            await store.SaveDecisionSessionAsync(sessionId, record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Kayıt hatası (DB kilitli/bozuk) raporu bozmaz; kullanıcıya kısa bir Türkçe uyarı gösterilir,
            // teknik ayrıntı (çoğu zaman İngilizce SQLite mesajı) yalnızca teşhis çıktısına gider.
            System.Diagnostics.Debug.WriteLine($"Karar kaydı saklanamadı: {ex}");
            _dispatch(() =>
            {
                if (generation == _reportGeneration)
                    Report.DecisionSaveStatus = "Karar kaydı saklanamadı — bu rapor yalnızca şimdilik ekranda; veritabanına yazılamadı.";
            });
        }
    }

    private async Task StartSpeakerAnalysisAsync()
    {
        if (Live.LastSessionId is not Guid sessionId) return;

        // Karşı tarafa odak: dosya modunda ayrım analizden önce yapıldı — bölgeler ve bellekteki ses kullanılır,
        // "Konuşmacı ayırımı" ayarından bağımsız. Bitince seçim (ve ses) bırakılır.
        if (_speakers is not null && _fileRoles?.Prepared is { } prepared && _fileRoles.Host is SpeakerId host)
        {
            _dispatch(Report.SetDiarizationRunning);
            try
            {
                var r = await _speakers.RunPrecomputedAsync(sessionId, prepared.Segments, prepared.Audio, CancellationToken.None)
                    .ConfigureAwait(false);
                _dispatch(() => ApplySpeakerResult(r, host));
            }
            catch (Exception)
            {
                _dispatch(() => Report.SetDiarizationOutcome(DiarizationOutcome.Failed));
            }
            finally
            {
                _fileRoles.Clear();
            }
            return;
        }

        if (_speakers is null || !_speakers.Enabled) return;

        _dispatch(Report.SetDiarizationRunning);

        SpeakerAnalysisResult result;
        try
        {
            result = await _speakers.RunAsync(sessionId, Live.LastSpeakerCount, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Arka plan işi uygulamayı çökertmemeli; kullanıcıya dürüstçe "başarısız" denir.
            _dispatch(() => Report.SetDiarizationOutcome(DiarizationOutcome.Failed));
            return;
        }

        _dispatch(() => ApplySpeakerResult(result, host: null));
    }

    private void ApplySpeakerResult(SpeakerAnalysisResult result, SpeakerId? host)
    {
        if (result.Outcome == DiarizationOutcome.Completed)
        {
            var minOverlap = _config?.Diarization.MinOverlapRatio ?? 0.5;
            Report.ApplySpeakers(result.Transcript, result.Speakers, minOverlap);
            Report.ApplySpeakerEmotions(result.Chunks, result.Speakers, minOverlap, result.SpeakerProsody);
        }

        // Sonuç DAİMA bildirilir — ApplySpeakers'tan SONRA çağrılır, çünkü durum metni
        // satır üretilip üretilmediğine bakar ("ayırt edilemedi" ile "hazır" ayrımı).
        Report.SetDiarizationOutcome(result.Outcome);
    }

    [RelayCommand] private void ShowLive() => CurrentView = Live;
    [RelayCommand] private void ShowReport() => CurrentView = Report;
    [RelayCommand] private void ShowHistory() => CurrentView = History;
    [RelayCommand] private void ShowSettings() => CurrentView = Settings;
}
