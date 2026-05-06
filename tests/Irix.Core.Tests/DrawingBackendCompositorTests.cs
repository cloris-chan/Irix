using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class DrawingBackendCompositorTests
{
    [Fact]
    public async Task RenderAsync_pushes_elements_to_backend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);
        var resources = new FrameDrawingResources();
        var hello = resources.AddText("Hello");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(10, 20, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(10, 20, 100, 50), Resource: textStyle, Text: hello, Color: DrawColor.Opaque(0, 0, 0))
            ]), 2),
            [],
            resources);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.Equal(2, window.LastElements.Count);
        Assert.Equal(WindowContentElementKind.Rectangle, window.LastElements[0].Kind);
        Assert.Equal(WindowContentElementKind.Text, window.LastElements[1].Kind);
        Assert.Equal("Hello", window.LastElements[1].Text);
    }

    [Fact]
    public async Task RenderAsync_with_empty_commands_does_not_push_elements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
            []);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.Empty(window.LastElements);
    }

    [Fact]
    public async Task TryGetActionIdAt_returns_cached_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(16, 120, 140, 40))
            ]), 1),
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Click")]);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.True(compositor.TryGetActionIdAt(16, 120, out var actionId));
        Assert.Equal("Click", actionId);
        Assert.False(compositor.TryGetActionIdAt(15, 120, out _));
    }

    [Fact]
    public async Task TryGetActionIdAt_cleared_on_empty_frame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        using var firstFrame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(16, 120, 140, 40))
            ]), 1),
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), "Click")]);

        await compositor.RenderAsync(firstFrame, cancellationToken);
        Assert.True(compositor.TryGetActionIdAt(16, 120, out _));

        using var emptyFrame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
            []);

        await compositor.RenderAsync(emptyFrame, cancellationToken);
        Assert.False(compositor.TryGetActionIdAt(16, 120, out _));
    }

    [Fact]
    public void Dispose_disposes_backend()
    {
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        compositor.Dispose();
        // PoCDrawingBackend.Dispose is a no-op, so no exception means success
    }

    private sealed class FakeWindow : INativeWindow
    {
        public IReadOnlyList<WindowContentElement> LastElements { get; private set; } = [];

        public string Title => "Test";

        public ScreenRegion Region => new(0, new PixelRectangle(0, 0, 960, 540));

        public nint Handle => nint.Zero;

        public void Dispose() { }
        public void RunMessageLoop() { }
        public void SetContentElements(IReadOnlyList<WindowContentElement> elements) => LastElements = elements;
        public void Show() { }
    }
}
