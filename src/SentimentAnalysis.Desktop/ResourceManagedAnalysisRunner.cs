using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop;

/// <summary>Models live from warm-up until the end of one run. Jobs share a memory budget slot.</summary>
public sealed class ResourceManagedAnalysisRunner(
    ModelWorkQueue queue, Func<OwnedModelSession<IAnalysisRunner>> createSession)
    : IAnalysisRunner, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private OwnedModelSession<IAnalysisRunner>? _session;
    private ModelWorkQueue.Lease? _lease;
    private CancellationTokenSource? _sessionCancellation;
    private AnalysisRunSnapshot _snapshot = new(null, 0);

    public Guid? LastSessionId => _snapshot.SessionId;
    public int SkippedChunkCount => _snapshot.SkippedChunkCount;
    public VideoFrame? LatestFrame => _session?.Service.LatestFrame;
    public IReadOnlyList<(int Index, FaceBox Box)> LatestFaces =>
        _session?.Service.LatestFaces ?? Array.Empty<(int, FaceBox)>();
    public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult chunk) =>
        _session?.Service.FacesFor(chunk) ?? _snapshot.Faces?.Invoke(chunk) ?? Array.Empty<FaceWindowSummary>();
    public AnalysisRunSnapshot CaptureSnapshot() => _snapshot;

    public async Task WarmUpAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await PrepareAsync(ct).ConfigureAwait(false);
            await Task.Run(() => _session!.Service.WarmUpAsync(_lease!.Token)).ConfigureAwait(false);
            _lease!.Token.ThrowIfCancellationRequested();
        }
        catch
        {
            await ReleaseAsync().ConfigureAwait(false);
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await PrepareAsync(ct).ConfigureAwait(false);
            using var runToken = CancellationTokenSource.CreateLinkedTokenSource(ct, _lease!.Token);
            runToken.Token.ThrowIfCancellationRequested();
            return await Task.Run(() => _session!.Service.RunAsync(progress, runToken.Token)).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (_session is not null) _snapshot = _session.Service.CaptureSnapshot();
            }
            finally
            {
                try { await ReleaseAsync().ConfigureAwait(false); }
                finally { _lifecycle.Release(); }
            }
        }
    }

    private async Task PrepareAsync(CancellationToken ct)
    {
        _shutdown.Token.ThrowIfCancellationRequested();
        ct.ThrowIfCancellationRequested();
        if (_session is not null) return;
        _sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        try
        {
            _lease = await queue.AcquireAsync(_sessionCancellation.Token).ConfigureAwait(false);
            _snapshot = new(null, 0);
            _session = createSession();
        }
        catch
        {
            _lease?.Dispose(); _lease = null;
            _sessionCancellation.Dispose(); _sessionCancellation = null;
            throw;
        }
    }

    private async Task ReleaseAsync()
    {
        var session = _session;
        _session = null;
        try { if (session is not null) await session.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            _lease?.Dispose(); _lease = null;
            _sessionCancellation?.Dispose(); _sessionCancellation = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        // Cancel first, then wait: native handles must never be disposed during inference.
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await ReleaseAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }
}
