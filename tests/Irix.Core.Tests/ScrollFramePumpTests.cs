using System.Collections.Concurrent;
using Irix.Poc;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ScrollFramePumpTests
{
    [Fact]
    public async Task First_pending_delta_dispatches_scroll_frame_with_zero_dt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pump = new ScrollFramePump();
        var frames = new ConcurrentQueue<CounterMessage.ScrollFrame>();

        pump.AddPendingPixels(54);

        await pump.RunUntilIdleAsync(
            (frame, _) =>
            {
                frames.Enqueue(frame);
                return Task.CompletedTask;
            },
            () => ScrollState.Default,
            cancellationToken);

        var frame = Assert.Single(frames);
        Assert.Equal(ScrollDeltaUnit.Pixel, frame.Delta.Unit);
        Assert.Equal(54, frame.Delta.Value);
        Assert.Equal(0, frame.DeltaTime);
        Assert.Equal(1, pump.DispatchedFrameCount);
        Assert.Equal(54, pump.DrainedPixels);
        Assert.Equal(0, pump.LastDt);
        Assert.True(pump.RenderWaitMs >= 0);
    }

    [Fact]
    public async Task Burst_input_coalesces_while_render_completion_is_pending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pump = new ScrollFramePump();
        var renderGate = new BlockingRenderGate();
        var frames = new ConcurrentQueue<CounterMessage.ScrollFrame>();

        pump.AddPendingPixels(54);
        var runTask = pump.RunUntilIdleAsync(
            async (frame, token) =>
            {
                frames.Enqueue(frame);
                await renderGate.WaitForRenderCompletionAsync(token);
            },
            () => ScrollState.Default,
            cancellationToken);

        await WaitForConditionAsync(() => frames.Count == 1 && pump.IsFrameQueued, cancellationToken);

        for (var i = 0; i < 100; i++)
        {
            pump.AddPendingPixels(13.5);
        }

        await Task.Delay(50, cancellationToken);
        Assert.Single(frames);
        Assert.True(pump.IsFrameQueued);

        renderGate.Release();
        await WaitForConditionAsync(() => frames.Count == 2, cancellationToken);

        Assert.True(frames.TryDequeue(out var firstFrame));
        Assert.True(frames.TryDequeue(out var coalescedFrame));
        Assert.Equal(54, firstFrame.Delta.Value);
        Assert.Equal(1350, coalescedFrame.Delta.Value);
        Assert.True(coalescedFrame.DeltaTime >= 0);
        Assert.Equal(2, pump.DispatchedFrameCount);
        Assert.Equal(2, renderGate.WaitCallCount);
        Assert.Equal(1350, pump.DrainedPixels);

        renderGate.Release();
        await runTask.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task Delta_time_includes_render_completion_wait()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pump = new ScrollFramePump();
        var renderGate = new BlockingRenderGate();
        var frames = new ConcurrentQueue<CounterMessage.ScrollFrame>();
        var scrollState = ScrollState.Default;

        pump.AddPendingPixels(54);
        var runTask = pump.RunUntilIdleAsync(
            async (frame, token) =>
            {
                frames.Enqueue(frame);
                scrollState = frames.Count == 1
                    ? new ScrollState { TargetPosition = 54, Position = 0, IsAnimating = true }
                    : ScrollState.Default;
                await renderGate.WaitForRenderCompletionAsync(token);
            },
            () => scrollState,
            cancellationToken);

        await WaitForConditionAsync(() => frames.Count == 1 && pump.IsFrameQueued, cancellationToken);
        await Task.Delay(80, cancellationToken);
        renderGate.Release();

        await WaitForConditionAsync(() => frames.Count == 2, cancellationToken);

        Assert.True(frames.TryDequeue(out var firstFrame));
        Assert.True(frames.TryDequeue(out var secondFrame));
        Assert.Equal(0, firstFrame.DeltaTime);
        Assert.True(secondFrame.DeltaTime >= 0.05, $"Expected dt to include render wait, got {secondFrame.DeltaTime:F4}s.");
        Assert.Equal(secondFrame.DeltaTime, pump.LastDt);
        Assert.True(pump.RenderWaitMs >= 0);

        renderGate.Release();
        await runTask.WaitAsync(cancellationToken);
    }

    private sealed class BlockingRenderGate
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _pending = new();
        private int _waitCallCount;

        public int WaitCallCount => Volatile.Read(ref _waitCallCount);

        public ValueTask WaitForRenderCompletionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waitCallCount);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(completion);
            return new ValueTask(completion.Task.WaitAsync(cancellationToken));
        }

        public void Release()
        {
            if (_pending.TryDequeue(out var completion))
            {
                completion.TrySetResult();
            }
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}