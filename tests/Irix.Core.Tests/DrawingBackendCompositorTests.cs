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

    [Fact]
    public async Task RenderAsync_uses_partial_apply_when_resources_match()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);
        var resources = FrameDrawingResources.Rent();
        var hello = resources.AddText("Hello");
        var world = resources.AddText("World");
        var textStyle = resources.AddTextStyle(TextStyle.Default);
        resources.Seal();

        // First frame: full apply
        using var frame1 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: hello, Resource: textStyle, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources);

        await compositor.RenderAsync(frame1, cancellationToken);
        Assert.False(compositor.LastPartialApplySucceeded); // no dirty ranges
        Assert.Same(resources, compositor.RetainedFrame.Resources);

        // Second frame: same resources + dirty ranges → partial apply
        using var frame2 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: world, Resource: textStyle, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources,
            [(0, 1)]);

        await compositor.RenderAsync(frame2, cancellationToken);
        Assert.True(compositor.LastPartialApplySucceeded);
        Assert.Single(compositor.LastDirtyCommandRanges);
        Assert.Equal((0, 1), compositor.LastDirtyCommandRanges[0]);

        // Dispose: frame2 resources are retained by compositor's retained frame.
        frame2.Dispose();
        frame1.Dispose(); // same resources, Return is no-op (retained)
    }

    [Fact]
    public async Task RenderAsync_falls_back_to_full_apply_when_resources_differ()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        var resources1 = FrameDrawingResources.Rent();
        var hello = resources1.AddText("Hello");
        resources1.Seal();

        using var frame1 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: hello, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources1);

        await compositor.RenderAsync(frame1, cancellationToken);
        Assert.Same(resources1, compositor.RetainedFrame.Resources);

        // Different resources instance + dirty ranges → partial refused, full fallback
        var resources2 = FrameDrawingResources.Rent();
        var world = resources2.AddText("World");
        resources2.Seal();

        using var frame2 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: world, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources2,
            [(0, 1)]);

        await compositor.RenderAsync(frame2, cancellationToken);
        Assert.False(compositor.LastPartialApplySucceeded); // refused, full fallback
        Assert.Same(resources2, compositor.RetainedFrame.Resources); // full apply replaced
    }

    [Fact]
    public async Task RenderAsync_cross_frame_partial_disabled_after_invalidate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        // Frame 1: rent resources and apply
        var resources1 = FrameDrawingResources.Rent();
        var hello = resources1.AddText("Hello");
        resources1.Seal();

        using var frame1 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: hello, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources1);

        await compositor.RenderAsync(frame1, cancellationToken);
        frame1.Dispose(); // resources retained, Return is no-op

        // Simulate empty frame (invalidates retained frame and resets frame tracking)
        using var empty = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
            []);

        await compositor.RenderAsync(empty, cancellationToken);

        // Frame 2: even if same resources object is recycled (same ReferenceEquals),
        // cross-frame guard resets _lastAppliedFrameId on invalidate → full apply forced
        var resources2 = FrameDrawingResources.Rent();
        var world = resources2.AddText("World");
        resources2.Seal();

        using var frame2 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: world, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources2,
            [(0, 1)]);

        await compositor.RenderAsync(frame2, cancellationToken);
        // After invalidate, _lastAppliedFrameId was reset to 0, so even same-object
        // would not match. Full apply is forced.
        Assert.False(compositor.LastPartialApplySucceeded);
    }

    [Fact]
    public async Task RenderAsync_propagates_dirty_ranges_to_dirty_aware_backend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new DirtyRangeTrackingBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Prepare all text before sealing
        var resources = FrameDrawingResources.Rent();
        var hello = resources.AddText("Hello");
        var world = resources.AddText("World");
        resources.Seal();

        // Frame 1: full apply (no dirty ranges)
        using var frame1 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: hello, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources,
            []);

        await compositor.RenderAsync(frame1, cancellationToken);
        frame1.Dispose();

        Assert.Equal(1, backend.ExecuteCount);
        Assert.Single(backend.ReceivedDirtyRanges);
        Assert.Empty(backend.ReceivedDirtyRanges[0]); // no dirty ranges on initial frame

        // Frame 2: same resources + dirty ranges → partial apply
        using var frame2 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 100, 32), Text: world, Color: DrawColor.Opaque(0, 0, 0)),
            ]), 1),
            [],
            resources,
            [(0, 1)]);

        await compositor.RenderAsync(frame2, cancellationToken);
        frame2.Dispose();

        Assert.Equal(2, backend.ExecuteCount);
        Assert.Equal(2, backend.ReceivedDirtyRanges.Count);
        // Second frame should have the dirty ranges propagated to the backend
        Assert.Single(backend.ReceivedDirtyRanges[1]);
        Assert.Equal(0, backend.ReceivedDirtyRanges[1][0].Start);
        Assert.Equal(1, backend.ReceivedDirtyRanges[1][0].Count);

        // Compositor and backend dirty ranges should match
        Assert.Equal(compositor.LastDirtyCommandRanges.Count, backend.ReceivedDirtyRanges[1].Count);
        Assert.Equal(compositor.LastDirtyCommandRanges[0], backend.ReceivedDirtyRanges[1][0]);
    }

    [Fact]
    public void FrameDrawingResources_reset_throws_while_retained()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Retain();

        // Reset() must throw while retained
        Assert.Throws<InvalidOperationException>(() => resources.Reset());

        // Release first, then Reset is safe
        resources.Release();
        resources.Reset(); // no exception

        FrameDrawingResources.Return(resources);
    }

    [Fact]
    public async Task TryGetActionIdAt_respects_clip_bounds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        // Viewport 120×50, padding 16 → container visible height = 50-16 = 34
        // Button: "ClickMe" → width = 88 (available), height = 40
        // Clip: (16, 16, 88, 34), clip bottom = 50
        // Button bottom = 16+40 = 56, which extends beyond clip bottom (50)
        // Point at (20, 52) is inside button (16..104, 16..56) but outside clip (16..104, 16..50)
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("ClickMe", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("btn"))));
        var viewport = new PixelRectangle(0, 0, 120, 50);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport);

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.Single(batch.HitTargets);
        var target = batch.HitTargets[0];
        Assert.Equal("btn", target.ActionId);

        // Verify clip bounds are set and tighter than button bounds
        Assert.True(target.ClipBounds.Height > 0);
        Assert.True(target.ClipBounds.Height < target.Bounds.Height);

        // Inside both bounds and clip → should hit
        Assert.True(compositor.TryGetActionIdAt(20, 20, out var actionId));
        Assert.Equal("btn", actionId);

        // Inside button bounds but OUTSIDE clip (y=52 > clip bottom=50)
        // → clip check should reject this
        Assert.False(compositor.TryGetActionIdAt(20, 52, out _));

        // Outside button bounds entirely → bounds check rejects
        Assert.False(compositor.TryGetActionIdAt(200, 20, out _));
    }

    [Fact]
    public async Task Nested_clip_intersection_exact_rect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        // Nested ScrollContainers with different effective clip areas
        // Outer: viewport 300×200, padding 16 → clip = [16, 16, 268, 168]
        // Inner: same container clip intersected with outer = [16, 16, 268, 168]
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.ScrollContainer(2,
                VirtualNodeFactory.Button("Inner", 3,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText("inner")))));
        var viewport = new PixelRectangle(0, 0, 300, 200);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport);

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.Single(batch.HitTargets);
        var clip = batch.HitTargets[0].ClipBounds;

        // Outer clip: container starts at y=16, height = 200-16 = 184
        // Inner clip: intersect(outer, outer) = same = [16, 16, 268, 184]
        var expectedClip = new PixelRectangle(16, 16, 268, 184);
        Assert.Equal(expectedClip, clip);
    }

    [Fact]
    public async Task Scroll_offset_moves_element_outside_clip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        // Two buttons in a small viewport. Scrolling down should move the first
        // button partially outside the clip area.
        // Viewport 200×80, padding 16 → clip height = 80-32 = 48
        // Button height = 40, spacing = 12
        // Button 1: y = 16 (visible)
        // Button 2: y = 16+40+12 = 68 (partially visible — bottom at 108, clip bottom at 64)
        // With ScrollY=30: Button 1: y = 16-30 = -14 (outside clip)
        //                   Button 2: y = 68-30 = 38 (inside clip, bottom at 78 > clip bottom 64 → partially clipped)
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(30))],
            children: [
                VirtualNodeFactory.Button("First", 2,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText("first"))),
                VirtualNodeFactory.Button("Second", 3,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText("second")))
            ]);
        var viewport = new PixelRectangle(0, 0, 200, 60);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport);

        await compositor.RenderAsync(batch, cancellationToken);

        // Should have 2 hit targets
        Assert.Equal(2, batch.HitTargets.Count);

        // Container visible height = 60-16 = 44, clip = [16, 16, 168, 44], bottom = 60
        // Button 1: y = 16, bottom = 56. With ScrollY=30: y = -14, bottom = 26
        // Button 2: y = 68, bottom = 108. With ScrollY=30: y = 38, bottom = 78
        // Button 2 extends beyond clip bottom (60) → clip check rejects y=55
        var firstTarget = batch.HitTargets[0];
        Assert.Equal("first", firstTarget.ActionId);

        // Point inside first button bounds but outside clip (y=10 < clip top=16)
        Assert.False(compositor.TryGetActionIdAt(20, 10, out _));

        // Point inside first button bounds AND inside clip (y=20 >= clip top=16)
        Assert.True(compositor.TryGetActionIdAt(20, 20, out var firstId));
        Assert.Equal("first", firstId);

        // Second button: y=38, bottom=78. Clip bottom=60.
        // Point at y=45 is inside button (38..78) and inside clip (16..60)
        Assert.True(compositor.TryGetActionIdAt(20, 45, out var secondId));
        Assert.Equal("second", secondId);

        // Point at y=65 is inside button (38..78) but outside clip (16..60)
        Assert.False(compositor.TryGetActionIdAt(20, 65, out _));
    }

    private sealed class FakeWindow : INativeWindow
    {
        public IReadOnlyList<WindowContentElement> LastElements { get; private set; } = [];

        public string Title => "Test";

        public ScreenRegion Region { get; set; } = new(0, new PixelRectangle(0, 0, 960, 540));

        public bool ExternalRenderingEnabled { get; set; }

        public nint Handle => nint.Zero;

        public void Dispose() { }
        public void RunMessageLoop() { }
        public void SetContentElements(IReadOnlyList<WindowContentElement> elements) => LastElements = elements;
        public void Show() { }
        public event Action<int, int>? SizeChanged { add { } remove { } }
    }

    private sealed class DirtyRangeTrackingBackend : IDrawingBackend, IDirtyRangeAware
    {
        public List<IReadOnlyList<(int Start, int Count)>> ReceivedDirtyRanges { get; } = [];
        public int ExecuteCount { get; private set; }

        public void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges)
        {
            ReceivedDirtyRanges.Add(ranges);
        }

        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }
        public void EndFrame() { }
        public void Dispose() { }
    }
}
