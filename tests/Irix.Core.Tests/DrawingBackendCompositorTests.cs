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
        Assert.Equal("Hello", window.LastTextResolver.Resolve(window.LastElements[1].Text).ToString());
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
    public async Task TryGetActionIdAtPhysicalPixel_returns_cached_hit_targets()
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

        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(16, 120, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(15, 120, out _));
    }

    [Fact]
    public async Task TryGetActionIdAtLogicalPixel_returns_topmost_hit_target_by_paint_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);
        var bounds = new PixelRectangle(16, 120, 140, 40);

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(16, 120, 140, 40)),
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(16, 120, 140, 40))
            ]), 2),
            [
                new HitTestTarget(bounds, new ActionId(100), default, 0, 1),
                new HitTestTarget(bounds, new ActionId(200), default, 1, 1)
            ]);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.True(compositor.TryGetActionIdAtLogicalPixel(24, 128, out var actionId));
        Assert.Equal(new ActionId(200), actionId);
    }

    [Fact]
    public void CompositorHitTestSnapshot_returns_local_coordinates_after_composition_transform()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(16, 20, 140, 40),
                new ActionId(100),
                default,
                0,
                1)
        };
        var layer = new CompositionLayer(
            new CompositionLayerId(1),
            0,
            1,
            new CompositionTransform(20, 8),
            CompositionOpacity.Opaque);
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 1, new CompositionFrame(layer));

        Assert.True(snapshot.TryHitTestLogicalPixel(36, 28, out var result));
        Assert.Equal(new ActionId(100), result.ActionId);
        Assert.Equal(16f, result.LocalX);
        Assert.Equal(20f, result.LocalY);
        Assert.Equal(new CompositionLayerId(1), result.LayerId);
        Assert.True(result.MappedThroughComposition);
        Assert.False(snapshot.TryHitTestLogicalPixel(16, 20, out _));
    }

    [Fact]
    public void CompositorHitTestSnapshot_marks_fixed_clip_remap_when_presented_layer_hits_inside_clip()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(100),
                default,
                0,
                1)
        };
        var layer = new CompositionLayer(
            new CompositionLayerId(1),
            0,
            1,
            new CompositionTransform(0, 8),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60));
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 1, new CompositionFrame(layer));

        Assert.True(snapshot.TryHitTestLogicalPixel(24, 28, out var result));
        Assert.Equal(new ActionId(100), result.ActionId);
        Assert.Equal(24f, result.LocalX);
        Assert.Equal(20f, result.LocalY);
        Assert.Equal(new CompositionLayerId(1), result.LayerId);
        Assert.Equal(1, result.AppliedLayerCount);
        Assert.True(result.MappedThroughComposition);
        Assert.True(result.MappedThroughFixedClip);
    }

    [Fact]
    public void CompositorHitTestSnapshot_maps_nested_composition_layers_to_source_local_coordinates()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(100),
                default,
                0,
                1)
        };
        var layers = new[]
        {
            new CompositionLayer(
                new CompositionLayerId(10),
                0,
                1,
                new CompositionTransform(12, 5),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(11),
                0,
                1,
                new CompositionTransform(0, 20),
                CompositionOpacity.Opaque,
                CompositionClipMode.Fixed,
                new DrawRect(30, 40, 160, 24))
        };
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 1, CompositionFrame.FromLayers(layers));

        Assert.True(snapshot.TryHitTestLogicalPixel(36, 49, out var result));
        Assert.Equal(new ActionId(100), result.ActionId);
        Assert.Equal(24f, result.LocalX);
        Assert.Equal(24f, result.LocalY);
        Assert.Equal(2, result.AppliedLayerCount);
        Assert.True(result.MappedThroughComposition);
        Assert.True(result.MappedThroughFixedClip);
    }

    [Fact]
    public void CompositorHitTestSnapshot_applies_only_layers_that_cover_hit_target_command_range()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(100),
                default,
                0,
                1),
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(200),
                default,
                1,
                1)
        };
        var layers = new[]
        {
            new CompositionLayer(
                new CompositionLayerId(10),
                0,
                1,
                new CompositionTransform(60, 0),
                CompositionOpacity.Opaque),
            new CompositionLayer(
                new CompositionLayerId(11),
                1,
                1,
                new CompositionTransform(0, 60),
                CompositionOpacity.Opaque,
                CompositionClipMode.Fixed,
                new DrawRect(0, 70, 200, 50))
        };
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 2, CompositionFrame.FromLayers(layers));

        Assert.True(snapshot.TryHitTestLogicalPixel(80, 20, out var result));
        Assert.Equal(new ActionId(100), result.ActionId);
        Assert.Equal(20f, result.LocalX);
        Assert.Equal(20f, result.LocalY);
        Assert.Equal(new CompositionLayerId(10), result.LayerId);
        Assert.Equal(1, result.AppliedLayerCount);
        Assert.True(result.MappedThroughComposition);
        Assert.False(result.MappedThroughFixedClip);
    }

    [Fact]
    public void CompositorHitTestSnapshot_rejects_nested_fixed_clip_before_local_bounds_accepts_target()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(100),
                default,
                0,
                1)
        };
        var layers = new[]
        {
            new CompositionLayer(
                new CompositionLayerId(10),
                0,
                1,
                new CompositionTransform(12, 5),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(11),
                0,
                1,
                new CompositionTransform(0, 20),
                CompositionOpacity.Opaque,
                CompositionClipMode.Fixed,
                new DrawRect(30, 40, 160, 24))
        };
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 1, CompositionFrame.FromLayers(layers));

        Assert.False(snapshot.TryHitTestLogicalPixel(36, 75, out _));
    }

    [Fact]
    public void CompositorHitTestSnapshot_does_not_occlude_static_target_when_fixed_clip_rejects_presented_layer()
    {
        var hitTargets = new[]
        {
            new HitTestTarget(
                new PixelRectangle(20, 80, 120, 40),
                new ActionId(100),
                default,
                0,
                1),
            new HitTestTarget(
                new PixelRectangle(20, 20, 120, 40),
                new ActionId(200),
                default,
                1,
                1)
        };
        var layer = new CompositionLayer(
            new CompositionLayerId(1),
            1,
            1,
            new CompositionTransform(0, 60),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60));
        var snapshot = CompositorHitTestSnapshot.Create(hitTargets, 2, new CompositionFrame(layer));

        Assert.True(snapshot.TryHitTestLogicalPixel(24, 88, out var result));
        Assert.Equal(new ActionId(100), result.ActionId);
        Assert.Equal(24f, result.LocalX);
        Assert.Equal(88f, result.LocalY);
        Assert.Equal(default, result.LayerId);
        Assert.False(result.MappedThroughComposition);
        Assert.False(result.MappedThroughFixedClip);
    }

    [Fact]
    public async Task TryGetActionIdAtPhysicalPixel_maps_physical_input_to_logical_hit_targets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var window = new FakeWindow();
        var backend = new PoCDrawingBackend(window);
        var compositor = new DrawingBackendCompositor(backend);
        compositor.SetViewport(new PixelRectangle(0, 0, 300, 180), new DisplayScale(1.5f, 1.5f));

        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 140, 40))
            ]), 1),
            [new HitTestTarget(new PixelRectangle(16, 20, 140, 40), new ActionId(100), new PixelRectangle(0, 0, 200, 120))]);

        await compositor.RenderAsync(frame, cancellationToken);

        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(24, 30, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.True(compositor.TryGetActionIdAtLogicalPixel(16, 20, out actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(234, 30, out _));
    }

    [Fact]
    public async Task TryGetActionIdAtPhysicalPixel_cleared_on_empty_frame()
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
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(16, 120, out _));

        using var emptyFrame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0),
            []);

        await compositor.RenderAsync(emptyFrame, cancellationToken);
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(16, 120, out _));
    }

    [Fact]
    public async Task TryGetActionIdAtLogicalPixel_maps_active_composition_transform_to_target_layer_only()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Move", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
            VirtualNodeBuilder.Button(_arena, "Still", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(2), out var animatedTarget));
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(3), out var staticTarget));
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            animatedTarget.LayerId,
            animatedTarget.CommandStart,
            animatedTarget.CommandCount,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            new CompositionTransformAnimation(
                CompositionScalarAnimation.Constant(160),
                CompositionScalarAnimation.Constant(12)),
            CompositionScalarAnimation.Constant(1f))));
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.False(compositor.TryGetActionIdAtLogicalPixel(20, 20, out _));
        Assert.True(compositor.TryGetActionIdAtLogicalPixel(180, 32, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.True(compositor.TryGetActionIdAtLogicalPixel(20, 72, out actionId));
        Assert.Equal(new ActionId(200), actionId);
        Assert.False(compositor.TryGetActionIdAtLogicalPixel(180, 84, out _));
        Assert.Equal(2, animatedTarget.CommandCount);
        Assert.Equal(2, staticTarget.CommandCount);
        Assert.Equal(animatedTarget.CommandStart + animatedTarget.CommandCount, staticTarget.CommandStart);
    }

    [Fact]
    public async Task TryGetActionIdAtPhysicalPixel_maps_scaled_input_through_active_composition_transform()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        compositor.SetViewport(new PixelRectangle(0, 0, 1920, 1080), new DisplayScale(2f, 2f));
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Move", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(2), out var target));
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            target.LayerId,
            target.CommandStart,
            target.CommandCount,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            new CompositionTransformAnimation(
                CompositionScalarAnimation.Constant(160),
                CompositionScalarAnimation.Constant(12)),
            CompositionScalarAnimation.Constant(1f))));
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(360, 64, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(40, 40, out _));
    }

    [Fact]
    public async Task RenderAsync_clears_composition_hit_test_transform()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Move", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(2), out var target));
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            target.LayerId,
            target.CommandStart,
            target.CommandCount,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            new CompositionTransformAnimation(
                CompositionScalarAnimation.Constant(160),
                CompositionScalarAnimation.Constant(12)),
            CompositionScalarAnimation.Constant(1f))));
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.True(compositor.TryGetActionIdAtLogicalPixel(180, 32, out _));

        using var nextFrame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(nextFrame, cancellationToken);

        Assert.False(compositor.TryGetActionIdAtLogicalPixel(180, 32, out _));
        Assert.True(compositor.TryGetActionIdAtLogicalPixel(20, 20, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
    }

    [Fact]
    public async Task RenderCompositionScrollPresentationTickAsync_uses_fixed_clip_layer_without_regular_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        Assert.True(snapshot.TryGetScrollCompositionTarget(new NodeKey(1), out var target));
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10)), snapshot);

        var result = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken);

        Assert.Equal(1, compositor.RenderCount);
        Assert.Equal(1, compositor.CompositionTickCount);
        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.Equal(CompositionClipMode.Fixed, backend.LastCompositionFrame.Layer.ClipMode);
        Assert.Equal(new DrawRect(target.ClipBounds.X, target.ClipBounds.Y, target.ClipBounds.Width, target.ClipBounds.Height), backend.LastCompositionFrame.Layer.ClipBounds);
        Assert.Equal(new CompositionTransform(0, 30), backend.LastCompositionFrame.Layer.Transform);
        Assert.Equal(CompositionOpacity.Opaque, backend.LastCompositionFrame.Layer.Opacity);
        Assert.Equal(frame.Commands.Count, result.CommandCount);
        Assert.Same(frame.Resources, backend.LastCompositionResources);
    }

    [Fact]
    public async Task StageRetainedFrameAsync_updates_scroll_retained_frame_without_regular_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        using var firstFrame = pipeline.Build(CreateScrollRoot(0), new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(firstFrame, cancellationToken);
        using var stagedFrame = pipeline.Build(CreateScrollRoot(54), new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        var stagedSnapshot = pipeline.LastRetainedInputSnapshot!;

        await compositor.StageRetainedFrameAsync(stagedFrame, null, cancellationToken);

        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, compositor.RenderCount);
        Assert.Equal(1, compositor.RetainedStageCount);
        Assert.True(stagedSnapshot.TryGetScrollCompositionTarget(new NodeKey(1), out _));

        var start = CompositionTimestamp.FromStopwatchTicks(100);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(0, 54, CompositionAnimationEasing.SineInOut)), stagedSnapshot);
        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(start, cancellationToken);

        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.True(compositor.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(0, presentedScrollY);

        VirtualNode CreateScrollRoot(double scrollY)
        {
            return new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(scrollY)],
                children:
                [
                    VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                    VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                    VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
                ]);
        }
    }

    [Fact]
    public async Task Composition_scroll_tick_waits_for_in_flight_regular_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new BlockingCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(40, 10)), pipeline.LastRetainedInputSnapshot!);
        using var hoverFrame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());

        backend.BlockNextBeginFrame();
        var renderTask = Task.Run(async () => await compositor.RenderAsync(hoverFrame, cancellationToken), cancellationToken);
        await backend.WaitForBlockedBeginFrameAsync(cancellationToken);

        var tickTask = Task.Run(async () => await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken), cancellationToken);
        await Task.Delay(50, cancellationToken);

        Assert.Equal(2, backend.BeginFrameCount);
        Assert.Equal(0, backend.ExecuteCompositionCount);

        backend.ReleaseBlockedBeginFrame();
        await renderTask.WaitAsync(cancellationToken);
        _ = await tickTask.WaitAsync(cancellationToken);

        Assert.Equal(3, backend.BeginFrameCount);
        Assert.Equal(2, backend.ExecuteCompositionCount);
    }

    [Fact]
    public async Task RenderAsync_during_active_scroll_presentation_composes_updated_retained_frame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(40, 10)), pipeline.LastRetainedInputSnapshot!);
        using var hoverFrame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());

        await compositor.RenderAsync(hoverFrame, cancellationToken);

        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.True(compositor.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task RenderCompositionScrollPresentationTickAsync_decomposes_nested_scroll_clips_into_ordered_layers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Outer", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 3,
                    properties: [VirtualNodeProperty.Height(48)],
                    children:
                    [
                        VirtualNodeBuilder.Button(_arena, "Inner A", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(200))),
                        VirtualNodeBuilder.Button(_arena, "Inner B", new NodeKey(5), VirtualNodeProperty.Action(new ActionId(300)))
                    ]),
                VirtualNodeBuilder.Button(_arena, "Outer tail", new NodeKey(6), VirtualNodeProperty.Action(new ActionId(400)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 260, 180), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.True(snapshot.TryGetScrollCompositionTarget(new NodeKey(1), out var target));
        Assert.True(target.LayerCount >= 3);
        Assert.NotEqual(target.GetLayer(0).ClipBounds, target.GetLayer(1).ClipBounds);
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10)), snapshot);

        var result = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken);

        Assert.Equal(target.LayerCount, result.LayerCount);
        Assert.Equal(target.LayerCount, backend.LastCompositionFrame.LayerCount);
        for (var i = 0; i < target.LayerCount; i++)
        {
            var expectedTargetLayer = target.GetLayer(i);
            var actualLayer = backend.LastCompositionFrame.GetLayer(i);
            Assert.Equal(expectedTargetLayer.LayerId, actualLayer.Id);
            Assert.Equal(expectedTargetLayer.CommandStart, actualLayer.CommandStart);
            Assert.Equal(expectedTargetLayer.CommandCount, actualLayer.CommandCount);
            Assert.Equal(new DrawRect(expectedTargetLayer.ClipBounds.X, expectedTargetLayer.ClipBounds.Y, expectedTargetLayer.ClipBounds.Width, expectedTargetLayer.ClipBounds.Height), actualLayer.ClipBounds);
            Assert.Equal(CompositionClipMode.Fixed, actualLayer.ClipMode);
            Assert.Equal(new CompositionTransform(0, 30), actualLayer.Transform);
        }
    }

    [Fact]
    public async Task TryGetActionIdAtLogicalPixel_maps_presented_scroll_offset_under_fixed_clip()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        Assert.True(snapshot.TryGetScrollCompositionTarget(new NodeKey(1), out _));
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10)), snapshot);
        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken);

        Assert.False(compositor.TryGetActionIdAtLogicalPixel(20, -4, out _));
        Assert.True(compositor.TryGetActionIdAtLogicalPixel(20, 28, out var actionId));
        Assert.Equal(new ActionId(100), actionId);
        Assert.False(compositor.TryGetActionIdAtLogicalPixel(20, 88, out _));
    }

    [Fact]
    public async Task TryGetPresentedScrollY_reads_active_scroll_presentation_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10)), pipeline.LastRetainedInputSnapshot!);

        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(10), cancellationToken);

        Assert.True(compositor.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(10, presentedScrollY);
        await using var compositorLoop = new CompositorLoop(new EmptyTranslator(), compositor);
        Assert.True(ScrollPresentationInputBridge.TryResolveWheelRetarget(
            compositorLoop,
            new NodeKey(1),
            new ScrollState { Position = 40, TargetPosition = 40, MaxScrollY = 120, HasMaxScrollY = true },
            54,
            out var decision));
        Assert.Equal(10, decision.Interrupt.PresentedScrollY);
        Assert.Equal(94, decision.Interrupt.NextState.TargetPosition);
        Assert.False(compositor.TryGetPresentedScrollY(new NodeKey(404), out _));
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
    public async Task RenderCompositionAnimationTickAsync_uses_retained_frame_without_regular_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3)),
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(10, 20, 30, 40), Color: DrawColor.Opaque(4, 5, 6)),
            ]), 2),
            [],
            resources);

        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(42),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 20),
                new CompositionScalarAnimation(0, 10)),
            new CompositionScalarAnimation(1f, 0.5f))));

        var result = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(5), cancellationToken);

        Assert.Equal(1, compositor.RenderCount);
        Assert.Equal(1, compositor.CompositionTickCount);
        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.Equal(new CompositionTransform(10, 5), backend.LastCompositionFrame.Layer.Transform);
        Assert.Equal(0.75f, backend.LastCompositionFrame.Layer.Opacity.Normalized);
        Assert.Equal(new CompositionLayerId(42), backend.LastCompositionFrame.Layer.Id);
        Assert.Equal(2, result.CommandCount);
        Assert.Same(resources, backend.LastCompositionResources);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_normalizes_non_finite_scalar_outputs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(float.NaN, 20),
                new CompositionScalarAnimation(10, float.PositiveInfinity)),
            new CompositionScalarAnimation(float.NegativeInfinity, 0.5f))));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(5), cancellationToken);

        var layer = backend.LastCompositionFrame.Layer;
        Assert.True(float.IsFinite(layer.Transform.TranslateX));
        Assert.True(float.IsFinite(layer.Transform.TranslateY));
        Assert.True(float.IsFinite(layer.Opacity.Normalized));
        Assert.Equal(20, layer.Transform.TranslateX);
        Assert.Equal(10, layer.Transform.TranslateY);
        Assert.Equal(0.5f, layer.Opacity.Normalized);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_uses_injected_composition_clock_source()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        var tickTimestamp = CompositionTimestamp.FromStopwatchTicks(125);
        using var compositor = new DrawingBackendCompositor(backend, new FixedCompositionClockSource(tickTimestamp));
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3)),
            ]), 1),
            [],
            resources);

        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(100),
                CompositionDuration.FromStopwatchTicks(50)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 20),
                new CompositionScalarAnimation(0, 10)),
            new CompositionScalarAnimation(1f, 0.5f))));

        _ = await compositor.RenderCompositionAnimationTickAsync(cancellationToken);

        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.Equal(tickTimestamp.StopwatchTicks, backend.LastBeginFrameContext.Timestamp);
        Assert.Equal(new CompositionTransform(10, 5), backend.LastCompositionFrame.Layer.Transform);
        Assert.Equal(0.75f, backend.LastCompositionFrame.Layer.Opacity.Normalized);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_requires_composition_backend()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var compositor = new DrawingBackendCompositor(new DirtyRangeTrackingBackend());
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        using var frame = new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3)),
            ]), 1),
            [],
            resources);
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f))));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));
        Assert.Contains("transform/opacity composition execution", exception.Message);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_records_missing_backend_skip_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var compositor = new DrawingBackendCompositor(new DirtyRangeTrackingBackend());
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f))));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        var status = compositor.LastCompositionExecutionStatus;
        Assert.Equal(CompositionExecutionKind.TransformOpacityTick, status.Kind);
        Assert.Equal(CompositionExecutionSkipReason.BackendDoesNotImplementComposition, status.SkipReason);
        Assert.Equal(CompositionBackendCapabilities.TransformOpacity, status.RequiredCapabilities);
        Assert.Equal(CompositionBackendCapabilities.None, status.BackendCapabilities);
        Assert.Equal(CompositionFramePacing.SoftwareTimer, status.FramePacing);
        Assert.Equal(1, status.LayerCount);
        Assert.Equal(1, status.CommandCount);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_records_pre_execution_skip_reasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var noPlanCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await noPlanCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            noPlanCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.TransformOpacityTick,
            CompositionExecutionSkipReason.NoActivePlan,
            CompositionBackendCapabilities.TransformOpacity,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 0,
            commandCount: 0);

        using var missingFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        missingFrameCompositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 1));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await missingFrameCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            missingFrameCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.TransformOpacityTick,
            CompositionExecutionSkipReason.MissingRetainedFrame,
            CompositionBackendCapabilities.TransformOpacity,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: 0);

        using var invalidFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        invalidFrameCompositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: 2));
        using var frame = CreateSingleRectFrame();
        await invalidFrameCompositor.RenderAsync(frame, cancellationToken);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await invalidFrameCompositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            invalidFrameCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.TransformOpacityTick,
            CompositionExecutionSkipReason.InvalidPlanForRetainedFrame,
            CompositionBackendCapabilities.TransformOpacity,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: 1);
    }

    [Fact]
    public void CompositionAnimationPlan_evaluates_timeline_and_easing()
    {
        var plan = new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(7),
            CommandStart: 2,
            CommandCount: 3,
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(10),
                CompositionDuration.FromStopwatchTicks(20),
                CompositionAnimationRepeatMode.Alternate),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 100),
                new CompositionScalarAnimation(10, 20, CompositionAnimationEasing.SineInOut)),
            new CompositionScalarAnimation(1f, 0f)));

        var forward = plan.Evaluate(8, CompositionTimestamp.FromStopwatchTicks(20)).Layer;
        var reverse = plan.Evaluate(8, CompositionTimestamp.FromStopwatchTicks(40)).Layer;

        Assert.Equal(new CompositionLayerId(7), forward.Id);
        Assert.Equal(50, forward.Transform.TranslateX);
        Assert.Equal(15, forward.Transform.TranslateY);
        Assert.Equal(0.5f, forward.Opacity.Normalized);
        Assert.Equal(50, reverse.Transform.TranslateX);

        var sineOut = new CompositionScalarAnimation(0, 100, CompositionAnimationEasing.SineOut);
        Assert.Equal(0, sineOut.Evaluate(0));
        Assert.InRange(sineOut.Evaluate(0.5f), 70.7f, 70.8f);
        Assert.Equal(100, sineOut.Evaluate(1));
    }

    [Fact]
    public void CompositionScalarAnimation_normalizes_invalid_progress_and_endpoints()
    {
        var range = new CompositionScalarAnimation(10, 20);
        Assert.Equal(10, range.Evaluate(float.NaN));
        Assert.Equal(10, range.Evaluate(float.NegativeInfinity));
        Assert.Equal(20, range.Evaluate(float.PositiveInfinity));

        Assert.Equal(20, new CompositionScalarAnimation(float.NaN, 20).Evaluate(0.5f));
        Assert.Equal(10, new CompositionScalarAnimation(10, float.PositiveInfinity).Evaluate(0.5f));
        Assert.Equal(0, new CompositionScalarAnimation(float.NaN, float.NegativeInfinity).Evaluate(0.5f));
    }

    [Fact]
    public async Task CompositionAnimationMarker_progress_crossing_publishes_after_successful_tick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(10),
            new NodeKey(20),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(1),
                new CompositionRuntimeEventId(100),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f))])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(20), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(1, count);
        Assert.Equal(new CompositionAnimationInstanceId(10), events[0].InstanceId);
        Assert.Equal(new CompositionAnimationMarkerId(1), events[0].MarkerId);
        Assert.Equal(new CompositionRuntimeEventId(100), events[0].RuntimeEventId);
        Assert.Equal(CompositionAnimationMarkerEventKind.Progress, events[0].Kind);
        Assert.Equal(CompositionAnimationMarkerOwnerKind.TransformOpacity, events[0].OwnerKind);
        Assert.Equal(new CompositionLayerId(1), events[0].LayerId);
        Assert.Equal(new NodeKey(20), events[0].TargetKey);
        Assert.Equal(0.8f, events[0].Progress);
        Assert.Equal(CompositionTimestamp.FromStopwatchTicks(80), events[0].Timestamp);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
    }

    [Fact]
    public async Task CompositionAnimationMarker_once_does_not_publish_duplicate_after_crossing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(11),
            new NodeKey(21),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(2),
                new CompositionRuntimeEventId(101),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f))])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(20), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(90), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(100), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        Assert.Equal(1, compositor.DrainCompositionMarkerEvents(events));
    }

    [Fact]
    public async Task CompositionAnimationMarker_loop_once_per_iteration_fires_each_iteration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(10), CompositionAnimationRepeatMode.Loop),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(12),
            new NodeKey(22),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(3),
                new CompositionRuntimeEventId(102),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f),
                CompositionAnimationMarkerRepeatPolicy.OncePerIteration)])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(6), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(16), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(2, count);
        Assert.Equal(0, events[0].Iteration);
        Assert.Equal(1, events[1].Iteration);
    }

    [Fact]
    public async Task CompositionAnimationMarker_alternate_reverse_crossing_publishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(10), CompositionAnimationRepeatMode.Alternate),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(13),
            new NodeKey(23),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(4),
                new CompositionRuntimeEventId(103),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f),
                CompositionAnimationMarkerRepeatPolicy.OncePerIteration)])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(11), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(16), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(1, count);
        Assert.Equal(1, events[0].Iteration);
        Assert.Equal(CompositionPlaybackDirection.Reverse, events[0].Direction);
    }

    [Fact]
    public async Task CompositionAnimationMarker_every_tick_publishes_each_successful_tick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(14),
            new NodeKey(24),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(5),
                new CompositionRuntimeEventId(104),
                CompositionAnimationMarkerTrigger.EveryTick())])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(2), cancellationToken);
        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(3), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(3, count);
        Assert.All(events[..count].ToArray(), e => Assert.Equal(CompositionAnimationMarkerEventKind.Tick, e.Kind));
    }

    [Fact]
    public async Task CompositionMarkerEventPump_dispatches_mapped_events_and_counts_unmapped_events_across_batches()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);

        var markers = new CompositionAnimationMarker[18];
        for (var i = 0; i < markers.Length; i++)
        {
            var runtimeEventId = new CompositionRuntimeEventId((uint)(i + 1));
            markers[i] = new CompositionAnimationMarker(
                new CompositionAnimationMarkerId((uint)(i + 1)),
                runtimeEventId,
                CompositionAnimationMarkerTrigger.EveryTick());
        }

        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(15),
            new NodeKey(25),
            markers)));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.Equal(markers.Length, compositor.PendingCompositionMarkerEventCount);

        var mapper = new EvenRuntimeEventMarkerMapper();
        var dispatcher = new RecordingMarkerDispatcher();
        var result = CompositionMarkerEventPump.DrainAndDispatch(compositor, ref mapper, dispatcher);

        Assert.Equal(markers.Length, mapper.MapCount);
        Assert.Equal(18, result.DrainedEvents);
        Assert.Equal(9, result.DispatchedMessages);
        Assert.Equal(9, result.UnmappedEvents);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
        Assert.Equal(new[] { 2, 4, 6, 8, 10, 12, 14, 16, 18 }, dispatcher.Messages);
    }

    [Fact]
    public async Task CompositionScrollPresentationMarker_progress_range_publishes_scroll_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(40, 10),
            new CompositionAnimationInstanceId(15),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(6),
                new CompositionRuntimeEventId(105),
                CompositionAnimationMarkerTrigger.EnterProgressRange(0.4f, 0.6f))]), pipeline.LastRetainedInputSnapshot!);

        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(20), cancellationToken);
        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(1, count);
        Assert.Equal(CompositionAnimationMarkerOwnerKind.ScrollPresentation, events[0].OwnerKind);
        Assert.Equal(new NodeKey(1), events[0].TargetKey);
        Assert.Equal(CompositionAnimationMarkerEventKind.ProgressRangeEntered, events[0].Kind);
    }

    [Fact]
    public async Task CompositionScrollPresentationMarker_clear_presentation_discards_pending_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(40, 10),
            new CompositionAnimationInstanceId(16),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(7),
                new CompositionRuntimeEventId(106),
                CompositionAnimationMarkerTrigger.EveryTick())]), pipeline.LastRetainedInputSnapshot!);

        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.True(compositor.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(1, compositor.PendingCompositionMarkerEventCount);

        compositor.ClearCompositionScrollPresentation();

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        Assert.False(compositor.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
        Assert.Equal(0, compositor.DrainCompositionMarkerEvents(events));
    }

    [Fact]
    public async Task CompositionScrollPresentationMarker_device_lost_before_first_tick_does_not_publish_or_backfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new DeviceLostCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationDeclaration(new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(40, 10),
            new CompositionAnimationInstanceId(16),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(7),
                new CompositionRuntimeEventId(106),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f))]), pipeline.LastRetainedInputSnapshot!);

        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);

        AssertCompositionStatus(
            compositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionSkipReason.DeviceLostRecovered,
            CompositionBackendCapabilities.ScrollPresentation,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: frame.Commands.Count);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.True(backend.RecoveryAttempted);
        Assert.True(backend.RecoverySucceeded);
        Assert.Equal(0, compositor.CompositionTickCount);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);

        _ = await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(90), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        Assert.Equal(0, compositor.DrainCompositionMarkerEvents(events));
        AssertCompositionStatus(
            compositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionSkipReason.None,
            CompositionBackendCapabilities.ScrollPresentation,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: frame.Commands.Count);
        Assert.Equal(2, backend.ExecuteCompositionCount);
        Assert.Equal(1, compositor.CompositionTickCount);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
    }

    [Fact]
    public async Task CompositionAnimationMarker_skipped_before_first_tick_does_not_backfill()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(CompositionTimestamp.Zero, CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(16),
            new NodeKey(26),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(7),
                new CompositionRuntimeEventId(106),
                CompositionAnimationMarkerTrigger.AtProgress(0.5f))])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(80), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        Assert.Equal(0, compositor.DrainCompositionMarkerEvents(events));
    }

    [Fact]
    public void CompositionAnimationDeclaration_resolves_normal_ui_node_key()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(2)),
            VirtualNodeBuilder.Button(_arena, "Move", new NodeKey(3)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.True(snapshot.TryGetCompositionTarget(new NodeKey(3), out var target));
        var declaration = new CompositionAnimationDeclaration(
            new NodeKey(3),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 20),
                CompositionScalarAnimation.Constant(0)),
            CompositionScalarAnimation.Constant(1f));

        Assert.True(declaration.TryResolve(snapshot, out var plan));
        var layer = plan.Evaluate(frame.Commands.Count, CompositionTimestamp.FromStopwatchTicks(5)).Layer;

        Assert.Equal(target.LayerId, layer.Id);
        Assert.Equal(target.CommandStart, layer.CommandStart);
        Assert.Equal(target.CommandCount, layer.CommandCount);
        Assert.Equal(10, layer.Transform.TranslateX);
    }

    [Fact]
    public void CompositionAnimationDeclaration_rejects_missing_target()
    {
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Text(_arena, "Static", new NodeKey(2)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var declaration = new CompositionAnimationDeclaration(
            new NodeKey(404),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f));

        Assert.False(declaration.TryResolve(pipeline.LastRetainedInputSnapshot!, frame.Commands.Count, out _));
    }

    [Fact]
    public void CompositionScrollPresentationDeclaration_resolves_scroll_container_to_fixed_clip_plan()
    {
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;

        Assert.True(snapshot.TryGetScrollCompositionTarget(new NodeKey(1), out var target));
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10));

        Assert.True(declaration.TryResolve(snapshot, out var plan));
        var layer = plan.Evaluate(frame.Commands.Count, CompositionTimestamp.FromStopwatchTicks(10)).Layer;

        Assert.Equal(target.LayerId, layer.Id);
        Assert.Equal(target.CommandStart, layer.CommandStart);
        Assert.Equal(target.CommandCount, layer.CommandCount);
        Assert.Equal(CompositionClipMode.Fixed, layer.ClipMode);
        Assert.Equal(new DrawRect(target.ClipBounds.X, target.ClipBounds.Y, target.ClipBounds.Width, target.ClipBounds.Height), layer.ClipBounds);
        Assert.Equal(30, layer.Transform.TranslateY);
    }

    [Theory]
    [InlineData(float.NaN, 10)]
    [InlineData(40, float.NaN)]
    [InlineData(float.PositiveInfinity, 10)]
    [InlineData(40, float.NegativeInfinity)]
    public void CompositionScrollPresentationDeclaration_rejects_non_finite_presented_range(float from, float to)
    {
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "First", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                VirtualNodeBuilder.Button(_arena, "Second", new NodeKey(3), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Button(_arena, "Third", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(300)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 240, 120), _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(from, to));

        Assert.False(declaration.TryResolve(pipeline.LastRetainedInputSnapshot!, frame.Commands.Count, out _));
    }

    [Fact]
    public void CompositionScrollPresentationDeclaration_resolves_nested_scroll_clip_runs_to_multilayer_plan()
    {
        var pipeline = new RenderPipeline();
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.Height(120), VirtualNodeProperty.ScrollY(40)],
            children:
            [
                VirtualNodeBuilder.Button(_arena, "Outer", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(100))),
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 3,
                    properties: [VirtualNodeProperty.Height(48)],
                    children:
                    [
                        VirtualNodeBuilder.Button(_arena, "Inner A", new NodeKey(4), VirtualNodeProperty.Action(new ActionId(200))),
                        VirtualNodeBuilder.Button(_arena, "Inner B", new NodeKey(5), VirtualNodeProperty.Action(new ActionId(300)))
                    ]),
                VirtualNodeBuilder.Button(_arena, "Outer tail", new NodeKey(6), VirtualNodeProperty.Action(new ActionId(400)))
            ]);
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 260, 180), _arena.GetOrCreateSnapshot());
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionScalarAnimation(40, 10));

        Assert.True(snapshot.TryGetScrollCompositionTarget(new NodeKey(1), out var target));
        Assert.True(declaration.TryResolve(snapshot, out var plan));
        var compositionFrame = plan.Evaluate(frame.Commands.Count, CompositionTimestamp.FromStopwatchTicks(10));

        Assert.True(compositionFrame.LayerCount >= 3);
        Assert.Equal(target.LayerCount, compositionFrame.LayerCount);
        Assert.NotEqual(compositionFrame.GetLayer(0).ClipBounds, compositionFrame.GetLayer(1).ClipBounds);
    }

    [Fact]
    public async Task SetCompositionAnimationDeclaration_installs_resolved_plan_for_tick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(_arena, "Move", new NodeKey(3)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var declaration = new CompositionAnimationDeclaration(
            new NodeKey(3),
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 20),
                CompositionScalarAnimation.Constant(0)),
            CompositionScalarAnimation.Constant(1f));

        compositor.SetCompositionAnimationDeclaration(declaration, pipeline.LastRetainedInputSnapshot!);
        var result = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(5), cancellationToken);

        Assert.Equal(1, result.LayerCount);
        Assert.Equal(new CompositionLayerId(3), backend.LastCompositionFrame.Layer.Id);
        Assert.Equal(10, backend.LastCompositionFrame.Layer.Transform.TranslateX);
    }

    [Fact]
    public async Task ValidateCompositionAnimationPresentationSet_reports_conflicts_without_installing_plan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(22),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)),
            VirtualNodeFactory.Rectangle(
                new NodeKey(23),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var first = CreatePresentationSetDeclaration(new NodeKey(22));
        var second = CreatePresentationSetDeclaration(new NodeKey(23));
        var duplicateFirst = CreatePresentationSetDeclaration(new NodeKey(22));
        var rootOverlap = CreatePresentationSetDeclaration(new NodeKey(1));

        var accepted = compositor.ValidateCompositionAnimationPresentationSet([first, second], snapshot);
        var conflicted = compositor.ValidateCompositionAnimationPresentationSet([first, duplicateFirst], snapshot);
        var overlapped = compositor.ValidateCompositionAnimationPresentationSet([rootOverlap, first], snapshot);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Accepted, accepted.Kind);
        Assert.Equal(2, accepted.AcceptedCount);
        Assert.Equal(0, accepted.RejectedCount);
        Assert.False(accepted.PresentationStateChanged);
        Assert.Equal(new CompositionLayerId(22), accepted.EntryResults[0].LayerId);
        Assert.Equal(new CompositionLayerId(23), accepted.EntryResults[1].LayerId);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Mixed, conflicted.Kind);
        Assert.Equal(1, conflicted.AcceptedCount);
        Assert.Equal(1, conflicted.RejectedCount);
        Assert.False(conflicted.PresentationStateChanged);
        Assert.Equal(CompositionAnimationPresentationSetEntryKind.Accepted, conflicted.EntryResults[0].Kind);
        Assert.Equal(CompositionAnimationPresentationSetEntryKind.Rejected, conflicted.EntryResults[1].Kind);
        Assert.Equal(CompositionAnimationPresentationSetRejectionReason.DuplicateLayerId, conflicted.EntryResults[1].RejectionReason);
        Assert.Equal(new CompositionLayerId(22), conflicted.EntryResults[1].LayerId);
        Assert.Equal(0, conflicted.EntryResults[1].ConflictingIndex);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Mixed, overlapped.Kind);
        Assert.Equal(1, overlapped.AcceptedCount);
        Assert.Equal(1, overlapped.RejectedCount);
        Assert.Equal(CompositionAnimationPresentationSetEntryKind.Accepted, overlapped.EntryResults[0].Kind);
        Assert.Equal(CompositionAnimationPresentationSetEntryKind.Rejected, overlapped.EntryResults[1].Kind);
        Assert.Equal(CompositionAnimationPresentationSetRejectionReason.OverlappingCommandRange, overlapped.EntryResults[1].RejectionReason);
        Assert.Equal(new CompositionLayerId(22), overlapped.EntryResults[1].LayerId);
        Assert.Equal(0, overlapped.EntryResults[1].ConflictingIndex);
        Assert.Null(compositor.CompositionAnimationPlan);
        Assert.Equal(0, backend.ExecuteCompositionCount);
    }

    [Fact]
    public async Task PrepareCompositionAnimationPresentationSetActivation_builds_plan_set_without_installing_plan()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(22),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)),
            VirtualNodeFactory.Rectangle(
                new NodeKey(23),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var first = CreatePresentationSetDeclaration(new NodeKey(22));
        var second = CreatePresentationSetDeclaration(new NodeKey(23));

        var preflight = compositor.PrepareCompositionAnimationPresentationSetActivation([first, second], snapshot);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Accepted, preflight.Kind);
        Assert.Equal(2, preflight.AcceptedCount);
        Assert.Equal(0, preflight.RejectedCount);
        Assert.Equal(snapshot.CommandCount, preflight.CommandCount);
        Assert.False(preflight.PresentationStateChanged);
        Assert.True(preflight.HasPlan);
        Assert.Equal(2, preflight.Plan.Count);
        Assert.True(preflight.Plan.IsValidForCommandCount(snapshot.CommandCount));
        Assert.Collection(
            preflight.Entries.ToArray(),
            firstEntry =>
            {
                Assert.True(firstEntry.HasPlan);
                Assert.Equal(new NodeKey(22), firstEntry.Validation.TargetKey);
                Assert.Equal(new CompositionLayerId(22), firstEntry.Plan.LayerAnimation.LayerId);
                Assert.Equal(firstEntry.Validation.CommandStart, firstEntry.Plan.LayerAnimation.CommandStart);
                Assert.Equal(firstEntry.Validation.CommandCount, firstEntry.Plan.LayerAnimation.CommandCount);
            },
            secondEntry =>
            {
                Assert.True(secondEntry.HasPlan);
                Assert.Equal(new NodeKey(23), secondEntry.Validation.TargetKey);
                Assert.Equal(new CompositionLayerId(23), secondEntry.Plan.LayerAnimation.LayerId);
                Assert.Equal(secondEntry.Validation.CommandStart, secondEntry.Plan.LayerAnimation.CommandStart);
                Assert.Equal(secondEntry.Validation.CommandCount, secondEntry.Plan.LayerAnimation.CommandCount);
            });

        var evaluated = preflight.Plan.Evaluate(snapshot.CommandCount, CompositionTimestamp.FromStopwatchTicks(5));
        Assert.Equal(2, evaluated.LayerCount);
        Assert.Equal(new CompositionLayerId(22), evaluated.GetLayer(0).Id);
        Assert.Equal(new CompositionLayerId(23), evaluated.GetLayer(1).Id);
        Assert.Null(compositor.CompositionAnimationPlan);
        Assert.Equal(0, backend.ExecuteCompositionCount);
    }

    [Fact]
    public async Task PrepareCompositionAnimationPresentationSetActivation_reports_conflicts_without_plan_or_state_change()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(22),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var first = CreatePresentationSetDeclaration(new NodeKey(22));
        var duplicateFirst = CreatePresentationSetDeclaration(new NodeKey(22));
        using var missingFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());

        var preflight = compositor.PrepareCompositionAnimationPresentationSetActivation([first, duplicateFirst], snapshot);
        var missingFrame = missingFrameCompositor.PrepareCompositionAnimationPresentationSetActivation([first], snapshot);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Mixed, preflight.Kind);
        Assert.Equal(1, preflight.AcceptedCount);
        Assert.Equal(1, preflight.RejectedCount);
        Assert.False(preflight.HasPlan);
        Assert.True(preflight.Plan.IsEmpty);
        Assert.False(preflight.PresentationStateChanged);
        Assert.Collection(
            preflight.Entries.ToArray(),
            firstEntry =>
            {
                Assert.True(firstEntry.HasPlan);
                Assert.Equal(CompositionAnimationPresentationSetEntryKind.Accepted, firstEntry.Validation.Kind);
            },
            secondEntry =>
            {
                Assert.False(secondEntry.HasPlan);
                Assert.Equal(CompositionAnimationPresentationSetEntryKind.Rejected, secondEntry.Validation.Kind);
                Assert.Equal(CompositionAnimationPresentationSetRejectionReason.DuplicateLayerId, secondEntry.Validation.RejectionReason);
                Assert.Equal(0, secondEntry.Validation.ConflictingIndex);
            });

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Rejected, missingFrame.Kind);
        Assert.Equal(CompositionAnimationPresentationSetRejectionReason.MissingRetainedFrame, missingFrame.Entries[0].Validation.RejectionReason);
        Assert.False(missingFrame.HasPlan);
        Assert.False(missingFrame.PresentationStateChanged);
        Assert.Null(compositor.CompositionAnimationPlan);
        Assert.Equal(0, backend.ExecuteCompositionCount);
    }

    [Fact]
    public async Task ActivateCompositionAnimationPresentationPlan_ticks_multi_layer_frame_without_regular_execute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        var root = CreatePresentationSetRoot();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var preflight = compositor.PrepareCompositionAnimationPresentationSetActivation(
            [CreatePresentationSetDeclaration(new NodeKey(22)), CreatePresentationSetDeclaration(new NodeKey(23))],
            snapshot);

        compositor.ActivateCompositionAnimationPresentationPlan(preflight.Plan);
        var result = await compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(5), cancellationToken);

        Assert.Null(compositor.CompositionAnimationPlan);
        Assert.Null(compositor.CompositionScrollPresentationPlan);
        Assert.NotNull(compositor.CompositionAnimationPresentationPlan);
        Assert.Equal(1, compositor.RenderCount);
        Assert.Equal(1, compositor.CompositionTickCount);
        Assert.Equal(1, backend.ExecuteCount);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.Equal(2, result.LayerCount);
        Assert.Equal(frame.Commands.Count, result.CommandCount);
        Assert.Equal(2, backend.LastCompositionFrame.LayerCount);
        Assert.Equal(new CompositionLayerId(22), backend.LastCompositionFrame.GetLayer(0).Id);
        Assert.Equal(new CompositionLayerId(23), backend.LastCompositionFrame.GetLayer(1).Id);
        Assert.Equal(new CompositionTransform(10, 0), backend.LastCompositionFrame.GetLayer(0).Transform);
        Assert.Equal(new CompositionTransform(10, 0), backend.LastCompositionFrame.GetLayer(1).Transform);
        Assert.Same(frame.Resources, backend.LastCompositionResources);
        AssertCompositionStatus(
            compositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.AnimationPresentationTick,
            CompositionExecutionSkipReason.None,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 2,
            commandCount: frame.Commands.Count);
    }

    [Fact]
    public async Task ActivateCompositionAnimationPresentationPlan_publishes_markers_per_layer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(CreatePresentationSetRoot(), new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        var firstMarker = new CompositionAnimationMarker(
            new CompositionAnimationMarkerId(1),
            new CompositionRuntimeEventId(101),
            CompositionAnimationMarkerTrigger.AtProgress(0.5f));
        var secondMarker = new CompositionAnimationMarker(
            new CompositionAnimationMarkerId(1),
            new CompositionRuntimeEventId(102),
            CompositionAnimationMarkerTrigger.AtProgress(0.5f));
        var preflight = compositor.PrepareCompositionAnimationPresentationSetActivation(
            [
                CreatePresentationSetDeclaration(
                    new NodeKey(22),
                    new CompositionAnimationInstanceId(22),
                    [firstMarker]),
                CreatePresentationSetDeclaration(
                    new NodeKey(23),
                    new CompositionAnimationInstanceId(23),
                    [secondMarker])
            ],
            snapshot);

        compositor.ActivateCompositionAnimationPresentationPlan(preflight.Plan);
        _ = await compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(2), cancellationToken);
        _ = await compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(8), cancellationToken);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        var count = compositor.DrainCompositionMarkerEvents(events);
        Assert.Equal(2, count);
        Assert.Equal(CompositionAnimationMarkerOwnerKind.TransformOpacity, events[0].OwnerKind);
        Assert.Equal(CompositionAnimationMarkerOwnerKind.TransformOpacity, events[1].OwnerKind);
        Assert.Equal(new CompositionAnimationInstanceId(22), events[0].InstanceId);
        Assert.Equal(new CompositionAnimationInstanceId(23), events[1].InstanceId);
        Assert.Equal(new NodeKey(22), events[0].TargetKey);
        Assert.Equal(new NodeKey(23), events[1].TargetKey);
        Assert.Equal(new CompositionLayerId(22), events[0].LayerId);
        Assert.Equal(new CompositionLayerId(23), events[1].LayerId);
        Assert.Equal(new CompositionRuntimeEventId(101), events[0].RuntimeEventId);
        Assert.Equal(new CompositionRuntimeEventId(102), events[1].RuntimeEventId);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
    }

    [Fact]
    public async Task ActivateCompositionAnimationPresentationPlan_is_exclusive_and_clear_discards_events()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(CreatePresentationSetRoot(), new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        await compositor.RenderAsync(frame, cancellationToken);
        var snapshot = pipeline.LastRetainedInputSnapshot!;
        compositor.SetCompositionAnimationPlan(CreateAnimationPlan(commandCount: frame.Commands.Count));
        compositor.SetCompositionScrollPresentationPlan(CreateScrollPresentationPlan(commandCount: frame.Commands.Count));
        var marker = new CompositionAnimationMarker(
            new CompositionAnimationMarkerId(2),
            new CompositionRuntimeEventId(202),
            CompositionAnimationMarkerTrigger.EveryTick());
        var preflight = compositor.PrepareCompositionAnimationPresentationSetActivation(
            [
                CreatePresentationSetDeclaration(
                    new NodeKey(22),
                    new CompositionAnimationInstanceId(24),
                    [marker]),
                CreatePresentationSetDeclaration(new NodeKey(23))
            ],
            snapshot);

        compositor.ActivateCompositionAnimationPresentationPlan(preflight.Plan);
        _ = await compositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        Assert.Null(compositor.CompositionAnimationPlan);
        Assert.Null(compositor.CompositionScrollPresentationPlan);
        Assert.NotNull(compositor.CompositionAnimationPresentationPlan);
        Assert.Equal(1, compositor.PendingCompositionMarkerEventCount);

        compositor.ClearCompositionAnimation();

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[4];
        Assert.Null(compositor.CompositionAnimationPresentationPlan);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
        Assert.Equal(0, compositor.DrainCompositionMarkerEvents(events));
    }

    [Fact]
    public async Task ActivateCompositionAnimationPresentationPlan_rejects_invalid_state_and_records_tick_skips()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var missingFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        var plan = new CompositionAnimationPresentationSetPlan(
            [CreateAnimationPlan(commandCount: 1), CreateAnimationPlan(new CompositionLayerId(2), commandStart: 1, commandCount: 1)]);

        var missingFrameException = Assert.Throws<InvalidOperationException>(() => missingFrameCompositor.ActivateCompositionAnimationPresentationPlan(plan));
        Assert.Contains("retained render frame", missingFrameException.Message);

        using var noPlanCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await noPlanCompositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));
        AssertCompositionStatus(
            noPlanCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.AnimationPresentationTick,
            CompositionExecutionSkipReason.NoActivePlan,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 0,
            commandCount: 0);

        var missingCapabilityBackend = new ConfigurableCompositionBackend(CompositionBackendCapabilities.TransformOpacity);
        using var missingCapabilityCompositor = new DrawingBackendCompositor(missingCapabilityBackend);
        using var frame = CreateTwoRectFrame();
        await missingCapabilityCompositor.RenderAsync(frame, cancellationToken);
        missingCapabilityCompositor.ActivateCompositionAnimationPresentationPlan(plan);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await missingCapabilityCompositor.RenderCompositionAnimationPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            missingCapabilityCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.AnimationPresentationTick,
            CompositionExecutionSkipReason.MissingBackendCapability,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.MultiLayer,
            CompositionBackendCapabilities.TransformOpacity,
            layerCount: 2,
            commandCount: 2);
        Assert.Equal(0, missingCapabilityBackend.ExecuteCompositionCount);
    }

    [Fact]
    public void ValidateCompositionAnimationPresentationSet_requires_retained_frame_without_installing_plan()
    {
        using var compositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        var pipeline = new RenderPipeline();
        var root = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(22),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)));
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540), _arena.GetOrCreateSnapshot());
        var declaration = CreatePresentationSetDeclaration(new NodeKey(22));

        var result = compositor.ValidateCompositionAnimationPresentationSet([declaration], pipeline.LastRetainedInputSnapshot!);

        Assert.Equal(CompositionAnimationPresentationSetResultKind.Rejected, result.Kind);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        Assert.False(result.PresentationStateChanged);
        Assert.Equal(CompositionAnimationPresentationSetRejectionReason.MissingRetainedFrame, result.EntryResults[0].RejectionReason);
        Assert.Null(compositor.CompositionAnimationPlan);
    }

    [Fact]
    public async Task RenderCompositionScrollPresentationTickAsync_records_missing_capability_skip_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new ConfigurableCompositionBackend(CompositionBackendCapabilities.TransformOpacity);
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionScrollPresentationPlan(new CompositionScrollPresentationPlan(new CompositionScrollLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new PixelRectangle(0, 0, 100, 80),
            RetainedScrollY: 0,
            MaxScrollY: 100,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            new CompositionScalarAnimation(0, 20))));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await compositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        var status = compositor.LastCompositionExecutionStatus;
        Assert.Equal(CompositionExecutionKind.ScrollPresentationTick, status.Kind);
        Assert.Equal(CompositionExecutionSkipReason.MissingBackendCapability, status.SkipReason);
        Assert.Equal(CompositionBackendCapabilities.ScrollPresentation, status.RequiredCapabilities);
        Assert.Equal(CompositionBackendCapabilities.TransformOpacity, status.BackendCapabilities);
        Assert.Equal(CompositionFramePacing.SoftwareTimer, status.FramePacing);
        Assert.Equal(1, status.LayerCount);
        Assert.Equal(1, status.CommandCount);
        Assert.Equal(0, backend.ExecuteCompositionCount);
    }

    [Fact]
    public async Task RenderCompositionScrollPresentationTickAsync_records_pre_execution_skip_reasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var noPlanCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await noPlanCompositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            noPlanCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionSkipReason.NoActivePlan,
            CompositionBackendCapabilities.ScrollPresentation,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 0,
            commandCount: 0);

        using var missingFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        missingFrameCompositor.SetCompositionScrollPresentationPlan(CreateScrollPresentationPlan(commandCount: 1));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await missingFrameCompositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            missingFrameCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionSkipReason.MissingRetainedFrame,
            CompositionBackendCapabilities.ScrollPresentation,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: 0);

        using var invalidFrameCompositor = new DrawingBackendCompositor(new CompositionTrackingBackend());
        invalidFrameCompositor.SetCompositionScrollPresentationPlan(CreateScrollPresentationPlan(commandCount: 2));
        using var frame = CreateSingleRectFrame();
        await invalidFrameCompositor.RenderAsync(frame, cancellationToken);

        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () => await invalidFrameCompositor.RenderCompositionScrollPresentationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken));

        AssertCompositionStatus(
            invalidFrameCompositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.ScrollPresentationTick,
            CompositionExecutionSkipReason.InvalidPlanForRetainedFrame,
            CompositionBackendCapabilities.ScrollPresentation,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: 1);
    }

    [Fact]
    public async Task RenderAsync_records_retained_update_scroll_presentation_skip_status_before_fallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var noPlanBackend = new CompositionTrackingBackend();
        using (var noPlanCompositor = new DrawingBackendCompositor(noPlanBackend))
        {
            using var frame = CreateSingleRectFrame();
            await noPlanCompositor.RenderAsync(frame, cancellationToken);

            AssertCompositionStatus(
                noPlanCompositor.LastCompositionExecutionStatus,
                CompositionExecutionKind.RetainedUpdateScrollPresentation,
                CompositionExecutionSkipReason.NoActivePlan,
                CompositionBackendCapabilities.ScrollPresentation,
                CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
                layerCount: 0,
                commandCount: 1);
            Assert.Equal(1, noPlanBackend.ExecuteCount);
            Assert.Equal(0, noPlanBackend.ExecuteCompositionCount);
        }

        var missingCapabilityBackend = new ConfigurableCompositionBackend(CompositionBackendCapabilities.TransformOpacity);
        using (var missingCapabilityCompositor = new DrawingBackendCompositor(missingCapabilityBackend))
        {
            using var initialFrame = CreateSingleRectFrame();
            await missingCapabilityCompositor.RenderAsync(initialFrame, cancellationToken);
            missingCapabilityCompositor.SetCompositionScrollPresentationPlan(CreateScrollPresentationPlan(commandCount: 1));
            using var nextFrame = CreateSingleRectFrame();
            await missingCapabilityCompositor.RenderAsync(nextFrame, cancellationToken);

            AssertCompositionStatus(
                missingCapabilityCompositor.LastCompositionExecutionStatus,
                CompositionExecutionKind.RetainedUpdateScrollPresentation,
                CompositionExecutionSkipReason.MissingBackendCapability,
                CompositionBackendCapabilities.ScrollPresentation,
                CompositionBackendCapabilities.TransformOpacity,
                layerCount: 1,
                commandCount: 1);
            Assert.Equal(2, missingCapabilityBackend.ExecuteCount);
            Assert.Equal(0, missingCapabilityBackend.ExecuteCompositionCount);
        }

        var invalidFrameBackend = new CompositionTrackingBackend();
        using (var invalidFrameCompositor = new DrawingBackendCompositor(invalidFrameBackend))
        {
            invalidFrameCompositor.SetCompositionScrollPresentationPlan(CreateScrollPresentationPlan(commandCount: 2));
            using var frame = CreateSingleRectFrame();
            await invalidFrameCompositor.RenderAsync(frame, cancellationToken);

            AssertCompositionStatus(
                invalidFrameCompositor.LastCompositionExecutionStatus,
                CompositionExecutionKind.RetainedUpdateScrollPresentation,
                CompositionExecutionSkipReason.InvalidPlanForRetainedFrame,
                CompositionBackendCapabilities.ScrollPresentation,
                CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
                layerCount: 1,
                commandCount: 1);
            Assert.Equal(1, invalidFrameBackend.ExecuteCount);
            Assert.Equal(0, invalidFrameBackend.ExecuteCompositionCount);
        }
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_records_success_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new CompositionTrackingBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f))));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        var status = compositor.LastCompositionExecutionStatus;
        Assert.Equal(CompositionExecutionKind.TransformOpacityTick, status.Kind);
        Assert.Equal(CompositionExecutionSkipReason.None, status.SkipReason);
        Assert.Equal(CompositionBackendCapabilities.TransformOpacity, status.RequiredCapabilities);
        Assert.Equal(CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer, status.BackendCapabilities);
        Assert.Equal(1, status.LayerCount);
        Assert.Equal(1, status.CommandCount);
    }

    [Fact]
    public async Task RenderCompositionAnimationTickAsync_records_device_lost_recovered_skip_status()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var backend = new DeviceLostCompositionBackend();
        using var compositor = new DrawingBackendCompositor(backend);
        using var frame = CreateSingleRectFrame();
        await compositor.RenderAsync(frame, cancellationToken);
        compositor.SetCompositionAnimationPlan(new CompositionAnimationPlan(new CompositionLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(100)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f),
            new CompositionAnimationInstanceId(30),
            new NodeKey(40),
            [new CompositionAnimationMarker(
                new CompositionAnimationMarkerId(1),
                new CompositionRuntimeEventId(200),
                CompositionAnimationMarkerTrigger.EveryTick())])));

        _ = await compositor.RenderCompositionAnimationTickAtAsync(CompositionTimestamp.FromStopwatchTicks(1), cancellationToken);

        AssertCompositionStatus(
            compositor.LastCompositionExecutionStatus,
            CompositionExecutionKind.TransformOpacityTick,
            CompositionExecutionSkipReason.DeviceLostRecovered,
            CompositionBackendCapabilities.TransformOpacity,
            CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer,
            layerCount: 1,
            commandCount: 1);
        Assert.Equal(1, backend.ExecuteCompositionCount);
        Assert.True(backend.RecoveryAttempted);
        Assert.True(backend.RecoverySucceeded);
        Assert.Equal(0, compositor.CompositionTickCount);
        Assert.Equal(0, compositor.PendingCompositionMarkerEventCount);
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

        Assert.Equal(1, backend.ExecuteCount);
        Assert.Single(backend.ReceivedDirtyRanges);
        Assert.Empty(backend.ReceivedDirtyRanges[0]); // no dirty ranges on initial frame

        // Frame 2: same resources + dirty ranges; keep frame1 alive until frame2 is retained.
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
        frame1.Dispose();

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

        resources.Release();

        var owned = FrameDrawingResources.Rent();
        owned.Reset();
        FrameDrawingResources.Return(owned);
    }

    [Fact]
    public async Task TryGetActionIdAtPhysicalPixel_respects_clip_bounds()
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
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(20, 20, out var actionId));
        Assert.Equal(new ActionId(100), actionId);

        // Inside button bounds but OUTSIDE clip (y=52 > clip bottom=50)
        // �?clip check should reject this
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(20, 52, out _));

        // Outside button bounds entirely �?bounds check rejects
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(200, 20, out _));
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
            properties: [VirtualNodeProperty.ScrollY(30)],
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
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(20, -1, out _));

        // Point inside first button bounds AND inside viewport clip
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(20, 10, out var firstId));
        Assert.Equal(new ActionId(100), firstId);

        // Second button: y=38, bottom=78. Clip bottom=60.
        // Point at y=45 is inside button (38..78) and inside clip (0..60)
        Assert.True(compositor.TryGetActionIdAtPhysicalPixel(20, 45, out var secondId));
        Assert.Equal(new ActionId(101), secondId);

        // Point at y=65 is inside button (38..78) but outside clip (0..60)
        Assert.False(compositor.TryGetActionIdAtPhysicalPixel(20, 65, out _));
    }

    private sealed class FakeWindow : INativeWindow
    {
        public IReadOnlyList<WindowContentElement> LastElements { get; private set; } = [];
        public ITextResolver LastTextResolver { get; private set; } = FrameTextArena.Empty;

        public string Title => "Test";

        public ScreenRegion Region { get; set; } = new(0, new PixelRectangle(0, 0, 960, 540));

        public bool ExternalRenderingEnabled { get; set; }

        public nint Handle => nint.Zero;

        public void Dispose() { }
        public void RunMessageLoop() { }
        public void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver)
        {
            LastElements = elements;
            LastTextResolver = textResolver;
        }
        public void Show() { }
        public event Action<int, int>? SizeChanged { add { } remove { } }
        public event Action<DisplayScale>? DpiChanged { add { } remove { } }
    }

    private static RenderFrameBatch CreateSingleRectFrame()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        return new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3))
            ]), 1),
            [],
            resources);
    }

    private static RenderFrameBatch CreateTwoRectFrame()
    {
        var resources = FrameDrawingResources.Rent();
        resources.Seal();
        return new RenderFrameBatch(
            new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3)),
                new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(120, 0, 100, 80), Color: DrawColor.Opaque(4, 5, 6))
            ]), 2),
            [],
            resources);
    }

    private static CompositionAnimationPlan CreateAnimationPlan(int commandCount)
    {
        return CreateAnimationPlan(new CompositionLayerId(1), commandStart: 0, commandCount: commandCount);
    }

    private static CompositionAnimationPlan CreateAnimationPlan(
        CompositionLayerId layerId,
        int commandStart,
        int commandCount)
    {
        return new CompositionAnimationPlan(new CompositionLayerAnimation(
            layerId,
            CommandStart: commandStart,
            CommandCount: commandCount,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            CompositionTransformAnimation.Identity,
            CompositionScalarAnimation.Constant(1f)));
    }

    private static CompositionAnimationDeclaration CreatePresentationSetDeclaration(NodeKey targetKey)
    {
        return CreatePresentationSetDeclaration(targetKey, default, null);
    }

    private static CompositionAnimationDeclaration CreatePresentationSetDeclaration(
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId,
        CompositionAnimationMarker[]? markers)
    {
        return new CompositionAnimationDeclaration(
            targetKey,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(10)),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(0, 20),
                CompositionScalarAnimation.Constant(0)),
            CompositionScalarAnimation.Constant(1f),
            instanceId,
            markers);
    }

    private static VirtualNode CreatePresentationSetRoot()
    {
        return VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeFactory.Rectangle(
                new NodeKey(22),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)),
            VirtualNodeFactory.Rectangle(
                new NodeKey(23),
                VirtualNodeProperty.Width(100),
                VirtualNodeProperty.Height(40),
                VirtualNodeProperty.LayerOpacity(1),
                VirtualNodeProperty.TranslateX(0),
                VirtualNodeProperty.TranslateY(0)));
    }

    private static CompositionScrollPresentationPlan CreateScrollPresentationPlan(int commandCount)
    {
        return new CompositionScrollPresentationPlan(new CompositionScrollLayerAnimation(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: commandCount,
            new PixelRectangle(0, 0, 100, 80),
            RetainedScrollY: 0,
            MaxScrollY: 100,
            new CompositionAnimationTimeline(
                CompositionTimestamp.Zero,
                CompositionDuration.FromStopwatchTicks(1)),
            new CompositionScalarAnimation(0, 20)));
    }

    private static void AssertCompositionStatus(
        in CompositionExecutionStatus status,
        CompositionExecutionKind kind,
        CompositionExecutionSkipReason skipReason,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        int layerCount,
        int commandCount)
    {
        Assert.Equal(kind, status.Kind);
        Assert.Equal(skipReason, status.SkipReason);
        Assert.Equal(skipReason != CompositionExecutionSkipReason.None, status.IsSkipped);
        Assert.Equal(requiredCapabilities, status.RequiredCapabilities);
        Assert.Equal(backendCapabilities, status.BackendCapabilities);
        Assert.Equal(CompositionFramePacing.SoftwareTimer, status.FramePacing);
        Assert.Equal(layerCount, status.LayerCount);
        Assert.Equal(commandCount, status.CommandCount);
    }

    private sealed class FixedCompositionClockSource(CompositionTimestamp timestamp) : ICompositionClockSource
    {
        public CompositionTimestamp TimestampNow() => timestamp;
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

    private sealed class EmptyTranslator : IPatchBatchTranslator
    {
        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            var resources = FrameDrawingResources.Rent();
            resources.Seal();
            return new RenderFrameBatch(new DrawCommandBatch(new ArrayMemoryOwner<DrawCommand>([]), 0), [], resources);
        }
    }

    private sealed class ClipCapabilityBackend(DrawingBackendClipMode clipMode) : IDrawingBackend, IClipScissorCapability
    {
        public DrawingBackendClipMode ClipMode { get; } = clipMode;

        public void BeginFrame(in FrameContext frameContext) { }
        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources) { }
        public void EndFrame() { }
        public void Dispose() { }
    }

    private sealed class CompositionTrackingBackend : IDrawingBackend, ICompositionDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public FrameContext LastBeginFrameContext { get; private set; }
        public CompositionFrame LastCompositionFrame { get; private set; }
        public IFrameResourceResolver? LastCompositionResources { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BeginFrame(in FrameContext frameContext)
        {
            LastBeginFrameContext = frameContext;
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            LastCompositionFrame = compositionFrame;
            LastCompositionResources = resources;
            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: CountTranslatedCommands(compositionFrame),
                OpacityAppliedCommands: CountOpacityCommands(compositionFrame));
        }

        public void EndFrame() { }

        public void Dispose() { }

        private static int CountTranslatedCommands(in CompositionFrame frame)
        {
            var count = 0;
            for (var i = 0; i < frame.LayerCount; i++)
            {
                var layer = frame.GetLayer(i);
                if (!layer.Transform.IsIdentity)
                {
                    count += layer.CommandCount;
                }
            }

            return count;
        }

        private static int CountOpacityCommands(in CompositionFrame frame)
        {
            var count = 0;
            for (var i = 0; i < frame.LayerCount; i++)
            {
                var layer = frame.GetLayer(i);
                if (!layer.Opacity.IsOpaque)
                {
                    count += layer.CommandCount;
                }
            }

            return count;
        }
    }

    private struct EvenRuntimeEventMarkerMapper : ICompositionMarkerEventMapper<int>
    {
        public int MapCount { get; private set; }

        public CompositionMarkerMappedMessage<int> Map(in CompositionAnimationMarkerEvent markerEvent)
        {
            MapCount++;
            return markerEvent.RuntimeEventId.Value % 2 == 0
                ? CompositionMarkerMappedMessage<int>.FromMessage((int)markerEvent.RuntimeEventId.Value)
                : CompositionMarkerMappedMessage<int>.Unmapped;
        }
    }

    private sealed class RecordingMarkerDispatcher : IMessageDispatcher<int>
    {
        public List<int> Messages { get; } = [];

        public void Dispatch(int message)
        {
            Messages.Add(message);
        }
    }

    private sealed class ConfigurableCompositionBackend(CompositionBackendCapabilities capabilities) : IDrawingBackend, ICompositionDrawingBackend
    {
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities { get; } = capabilities;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: 0,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }
        public void Dispose() { }
    }

    private sealed class DeviceLostCompositionBackend : IDrawingBackend, ICompositionDrawingBackend, IDeviceRecovery
    {
        private bool _deviceRemoved;
        private bool _throwOnNextComposition = true;

        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public bool IsDeviceRemoved => _deviceRemoved;
        public bool RecoveryAttempted { get; private set; }
        public bool RecoverySucceeded { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BeginFrame(in FrameContext frameContext) { }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            if (_throwOnNextComposition)
            {
                _deviceRemoved = true;
                throw new InvalidOperationException("Device removed during composition.");
            }

            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: 0,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }

        public bool TryRecover()
        {
            RecoveryAttempted = true;
            _deviceRemoved = false;
            _throwOnNextComposition = false;
            RecoverySucceeded = true;
            return true;
        }

        public void Dispose() { }
    }

    private sealed class BlockingCompositionBackend : IDrawingBackend, ICompositionDrawingBackend
    {
        private int _blockNextBeginFrame;
        private TaskCompletionSource _blockedBeginFrame = NewCompletionSource();
        private TaskCompletionSource _releaseBeginFrame = NewCompletionSource();

        public int BeginFrameCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public int ExecuteCompositionCount { get; private set; }
        public CompositionBackendCapabilities CompositionCapabilities => CompositionBackendCapabilities.TransformOpacity | CompositionBackendCapabilities.ScrollPresentation | CompositionBackendCapabilities.MultiLayer;

        public void BlockNextBeginFrame()
        {
            _blockedBeginFrame = NewCompletionSource();
            _releaseBeginFrame = NewCompletionSource();
            Volatile.Write(ref _blockNextBeginFrame, 1);
        }

        public Task WaitForBlockedBeginFrameAsync(CancellationToken cancellationToken)
        {
            return _blockedBeginFrame.Task.WaitAsync(cancellationToken);
        }

        public void ReleaseBlockedBeginFrame()
        {
            _releaseBeginFrame.TrySetResult();
        }

        public void BeginFrame(in FrameContext frameContext)
        {
            BeginFrameCount++;
            if (Interlocked.Exchange(ref _blockNextBeginFrame, 0) == 0)
            {
                return;
            }

            _blockedBeginFrame.TrySetResult();
            _releaseBeginFrame.Task.GetAwaiter().GetResult();
        }

        public void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources)
        {
            ExecuteCount++;
        }

        public CompositionBackendExecutionResult ExecuteComposition(
            ReadOnlySpan<DrawCommand> commands,
            IFrameResourceResolver resources,
            in CompositionFrame compositionFrame)
        {
            ExecuteCompositionCount++;
            return new CompositionBackendExecutionResult(
                D3D12Backed: true,
                LayerCount: compositionFrame.LayerCount,
                CommandCount: commands.Length,
                TranslatedCommands: 0,
                OpacityAppliedCommands: 0);
        }

        public void EndFrame() { }

        public void Dispose() { }

        private static TaskCompletionSource NewCompletionSource()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
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
