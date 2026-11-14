namespace SentimentAnalysis.Application.Runtime;

/// <summary>Owns one job's model graph. Native disposal runs away from the UI thread.</summary>
public sealed class OwnedModelSession<T>(T service, IDisposable owner) : IAsyncDisposable
{
    private IDisposable? _owner = owner;
    public T Service { get; } = service;

    public async ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref _owner, null);
        if (current is not null)
            await Task.Run(current.Dispose).ConfigureAwait(false);
    }
}
