using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Poc;
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
        TestContext.Current.SendDiagnosticMessage($"Mock backend average frame time: {compositor.AverageFrameTimeUs}us over {frameCount} frames.");
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
        TestContext.Current.SendDiagnosticMessage($"FrameDrawingResources warm pool allocation: {allocatedBytes:N0} bytes over {frameCount} frames.");
        Assert.True(allocatedBytes < 2_000_000, $"FrameDrawingResources warm pool allocated {allocatedBytes:N0} bytes over {frameCount} frames");
    }

    [Fact]
    public void VirtualNode_builder_authoring_path_matches_inline_helper_allocation_baseline()
    {
        const int iterations = 5_000;
        var childA = VirtualNodeFactory.Rectangle(new NodeKey(100), VirtualNodeProperty.Width(20), VirtualNodeProperty.Height(10));
        var childB = VirtualNodeFactory.Rectangle(new NodeKey(101), VirtualNodeProperty.Width(20), VirtualNodeProperty.Height(10));
        var childC = VirtualNodeFactory.Rectangle(new NodeKey(102), VirtualNodeProperty.Width(20), VirtualNodeProperty.Height(10));

        var inlineHelperAllocated = MeasureAllocatedBytes(() =>
        {
            VirtualNode node = default;
            for (var i = 0; i < iterations; i++)
            {
                node = VirtualNodeFactory.ScrollContainer(
                    new NodeKey((uint)i),
                    VirtualNodeFactory.Rectangle(new NodeKey(1), VirtualNodeProperty.Width(120), VirtualNodeProperty.Height(48)),
                    childA,
                    childB,
                    childC);
            }

            GC.KeepAlive(node);
        });

        var builderAllocated = MeasureAllocatedBytes(() =>
        {
            VirtualNode node = default;
            Span<VirtualNodeProperty> rectangleProperties = stackalloc VirtualNodeProperty[2];

            for (var i = 0; i < iterations; i++)
            {
                var propertyBuilder = new VirtualNodePropertyListBuilder(rectangleProperties);
                propertyBuilder.AddWidth(120);
                propertyBuilder.AddHeight(48);

                var rectangle = VirtualNodeFactory.Rectangle(new NodeKey(1), propertyBuilder.Written);

                var children = new VirtualNodeChildrenBuilder();
                children.Add(rectangle);
                children.Add(childA);
                children.Add(childB);
                children.Add(childC);

                node = VirtualNodeFactory.ScrollContainer(
                    new NodeKey((uint)i),
                    ReadOnlySpan<VirtualNodeProperty>.Empty,
                    ref children);
            }

            GC.KeepAlive(node);
        });

        TestContext.Current.SendDiagnosticMessage($"VirtualNode authoring allocation: builder={builderAllocated:N0} bytes, inline={inlineHelperAllocated:N0} bytes over {iterations} iterations.");
        AssertAllocationDoesNotRegress(
            builderAllocated,
            inlineHelperAllocated,
            "Builder authoring path",
            "inline helper path");
    }

    [Fact]
    public void VirtualNode_builder_pipeline_path_matches_inline_helper_allocation_baseline()
    {
        const int iterations = 500;
        var arena = new VirtualTextArena();
        var title = arena.AddText("Title".AsSpan());
        var button = arena.AddText("Click".AsSpan());
        var row = arena.AddText("Row".AsSpan());
        var snapshot = arena.GetOrCreateSnapshot();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var inlineHelperAllocated = MeasurePipelineAllocatedBytes(
            iterations,
            i => BuildInlineHelperPipelineRoot(i, title, button, row),
            snapshot,
            viewport);

        var builderAllocated = MeasurePipelineAllocatedBytes(
            iterations,
            i => BuildBuilderPipelineRoot(i, title, button, row),
            snapshot,
            viewport);

        TestContext.Current.SendDiagnosticMessage($"VirtualNode pipeline allocation: builder={builderAllocated:N0} bytes, inline={inlineHelperAllocated:N0} bytes over {iterations} iterations.");
        AssertAllocationDoesNotRegress(
            builderAllocated,
            inlineHelperAllocated,
            "Builder pipeline path",
            "inline helper path");
    }

    [Fact]
    public void Frame_stage_allocation_baseline_is_split_by_stage()
    {
        var arena = new VirtualTextArena();
        var title = arena.AddText("Title".AsSpan());
        var button = arena.AddText("Click".AsSpan());
        var row = arena.AddText("Row".AsSpan());
        var snapshot = arena.GetOrCreateSnapshot();
        var viewport = new PixelRectangle(0, 0, 960, 540);

        var previousRoot = BuildMeasuredRoot(title, button, row, rectangleWidth: 120);
        var nextRoot = BuildMeasuredRoot(title, button, row, rectangleWidth: 136);
        var previousTree = new VirtualNodeTree(previousRoot, snapshot);
        var nextTree = new VirtualNodeTree(nextRoot, snapshot);
        var dirtyNodes = new[] { 2 };
        var layoutBuilder = new LayoutTreeBuilder();
        var recorder = new DrawCommandRecorder();
        var counterApp = new CounterApplication();
        var counterModel = counterApp.Initialize();

        for (var i = 0; i < 8; i++)
        {
            _ = counterApp.BuildView(counterModel with { Count = i });
            _ = BuildMeasuredRoot(title, button, row, rectangleWidth: 128 + i);
            using var patch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
            var warmRetained = new RetainedTree(previousTree);
            warmRetained.Apply(patch);
            var warmFullLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport);
            var warmDirtyLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
            var warmFullRecord = recorder.Record(warmFullLayout.Elements, snapshot);
            ReleaseRecordResult(warmFullRecord);
            var warmDirtyRecord = recorder.Record(warmDirtyLayout.Elements, warmDirtyLayout.DirtyElementRanges, snapshot);
            using var warmRects = new FrameRenderList<D3D12Renderer2D.RectData>();
            using var warmTexts = new FrameRenderList<D3D12TextRenderer.TextData>();
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, viewport.Width, viewport.Height),
                warmDirtyRecord.Commands.Memory.Span,
                warmDirtyRecord.Resources,
                DisplayScale.Identity,
                warmRects,
                warmTexts);
            warmRects.Reset();
            warmTexts.Reset();
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, viewport.Width, viewport.Height),
                warmDirtyRecord.Commands.Memory.Span,
                warmDirtyRecord.Resources,
                new DisplayScale(1.5f, 1.5f),
                warmRects,
                warmTexts);
            ReleaseRecordResult(warmDirtyRecord);
            var warmPipeline = new RenderPipeline();
            using var warmFrame1 = warmPipeline.Build(nextRoot, viewport, snapshot);
            using var warmFrame2 = warmPipeline.Build(nextRoot, viewport, snapshot);
        }

        var buildViewAllocated = MeasureAllocatedBytes(() =>
        {
            var tree = counterApp.BuildView(counterModel with { Count = 100 });
            GC.KeepAlive(tree.Root.Kind);
            GC.KeepAlive(tree.TextSnapshot.Buffer.Length);
        });

        var diffAllocated = MeasureAllocatedBytes(() =>
        {
            using var patch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
            GC.KeepAlive(patch.Count);
        });

        using var retainedApplyForwardPatch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
        using var retainedApplyBackwardPatch = VirtualNodeDiffer.CreatePatchBatch(nextTree, previousTree);
        var retainedTree = new RetainedTree(previousTree);
        retainedTree.Apply(retainedApplyForwardPatch);
        retainedTree.Apply(retainedApplyBackwardPatch);
        var retainedApplyAllocated = MeasureAllocatedBytes(() =>
        {
            var result = retainedTree.Apply(retainedApplyForwardPatch);
            GC.KeepAlive(result.Dirty.Count);
            GC.KeepAlive(retainedTree.Tree.Root.Kind);
        });

        var layoutFullAllocated = MeasureAllocatedBytes(() =>
        {
            var result = layoutBuilder.BuildLayoutTree(nextRoot, viewport);
            GC.KeepAlive(result.Elements.Count);
            GC.KeepAlive(result.DirtyElementRanges.Count);
        });

        var layoutDirtyAllocated = MeasureAllocatedBytes(() =>
        {
            var result = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
            GC.KeepAlive(result.Elements.Count);
            GC.KeepAlive(result.DirtyElementRanges.Count);
        });

        var recordFullLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport);
        var recordFullAllocated = MeasureAllocatedBytes(() =>
        {
            var result = recorder.Record(recordFullLayout.Elements, snapshot);
            GC.KeepAlive(result.Commands.Count);
            GC.KeepAlive(result.DirtyCommandRanges.Count);
            ReleaseRecordResult(result);
        });

        var recordDirtyLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
        var recordDirtyAllocated = MeasureAllocatedBytes(() =>
        {
            var result = recorder.Record(recordDirtyLayout.Elements, recordDirtyLayout.DirtyElementRanges, snapshot);
            GC.KeepAlive(result.Commands.Count);
            GC.KeepAlive(result.DirtyCommandRanges.Count);
            ReleaseRecordResult(result);
        });

        var executeRecord = recorder.Record(recordDirtyLayout.Elements, recordDirtyLayout.DirtyElementRanges, snapshot);
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRenderer.TextData>();
        _ = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, viewport.Width, viewport.Height),
            executeRecord.Commands.Memory.Span,
            executeRecord.Resources,
            DisplayScale.Identity,
            rects,
            texts);
        rects.Reset();
        texts.Reset();
        _ = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, viewport.Width, viewport.Height),
            executeRecord.Commands.Memory.Span,
            executeRecord.Resources,
            new DisplayScale(1.5f, 1.5f),
            rects,
            texts);
        rects.Reset();
        texts.Reset();
        var d3d12Execute100Allocated = MeasureAllocatedBytes(() =>
        {
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, viewport.Width, viewport.Height),
                executeRecord.Commands.Memory.Span,
                executeRecord.Resources,
                DisplayScale.Identity,
                rects,
                texts);
        });
        rects.Reset();
        texts.Reset();
        var d3d12Execute150Allocated = MeasureAllocatedBytes(() =>
        {
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, viewport.Width, viewport.Height),
                executeRecord.Commands.Memory.Span,
                executeRecord.Resources,
                new DisplayScale(1.5f, 1.5f),
                rects,
                texts);
        });
        ReleaseRecordResult(executeRecord);

        var renderRequestPipeline = new RenderPipeline();
        using (renderRequestPipeline.Build(nextRoot, viewport, snapshot))
        {
        }

        var layoutRebuildCountBeforeRenderRequest = renderRequestPipeline.LayoutRebuildCount;
        var renderRequestAllocated = MeasureAllocatedBytes(() =>
        {
            using var frame = renderRequestPipeline.Build(nextRoot, viewport, snapshot);
            GC.KeepAlive(frame.Commands.Count);
            GC.KeepAlive(frame.HitTargets.Count);
        });

        var message =
            $"Warm path allocation baseline: buildView={buildViewAllocated:N0} bytes, diff={diffAllocated:N0} bytes, retainedApply={retainedApplyAllocated:N0} bytes, " +
            $"layoutFull={layoutFullAllocated:N0} bytes, layoutDirty={layoutDirtyAllocated:N0} bytes, recordFull={recordFullAllocated:N0} bytes, recordDirty={recordDirtyAllocated:N0} bytes, " +
            $"d3d12Execute100={d3d12Execute100Allocated:N0} bytes, d3d12Execute150={d3d12Execute150Allocated:N0} bytes, renderRequestReuse={renderRequestAllocated:N0} bytes.";
        Console.WriteLine(message);
        TestContext.Current.SendDiagnosticMessage(message);

        Assert.True(buildViewAllocated < 128 * 1024, $"BuildView warm path allocated {buildViewAllocated:N0} bytes.");
        Assert.True(diffAllocated < 16 * 1024, $"Diff warm path allocated {diffAllocated:N0} bytes.");
        Assert.True(retainedApplyAllocated < 8 * 1024, $"Retained apply warm path allocated {retainedApplyAllocated:N0} bytes.");
        Assert.True(layoutFullAllocated < 32 * 1024, $"Layout full warm path allocated {layoutFullAllocated:N0} bytes.");
        Assert.True(layoutDirtyAllocated < 32 * 1024, $"Layout dirty warm path allocated {layoutDirtyAllocated:N0} bytes.");
        Assert.True(recordFullAllocated < 64 * 1024, $"Record full warm path allocated {recordFullAllocated:N0} bytes.");
        Assert.True(recordDirtyAllocated < 64 * 1024, $"Record dirty warm path allocated {recordDirtyAllocated:N0} bytes.");
        Assert.Equal(0, d3d12Execute100Allocated);
        Assert.Equal(0, d3d12Execute150Allocated);
        Assert.True(renderRequestAllocated < 64 * 1024, $"Render request reuse warm path allocated {renderRequestAllocated:N0} bytes.");
        Assert.Equal(layoutRebuildCountBeforeRenderRequest, renderRequestPipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.None, renderRequestPipeline.LastLayoutRebuildReason);
    }

    [Fact]
    public void Render_request_reuse_allocation_is_attributed_by_stage()
    {
        var arena = new VirtualTextArena();
        var title = arena.AddText("Title".AsSpan());
        var button = arena.AddText("Click".AsSpan());
        var row = arena.AddText("Row".AsSpan());
        var snapshot = arena.GetOrCreateSnapshot();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root = BuildMeasuredRoot(title, button, row, rectangleWidth: 136);
        var pipeline = new RenderPipeline();
        using (pipeline.Build(root, viewport, snapshot))
        {
        }

        var layoutResult = pipeline.LastLayoutResult!;
        var layout = layoutResult.Elements;
        var recorder = new DrawCommandRecorder();
        var emptyClassifications = Array.Empty<LayoutDirtyClassification>();
        var emptyRanges = Array.Empty<(int Start, int Count)>();

        for (var i = 0; i < 8; i++)
        {
            var warmRecord = recorder.Record(layout, snapshot);
            var warmHitTargets = RenderPipeline.BuildHitTargets(layout);
            var warmSnapshot = RenderPipeline.CreateRetainedInputSnapshot(
                layoutResult,
                warmRecord.ElementCommandRanges,
                warmHitTargets,
                root,
                viewport,
                emptyClassifications,
                emptyRanges,
                warmRecord.DirtyCommandRanges,
                LayoutRebuildReason.None,
                snapshot,
                snapshot);
            GC.KeepAlive(warmSnapshot.HitTargets.Count);
            using var warmBatch = new RenderFrameBatch(warmRecord.Commands, warmHitTargets, warmRecord.Resources, warmRecord.DirtyCommandRanges);
        }

        var recordAllocated = MeasureAllocatedBytes(() =>
        {
            var result = recorder.Record(layout, snapshot);
            GC.KeepAlive(result.Commands.Count);
            GC.KeepAlive(result.ElementCommandRanges.Length);
            ReleaseRecordResult(result);
        });

        var buildHitTargetsAllocated = MeasureAllocatedBytes(() =>
        {
            var hitTargets = RenderPipeline.BuildHitTargets(layout);
            GC.KeepAlive(hitTargets.Count);
        });

        var snapshotRecord = recorder.Record(layout, snapshot);
        var snapshotHitTargets = RenderPipeline.BuildHitTargets(layout);
        var snapshotSpreadCopyAllocated = MeasureAllocatedBytes(() =>
        {
            var retainedInputSnapshot = RenderPipeline.CreateRetainedInputSnapshot(
                layoutResult,
                snapshotRecord.ElementCommandRanges,
                snapshotHitTargets,
                root,
                viewport,
                emptyClassifications,
                emptyRanges,
                snapshotRecord.DirtyCommandRanges,
                LayoutRebuildReason.None,
                snapshot,
                snapshot);
            GC.KeepAlive(retainedInputSnapshot.ElementCommandRanges.Length);
            GC.KeepAlive(retainedInputSnapshot.HitTargets.Count);
        });
        ReleaseRecordResult(snapshotRecord);

        var batchRecord = recorder.Record(layout, snapshot);
        var batchHitTargets = RenderPipeline.BuildHitTargets(layout);
        var batchResources = Assert.IsType<FrameDrawingResources>(batchRecord.Resources);
        batchResources.Retain();
        var renderFrameBatchAllocated = MeasureAllocatedBytes(() =>
        {
            var batch = new RenderFrameBatch(batchRecord.Commands, batchHitTargets, batchRecord.Resources, batchRecord.DirtyCommandRanges);
            GC.KeepAlive(batch.HitTargets.Count);
            if (batch.Resources is FrameDrawingResources resources)
            {
                resources.Release(batch.ResourceFrameId);
            }
        });
        batchResources.Release();
        batchRecord.Commands.Dispose();

        var layoutRebuildCountBeforeRenderRequest = pipeline.LayoutRebuildCount;
        var renderRequestTotalAllocated = MeasureAllocatedBytes(() =>
        {
            using var frame = pipeline.Build(root, viewport, snapshot);
            GC.KeepAlive(frame.Commands.Count);
            GC.KeepAlive(frame.HitTargets.Count);
        });

        var message =
            $"Render-request reuse allocation attribution: total={renderRequestTotalAllocated:N0} bytes, record={recordAllocated:N0} bytes, " +
            $"hitTargets={buildHitTargetsAllocated:N0} bytes, snapshotSpreadCopy={snapshotSpreadCopyAllocated:N0} bytes, renderFrameBatch={renderFrameBatchAllocated:N0} bytes.";
        Console.WriteLine(message);
        TestContext.Current.SendDiagnosticMessage(message);

        Assert.Equal(layoutRebuildCountBeforeRenderRequest, pipeline.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.None, pipeline.LastLayoutRebuildReason);
        Assert.True(renderRequestTotalAllocated < 64 * 1024, $"Render request reuse warm path allocated {renderRequestTotalAllocated:N0} bytes.");
        Assert.True(recordAllocated < 64 * 1024, $"Render-request record stage allocated {recordAllocated:N0} bytes.");
        Assert.True(buildHitTargetsAllocated < 8 * 1024, $"BuildHitTargets allocated {buildHitTargetsAllocated:N0} bytes.");
        Assert.True(snapshotSpreadCopyAllocated < 16 * 1024, $"Retained input snapshot spread/copy allocated {snapshotSpreadCopyAllocated:N0} bytes.");
        Assert.True(renderFrameBatchAllocated < 1 * 1024, $"RenderFrameBatch construction allocated {renderFrameBatchAllocated:N0} bytes.");
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static void AssertAllocationDoesNotRegress(
        long actualAllocated,
        long baselineAllocated,
        string actualName,
        string baselineName)
    {
        const long allowedDeltaBytes = 128 * 1024;
        var regression = actualAllocated - baselineAllocated;

        Assert.True(regression <= allowedDeltaBytes,
            $"{actualName} allocated {actualAllocated:N0} bytes; {baselineName} allocated {baselineAllocated:N0} bytes; regression {regression:N0} exceeded {allowedDeltaBytes:N0} bytes.");
    }

    private static long MeasurePipelineAllocatedBytes(
        int iterations,
        Func<int, VirtualNode> buildRoot,
        TextBufferSnapshot snapshot,
        PixelRectangle viewport)
    {
        var pipeline = new RenderPipeline();
        var previousTree = new VirtualNodeTree(buildRoot(0), snapshot);
        using (pipeline.Build(previousTree.Root, viewport, snapshot))
        {
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i <= iterations; i++)
        {
            var nextTree = new VirtualNodeTree(buildRoot(i), snapshot);
            using var patch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
            using var frame = pipeline.Build(patch.Root, viewport, snapshot);
            previousTree = nextTree;
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static VirtualNode BuildInlineHelperPipelineRoot(
        int iteration,
        TextNodeContent title,
        TextNodeContent button,
        TextNodeContent row) =>
        VirtualNodeFactory.ScrollContainer(
            new NodeKey(1),
            VirtualNodeFactory.Text(title, new NodeKey(2)),
            VirtualNodeFactory.Rectangle(new NodeKey(3), VirtualNodeProperty.Width(120 + (iteration & 7)), VirtualNodeProperty.Height(48)),
            VirtualNodeFactory.Button(button, new NodeKey(4), VirtualNodeProperty.Action(new ActionId(100))),
            VirtualNodeFactory.Text(row, new NodeKey(5)));

    private static VirtualNode BuildBuilderPipelineRoot(
        int iteration,
        TextNodeContent title,
        TextNodeContent button,
        TextNodeContent row)
    {
        Span<VirtualNodeProperty> rectangleStorage = stackalloc VirtualNodeProperty[2];
        var rectangleProperties = new VirtualNodePropertyListBuilder(rectangleStorage);
        rectangleProperties.AddWidth(120 + (iteration & 7));
        rectangleProperties.AddHeight(48);

        Span<VirtualNodeProperty> buttonStorage = stackalloc VirtualNodeProperty[1];
        var buttonProperties = new VirtualNodePropertyListBuilder(buttonStorage);
        buttonProperties.AddAction(new ActionId(100));

        var children = new VirtualNodeChildrenBuilder();
        children.Add(VirtualNodeFactory.Text(title, new NodeKey(2), ReadOnlySpan<VirtualNodeProperty>.Empty));
        children.Add(VirtualNodeFactory.Rectangle(new NodeKey(3), rectangleProperties.Written));
        children.Add(VirtualNodeFactory.Button(button, new NodeKey(4), buttonProperties.Written));
        children.Add(VirtualNodeFactory.Text(row, new NodeKey(5), ReadOnlySpan<VirtualNodeProperty>.Empty));

        return VirtualNodeFactory.ScrollContainer(new NodeKey(1), ReadOnlySpan<VirtualNodeProperty>.Empty, ref children);
    }

    private static VirtualNode BuildMeasuredRoot(
        TextNodeContent title,
        TextNodeContent button,
        TextNodeContent row,
        double rectangleWidth) =>
        VirtualNodeFactory.ScrollContainer(
            new NodeKey(1),
            VirtualNodeFactory.Text(title, new NodeKey(2)),
            VirtualNodeFactory.Rectangle(new NodeKey(3), VirtualNodeProperty.Width(rectangleWidth), VirtualNodeProperty.Height(48)),
            VirtualNodeFactory.Button(button, new NodeKey(4), VirtualNodeProperty.Action(new ActionId(100))),
            VirtualNodeFactory.Text(row, new NodeKey(5)));

    private static void ReleaseRecordResult(DrawCommandRecordResult result)
    {
        result.Commands.Dispose();
        if (result.Resources is FrameDrawingResources resources)
        {
            FrameDrawingResources.Return(resources);
        }
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
