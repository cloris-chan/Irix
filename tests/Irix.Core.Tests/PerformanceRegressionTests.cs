using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

[Trait("Category", "Performance")]
public sealed class PerformanceRegressionTests
{
    [Fact]
    public async Task Mock_backend_frame_timing_stays_under_baseline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var backend = new CountingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetViewport(new PixelRectangle(0, 0, 960, 540), DisplayScale.Identity);

        const int frameCount = 180;
        for (var i = 0; i < frameCount; i++)
        {
            using var frame = new RenderFrameBatch(
                new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
                [
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 960, 540), Color: DrawColor.Opaque(24, 32, 40)),
                    new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(24 + i % 12, 24, 180, 48), Color: DrawColor.Opaque(72, 120, 180))
                ]), 2),
                [new HitTestTarget(new PixelRectangle(24, 24, 180, 48), new ActionId(100))]);

            await compositor.RenderAsync(frame, cancellationToken);
        }

        Assert.Equal(frameCount, compositor.RenderCount);
        Assert.Equal(frameCount, backend.BeginFrameCount);
        Assert.Equal(frameCount, backend.ExecuteCount);
        Assert.Equal(frameCount, backend.EndFrameCount);
        Assert.True(compositor.AverageFrameTimeUs < 20_000, $"Mock backend average frame time {compositor.AverageFrameTimeUs}us exceeded 20,000us baseline");
    }

    [Fact]
    public void FrameDrawingResources_warm_pool_allocation_stays_under_baseline()
    {
        for (var i = 0; i < 8; i++)
        {
            var resources = FrameDrawingResources.Rent();
            resources.AddText("Warm text cache allocation baseline");
            resources.Seal();
            FrameDrawingResources.Return(resources);
        }

        const int frameCount = 1_000;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < frameCount; i++)
        {
            var resources = FrameDrawingResources.Rent();
            resources.AddText("Stable text cache allocation baseline");
            resources.Seal();
            FrameDrawingResources.Return(resources);
        }

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(allocatedBytes < 2_000_000, $"FrameDrawingResources warm pool allocated {allocatedBytes:N0} bytes over {frameCount} frames");
    }

    private sealed class CountingBackend : IDrawingBackend
    {
        public int BeginFrameCount { get; private set; }

        public int ExecuteCount { get; private set; }

        public int EndFrameCount { get; private set; }

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
            Assert.Equal(2, commands.Length);
        }

        public void EndFrame()
        {
            EndFrameCount++;
        }

        public void Dispose()
        {
        }
    }
}