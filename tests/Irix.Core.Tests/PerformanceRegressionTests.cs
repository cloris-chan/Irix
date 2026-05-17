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
    public void Diff_layout_record_warm_path_allocation_baseline_is_split_by_stage()
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

        for (var i = 0; i < 8; i++)
        {
            _ = BuildMeasuredRoot(title, button, row, rectangleWidth: 128 + i);
            using var patch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
            var warmRetained = new RetainedTree(previousTree);
            warmRetained.Apply(patch);
            var warmLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
            var warmRecord = recorder.Record(warmLayout.Elements, warmLayout.DirtyElementRanges, snapshot);
            using var warmRects = new FrameRenderList<D3D12Renderer2D.RectData>();
            using var warmTexts = new FrameRenderList<D3D12TextRenderer.TextData>();
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, viewport.Width, viewport.Height),
                warmRecord.Commands.Memory.Span,
                warmRecord.Resources,
                new DisplayScale(1.5f, 1.5f),
                warmRects,
                warmTexts);
            ReleaseRecordResult(warmRecord);
        }

        var buildViewAllocated = MeasureAllocatedBytes(() =>
        {
            var root = BuildMeasuredRoot(title, button, row, rectangleWidth: 144);
            GC.KeepAlive(root);
        });

        var diffAllocated = MeasureAllocatedBytes(() =>
        {
            using var patch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
            GC.KeepAlive(patch.Count);
        });

        using var retainedApplyPatch = VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree);
        var retainedApplyAllocated = MeasureAllocatedBytes(() =>
        {
            var retained = new RetainedTree(previousTree);
            var result = retained.Apply(retainedApplyPatch);
            GC.KeepAlive(result.Dirty.Count);
            GC.KeepAlive(retained.Tree.Root.Kind);
        });

        var layoutAllocated = MeasureAllocatedBytes(() =>
        {
            var result = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
            GC.KeepAlive(result.Elements.Count);
            GC.KeepAlive(result.DirtyElementRanges.Count);
        });

        var recordLayout = layoutBuilder.BuildLayoutTree(nextRoot, viewport, dirtyNodes);
        var recordAllocated = MeasureAllocatedBytes(() =>
        {
            var result = recorder.Record(recordLayout.Elements, recordLayout.DirtyElementRanges, snapshot);
            GC.KeepAlive(result.Commands.Count);
            GC.KeepAlive(result.DirtyCommandRanges.Count);
            ReleaseRecordResult(result);
        });

        var executeRecord = recorder.Record(recordLayout.Elements, recordLayout.DirtyElementRanges, snapshot);
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRenderer.TextData>();
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
        var d3d12ExecuteAllocated = MeasureAllocatedBytes(() =>
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

        var message = $"Warm path allocation baseline: buildView={buildViewAllocated:N0} bytes, diff={diffAllocated:N0} bytes, retainedApply={retainedApplyAllocated:N0} bytes, layout={layoutAllocated:N0} bytes, record={recordAllocated:N0} bytes, d3d12Execute={d3d12ExecuteAllocated:N0} bytes.";
        Console.WriteLine(message);
        TestContext.Current.SendDiagnosticMessage(message);

        Assert.True(buildViewAllocated < 16 * 1024, $"BuildView warm path allocated {buildViewAllocated:N0} bytes.");
        Assert.True(diffAllocated < 16 * 1024, $"Diff warm path allocated {diffAllocated:N0} bytes.");
        Assert.True(retainedApplyAllocated < 8 * 1024, $"Retained apply warm path allocated {retainedApplyAllocated:N0} bytes.");
        Assert.True(layoutAllocated < 32 * 1024, $"Layout warm path allocated {layoutAllocated:N0} bytes.");
        Assert.True(recordAllocated < 64 * 1024, $"Record warm path allocated {recordAllocated:N0} bytes.");
        Assert.Equal(0, d3d12ExecuteAllocated);
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
