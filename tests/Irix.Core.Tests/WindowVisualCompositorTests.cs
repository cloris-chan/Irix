using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
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

        using var batch = new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 140, 40),
                Color: DrawColor.Opaque(52, 120, 246),
                Metadata: "Increment"),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 140, 40),
                Text: "Increment",
                Color: DrawColor.Opaque(255, 255, 255),
                Metadata: "Increment")
        ]), 2);

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.Single(window.LastElements);
        Assert.True(compositor.TryGetActionAt(16, 120, out var action));
        Assert.Equal("Increment", action);
        Assert.True(compositor.TryGetActionAt(155, 159, out action));
        Assert.Equal("Increment", action);
    }

    [Fact]
    public async Task TryGetActionAt_uses_inclusive_top_left_and_exclusive_bottom_right_bounds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var compositor = new WindowVisualCompositor(window);

        using var batch = new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 140, 40),
                Color: DrawColor.Opaque(52, 120, 246),
                Metadata: "Increment"),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 140, 40),
                Text: "Increment",
                Color: DrawColor.Opaque(255, 255, 255),
                Metadata: "Increment")
        ]), 2);

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.False(compositor.TryGetActionAt(15, 120, out _));
        Assert.False(compositor.TryGetActionAt(16, 119, out _));
        Assert.False(compositor.TryGetActionAt(156, 120, out _));
        Assert.False(compositor.TryGetActionAt(16, 160, out _));
    }

    [Fact]
    public async Task RenderAsync_with_empty_batch_clears_content_and_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow(new ScreenRegion(0, new PixelRectangle(0, 0, 960, 540)));
        var compositor = new WindowVisualCompositor(window);

        using var firstBatch = new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 120, 140, 40),
                Color: DrawColor.Opaque(52, 120, 246),
                Metadata: "Increment"),
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 120, 140, 40),
                Text: "Increment",
                Color: DrawColor.Opaque(255, 255, 255),
                Metadata: "Increment")
        ]), 2);

        await compositor.RenderAsync(firstBatch, cancellationToken);
        Assert.True(compositor.TryGetActionAt(32, 140, out _));

        using var emptyBatch = new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0);

        await compositor.RenderAsync(emptyBatch, cancellationToken);

        Assert.Empty(window.LastElements);
        Assert.False(compositor.TryGetActionAt(32, 140, out _));
    }

    private sealed class FakeWindow(ScreenRegion region) : INativeWindow
    {
        public IReadOnlyList<WindowContentElement> LastElements { get; private set; } = [];

        public string Title => "Test";

        public ScreenRegion Region => region;

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
    }
}
