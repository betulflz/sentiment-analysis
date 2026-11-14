using SentimentAnalysis.Application.Runtime;
using SentimentAnalysis.Core;

namespace SentimentAnalysis.Desktop.Tests;

public sealed class ModelLifetimeTests
{
    private static readonly IProgress<ChunkResult> Progress = new Progress<ChunkResult>();

    [Fact]
    public async Task Queue_serializes_jobs_and_canceled_waiter_does_not_take_slot()
    {
        await using var queue = new ModelWorkQueue();
        using var first = await queue.AcquireAsync(default);
        using var cancel = new CancellationTokenSource();
        var waiting = queue.AcquireAsync(cancel.Token);
        Assert.False(waiting.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var next = queue.AcquireAsync(default);
        Assert.False(next.IsCompleted);
        first.Dispose();
        using var second = await next.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Run_releases_models_on_success_and_failure_and_next_run_is_fresh(bool fail)
    {
        await using var queue = new ModelWorkQueue();
        var created = new List<FakeRunner>();
        await using var runner = new ResourceManagedAnalysisRunner(queue, () =>
        {
            var model = new FakeRunner { Fail = fail };
            created.Add(model);
            return new OwnedModelSession<IAnalysisRunner>(model, model);
        });
        await runner.WarmUpAsync(default);
        Assert.Single(created);
        Assert.False(created[0].Disposed);
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Progress, default));
        else await runner.RunAsync(Progress, default);
        Assert.True(created[0].Disposed);
        Assert.Equal(created[0].LastSessionId, runner.LastSessionId);
        Assert.Equal(3, runner.SkippedChunkCount);
        if (fail) await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Progress, default));
        else await runner.RunAsync(Progress, default);
        Assert.Equal(2, created.Count);
        Assert.True(created[1].Disposed);
    }

    [Fact]
    public async Task Warmup_failure_releases_models_and_queue()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner { FailWarmup = true };
        await using var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.WarmUpAsync(default));
        Assert.True(model.Disposed);
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Disposing_prepared_runner_releases_its_reservation()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner();
        var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        await runner.WarmUpAsync(default);
        await runner.DisposeAsync();
        await runner.DisposeAsync();
        Assert.True(model.Disposed);
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shutdown_cancels_active_job_waits_for_it_then_disposes_models()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner { Block = true };
        var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        var run = runner.RunAsync(Progress, default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiting = queue.AcquireAsync(default);
        queue.RequestStop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await runner.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(model.Disposed);
        Assert.False(model.DisposedWhileRunning);
    }

    [Fact]
    public async Task Stop_is_available_during_model_preparation()
    {
        var model = new FakeRunner { BlockWarmup = true };
        var vm = new LiveViewModel(model);
        vm.StartCommand.Execute(null);
        Assert.True(vm.IsPreparingModels);
        Assert.True(vm.StopCommand.CanExecute(null));
        vm.StopCommand.Execute(null);
        await vm.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public async Task Runner_dispose_alone_cancels_active_models_before_releasing_queue()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner { Block = true };
        var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        var run = runner.RunAsync(Progress, default);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await runner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(model.Disposed);
        Assert.False(model.DisposedWhileRunning);
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Canceled_preparation_releases_models_and_slot()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner { BlockWarmup = true };
        await using var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        using var cancel = new CancellationTokenSource();
        var warmup = runner.WarmUpAsync(cancel.Token);
        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => warmup);
        Assert.True(model.Disposed);
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cleanup_failure_still_releases_queue_and_allows_a_fresh_run()
    {
        await using var queue = new ModelWorkQueue();
        var count = 0;
        await using var runner = new ResourceManagedAnalysisRunner(queue, () =>
        {
            var model = new FakeRunner { FailDispose = ++count == 1 };
            return new OwnedModelSession<IAnalysisRunner>(model, model);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(Progress, default));
        await runner.RunAsync(Progress, default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Factory_failure_releases_slot()
    {
        await using var queue = new ModelWorkQueue();
        await using var runner = new ResourceManagedAnalysisRunner(queue,
            () => throw new InvalidOperationException("factory"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.WarmUpAsync(default));
        using var next = await queue.AcquireAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Delayed_report_callbacks_use_metadata_after_models_are_closed()
    {
        await using var queue = new ModelWorkQueue();
        var model = new FakeRunner();
        await using var runner = new ResourceManagedAnalysisRunner(queue,
            () => new OwnedModelSession<IAnalysisRunner>(model, model));
        await runner.RunAsync(Progress, default);
        Assert.True(model.Disposed);
        Assert.Same(model.FaceMetadata, runner.FacesFor(null!));
        Assert.Null(runner.LatestFrame);
        Assert.Empty(runner.LatestFaces);
    }

    private sealed class FakeRunner : IAnalysisRunner, IDisposable
    {
        public bool Fail, FailWarmup, FailDispose, Block, BlockWarmup, Disposed, DisposedWhileRunning;
        private bool _running;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid? LastSessionId { get; } = Guid.NewGuid();
        public int SkippedChunkCount => 3;
        public IReadOnlyList<FaceWindowSummary> FaceMetadata { get; } =
            new[] { new FaceWindowSummary(1, 1, 0, 0, 0, 0, 1) };
        public AnalysisRunSnapshot CaptureSnapshot()
        {
            var metadata = FaceMetadata;
            return new(LastSessionId, SkippedChunkCount, _ => metadata);
        }
        public IReadOnlyList<FaceWindowSummary> FacesFor(ChunkResult chunk)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return FaceMetadata;
        }
        public async Task WarmUpAsync(CancellationToken ct)
        {
            if (FailWarmup) throw new InvalidOperationException("warmup");
            if (BlockWarmup) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
        }
        public async Task<MeetingSummary> RunAsync(IProgress<ChunkResult> progress, CancellationToken ct)
        {
            _running = true;
            try
            {
                Started.TrySetResult();
                if (Block) await Task.Delay(Timeout.Infinite, ct);
                if (Fail) throw new InvalidOperationException("inference");
                return MeetingSummary.CreateEmpty(AnalysisOutcome.NoResult);
            }
            finally { _running = false; }
        }
        public void Dispose()
        {
            DisposedWhileRunning = _running; Disposed = true;
            if (FailDispose) throw new InvalidOperationException("cleanup");
        }
    }
}
