using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Resize stress tests: simulate rapid viewport changes to validate
/// compositor stability, scale consistency, and hit-test correctness.
/// </summary>
public sealed class ResizeStressTests
{
    private readonly VirtualTextArena _arena = new();
    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public async Task Rapid_resize_preserves_scale_consistency(float scaleValue)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);
        var scale = new DisplayScale(scaleValue, scaleValue);

        // Simulate 200 rapid resize events
        for (var i = 0; i < 200; i++)
        {
            var w = 800 + (i % 50) * 10; // 800..1300
            var h = 600 + (i % 30) * 10; // 600..890
            compositor.SetViewport(new PixelRectangle(0, 0, w, h), scale);

            var pipeline = new RenderPipeline();
            var root = VirtualNodeBuilder.Button(_arena, $"Btn{i}", new NodeKey(1),
                VirtualNodeProperty.Action(new ActionId(100)));
            var logicalW = (int)(w / scale.ScaleX);
            var logicalH = (int)(h / scale.ScaleY);
            var viewport = new PixelRectangle(0, 0, logicalW, logicalH);
            using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

            await compositor.RenderAsync(batch, cancellationToken);
        }

        Assert.Equal(200, compositor.RenderCount);
        Assert.Equal(200, backend.ExecuteCount);

        // Backend should have received physical viewport dimensions
        Assert.True(backend.LastFrameContext.Width > 0);
        Assert.True(backend.LastFrameContext.Height > 0);
        Assert.Equal(scale, backend.LastFrameContext.Scale);
    }

    [Fact]
    public async Task Resize_from_large_to_small_to_large_no_crash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        var sizes = new[]
        {
            (1920, 1080),
            (100, 100),
            (3840, 2160),
            (1, 1),
            (1920, 1080),
            (50, 50),
            (2560, 1440),
        };

        foreach (var (w, h) in sizes)
        {
            compositor.SetViewport(new PixelRectangle(0, 0, w, h), DisplayScale.Identity);

            var pipeline = new RenderPipeline();
            var root = VirtualNodeBuilder.Button(_arena, "Btn", new NodeKey(1),
                VirtualNodeProperty.Action(new ActionId(100)));
            var viewport = new PixelRectangle(0, 0, w, h);
            using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

            await compositor.RenderAsync(batch, cancellationToken);
        }

        Assert.Equal(sizes.Length, compositor.RenderCount);
    }

    [Fact]
    public async Task Resize_with_runtime_scale_change_hit_targets_correct()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Start at 100% scale, 1000x800
        var scale = DisplayScale.Identity;
        compositor.SetViewport(new PixelRectangle(0, 0, 1000, 800), scale);

        var pipeline = new RenderPipeline();
        var root = VirtualNodeBuilder.Button(_arena, "Click", new NodeKey(1),
            VirtualNodeProperty.Action(new ActionId(100)));
        var viewport = new PixelRectangle(0, 0, 1000, 800);
        using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(batch, cancellationToken);

        // Hit-test at physical coordinates (100% scale = logical = physical)
        Assert.True(compositor.TryGetActionIdAt(50, 25, out var id1));
        Assert.Equal(new ActionId(100), id1);

        // Change to 150% scale
        scale = new DisplayScale(1.5f, 1.5f);
        compositor.SetViewport(new PixelRectangle(0, 0, 1500, 1200), scale);

        // Layout in logical units (1000x800)
        var viewport2 = new PixelRectangle(0, 0, 1000, 800);
        using var batch2 = pipeline.Build(root, viewport2, textSnapshot: _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(batch2, cancellationToken);

        // Hit-test at physical coordinates (150% scale)
        // Button at logical (16,16,68,32) maps to physical (24,24,102,48)
        Assert.True(compositor.TryGetActionIdAt(50, 30, out var id2));
        Assert.Equal(new ActionId(100), id2);

        // Point outside physical button bounds should miss
        Assert.False(compositor.TryGetActionIdAt(200, 200, out _));
    }

    [Fact]
    public async Task Thousand_resizes_with_scale_change_no_exception()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CountingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        var scales = new[] { 1.0f, 1.25f, 1.5f, 2.0f, 1.0f };

        for (var i = 0; i < 1000; i++)
        {
            var scaleValue = scales[i % scales.Length];
            var scale = new DisplayScale(scaleValue, scaleValue);
            var w = 800 + (i % 100) * 10;
            var h = 600 + (i % 80) * 10;
            compositor.SetViewport(new PixelRectangle(0, 0, w, h), scale);

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

        Assert.Equal(1000, compositor.RenderCount);
        Assert.Equal(1000, backend.ExecuteCount);
    }

    private sealed class CountingBackend : IDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public FrameContext LastFrameContext { get; private set; }

        public void BeginFrame(in FrameContext frameContext)
        {
            LastFrameContext = frameContext;
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public void EndFrame() { }
        public void Dispose() { }
    }
}
