using System.Collections.Concurrent;
using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Validates that concurrent input dispatch and rendering do not deadlock or lose frames.
/// Tests the ScrollFramePump + CompositorLoop interaction pattern: input arrives on one
/// thread while render completes on another.
/// </summary>
public sealed class ConcurrentInputRenderTests
{
    [Fact]
    public async Task Sequential_scroll_frames_render_without_deadlock()
    {
        // Simulates the actual pattern: ScrollFramePump coalesces input,
        // CompositorLoop renders frames one at a time.
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new ConcurrentTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        for (var i = 0; i < 500; i++)
        {
            var scrollY = i * 3;
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("Btn", 2,
                        new VirtualNodeAttribute("ActionId", AttributeValue.FromText("btn"))),
                    VirtualNodeFactory.Text($"Frame {i}", 3),
                ]);

            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);
            await compositor.RenderAsync(batch, cancellationToken);
        }

        Assert.Equal(500, compositor.RenderCount);
        Assert.Equal(500, backend.ExecuteCount);
    }

    [Fact]
    public async Task ScrollFramePump_dispatches_while_render_happens()
    {
        // Simulates ScrollFramePump coalescing scroll input while render is in progress.
        // The pump accumulates pending pixels and dispatches one frame per render cycle.
        var cancellationToken = TestContext.Current.CancellationToken;
        var pump = new ScrollFramePump();
        var frames = new ConcurrentQueue<CounterMessage.ScrollFrame>();
        var renderGate = new ManualResetEventSlim(true);

        // Add initial scroll delta
        pump.AddPendingPixels(50);

        var runTask = pump.RunUntilIdleAsync(
            async (frame, token) =>
            {
                frames.Enqueue(frame);
                // Simulate render taking some time
                await Task.Delay(5, token);
            },
            () => ScrollState.Default,
            cancellationToken);

        // While first frame is rendering, add more scroll input
        await Task.Delay(2, cancellationToken);
        for (var i = 0; i < 50; i++)
        {
            pump.AddPendingPixels(10);
        }

        await runTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        // All input should be coalesced into frames
        Assert.True(frames.Count >= 1, "At least one scroll frame should be dispatched");
        Assert.True(pump.DrainedPixels > 0, "Pixels should be drained");
    }

    [Fact]
    public async Task Rapid_scroll_coalescing_preserves_total_pixels()
    {
        // Simulates rapid mouse wheel: many small deltas should coalesce
        // into fewer frames, preserving total pixel count.
        var cancellationToken = TestContext.Current.CancellationToken;
        var pump = new ScrollFramePump();
        var frames = new ConcurrentQueue<CounterMessage.ScrollFrame>();

        // Simulate 100 rapid wheel ticks (like fast scrolling)
        for (var i = 0; i < 100; i++)
        {
            pump.AddPendingPixels(13.5);
        }

        await pump.RunUntilIdleAsync(
            (frame, _) =>
            {
                frames.Enqueue(frame);
                return Task.CompletedTask;
            },
            () => ScrollState.Default,
            cancellationToken);

        // Total drained should equal total input
        Assert.Equal(1350, pump.DrainedPixels);
        Assert.True(frames.Count >= 1, "At least one frame dispatched");
        Assert.Equal(pump.DrainedPixels, frames.Sum(f => f.Delta.Value));
    }

    [Fact]
    public async Task Concurrent_scroll_input_add_pending_pixels_is_thread_safe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // Verify that AddPendingPixels is safe to call from multiple threads.
        // In the real app, scroll events arrive on the input thread while
        // the pump loop runs on another thread.
        var pump = new ScrollFramePump();
        var tasks = new List<Task>();

        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 100; j++)
                {
                    pump.AddPendingPixels(1.0);
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        // Total pending should be 1000 (10 threads × 100 × 1.0px)
        Assert.Equal(1000, pump.PendingPixels, 0.01);
    }

    [Fact]
    public async Task Multiple_render_cycles_with_scroll_no_frame_loss()
    {
        // Simulates the full cycle: scroll input → render → scroll input → render
        // repeated many times. Each cycle should produce a frame.
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new ConcurrentTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        for (var cycle = 0; cycle < 100; cycle++)
        {
            var scrollY = cycle * 10;
            var root = new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
                children:
                [
                    VirtualNodeFactory.Button("CycleBtn", 2,
                        new VirtualNodeAttribute("ActionId", AttributeValue.FromText("cycle"))),
                ]);

            var viewport = new PixelRectangle(0, 0, 960, 540);
            var pipeline = new RenderPipeline();
            using var batch = pipeline.Build(root, viewport);
            await compositor.RenderAsync(batch, cancellationToken);
        }

        Assert.Equal(100, compositor.RenderCount);
        Assert.Equal(100, backend.ExecuteCount);
    }

    private sealed class ConcurrentTrackingBackend : IDrawingBackend
    {
        public int ExecuteCount => _executeCount;
        private int _executeCount;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            Interlocked.Increment(ref _executeCount);
        }

        public void EndFrame() { }
        public void Dispose() { }
    }
}
