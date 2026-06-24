#if IRIX_DIAGNOSTICS
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class RenderSteadyStateAllocationDiagnosticRunner
{
    internal const int DefaultFrameCount = 30;
    private const int RectangleDfsIndex = 2;
    private static readonly int[] RectangleDirtyNodes = [RectangleDfsIndex];
    private static readonly PixelRectangle Viewport = new(0, 0, 960, 540);

    internal static void Run(TextWriter output, int frameCount = DefaultFrameCount)
    {
        var snapshot = Capture(frameCount);

        output.WriteLine("=== Render Steady-State Allocation Diagnostic ===");
        output.WriteLine(FormatHeader(snapshot));
        output.WriteLine(FormatScenario(snapshot.WarmReuse));
        output.WriteLine(FormatScenario(snapshot.StyleOnly));
        output.WriteLine(FormatScenario(snapshot.LayoutChange));
        output.WriteLine(FormatFocus(snapshot));
        output.WriteLine("=== render steady-state allocation diagnostic complete ===");
    }

    internal static RenderSteadyStateAllocationSnapshot Capture(int frameCount = DefaultFrameCount)
    {
        frameCount = frameCount > 0 ? frameCount : DefaultFrameCount;
        var trees = PrebuildScenarioTrees();

        var warmReuse = MeasureScenario(
            "warm-reuse",
            frameCount,
            trees.WarmRoot,
            trees.WarmRoot,
            dirtyNodes: null);
        var styleOnly = MeasureScenario(
            "style-only",
            frameCount,
            trees.StyleRootA,
            trees.StyleRootB,
            RectangleDirtyNodes);
        var layoutChange = MeasureScenario(
            "layout-change",
            frameCount,
            trees.LayoutRootA,
            trees.LayoutRootB,
            RectangleDirtyNodes);

        return new RenderSteadyStateAllocationSnapshot(
            frameCount,
            PrebuiltTrees: true,
            KnownResources: true,
            CapacityReserved: false,
            warmReuse,
            styleOnly,
            layoutChange);
    }

    internal static string FormatHeader(RenderSteadyStateAllocationSnapshot snapshot)
    {
        return "render-steady-state "
            + "scope=core-render-pipeline "
            + $"prebuiltTrees={FormatBool(snapshot.PrebuiltTrees)} "
            + $"knownResources={FormatBool(snapshot.KnownResources)} "
            + $"capacityReserved={FormatBool(snapshot.CapacityReserved)} "
            + $"frames={snapshot.FrameCount} "
            + $"targetMet={FormatBool(snapshot.TargetMet)} "
            + $"totalBytes={snapshot.TotalBytes}";
    }

    internal static string FormatScenario(RenderSteadyStateAllocationScenario scenario)
    {
        var pipelineAttribution = scenario.PipelineAttribution;
        return "render-steady-state "
            + $"scenario={scenario.Name} "
            + $"frames={scenario.FrameCount} "
            + $"threadBytes={scenario.ThreadAllocatedBytes} "
            + $"threadPerFrame={PerFrame(scenario.ThreadAllocatedBytes, scenario.FrameCount)} "
            + $"targetMet={FormatBool(scenario.TargetMet)} "
            + $"layoutReason={scenario.LastLayoutRebuildReason} "
            + $"layoutRebuilds={scenario.LayoutRebuildCountDelta} "
            + $"pipelineBytes={pipelineAttribution.TotalBytes} "
            + $"classify={pipelineAttribution.ClassificationBytes} "
            + $"layout={pipelineAttribution.LayoutBytes} "
            + $"record={pipelineAttribution.RecordBytes} "
            + $"hitTargets={pipelineAttribution.HitTargetsBytes} "
            + $"snapshot={pipelineAttribution.SnapshotBytes} "
            + $"retainedFrame={pipelineAttribution.RetainedFrameBytes}";
    }

    internal static string FormatFocus(RenderSteadyStateAllocationSnapshot snapshot)
    {
        var largest = snapshot.WarmReuse;
        if (snapshot.StyleOnly.ThreadAllocatedBytes > largest.ThreadAllocatedBytes)
        {
            largest = snapshot.StyleOnly;
        }

        if (snapshot.LayoutChange.ThreadAllocatedBytes > largest.ThreadAllocatedBytes)
        {
            largest = snapshot.LayoutChange;
        }

        return "render-steady-state "
            + $"focus largestScenario={largest.Name} "
            + $"largestBytes={largest.ThreadAllocatedBytes} "
            + $"largestPerFrame={PerFrame(largest.ThreadAllocatedBytes, largest.FrameCount)} "
            + $"totalBytes={snapshot.TotalBytes} "
            + $"targetMet={FormatBool(snapshot.TargetMet)}";
    }

    private static RenderSteadyStateAllocationScenario MeasureScenario(
        string name,
        int frameCount,
        VirtualNodeTree firstTree,
        VirtualNodeTree secondTree,
        IReadOnlyList<int>? dirtyNodes)
    {
        var pipeline = new RenderPipeline();
        using (pipeline.BuildWithAllocationAttribution(
            firstTree.Root,
            Viewport,
            firstTree.TextSnapshot,
            null,
            null,
            default,
            out _))
        {
        }

        CollectBeforeMeasurement();

        var previousRoot = firstTree.Root;
        var previousSnapshot = firstTree.TextSnapshot;
        var pipelineAttribution = default(RenderPipelineBuildAllocationAttribution);
        var layoutRebuildCountBefore = pipeline.LayoutRebuildCount;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (var frame = 0; frame < frameCount; frame++)
        {
            var nextTree = (frame & 1) == 0 ? secondTree : firstTree;
            using var batch = pipeline.BuildWithAllocationAttribution(
                nextTree.Root,
                Viewport,
                nextTree.TextSnapshot,
                dirtyNodes,
                previousSnapshot,
                previousRoot,
                out var frameAttribution);
            pipelineAttribution = pipelineAttribution.Add(frameAttribution);
            previousRoot = nextTree.Root;
            previousSnapshot = nextTree.TextSnapshot;
        }

        var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        return new RenderSteadyStateAllocationScenario(
            name,
            frameCount,
            allocatedAfter - allocatedBefore,
            pipelineAttribution,
            pipeline.LastLayoutRebuildReason,
            pipeline.LayoutRebuildCount - layoutRebuildCountBefore);
    }

    private static ScenarioTrees PrebuildScenarioTrees()
    {
        var arena = new VirtualTextArena();
        var title = arena.AddText("Render steady-state baseline".AsSpan());
        var detail = arena.AddText("Known resources stay stable".AsSpan());
        var snapshot = arena.GetOrCreateSnapshot();

        return new ScenarioTrees(
            new VirtualNodeTree(
                BuildRoot(title, detail, rectangleWidth: 132, Color.FromSrgb(35, 96, 142)),
                snapshot),
            new VirtualNodeTree(
                BuildRoot(title, detail, rectangleWidth: 132, Color.FromSrgb(35, 96, 142)),
                snapshot),
            new VirtualNodeTree(
                BuildRoot(title, detail, rectangleWidth: 132, Color.FromSrgb(76, 112, 53)),
                snapshot),
            new VirtualNodeTree(
                BuildRoot(title, detail, rectangleWidth: 132, Color.FromSrgb(35, 96, 142)),
                snapshot),
            new VirtualNodeTree(
                BuildRoot(title, detail, rectangleWidth: 164, Color.FromSrgb(35, 96, 142)),
                snapshot));
    }

    private static VirtualNode BuildRoot(
        TextContentResource title,
        TextContentResource detail,
        int rectangleWidth,
        Color rectangleColor)
    {
        var titleNode = VirtualNodeFactory.Text(title, new NodeKey(2));
        var rectangleNode = VirtualNodeFactory.Rectangle(
            new NodeKey(3),
            VirtualNodeProperty.Width(rectangleWidth),
            VirtualNodeProperty.Height(48),
            VirtualNodeProperty.Background(rectangleColor));
        var detailNode = VirtualNodeFactory.Text(detail, new NodeKey(4));
        return VirtualNodeFactory.Container(new NodeKey(1), titleNode, rectangleNode, detailNode);
    }

    private static void CollectBeforeMeasurement()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long PerFrame(long bytes, int frameCount) => frameCount > 0 ? bytes / frameCount : 0;

    private static string FormatBool(bool value) => value ? "true" : "false";

    private readonly struct ScenarioTrees(
        VirtualNodeTree WarmRoot,
        VirtualNodeTree StyleRootA,
        VirtualNodeTree StyleRootB,
        VirtualNodeTree LayoutRootA,
        VirtualNodeTree LayoutRootB)
    {
        public VirtualNodeTree WarmRoot { get; } = WarmRoot;
        public VirtualNodeTree StyleRootA { get; } = StyleRootA;
        public VirtualNodeTree StyleRootB { get; } = StyleRootB;
        public VirtualNodeTree LayoutRootA { get; } = LayoutRootA;
        public VirtualNodeTree LayoutRootB { get; } = LayoutRootB;
    }
}

internal readonly struct RenderSteadyStateAllocationSnapshot(
    int FrameCount,
    bool PrebuiltTrees,
    bool KnownResources,
    bool CapacityReserved,
    RenderSteadyStateAllocationScenario WarmReuse,
    RenderSteadyStateAllocationScenario StyleOnly,
    RenderSteadyStateAllocationScenario LayoutChange)
{
    public int FrameCount { get; } = FrameCount;
    public bool PrebuiltTrees { get; } = PrebuiltTrees;
    public bool KnownResources { get; } = KnownResources;
    public bool CapacityReserved { get; } = CapacityReserved;
    public RenderSteadyStateAllocationScenario WarmReuse { get; } = WarmReuse;
    public RenderSteadyStateAllocationScenario StyleOnly { get; } = StyleOnly;
    public RenderSteadyStateAllocationScenario LayoutChange { get; } = LayoutChange;
    public long TotalBytes => WarmReuse.ThreadAllocatedBytes + StyleOnly.ThreadAllocatedBytes + LayoutChange.ThreadAllocatedBytes;
    public bool TargetMet => WarmReuse.TargetMet && StyleOnly.TargetMet && LayoutChange.TargetMet;
}

internal readonly struct RenderSteadyStateAllocationScenario(
    string Name,
    int FrameCount,
    long ThreadAllocatedBytes,
    RenderPipelineBuildAllocationAttribution PipelineAttribution,
    LayoutRebuildReason LastLayoutRebuildReason,
    long LayoutRebuildCountDelta)
{
    public string Name { get; } = Name;
    public int FrameCount { get; } = FrameCount;
    public long ThreadAllocatedBytes { get; } = ThreadAllocatedBytes;
    public RenderPipelineBuildAllocationAttribution PipelineAttribution { get; } = PipelineAttribution;
    public LayoutRebuildReason LastLayoutRebuildReason { get; } = LastLayoutRebuildReason;
    public long LayoutRebuildCountDelta { get; } = LayoutRebuildCountDelta;
    public bool TargetMet => ThreadAllocatedBytes == 0;
}
#endif
