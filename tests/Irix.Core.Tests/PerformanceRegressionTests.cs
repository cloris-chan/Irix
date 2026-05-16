using Irix.Drawing;
using Irix.Platform;
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

        AssertAllocationParity(
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

        AssertAllocationParity(
            builderAllocated,
            inlineHelperAllocated,
            "Builder pipeline path",
            "inline helper path");
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

    private static void AssertAllocationParity(
        long actualAllocated,
        long baselineAllocated,
        string actualName,
        string baselineName)
    {
        const long allowedDeltaBytes = 128 * 1024;
        var delta = Math.Abs(actualAllocated - baselineAllocated);

        Assert.True(delta <= allowedDeltaBytes,
            $"{actualName} allocated {actualAllocated:N0} bytes; {baselineName} allocated {baselineAllocated:N0} bytes; delta {delta:N0} exceeded {allowedDeltaBytes:N0} bytes.");
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
