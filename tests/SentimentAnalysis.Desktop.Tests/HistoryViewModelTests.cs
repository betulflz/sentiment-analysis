using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;
using SentimentAnalysis.Desktop;
using Xunit;

namespace SentimentAnalysis.Desktop.Tests;

public class HistoryViewModelTests
{
    private sealed class FakeStore : ISessionStore
    {
        private readonly List<MeetingSession> _sessions;
        public FakeStore(params MeetingSession[] s) => _sessions = new(s);
        public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<MeetingSession>>(_sessions);
        public Task SaveChunkAsync(Guid id, ChunkResult r, CancellationToken ct) => Task.CompletedTask;
        public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct) => Task.FromResult<MeetingSession?>(null);
        public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static MeetingSession S(int chunks) => new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, DetectedLanguage.Turkish,
        Enumerable_Range(chunks));
    private static IReadOnlyList<ChunkResult> Enumerable_Range(int n)
    {
        var list = new List<ChunkResult>();
        for (int i = 0; i < n; i++)
            list.Add(new ChunkResult(TimeSpan.FromSeconds(i), TimeSpan.FromSeconds(i+1),
                new FusedResult(new ValenceArousal(0,0.5), Emotion.Notr, Emotion.Notr, Confidence.Orta,
                    true, false, IncongruencePattern.Yok, 1,
                    TextVa: ValenceArousal.Neutral, ProsodyVa: ValenceArousal.Neutral,
                    TextConfidence: 0, ProsodyConfidence: 0), "x"));
        return list;
    }

    [Fact]
    public async Task Refresh_LoadsSessionsFromStore()
    {
        var vm = new HistoryViewModel(new FakeStore(S(2), S(5)));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Sessions.Count);
        Assert.Contains(vm.Sessions, s => s.ChunkCount == 2);
        Assert.Contains(vm.Sessions, s => s.ChunkCount == 5);
    }
}
