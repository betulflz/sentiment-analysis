using SentimentAnalysis.Application.Runtime;

namespace SentimentAnalysis.Desktop;

/// <summary>Cancel, await native work, then close shared services even if a cleanup step fails.</summary>
public static class ModelWorkShutdown
{
    public static async Task RunAsync(ModelWorkQueue queue, IAsyncDisposable analysisOwner,
        Func<Task> waitForLiveUi, Func<ValueTask> disposeAppServices)
    {
        var errors = new List<Exception>();
        async Task Attempt(Func<Task> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { errors.Add(ex); }
        }

        await Attempt(() => { queue.RequestStop(); return Task.CompletedTask; }).ConfigureAwait(false);
        // A prepared-only analysis owns the slot too, so release it before draining the queue.
        await Attempt(() => analysisOwner.DisposeAsync().AsTask()).ConfigureAwait(false);
        await Attempt(waitForLiveUi).ConfigureAwait(false);
        await Attempt(() => queue.DisposeAsync().AsTask()).ConfigureAwait(false);
        await Attempt(() => disposeAppServices().AsTask()).ConfigureAwait(false);
        if (errors.Count > 0) throw new AggregateException("Application cleanup failed.", errors);
    }
}
