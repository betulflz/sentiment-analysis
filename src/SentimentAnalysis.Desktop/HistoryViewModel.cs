using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

public sealed class SessionRowVm
{
    public Guid Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public int ChunkCount { get; init; }
}

/// <summary>Kayıtlı oturumları <see cref="ISessionStore"/>'dan listeler ve seçileni açar (İt.5).</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ISessionStore _store;
    private readonly IDecisionStore? _decisions;
    public ObservableCollection<SessionRowVm> Sessions { get; } = new();

    /// <summary>Açma hatası/eksik oturum gibi kısa durum metni; boşsa gösterilmez.</summary>
    [ObservableProperty] private string _status = string.Empty;

    /// <summary>Kayıtlı oturum açıldı: parçalar + (varsa) karar kaydı. Ana ekran raporu bununla doldurur.</summary>
    public event Action<MeetingSession, DecisionSession?>? Opened;

    public HistoryViewModel(ISessionStore store, IDecisionStore? decisions = null)
    {
        _store = store;
        _decisions = decisions;
    }

    [RelayCommand]
    private async Task OpenAsync(SessionRowVm? row, CancellationToken ct)
    {
        if (row is null) return;
        try
        {
            var session = await _store.GetSessionAsync(row.Id, ct).ConfigureAwait(true);
            if (session is null)
            {
                Status = "Oturum bulunamadı (silinmiş olabilir).";
                return;
            }
            DecisionSession? decisions = null;
            if (_decisions is not null)
                decisions = (await _decisions.GetDecisionSessionAsync(row.Id, ct).ConfigureAwait(true))?.Session;
            Status = string.Empty;
            Opened?.Invoke(session, decisions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Status = "Oturum açılamadı: kayıt okunamadı.";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        var sessions = await _store.ListSessionsAsync(ct).ConfigureAwait(true);
        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(new SessionRowVm { Id = s.Id, StartedAt = s.StartedAt, ChunkCount = s.Chunks.Count });
    }
}
