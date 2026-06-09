using System.Buffers;
using System.Collections.Concurrent;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class BatchOwnershipTests
{
    private readonly VirtualTextArena _arena = new();
    [Fact]
    public void PatchBatch_dispose_releases_owner_memory()
    {
        var owner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)))
        ]);
        var batch = new PatchBatch(owner, 1);

        batch.Dispose();

        Assert.Equal(1, owner.DisposeCallCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = batch.Memory);
    }

    [Fact]
    public void DrawCommandBatch_dispose_releases_owner_memory()
    {
        var owner = new TrackingMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32))
        ]);
        var batch = new DrawCommandBatch(owner, 1);

        batch.Dispose();

        Assert.Equal(1, owner.DisposeCallCount);
        Assert.Equal(0, batch.Memory.Length);
    }

    [Fact]
    public void PooledArrayMemoryOwner_dispose_releases_memory()
    {
        var owner = PooledArrayMemoryOwner<DrawCommand>.Rent(4);

        Assert.Equal(4, owner.Memory.Length);

        owner.Dispose();

        Assert.Equal(0, owner.Memory.Length);
    }

    [Fact]
    public async Task CompositorLoop_disposes_patch_and_draw_batches_after_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)))
        ]);
        var drawOwner = new TrackingMemoryOwner<DrawCommand>(
        [
            new DrawCommand(
                DrawCommandKind.DrawTextRun,
                Rect: new DrawRect(16, 16, 928, 32))
        ]);
        var translator = new FakeTranslator(drawOwner);
        var compositor = new RecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var patchBatch = new PatchBatch(patchOwner, 1);

        await loop.PublishAsync(patchBatch, cancellationToken);
        await compositor.WaitForRenderAsync(cancellationToken);
        await WaitForConditionAsync(() => patchOwner.DisposeCallCount == 1 && drawOwner.DisposeCallCount == 1, cancellationToken);

        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, patchOwner.DisposeCallCount);
        Assert.Equal(1, drawOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_render_requests_trigger_translation_and_rendering()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var drawOwner = new TrackingMemoryOwner<DrawCommand>([]);
        var translator = new FakeTranslator(drawOwner);
        var compositor = new RecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        await loop.RequestRenderAsync(cancellationToken);

        await compositor.WaitForRenderAsync(cancellationToken);

        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(1, compositor.RenderCallCount);
    }

    [Fact]
    public async Task CompositorLoop_request_render_and_wait_completes_after_render_async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new BlockingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        var renderTask = loop.RequestRenderAndWaitAsync(cancellationToken).AsTask();
        await compositor.WaitForRenderCountAsync(1, cancellationToken);

        Assert.False(renderTask.IsCompleted);

        compositor.Release();
        await renderTask.WaitAsync(cancellationToken);

        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(1, compositor.RenderCallCount);
    }

    [Fact]
    public async Task CompositorLoop_publish_and_wait_render_completes_after_patch_render_async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new BlockingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)))
        ]);
        var patchBatch = new PatchBatch(patchOwner, 1);

        var renderTask = loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken).AsTask();
        await compositor.WaitForRenderCountAsync(1, cancellationToken);

        Assert.False(renderTask.IsCompleted);
        Assert.Equal(1, translator.TranslateCallCount);

        compositor.Release();
        await renderTask.WaitAsync(cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, patchOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_publish_and_wait_retained_frame_stages_without_render_async()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new StageRecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)))
        ]);
        var patchBatch = new PatchBatch(patchOwner, 1);

        await loop.PublishAndWaitRetainedFrameAsync(patchBatch, cancellationToken);

        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(0, compositor.RenderCallCount);
        Assert.Equal(1, compositor.StageCallCount);
        Assert.Equal(RetainedFrameStagePresentationMode.RenderActiveScrollPresentationAfterStage, compositor.LastPresentationMode);
        Assert.Equal(1, patchOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_retained_frame_scroll_presentation_start_is_atomic_before_render_request()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new SnapshotTranslator();
        var compositor = new StageScrollPresentationOrderingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 0", new NodeKey(1)))
        ]);
        var patchBatch = new PatchBatch(patchOwner, 1);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(100),
                CompositionDuration.Zero),
            new CompositionScalarAnimation(0, 54));

        var publishTask = loop.PublishRetainedFrameAndStartCompositionScrollPresentationAsync(
            patchBatch,
            declaration,
            cancellationToken).AsTask();
        var renderTask = loop.RequestRenderAndWaitAsync(cancellationToken).AsTask();

        await Task.WhenAll(publishTask, renderTask).WaitAsync(cancellationToken);

        Assert.Equal(["Stage", "Install", "Tick", "Render"], compositor.Events);
        Assert.DoesNotContain("StageActivePresentationTick", compositor.Events);
        Assert.Equal(2, translator.TranslateCallCount);
        Assert.Equal(1, compositor.StageCallCount);
        Assert.Equal(1, compositor.InstallCount);
        Assert.Equal(1, compositor.TickCount);
        Assert.Equal(1, compositor.RenderCallCount);
        Assert.True(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, patchOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_retained_frame_scroll_presentation_retarget_holds_previous_visual_through_layout_invalidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new InvalidatingSnapshotTranslator(
            new CompositionRenderInvalidation(CompositionRenderInvalidationKind.LayoutAffecting));
        var compositor = new StageScrollPresentationOrderingCompositor();
        var clock = new ManualCompositionClockSource(CompositionTimestamp.FromStopwatchTicks(100));
        await using var loop = new CompositorLoop(translator, compositor, ownershipProvider: null, clock);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var initialDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(100),
                CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(0, 54));
        var retargetDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(120),
                CompositionDuration.Zero),
            new CompositionScalarAnimation(18, 108));
        var patchOwner = new TrackingMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Count: 1", new NodeKey(1)))
        ]);
        var patchBatch = new PatchBatch(patchOwner, 1);

        await loop.StartCompositionScrollPresentationAsync(initialDeclaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        clock.Set(CompositionTimestamp.FromStopwatchTicks(120));
        var sample = await loop.SampleAndHoldCompositionScrollPresentationAsync(new NodeKey(1), cancellationToken);
        await loop.PublishRetainedFrameAndStartCompositionScrollPresentationAsync(
            patchBatch,
            retargetDeclaration,
            cancellationToken);

        Assert.True(sample.HasValue);
        Assert.Equal(0, sample.PresentedScrollY);
        Assert.Equal(["Install", "Tick", "Stage", "Install", "Tick"], compositor.Events);
        Assert.DoesNotContain("StageActivePresentationTick", compositor.Events);
        Assert.Equal(1, translator.TranslateCallCount);
        Assert.Equal(1, compositor.StageCallCount);
        Assert.Equal(2, compositor.InstallCount);
        Assert.Equal(2, compositor.TickCount);
        Assert.Equal(0, compositor.ClearCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(108, presentedScrollY);
        Assert.Equal(1, patchOwner.DisposeCallCount);
    }

    [Fact]
    public async Task CompositorLoop_scroll_presentation_scheduler_installs_and_ticks_until_idle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now();
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromStopwatchTicks(1)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        await loop.WaitForScrollPresentationIdleAsync(cancellationToken);

        Assert.Equal(1, compositor.InstallCount);
        Assert.True(compositor.TickCount >= 2);
        Assert.Equal(compositor.TickCount, loop.ScrollPresentationTickCount);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(54, presentedScrollY);
        Assert.False(loop.HasActiveScrollPresentation(new NodeKey(1)));
    }

    [Fact]
    public async Task CompositorLoop_scroll_presentation_uses_injected_composition_clock_source_for_ticks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        var clock = new ManualCompositionClockSource(CompositionTimestamp.FromStopwatchTicks(100));
        await using var loop = new CompositorLoop(translator, compositor, ownershipProvider: null, clock);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(
                CompositionTimestamp.FromStopwatchTicks(100),
                CompositionDuration.FromStopwatchTicks(100)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        Assert.Equal(1, compositor.TickCount);
        Assert.Equal(CompositionTimestamp.FromStopwatchTicks(100), compositor.LastTickTimestamp);
        Assert.True(loop.HasActiveScrollPresentation(new NodeKey(1)));

        clock.Set(CompositionTimestamp.FromStopwatchTicks(150));
        await WaitForConditionAsync(() => compositor.TickCount >= 2, cancellationToken);

        Assert.Equal(CompositionTimestamp.FromStopwatchTicks(150), compositor.LastTickTimestamp);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(27, presentedScrollY);

        await loop.CancelCompositionScrollPresentationAsync(cancellationToken);
    }

    [Fact]
    public async Task CompositorLoop_completed_scroll_presentation_retains_sample_but_is_not_active_for_input()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.Zero),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);

        Assert.False(loop.HasActiveScrollPresentation(new NodeKey(1)));
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(54, presentedScrollY);
        Assert.False(loop.HasActiveScrollPresentation(new NodeKey(404)));
    }

    [Fact]
    public async Task CompositorLoop_cancel_scroll_presentation_clears_active_schedule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var ticksAfterStart = loop.ScrollPresentationTickCount;
        await loop.CancelCompositionScrollPresentationAsync(cancellationToken);
        await loop.WaitForScrollPresentationIdleAsync(cancellationToken);
        await Task.Delay(20, cancellationToken);

        Assert.Equal(1, compositor.ClearCount);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.Explicit, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(ticksAfterStart, loop.ScrollPresentationTickCount);
    }

    [Fact]
    public async Task CompositorLoop_explicit_cancel_skips_stale_delayed_scroll_tick_after_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var oldStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(30);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(oldStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        Assert.Equal(1, compositor.TickCount);

        var staleSkipsBeforeCancel = loop.ScrollPresentationStaleDelayedTickSkipCount;
        await loop.CancelCompositionScrollPresentationAsync(cancellationToken);
        var tickCountAfterCancel = compositor.TickCount;

        await WaitForConditionAsync(
            () => loop.ScrollPresentationStaleDelayedTickSkipCount > staleSkipsBeforeCancel,
            cancellationToken);

        Assert.Equal(1, compositor.ClearCount);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.Explicit, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(1, tickCountAfterCancel);
        Assert.Equal(tickCountAfterCancel, compositor.TickCount);
        Assert.Equal(staleSkipsBeforeCancel + 1, loop.ScrollPresentationStaleDelayedTickSkipCount);
    }

    [Fact]
    public async Task CompositorLoop_lost_scroll_presentation_plan_skips_stale_delayed_tick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var oldStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(30);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(oldStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        Assert.Equal(1, compositor.TickCount);
        Assert.True(loop.HasActiveScrollPresentation(new NodeKey(1)));

        var staleSkipsBeforePlanLoss = loop.ScrollPresentationStaleDelayedTickSkipCount;
        compositor.LosePresentationWithoutClear();
        var tickCountAfterPlanLoss = compositor.TickCount;

        await WaitForConditionAsync(
            () => loop.ScrollPresentationStaleDelayedTickSkipCount > staleSkipsBeforePlanLoss,
            cancellationToken);
        await loop.WaitForScrollPresentationIdleAsync(cancellationToken);

        Assert.Equal(0, compositor.ClearCount);
        Assert.False(loop.HasActiveScrollPresentation(new NodeKey(1)));
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(tickCountAfterPlanLoss, compositor.TickCount);
        Assert.Equal(staleSkipsBeforePlanLoss + 1, loop.ScrollPresentationStaleDelayedTickSkipCount);
    }

    [Fact]
    public async Task CompositorLoop_sample_and_hold_scroll_presentation_returns_latest_presented_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        await WaitForConditionAsync(() => compositor.TickCount >= 2, cancellationToken);

        var sample = await loop.SampleAndHoldCompositionScrollPresentationAsync(new NodeKey(1), cancellationToken);
        await loop.WaitForScrollPresentationIdleAsync(cancellationToken);
        var tickCountAfterSample = compositor.TickCount;
        await Task.Delay(20, cancellationToken);

        Assert.True(sample.HasValue);
        Assert.True(sample.PresentedScrollY > 0);
        Assert.Equal(0, compositor.ClearCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(sample.PresentedScrollY, presentedScrollY);
        Assert.Equal(tickCountAfterSample, compositor.TickCount);
    }

    [Fact]
    public async Task CompositorLoop_sample_and_hold_without_active_presentation_returns_empty_sample_without_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        var sample = await loop.SampleAndHoldCompositionScrollPresentationAsync(new NodeKey(1), cancellationToken);
        await loop.WaitForScrollPresentationIdleAsync(cancellationToken);

        Assert.False(sample.HasValue);
        Assert.Equal(0, sample.PresentedScrollY);
        Assert.Equal(0, compositor.ClearCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task CompositorLoop_sample_and_hold_scroll_presentation_completes_existing_idle_waiter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var idleWait = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();

        Assert.Equal(1, compositor.TickCount);
        Assert.False(idleWait.IsCompleted);

        var sample = await loop.SampleAndHoldCompositionScrollPresentationAsync(new NodeKey(1), cancellationToken);

        await idleWait.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.True(sample.HasValue);
        Assert.Equal(0, sample.PresentedScrollY);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(sample.PresentedScrollY, presentedScrollY);
        Assert.Equal(0, compositor.ClearCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
    }

    [Fact]
    public async Task CompositorLoop_sample_and_hold_skips_stale_delayed_tick_after_replacement_segment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var oldStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(30);
        var oldDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(oldStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(oldDeclaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        Assert.Equal(1, compositor.TickCount);

        var staleSkipsBeforeHold = loop.ScrollPresentationStaleDelayedTickSkipCount;
        var sample = await loop.SampleAndHoldCompositionScrollPresentationAsync(new NodeKey(1), cancellationToken);
        var replacementDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.Zero),
            new CompositionScalarAnimation((float)sample.PresentedScrollY, 12));
        await loop.StartCompositionScrollPresentationAsync(replacementDeclaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var tickCountAfterReplacement = compositor.TickCount;

        await WaitForConditionAsync(
            () => loop.ScrollPresentationStaleDelayedTickSkipCount > staleSkipsBeforeHold,
            cancellationToken);

        Assert.True(sample.HasValue);
        Assert.Equal(2, tickCountAfterReplacement);
        Assert.Equal(tickCountAfterReplacement, compositor.TickCount);
        Assert.Equal(staleSkipsBeforeHold + 1, loop.ScrollPresentationStaleDelayedTickSkipCount);
        Assert.Equal(0, compositor.ClearCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(12, presentedScrollY);
    }

    [Fact]
    public async Task CompositorLoop_superseded_scroll_presentation_completes_existing_idle_waiter_and_skips_old_delayed_tick()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var oldStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
        var oldDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(oldStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(oldDeclaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var oldIdleWait = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();

        Assert.Equal(1, compositor.TickCount);
        Assert.False(oldIdleWait.IsCompleted);

        var staleSkipsBeforeReplacement = loop.ScrollPresentationStaleDelayedTickSkipCount;
        var replacementStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(5000);
        var replacementDeclaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(replacementStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(12, 42));
        await loop.StartCompositionScrollPresentationAsync(replacementDeclaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var tickCountAfterReplacement = compositor.TickCount;

        await oldIdleWait.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var replacementPresentedScrollY));
        Assert.Equal(12, replacementPresentedScrollY);

        await WaitForConditionAsync(
            () => loop.ScrollPresentationStaleDelayedTickSkipCount > staleSkipsBeforeReplacement,
            cancellationToken);

        Assert.Equal(2, tickCountAfterReplacement);
        Assert.Equal(tickCountAfterReplacement, compositor.TickCount);
        Assert.Equal(staleSkipsBeforeReplacement + 1, loop.ScrollPresentationStaleDelayedTickSkipCount);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
    }

    [Fact]
    public async Task CompositorLoop_explicit_cancel_completes_all_existing_scroll_presentation_idle_waiters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var idleWait1 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();
        var idleWait2 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();

        Assert.Equal(1, compositor.TickCount);
        Assert.False(idleWait1.IsCompleted);
        Assert.False(idleWait2.IsCompleted);

        await loop.CancelCompositionScrollPresentationAsync(cancellationToken);

        await idleWait1.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        await idleWait2.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(1, compositor.ClearCount);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.Explicit, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task CompositorLoop_render_invalidation_completes_all_existing_scroll_presentation_idle_waiters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new OneShotInvalidatingTranslator(
            CompositionRenderInvalidation.FromLayoutRebuildReason(LayoutRebuildReason.ViewportChanged));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        var idleWait1 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();
        var idleWait2 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();

        Assert.Equal(1, compositor.TickCount);
        Assert.False(idleWait1.IsCompleted);
        Assert.False(idleWait2.IsCompleted);

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);

        await idleWait1.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        await idleWait2.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, compositor.ClearCount);
        Assert.False(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.ViewportChanged, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task CompositorLoop_render_invalidation_after_explicit_cancel_does_not_double_count_scroll_presentation_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new OneShotInvalidatingTranslator(
            CompositionRenderInvalidation.FromLayoutRebuildReason(LayoutRebuildReason.ViewportChanged));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(30);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));

        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        await loop.CancelCompositionScrollPresentationAsync(cancellationToken);
        var cancelCountAfterExplicitCancel = loop.ScrollPresentationCancelCount;

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.False(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, cancelCountAfterExplicitCancel);
        Assert.Equal(cancelCountAfterExplicitCancel, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.Explicit, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task CompositorLoop_dispose_completes_all_existing_scroll_presentation_idle_waiters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new ScrollPresentationSchedulerCompositor();
        var loop = new CompositorLoop(translator, compositor);
        var disposed = false;
        try
        {
            var pipeline = new RenderPipeline();
            using var frame = pipeline.Build(
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 1,
                    properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                    children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
                new PixelRectangle(0, 0, 240, 120),
                _arena.GetOrCreateSnapshot());
            var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
            var declaration = new CompositionScrollPresentationDeclaration(
                new NodeKey(1),
                new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
                new CompositionScalarAnimation(0, 54));

            await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
            var idleWait1 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();
            var idleWait2 = loop.WaitForScrollPresentationIdleAsync(cancellationToken).AsTask();

            Assert.Equal(1, compositor.TickCount);
            Assert.False(idleWait1.IsCompleted);
            Assert.False(idleWait2.IsCompleted);

            await loop.DisposeAsync();
            disposed = true;

            await idleWait1.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            await idleWait2.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken);
            Assert.Equal(0, loop.ScrollPresentationCancelCount);
            Assert.Equal(ScrollPresentationCancellationReason.Dispose, loop.ScrollPresentationCancellationDiagnostics.LastReason);
            Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
            Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.DisposeCount);
        }
        finally
        {
            if (!disposed)
            {
                await loop.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData((int)CompositionRenderInvalidationKind.ScrollPresentation)]
    [InlineData((int)CompositionRenderInvalidationKind.ViewportChanged)]
    [InlineData((int)CompositionRenderInvalidationKind.TreeStructure)]
    [InlineData((int)CompositionRenderInvalidationKind.LayoutAffecting)]
    [InlineData((int)CompositionRenderInvalidationKind.MaxScrollChanged)]
    public async Task CompositorLoop_render_invalidation_cancels_scroll_presentation_before_render(int invalidationKindValue)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var invalidationKind = (CompositionRenderInvalidationKind)invalidationKindValue;
        var translator = new InvalidatingTranslator(new CompositionRenderInvalidation(invalidationKind));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));
        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, compositor.ClearCount);
        Assert.False(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(invalidationKind, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
    }

    [Fact]
    public async Task CompositorLoop_text_invalidation_preserves_scroll_presentation_when_retained_target_continues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new PreservingScrollPresentationTranslator(
            new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TextSizeAffecting));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var start = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(250);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(start, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));
        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(0, compositor.ClearCount);
        Assert.True(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(0, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.None, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.None, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.True(loop.TryGetPresentedScrollY(new NodeKey(1), out var presentedScrollY));
        Assert.Equal(0, presentedScrollY);
    }

    [Fact]
    public async Task CompositorLoop_text_invalidation_without_retained_snapshot_falls_back_to_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new InvalidatingTranslator(
            new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TextSizeAffecting));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(CompositionTimestamp.Now(), CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));
        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);

        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, compositor.ClearCount);
        Assert.False(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.TextSizeAffecting, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
    }

    [Fact]
    public async Task CompositorLoop_render_invalidation_skips_stale_delayed_scroll_tick_after_cancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new OneShotInvalidatingTranslator(
            CompositionRenderInvalidation.FromLayoutRebuildReason(LayoutRebuildReason.ViewportChanged));
        var compositor = new ScrollPresentationSchedulerCompositor();
        await using var loop = new CompositorLoop(translator, compositor);
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());
        var oldStart = CompositionTimestamp.Now() + CompositionDuration.FromMilliseconds(30);
        var declaration = new CompositionScrollPresentationDeclaration(
            new NodeKey(1),
            new CompositionAnimationTimeline(oldStart, CompositionDuration.FromMilliseconds(160)),
            new CompositionScalarAnimation(0, 54));
        await loop.StartCompositionScrollPresentationAsync(declaration, pipeline.LastRetainedInputSnapshot!, cancellationToken);
        Assert.Equal(1, compositor.TickCount);

        var staleSkipsBeforeInvalidation = loop.ScrollPresentationStaleDelayedTickSkipCount;
        var patchBatch = new PatchBatch(new ArrayMemoryOwner<VirtualNodePatch>(
        [
            new VirtualNodePatch(
                VirtualNodePatchOperation.ReplaceRoot,
                0,
                VirtualNodeBuilder.Text(_arena, "Next", new NodeKey(1)))
        ]), 1);
        await loop.PublishAndWaitRenderAsync(patchBatch, cancellationToken);
        var tickCountAfterInvalidation = compositor.TickCount;

        await WaitForConditionAsync(
            () => loop.ScrollPresentationStaleDelayedTickSkipCount > staleSkipsBeforeInvalidation,
            cancellationToken);

        Assert.Equal(1, compositor.RenderCallCount);
        Assert.Equal(1, compositor.ClearCount);
        Assert.False(compositor.PresentationActiveDuringLastRender);
        Assert.Equal(1, loop.ScrollPresentationCancelCount);
        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.ViewportChanged, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
        Assert.False(loop.TryGetPresentedScrollY(new NodeKey(1), out _));
        Assert.Equal(1, tickCountAfterInvalidation);
        Assert.Equal(tickCountAfterInvalidation, compositor.TickCount);
        Assert.Equal(staleSkipsBeforeInvalidation + 1, loop.ScrollPresentationStaleDelayedTickSkipCount);
    }

    [Fact]
    public async Task CompositorLoop_skips_regular_empty_diffs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new RecordingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        // Publish a regular empty diff (Count == 0, Kind == Diff) �?should be skipped
        var emptyOwner = new ArrayMemoryOwner<VirtualNodePatch>([]);
        var emptyBatch = new PatchBatch(emptyOwner, 0);
        await loop.PublishAsync(emptyBatch, cancellationToken);

        // Give the processing loop time to consume the batch
        await Task.Delay(100, cancellationToken);

        Assert.Equal(0, translator.TranslateCallCount);
        Assert.Equal(0, compositor.RenderCallCount);
        Assert.Equal(PatchBatchKind.Diff, emptyBatch.Kind);
    }

    [Fact]
    public async Task CompositorLoop_render_requests_coalesce_and_reschedule_after_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new AllocatingTranslator();
        var compositor = new BlockingCompositor();
        await using var loop = new CompositorLoop(translator, compositor);

        // First render request: loop processes it, compositor blocks on RenderAsync (count=1)
        await loop.RequestRenderAsync(cancellationToken);
        await compositor.WaitForRenderCountAsync(1, cancellationToken);

        // While first render is blocked, enqueue 3 more render requests.
        // Request #2: _renderRequestDirty=0�?, _renderRequestQueued=0�?, writes batch
        // Requests #3,#4: _renderRequestDirty already 1, ScheduleRenderRequest CAS fails (queued=1)
        await loop.RequestRenderAsync(cancellationToken);
        await loop.RequestRenderAsync(cancellationToken);
        await loop.RequestRenderAsync(cancellationToken);

        // Release render #1 �?loop picks up coalesced batch, clears queued+dirty, renders (count=2, blocks)
        compositor.Release();
        await compositor.WaitForRenderCountAsync(2, cancellationToken);

        // Release render #2 �?channel is empty, no reschedule (dirty was cleared when batch was picked up).
        // This is correct coalescing: 3 extra requests �?1 coalesced render.
        // Total renders: 2 (initial + 1 coalesced).
        compositor.Release();
        await Task.Delay(50, cancellationToken);

        Assert.Equal(2, translator.TranslateCallCount);
        Assert.Equal(2, compositor.RenderCallCount);
    }

    private sealed class TrackingMemoryOwner<T>(T[] buffer) : IMemoryOwner<T>
    {
        private T[]? _buffer = buffer;

        public int DisposeCallCount { get; private set; }

        public Memory<T> Memory => _buffer ?? Memory<T>.Empty;

        public void Dispose()
        {
            DisposeCallCount++;
            _buffer = null;
        }
    }

    private sealed class FakeTranslator(TrackingMemoryOwner<DrawCommand> drawOwner) : IPatchBatchTranslator
    {
        public int TranslateCallCount { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            return new RenderFrameBatch(new DrawCommandBatch(drawOwner, 1), []);
        }
    }

    private sealed class AllocatingTranslator : IPatchBatchTranslator
    {
        public int TranslateCallCount { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            var owner = new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 928, 32))
            ]);
            return new RenderFrameBatch(new DrawCommandBatch(owner, 1), []);
        }
    }

    private sealed class SnapshotTranslator : IPatchBatchTranslator, IRetainedInputSnapshotProvider
    {
        public int TranslateCallCount { get; private set; }
        public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            var arena = new VirtualTextArena();
            var pipeline = new RenderPipeline();
            var frame = pipeline.Build(
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 1,
                    properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                    children: [VirtualNodeBuilder.Text(arena, "Item", new NodeKey(2))]),
                new PixelRectangle(0, 0, 240, 120),
                arena.GetOrCreateSnapshot());
            LastRetainedInputSnapshot = pipeline.LastRetainedInputSnapshot;
            return frame;
        }
    }

    private sealed class InvalidatingSnapshotTranslator(CompositionRenderInvalidation invalidation) :
        IPatchBatchTranslator,
        IRetainedInputSnapshotProvider,
        ICompositionInvalidationProvider
    {
        public int TranslateCallCount { get; private set; }
        public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; private set; }
        public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            LastCompositionInvalidation = invalidation;
            var arena = new VirtualTextArena();
            var pipeline = new RenderPipeline();
            var frame = pipeline.Build(
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 1,
                    properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(108)],
                    children: [VirtualNodeBuilder.Text(arena, "Item", new NodeKey(2))]),
                new PixelRectangle(0, 0, 240, 120),
                arena.GetOrCreateSnapshot());
            LastRetainedInputSnapshot = pipeline.LastRetainedInputSnapshot;
            return frame;
        }
    }

    private sealed class InvalidatingTranslator(CompositionRenderInvalidation invalidation) : IPatchBatchTranslator, ICompositionInvalidationProvider
    {
        public int TranslateCallCount { get; private set; }
        public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            LastCompositionInvalidation = invalidation;
            var owner = new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 928, 32))
            ]);
            return new RenderFrameBatch(new DrawCommandBatch(owner, 1), []);
        }
    }

    private sealed class PreservingScrollPresentationTranslator(CompositionRenderInvalidation invalidation) :
        IPatchBatchTranslator,
        ICompositionInvalidationProvider,
        IRetainedInputSnapshotProvider
    {
        private readonly VirtualTextArena _arena = new();
        private readonly RenderPipeline _pipeline = new();
        private int _frame;

        public int TranslateCallCount { get; private set; }
        public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }
        public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            LastCompositionInvalidation = invalidation;
            _arena.BeginFrame();
            var label = _frame++ == 0 ? "Count: 0" : "Count: 1";
            var frame = _pipeline.Build(
                new VirtualNode(
                    VirtualNodeKind.ScrollContainer,
                    key: 1,
                    properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(54)],
                    children:
                    [
                        VirtualNodeBuilder.Text(_arena, label, new NodeKey(2)),
                        VirtualNodeBuilder.Text(_arena, "Row 1", new NodeKey(3)),
                        VirtualNodeBuilder.Text(_arena, "Row 2", new NodeKey(4)),
                        VirtualNodeBuilder.Text(_arena, "Row 3", new NodeKey(5))
                    ]),
                new PixelRectangle(0, 0, 240, 120),
                _arena.GetOrCreateSnapshot());
            LastRetainedInputSnapshot = _pipeline.LastRetainedInputSnapshot;
            return frame;
        }
    }

    private sealed class OneShotInvalidatingTranslator(CompositionRenderInvalidation invalidation) : IPatchBatchTranslator, ICompositionInvalidationProvider
    {
        private bool _hasInvalidated;

        public int TranslateCallCount { get; private set; }
        public CompositionRenderInvalidation LastCompositionInvalidation { get; private set; }

        public RenderFrameBatch Translate(PatchBatch patchBatch)
        {
            TranslateCallCount++;
            LastCompositionInvalidation = _hasInvalidated ? CompositionRenderInvalidation.None : invalidation;
            _hasInvalidated = true;
            var owner = new ArrayMemoryOwner<DrawCommand>(
            [
                new DrawCommand(
                    DrawCommandKind.DrawTextRun,
                    Rect: new DrawRect(16, 16, 928, 32))
            ]);
            return new RenderFrameBatch(new DrawCommandBatch(owner, 1), []);
        }
    }

    private sealed class RecordingCompositor : ICompositor
    {
        private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RenderCallCount { get; private set; }

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            _rendered.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public Task WaitForRenderAsync(CancellationToken cancellationToken)
        {
            return _rendered.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BlockingCompositor : ICompositor
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _pending = new();
        private int _renderCallCount;

        public int RenderCallCount => Volatile.Read(ref _renderCallCount);

        public async ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _renderCallCount);
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(tcs);
            await tcs.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            if (_pending.TryDequeue(out var tcs))
            {
                tcs.TrySetResult();
            }
        }

        public Task WaitForRenderCountAsync(int expectedCount, CancellationToken cancellationToken)
        {
            return WaitForConditionAsync(() => RenderCallCount >= expectedCount, cancellationToken);
        }
    }

    private sealed class StageRecordingCompositor : ICompositor, IRetainedFrameStagingCompositor
    {
        public int RenderCallCount { get; private set; }
        public int StageCallCount { get; private set; }
        public RetainedFrameStagePresentationMode LastPresentationMode { get; private set; }

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRetainedFrameAsync(
            RenderFrameBatch renderFrameBatch,
            RetainedRenderFrameSegmentOwnership? ownership,
            CancellationToken cancellationToken = default,
            RetainedFrameStagePresentationMode presentationMode = RetainedFrameStagePresentationMode.RenderActiveScrollPresentationAfterStage)
        {
            StageCallCount++;
            LastPresentationMode = presentationMode;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StageScrollPresentationOrderingCompositor : ICompositor, IRetainedFrameStagingCompositor, ICompositionScrollPresentationCompositor
    {
        private CompositionScrollPresentationDeclaration _declaration;
        private bool _presentationActive;
        private double _presentedScrollY;

        public List<string> Events { get; } = [];
        public int RenderCallCount { get; private set; }
        public int StageCallCount { get; private set; }
        public int InstallCount { get; private set; }
        public int ClearCount { get; private set; }
        public int TickCount { get; private set; }
        public bool PresentationActiveDuringLastRender { get; private set; }

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            Events.Add("Render");
            RenderCallCount++;
            PresentationActiveDuringLastRender = _presentationActive;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRetainedFrameAsync(
            RenderFrameBatch renderFrameBatch,
            RetainedRenderFrameSegmentOwnership? ownership,
            CancellationToken cancellationToken = default,
            RetainedFrameStagePresentationMode presentationMode = RetainedFrameStagePresentationMode.RenderActiveScrollPresentationAfterStage)
        {
            Events.Add("Stage");
            StageCallCount++;
            if (_presentationActive
                && presentationMode == RetainedFrameStagePresentationMode.RenderActiveScrollPresentationAfterStage)
            {
                Events.Add("StageActivePresentationTick");
            }

            return ValueTask.CompletedTask;
        }

        public void SetCompositionScrollPresentationDeclaration(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot)
        {
            Events.Add("Install");
            _declaration = declaration;
            _presentationActive = true;
            InstallCount++;
        }

        public bool TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot)
        {
            return declaration.TryResolve(snapshot, out _);
        }

        public void ClearCompositionScrollPresentation()
        {
            _presentationActive = false;
            _presentedScrollY = 0;
            ClearCount++;
        }

        public ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
            CompositionTimestamp timestamp,
            CancellationToken cancellationToken = default)
        {
            Events.Add("Tick");
            TickCount++;
            _presentedScrollY = _declaration.PresentedScrollY.Evaluate(_declaration.Timeline.ProgressAt(timestamp));
            return ValueTask.FromResult(default(CompositionBackendExecutionResult));
        }

        public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
        {
            if (_presentationActive && targetKey == _declaration.TargetKey)
            {
                presentedScrollY = _presentedScrollY;
                return true;
            }

            presentedScrollY = 0;
            return false;
        }
    }

    private sealed class ScrollPresentationSchedulerCompositor : ICompositor, ICompositionScrollPresentationCompositor
    {
        private CompositionScrollPresentationDeclaration _declaration;
        private double _presentedScrollY;
        private bool _presentationActive;

        public int RenderCallCount { get; private set; }
        public int InstallCount { get; private set; }
        public int ClearCount { get; private set; }
        public long TickCount { get; private set; }
        public bool PresentationActiveDuringLastRender { get; private set; }
        public CompositionTimestamp LastTickTimestamp { get; private set; }

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            PresentationActiveDuringLastRender = _presentationActive;
            return ValueTask.CompletedTask;
        }

        public void SetCompositionScrollPresentationDeclaration(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot)
        {
            _declaration = declaration;
            _presentationActive = true;
            InstallCount++;
        }

        public bool TryPrepareCompositionScrollPresentationRetainedFrameUpdate(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot)
        {
            return declaration.TryResolve(snapshot, out _);
        }

        public void ClearCompositionScrollPresentation()
        {
            _presentationActive = false;
            _presentedScrollY = 0;
            ClearCount++;
        }

        public void LosePresentationWithoutClear()
        {
            _presentationActive = false;
            _presentedScrollY = 0;
        }

        public ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
            CompositionTimestamp timestamp,
            CancellationToken cancellationToken = default)
        {
            TickCount++;
            LastTickTimestamp = timestamp;
            _presentedScrollY = _declaration.PresentedScrollY.Evaluate(_declaration.Timeline.ProgressAt(timestamp));
            return ValueTask.FromResult(default(CompositionBackendExecutionResult));
        }

        public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
        {
            if (_presentationActive && targetKey == _declaration.TargetKey)
            {
                presentedScrollY = _presentedScrollY;
                return true;
            }

            presentedScrollY = 0;
            return false;
        }
    }

    private sealed class ManualCompositionClockSource(CompositionTimestamp timestamp) : ICompositionClockSource
    {
        private long _stopwatchTicks = timestamp.StopwatchTicks;

        public CompositionTimestamp TimestampNow() => CompositionTimestamp.FromStopwatchTicks(Volatile.Read(ref _stopwatchTicks));

        public void Set(CompositionTimestamp timestamp)
        {
            Volatile.Write(ref _stopwatchTicks, timestamp.StopwatchTicks);
        }
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
