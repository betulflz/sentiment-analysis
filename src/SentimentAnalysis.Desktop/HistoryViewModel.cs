using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SentimentAnalysis.Application.Abstractions;

namespace SentimentAnalysis.Desktop;

public sealed class SessionRowVm
{
    public Guid Id { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public int ChunkCount { get; init; }
}

/// <summary>Kayıtlı oturumları <see cref="ISessionStore"/>'dan listeler.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ISessionStore _store;
    public ObservableCollection<SessionRowVm> Sessions { get; } = new();

    public HistoryViewModel(ISessionStore store) => _store = store;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        var sessions = await _store.ListSessionsAsync(ct).ConfigureAwait(true);
        Sessions.Clear();
        foreach (var s in sessions)
            Sessions.Add(new SessionRowVm { Id = s.Id, StartedAt = s.StartedAt, ChunkCount = s.Chunks.Count });
    }
}
