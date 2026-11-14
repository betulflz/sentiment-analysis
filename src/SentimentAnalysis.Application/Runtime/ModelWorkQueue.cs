namespace SentimentAnalysis.Application.Runtime;

/// <summary>One native-model job at a time, including warm-up and resource cleanup.</summary>
public sealed class ModelWorkQueue : IAsyncDisposable
{
    private readonly SemaphoreSlim _slot = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();

    public async Task<Lease> AcquireAsync(CancellationToken ct)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
        try
        {
            await _slot.WaitAsync(linked.Token).ConfigureAwait(false);
            if (linked.IsCancellationRequested)
            {
                _slot.Release();
                linked.Token.ThrowIfCancellationRequested();
            }
            return new Lease(_slot, linked);
        }
        catch { linked.Dispose(); throw; }
    }

    public void RequestStop() => _shutdown.Cancel();

    public async ValueTask DisposeAsync()
    {
        RequestStop();
        // Do not dispose the semaphore: canceled waiters can still be unwinding.
        await _slot.WaitAsync().ConfigureAwait(false);
        _slot.Release();
    }

    public sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _slot;
        private readonly CancellationTokenSource _linked;
        internal Lease(SemaphoreSlim slot, CancellationTokenSource linked)
        { _slot = slot; _linked = linked; }
        public CancellationToken Token => _linked.Token;
        public void Dispose()
        {
            var slot = Interlocked.Exchange(ref _slot, null);
            if (slot is null) return;
            _linked.Dispose();
            slot.Release();
        }
    }
}
