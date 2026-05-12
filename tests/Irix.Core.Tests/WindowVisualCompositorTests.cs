using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class WindowVisualCompositorTests
{
    [Fact]
    public async Task RenderAsync_updates_content_and_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var compositor = new WindowVisualCompositor(window);
        var resources = new FrameDrawingResources();
        var increment = resources.AddText("Increment");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.FillRect,
                    Rect: new DrawRect(16, 120, 140, 40),
                    Color: DrawColor.Opaque(52, 120, 246)),
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 120, 140, 40),
                        Resource: textStyle,
                    Text: increment,
                    Color: DrawColor.Opaque(255, 255, 255))
            ]), 2),
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")],
                    resources);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.Single(window.LastElements);
        Assert.True(compositor.TryGetActionIdAt(16, 120, out var actionId));
        Assert.Equal("Increment", actionId);
        Assert.True(compositor.TryGetActionIdAt(155, 159, out actionId));
        Assert.Equal("Increment", actionId);
    }

    [Fact]
    public async Task TryGetActionIdAt_uses_inclusive_top_left_and_exclusive_bottom_right_bounds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var compositor = new WindowVisualCompositor(window);
        var resources = new FrameDrawingResources();
        var increment = resources.AddText("Increment");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.FillRect,
                    Rect: new DrawRect(16, 120, 140, 40),
                    Color: DrawColor.Opaque(52, 120, 246)),
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 120, 140, 40),
                        Resource: textStyle,
                    Text: increment,
                    Color: DrawColor.Opaque(255, 255, 255))
            ]), 2),
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")],
                    resources);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.False(compositor.TryGetActionIdAt(15, 120, out _));
        Assert.False(compositor.TryGetActionIdAt(16, 119, out _));
        Assert.False(compositor.TryGetActionIdAt(156, 120, out _));
        Assert.False(compositor.TryGetActionIdAt(16, 160, out _));
    }

    [Fact]
    public async Task RenderAsync_with_empty_batch_clears_content_and_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var compositor = new WindowVisualCompositor(window);
        var resources = new FrameDrawingResources();
        var increment = resources.AddText("Increment");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();

        using var firstFrame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.FillRect,
                    Rect: new DrawRect(16, 120, 140, 40),
                    Color: DrawColor.Opaque(52, 120, 246)),
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 120, 140, 40),
                        Resource: textStyle,
                    Text: increment,
                    Color: DrawColor.Opaque(255, 255, 255))
            ]), 2),
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Increment")],
                    resources);

        await compositor.RenderAsync(firstFrame, cancellationToken);
        Assert.True(compositor.TryGetActionIdAt(32, 140, out _));

        using var emptyFrame = new RenderFrameBatch(new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0), []);

        await compositor.RenderAsync(emptyFrame, cancellationToken);

        Assert.Empty(window.LastElements);
        Assert.False(compositor.TryGetActionIdAt(32, 140, out _));
    }

    private sealed class FakeWindow(ScreenRegion region) : INativeWindow
    {
        public IReadOnlyList<WindowContentElement> LastElements { get; private set; } = [];

        public string Title => "Test";

        public ScreenRegion Region { get; set; } = region;

        public bool ExternalRenderingEnabled { get; set; }

        public nint Handle => nint.Zero;

        public void Dispose()
        {
        }

        public void RunMessageLoop()
        {
        }

        public void SetContentElements(IReadOnlyList<WindowContentElement> elements)
        {
            LastElements = [.. elements];
        }

        public void Show()
        {
        }

        public event Action<int, int>? SizeChanged { add { } remove { } }
        public event Action<DisplayScale>? DpiChanged { add { } remove { } }
    }
}
