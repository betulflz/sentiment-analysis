using System.Collections.Concurrent;
using SentimentAnalysis.Application.Abstractions;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Application.Pipeline;

/// <summary>Oturum chunk'larını BELLEKTE biriktiren <see cref="ISessionStore"/> (Alt-Proje 06;
/// gerçek SQLite/dosya + retention → Alt-Proje 07). Thread-safe. Metadata sınırı: StartedAt ilk
/// kayıt anıdır, Language <see cref="DetectedLanguage.Other"/> (SaveChunkAsync bunları taşımaz;
/// toplantı-düzeyi metadata pipeline tarafından ayrıca yönetilir).</summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private sealed class Entry
    {
        public DateTimeOffset StartedAt { get; init; }
        public List<ChunkResult> Chunks { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, Entry> _sessions = new();
    private readonly Func<DateTimeOffset> _clock;

    public InMemorySessionStore() : this(() => DateTimeOffset.UtcNow) { }
    public InMemorySessionStore(Func<DateTimeOffset> clock) => _clock = clock;

    public Task SaveChunkAsync(Guid sessionId, ChunkResult result, CancellationToken ct)
    {
        var entry = _sessions.GetOrAdd(sessionId, _ => new Entry { StartedAt = _clock() });
        lock (entry.Chunks) entry.Chunks.Add(result);
        return Task.CompletedTask;
    }

    public Task<MeetingSession?> GetSessionAsync(Guid id, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(id, out var entry)) return Task.FromResult<MeetingSession?>(null);
        MeetingSession session;
        lock (entry.Chunks)
            session = new MeetingSession(id, entry.StartedAt, DetectedLanguage.Other, entry.Chunks.ToList());
        return Task.FromResult<MeetingSession?>(session);
    }

    public Task<IReadOnlyList<MeetingSession>> ListSessionsAsync(CancellationToken ct)
    {
        var list = new List<MeetingSession>();
        foreach (var kv in _sessions)
        {
            lock (kv.Value.Chunks)
                list.Add(new MeetingSession(kv.Key, kv.Value.StartedAt, DetectedLanguage.Other, kv.Value.Chunks.ToList()));
        }
        return Task.FromResult<IReadOnlyList<MeetingSession>>(list);
    }

    public Task PurgeExpiredAudioAsync(CancellationToken ct) => Task.CompletedTask;
}
