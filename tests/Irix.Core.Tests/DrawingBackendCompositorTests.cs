using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class DrawingBackendCompositorTests
{
    private readonly VirtualTextArena _arena = new();
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
    public void BackendClipMode_reports_none_without_capability()
    {
        var compositor = new DrawingBackendCompositor(new PoCDrawingBackend(new FakeWindow()));

        Assert.Equal(DrawingBackendClipMode.None, compositor.BackendClipMode);
    }

    [Fact]
    public void BackendClipMode_reports_backend_capability()
    {
        var compositor = new DrawingBackendCompositor(new ClipCapabilityBackend(DrawingBackendClipMode.Diagnostic));

        Assert.Equal(DrawingBackendClipMode.Diagnostic, compositor.BackendClipMode);
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
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(100))]);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.True(compositor.TryGetActionIdAt(16, 120, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
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
            [new HitTestTarget(new PixelRectangle(16, 120, 140, 40), new ActionId(100))]);

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

        // Second frame: same resources + dirty ranges �?partial apply
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

        // Different resources instance + dirty ranges �?partial refused, full fallback
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
        // cross-frame guard resets _lastAppliedFrameId on invalidate �?full apply forced
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

        // Frame 2: same resources + dirty ranges �?partial apply
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

        // Root clip uses the viewport top while the button still starts after padding.
        // Button: "ClickMe" -> width = 88, height = 40, bounds bottom = 56.
        // Clip: (0, 0, 120, 50), clip bottom = 50.
        // Button bottom = 16+40 = 56, which extends beyond clip bottom (50)
        // Point at (20, 52) is inside button (16..104, 16..56) but outside clip (0..120, 0..50)
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "ClickMe", new NodeKey(2),
                VirtualNodeProperty.Action(new ActionId(100))));
        var viewport = new PixelRectangle(0, 0, 120, 50);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.Single(batch.HitTargets);
        var target = batch.HitTargets[0];
        Assert.Equal(new ActionId(100), target.ActionId);

        Assert.Equal(new PixelRectangle(0, 0, 120, 50), target.ClipBounds);
        Assert.True(target.ClipBounds.Y + target.ClipBounds.Height < target.Bounds.Y + target.Bounds.Height);

        // Inside both bounds and clip �?should hit
        Assert.True(compositor.TryGetActionIdAt(20, 20, out var actionId));
        Assert.Equal(new ActionId(100), actionId);

        // Inside button bounds but OUTSIDE clip (y=52 > clip bottom=50)
        // �?clip check should reject this
        Assert.False(compositor.TryGetActionIdAt(20, 52, out _));

        // Outside button bounds entirely �?bounds check rejects
        Assert.False(compositor.TryGetActionIdAt(200, 20, out _));
    }

    [Fact]
    public async Task Nested_clip_intersection_exact_rect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);

        // Root uses viewport clip; nested containers keep the padded container clip.
        // Inner: [16, 16, 268, 184] intersected with root [0, 0, 300, 200].
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.ScrollContainer(new NodeKey(2),
                VirtualNodeBuilder.Button(_arena, "Inner", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(100)))));
        var viewport = new PixelRectangle(0, 0, 300, 200);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(batch, cancellationToken);

        Assert.Single(batch.HitTargets);
        var clip = batch.HitTargets[0].ClipBounds;

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
        // button partially outside the viewport clip area.
        // Viewport 200×60, root clip = [0, 0, 200, 60]
        // Button height = 40, spacing = 12
        // Button 1: y = 16 (visible)
        // Button 2: y = 16+40+12 = 68
        // With ScrollY=30: Button 1: y = 16-30 = -14 (partially above viewport top)
        //                   Button 2: y = 68-30 = 38 (inside clip, bottom at 78 > clip bottom 60)
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [new VirtualNodeProperty(VirtualPropertyKey.ScrollY, PropertyValue.FromNumber(30))],
            children: [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2),
                    VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3),
                    VirtualNodeProperty.Action(new ActionId(101)))
            ]);
        var viewport = new PixelRectangle(0, 0, 200, 60);
        var pipeline = new RenderPipeline();
        using var batch = pipeline.Build(root, viewport, textSnapshot: _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(batch, cancellationToken);

        // Should have 2 hit targets
        Assert.Equal(2, batch.HitTargets.Count);

        // Root clip = [0, 0, 200, 60], bottom = 60
        // Button 1: y = 16, bottom = 56. With ScrollY=30: y = -14, bottom = 26
        // Button 2: y = 68, bottom = 108. With ScrollY=30: y = 38, bottom = 78
        // Button 2 extends beyond clip bottom (60) �?clip check rejects y=55
        var firstTarget = batch.HitTargets[0];
        Assert.Equal(new ActionId(100), firstTarget.ActionId);
        Assert.Equal(new PixelRectangle(0, 0, 200, 60), firstTarget.ClipBounds);

        // Point inside first button bounds but outside clip (y=-1 < clip top=0)
        Assert.False(compositor.TryGetActionIdAt(20, -1, out _));

        // Point inside first button bounds AND inside viewport clip
        Assert.True(compositor.TryGetActionIdAt(20, 10, out var firstId));
        Assert.Equal(new ActionId(100), firstId);

        // Second button: y=38, bottom=78. Clip bottom=60.
        // Point at y=45 is inside button (38..78) and inside clip (0..60)
        Assert.True(compositor.TryGetActionIdAt(20, 45, out var secondId));
        Assert.Equal(new ActionId(101), secondId);

        // Point at y=65 is inside button (38..78) but outside clip (0..60)
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
        public event Action<DisplayScale>? DpiChanged { add { } remove { } }
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

    private sealed class ClipCapabilityBackend(DrawingBackendClipMode clipMode) : IDrawingBackend, IClipScissorCapability
    {
        public DrawingBackendClipMode ClipMode { get; } = clipMode;

        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }

    [Fact]
    public async Task RenderAsync_recovers_from_device_lost_mid_frame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new DeviceLostBackend();
        var compositor = new DrawingBackendCompositor(backend);

        // Frame 1: normal render
        var resources = FrameDrawingResources.Rent();
        var hello = resources.AddText("Hello");
        resources.Seal();

        using var frame1 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
            ]), 1),
            [],
            resources);

        await compositor.RenderAsync(frame1, cancellationToken);
        Assert.Equal(1, backend.ExecuteCount);
        Assert.False(backend.IsDeviceRemoved);

        // Frame 2: backend throws during Execute (simulating device-lost)
        var resources2 = FrameDrawingResources.Rent();
        resources2.AddText("World");
        resources2.Seal();

        using var frame2 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(0, 255, 0)),
            ]), 1),
            [],
            resources2);

        // Should not throw �?compositor catches and recovers
        await compositor.RenderAsync(frame2, cancellationToken);

        Assert.True(backend.RecoveryAttempted);
        Assert.True(backend.RecoverySucceeded);
        Assert.False(backend.IsDeviceRemoved);
        Assert.Equal(2, backend.ExecuteCount); // Execute was called

        // Frame 3: after recovery, next frame should render normally
        var resources3 = FrameDrawingResources.Rent();
        resources3.AddText("After");
        resources3.Seal();

        using var frame3 = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(0, 0, 255)),
            ]), 1),
            [],
            resources3);

        await compositor.RenderAsync(frame3, cancellationToken);
        Assert.Equal(3, backend.ExecuteCount);
        Assert.False(backend.IsDeviceRemoved);
    }

    [Fact]
    public async Task RenderAsync_propagates_when_recovery_fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new DeviceLostBackend { RecoveryShouldSucceed = false, ThrowOnNextExecute = true };
        var compositor = new DrawingBackendCompositor(backend);

        var resources = FrameDrawingResources.Rent();
        resources.AddText("Hello");
        resources.Seal();

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(255, 0, 0)),
            ]), 1),
            [],
            resources);

        // Backend throws, recovery fails �?exception propagates
        bool threw = false;
        try
        {
            await compositor.RenderAsync(frame, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(backend.RecoveryAttempted, $"Expected RecoveryAttempted=true but got false (ExecuteCount={backend.ExecuteCount}, IsDeviceRemoved={backend.IsDeviceRemoved})");
        Assert.False(backend.RecoverySucceeded);
        Assert.True(threw, "Expected InvalidOperationException to propagate when recovery fails");
    }

    private sealed class DeviceLostBackend : IDrawingBackend, IDeviceRecovery
    {
        private bool _deviceRemoved;

        public int ExecuteCount { get; private set; }
        public bool IsDeviceRemoved => _deviceRemoved;
        public bool RecoveryAttempted { get; private set; }
        public bool RecoverySucceeded { get; private set; }
        public bool RecoveryShouldSucceed { get; set; } = true;
        public bool ThrowOnNextExecute { get; set; }

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
            if (ThrowOnNextExecute || ExecuteCount == 2)
            {
                _deviceRemoved = true;
                ThrowOnNextExecute = true;
                throw new InvalidOperationException("Device removed (simulated)");
            }
        }

        public void EndFrame() { }

        public bool TryRecover()
        {
            RecoveryAttempted = true;
            if (!RecoveryShouldSucceed) return false;

            _deviceRemoved = false;
            ThrowOnNextExecute = false;
            RecoverySucceeded = true;
            return true;
        }

        public void Dispose() { }
    }
}
