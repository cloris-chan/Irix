using Irix.Drawing;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Soak tests: run many frames to validate resource stability, counter accuracy,
/// and absence of monotonic memory growth.
/// </summary>
public sealed class SoakTests
{
    [Fact]
    public async Task Thousand_frames_render_count_matches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        for (var i = 0; i < 1000; i++)
        {
            var resources = FrameDrawingResources.Rent();
            var text = resources.AddText($"Frame {i}");
            resources.Seal();

            using var frame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
                [
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
                    new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: text, Color: DrawColor.Opaque(0, 0, 0)),
                ]), 2),
                [],
                resources);

            await compositor.RenderAsync(frame, cancellationToken);
            frame.Dispose();
        }

        Assert.Equal(1000, compositor.RenderCount);
        Assert.Equal(1000, backend.ExecuteCount);
        Assert.Equal(0, compositor.EmptyFrameCount);
    }

    [Fact]
    public async Task Thousand_frames_empty_and_nonempty_interleaved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        for (var i = 0; i < 500; i++)
        {
            // Non-empty frame
            var resources = FrameDrawingResources.Rent();
            resources.AddText($"Frame {i}");
            resources.Seal();

            using var frame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
                [
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
                ]), 1),
                [],
                resources);

            await compositor.RenderAsync(frame, cancellationToken);
            frame.Dispose();

            // Empty frame
            using var empty = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
                []);

            await compositor.RenderAsync(empty, cancellationToken);
        }

        Assert.Equal(500, compositor.RenderCount);
        Assert.Equal(500, compositor.EmptyFrameCount);
        Assert.Equal(500, backend.ExecuteCount);
    }

    [Fact]
    public async Task Thousand_frames_memory_stable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Warmup: run 100 frames to stabilize JIT and pool
        for (var i = 0; i < 100; i++)
        {
            var resources = FrameDrawingResources.Rent();
            resources.AddText($"Warmup {i}");
            resources.Seal();

            using var frame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
                [
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
                ]), 1),
                [],
                resources);
            await compositor.RenderAsync(frame, cancellationToken);
            frame.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(false);

        // Run 1000 frames
        for (var i = 0; i < 1000; i++)
        {
            var resources = FrameDrawingResources.Rent();
            resources.AddText($"Frame {i}");
            resources.Seal();

            using var frame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
                [
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
                ]), 1),
                [],
                resources);
            await compositor.RenderAsync(frame, cancellationToken);
            frame.Dispose();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryAfter = GC.GetTotalMemory(false);

        Assert.Equal(1100, compositor.RenderCount);

        // Memory should not grow more than 50% (generous threshold for CI stability)
        var growth = memoryAfter - memoryBefore;
        Assert.True(growth < memoryBefore / 2,
            $"Memory grew {growth:N0} bytes (before={memoryBefore:N0}, after={memoryAfter:N0}). Possible leak.");
    }

    private sealed class CountingBackend : IDrawingBackend
    {
        public int ExecuteCount { get; private set; }

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public void EndFrame() { }
        public void Dispose() { }
    }
}
