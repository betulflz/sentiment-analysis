using SentimentAnalysis.Application.Runtime;

namespace SentimentAnalysis.Desktop.Tests;

public sealed class ModelWorkShutdownTests
{
    [Fact]
    public async Task Cleanup_error_still_drains_work_and_disposes_app_services()
    {
        await using var queue = new ModelWorkQueue();
        using var active = await queue.AcquireAsync(default);
        var order = new List<string>();
        var owner = new FailingOwner(() => { active.Dispose(); order.Add("models"); });
        await Assert.ThrowsAsync<AggregateException>(() => ModelWorkShutdown.RunAsync(queue, owner,
            () => { order.Add("ui"); return Task.CompletedTask; },
            () => { order.Add("app"); return ValueTask.CompletedTask; }));
        Assert.Equal(new[] { "models", "ui", "app" }, order);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.AcquireAsync(default));
    }

    [Fact]
    public async Task App_services_remain_alive_until_background_models_finish()
    {
        await using var queue = new ModelWorkQueue();
        using var background = await queue.AcquireAsync(default);
        var closed = false;
        var shutdown = ModelWorkShutdown.RunAsync(queue, new EmptyOwner(),
            () => Task.CompletedTask, () => { closed = true; return ValueTask.CompletedTask; });
        Assert.True(background.Token.IsCancellationRequested);
        Assert.False(closed);
        Assert.False(shutdown.IsCompleted);
        background.Dispose();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(closed);
    }

    private sealed class EmptyOwner : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingOwner(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            release();
            throw new InvalidOperationException("native cleanup failed");
        }
    }
}
