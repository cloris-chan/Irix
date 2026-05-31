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
    public async Task CompositorLoop_render_invalidation_cancels_scroll_presentation_before_render()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new InvalidatingTranslator(CompositionRenderInvalidation.ScrollPresentation);
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
        Assert.Equal(CompositionRenderInvalidationKind.ScrollPresentation, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(0, loop.ScrollPresentationCancellationDiagnostics.ExplicitCount);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
    }

    [Fact]
    public async Task CompositorLoop_render_invalidation_records_max_scroll_changed_reason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var translator = new InvalidatingTranslator(CompositionRenderInvalidation.MaxScrollChanged);
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

        Assert.Equal(ScrollPresentationCancellationReason.RenderInvalidation, loop.ScrollPresentationCancellationDiagnostics.LastReason);
        Assert.Equal(CompositionRenderInvalidationKind.MaxScrollChanged, loop.ScrollPresentationCancellationDiagnostics.LastInvalidationKind);
        Assert.Equal(1, loop.ScrollPresentationCancellationDiagnostics.RenderInvalidationCount);
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

        public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
        {
            RenderCallCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRetainedFrameAsync(
            RenderFrameBatch renderFrameBatch,
            RetainedRenderFrameSegmentOwnership? ownership,
            CancellationToken cancellationToken = default)
        {
            StageCallCount++;
            return ValueTask.CompletedTask;
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

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }
}
