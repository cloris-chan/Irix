using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Rendering;

namespace Irix.Poc;

internal static class TextCacheAllocationDiagnosticRunner
{
    internal static void Run(
        TextWriter output,
        int frameCount = 180,
        TextCompositionMode textCompositionMode = TextCompositionMode.GlyphAtlas,
        DisplayScale diagnosticScale = default)
    {
        using var platformHost = new WindowsPlatformHost();
        var screen = platformHost.Screens[0];
        var displayScale = diagnosticScale.Normalize();
        if (diagnosticScale == default)
        {
            displayScale = screen.Scale.Normalize();
        }
        using var window = platformHost.CreateSubViewport(CreatePrimaryWindowRegion(screen));

        using var d3d12Renderer = new D3D12Renderer(window.Handle, window.Region.PhysicalBounds.Width, window.Region.PhysicalBounds.Height);
        d3d12Renderer.TextCompositionMode = textCompositionMode;
        using var d3d12Backend = new D3D12DrawingBackend(d3d12Renderer);
        using var compositor = new DrawingBackendCompositor(d3d12Backend);
        compositor.SetViewport(window.Region.PhysicalBounds, displayScale);

        var translator = new WindowDrawCommandTranslator(
            window,
            () => _ = d3d12Renderer.ApplyPendingResize(),
            () =>
            {
                var bounds = window.Region.PhysicalBounds;
                return new PixelRectangle(bounds.X, bounds.Y, d3d12Renderer.Width, d3d12Renderer.Height);
            },
            postFrameCallback: null,
            displayScale: displayScale);

        output.WriteLine("=== D3D12 Text Cache / Allocation Diagnostics ===");
        output.WriteLine($"Frames per scenario: {frameCount}");
        output.WriteLine($"Display refresh: {screen.RefreshRateHz}Hz");
        output.WriteLine($"Display scale: {displayScale.ScaleX:0.##}x{displayScale.ScaleY:0.##}");
        output.WriteLine($"Text composition mode: {d3d12Renderer.TextCompositionMode}");
        output.WriteLine();

        var arena = new VirtualTextArena();
        RunScenario(output, "static", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (int i, DisplayScale _, out TreeAllocationAttribution treeFrameAttribution) =>
            BuildScenarioTree(arena, "Static cache baseline", scrollY: 0, measureAllocation: true, out treeFrameAttribution));
        RunScenario(output, "scroll", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (int i, DisplayScale _, out TreeAllocationAttribution treeFrameAttribution) =>
            BuildScenarioTree(arena, "Scrolling cache baseline", scrollY: i * 2, measureAllocation: true, out treeFrameAttribution));
        RunScenario(output, "scale-change", frameCount, d3d12Renderer, d3d12Backend, compositor, translator, displayScale, (int i, DisplayScale scale, out TreeAllocationAttribution treeFrameAttribution) =>
            BuildScenarioTree(arena, $"Scale cache baseline {scale.ScaleX:0.##}x", scrollY: 0, measureAllocation: true, out treeFrameAttribution), scaleChangeAtHalf: true);

        output.WriteLine("=== Text cache / allocation diagnostic complete ===");
    }

    private static void RunScenario(
        TextWriter output,
        string name,
        int frameCount,
        D3D12Renderer renderer,
        D3D12DrawingBackend backend,
        DrawingBackendCompositor compositor,
        WindowDrawCommandTranslator translator,
        DisplayScale displayScale,
        ScenarioTreeFactory treeFactory,
        bool scaleChangeAtHalf = false)
    {
        renderer.ResetTextDiagnostics();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var poolBefore = FrameDrawingResources.GetPoolDiagnostics();
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var attribution = default(AllocationAttribution);
        var treeAttribution = default(TreeAllocationAttribution);
        var translateAttribution = default(WindowTranslateAllocationAttribution);
        var previousTree = default(VirtualNodeTree);
        var hasPreviousTree = false;
        var activeScale = displayScale;

        for (var i = 0; i < frameCount; i++)
        {
            if (scaleChangeAtHalf && i == frameCount / 2)
            {
                activeScale = displayScale.ScaleX >= 2f || displayScale.ScaleY >= 2f
                    ? DisplayScale.Identity
                    : new DisplayScale(2f, 2f).Normalize();
                translator.SetDisplayScale(activeScale);
                compositor.SetViewport(new PixelRectangle(0, 0, renderer.Width, renderer.Height), activeScale);
            }

            var beforeTree = GC.GetTotalAllocatedBytes(false);
            var nextTree = treeFactory(i, activeScale, out var treeFrameAttribution);
            var afterTree = GC.GetTotalAllocatedBytes(false);
            attribution = attribution.AddTree(afterTree - beforeTree);
            treeAttribution = treeAttribution.Add(treeFrameAttribution);

            var beforeDiff = GC.GetTotalAllocatedBytes(false);
            using (var patch = hasPreviousTree
                ? VirtualNodeDiffer.CreatePatchBatch(previousTree, nextTree)
                : VirtualNodeDiffer.CreatePatchBatch(default, nextTree))
            {
                var afterDiff = GC.GetTotalAllocatedBytes(false);
                attribution = attribution.AddDiff(afterDiff - beforeDiff);

                var beforeTranslate = GC.GetTotalAllocatedBytes(false);
                using var batch = translator.Translate(patch, out var translateFrameAttribution);
                var afterTranslate = GC.GetTotalAllocatedBytes(false);
                attribution = attribution.AddTranslate(afterTranslate - beforeTranslate);
                translateAttribution = translateAttribution.Add(translateFrameAttribution);

                previousTree = nextTree;
                hasPreviousTree = true;

                var beforeRender = GC.GetTotalAllocatedBytes(false);
                compositor.RenderAsync(batch).AsTask().GetAwaiter().GetResult();
                var afterRender = GC.GetTotalAllocatedBytes(false);
                attribution = attribution.AddRender(afterRender - beforeRender);

                if (renderer.IsDeviceRemoved)
                {
                    output.WriteLine($"Scenario {name}: device removed at frame {i}: {renderer.DeviceError}");
                    break;
                }
            }
        }

        translator.SetDisplayScale(displayScale);
        compositor.SetViewport(new PixelRectangle(0, 0, renderer.Width, renderer.Height), displayScale);

        var allocatedAfter = GC.GetTotalAllocatedBytes(true);
        var poolDelta = FrameDrawingResources.GetPoolDiagnostics().Delta(poolBefore);
        output.WriteLine($"--- Scenario: {name} ---");
        var glyphAtlasDiagnostics = renderer.GetGlyphAtlasTextDiagnostics();
        if (glyphAtlasDiagnostics.HasValue)
        {
            output.WriteLine($"Glyph atlas: {glyphAtlasDiagnostics.Value.FormatSummary()}");
        }

        var allocatedBytes = allocatedAfter - allocatedBefore;
        output.WriteLine($"Allocation: total={allocatedBytes} bytes, perFrame={(frameCount > 0 ? allocatedBytes / frameCount : 0)} bytes");
        output.WriteLine(FormatAllocationAttribution(attribution, frameCount));
        output.WriteLine(FormatTreeAllocationAttribution(treeAttribution, frameCount));
        output.WriteLine(FormatTranslateAllocationAttribution(translateAttribution, frameCount));
        output.WriteLine(FormatPipelineAllocationAttribution(translateAttribution.PipelineAttribution, frameCount));
        output.WriteLine(FormatRecordAllocationAttribution(translateAttribution.PipelineAttribution.RecordAttribution, frameCount));
        output.WriteLine($"FrameDrawingResources: rents={poolDelta.RentCount}, created={poolDelta.CreatedCount}, reused={poolDelta.ReusedCount}, returns={poolDelta.ReturnCallCount}, returnedToPool={poolDelta.ReturnedToPoolCount}, retainedSkips={poolDelta.RetainedReturnSkipCount}, duplicateSkips={poolDelta.DuplicateReturnSkipCount}, staleSkips={poolDelta.StaleReturnSkipCount}, overflowDisposals={poolDelta.DisposedOverflowCount}, poolCount={poolDelta.PoolCount}");
        output.WriteLine();
    }

    internal static string FormatAllocationAttribution(AllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Allocation attribution: tree={attribution.TreeBytes} bytes ({PerFrame(attribution.TreeBytes, divisor)}/frame), diff={attribution.DiffBytes} bytes ({PerFrame(attribution.DiffBytes, divisor)}/frame), translate={attribution.TranslateBytes} bytes ({PerFrame(attribution.TranslateBytes, divisor)}/frame), render={attribution.RenderBytes} bytes ({PerFrame(attribution.RenderBytes, divisor)}/frame)";
    }

    internal static string FormatTreeAllocationAttribution(TreeAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Tree allocation: beginFrame={attribution.BeginFrameBytes} bytes ({PerFrame(attribution.BeginFrameBytes, divisor)}/frame), buildRoot={attribution.BuildRootBytes} bytes ({PerFrame(attribution.BuildRootBytes, divisor)}/frame), snapshot={attribution.SnapshotBytes} bytes ({PerFrame(attribution.SnapshotBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatTranslateAllocationAttribution(WindowTranslateAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Translate allocation: retainedApply={attribution.RetainedApplyBytes} bytes ({PerFrame(attribution.RetainedApplyBytes, divisor)}/frame), viewport={attribution.ViewportBytes} bytes ({PerFrame(attribution.ViewportBytes, divisor)}/frame), pipeline={attribution.PipelineBuildBytes} bytes ({PerFrame(attribution.PipelineBuildBytes, divisor)}/frame), feedback={attribution.FeedbackBytes} bytes ({PerFrame(attribution.FeedbackBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatPipelineAllocationAttribution(RenderPipelineBuildAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Pipeline allocation: classify={attribution.ClassificationBytes} bytes ({PerFrame(attribution.ClassificationBytes, divisor)}/frame), layout={attribution.LayoutBytes} bytes ({PerFrame(attribution.LayoutBytes, divisor)}/frame), record={attribution.RecordBytes} bytes ({PerFrame(attribution.RecordBytes, divisor)}/frame), hitTargets={attribution.HitTargetsBytes} bytes ({PerFrame(attribution.HitTargetsBytes, divisor)}/frame), snapshot={attribution.SnapshotBytes} bytes ({PerFrame(attribution.SnapshotBytes, divisor)}/frame), retainedFrame={attribution.RetainedFrameBytes} bytes ({PerFrame(attribution.RetainedFrameBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatRecordAllocationAttribution(DrawCommandRecordAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Record allocation: resources={attribution.ResourcesBytes} bytes ({PerFrame(attribution.ResourcesBytes, divisor)}/frame), styles={attribution.StylesBytes} bytes ({PerFrame(attribution.StylesBytes, divisor)}/frame), commandBuild={attribution.CommandBuildBytes} bytes ({PerFrame(attribution.CommandBuildBytes, divisor)}/frame), dirtyRanges={attribution.DirtyRangesBytes} bytes ({PerFrame(attribution.DirtyRangesBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    private static long PerFrame(long bytes, int frameCount) => frameCount > 0 ? bytes / frameCount : 0;

    private static long GetAllocatedBytes(bool enabled) => enabled ? GC.GetTotalAllocatedBytes(false) : 0;

    private static long AllocatedDelta(bool enabled, long before) => enabled ? GC.GetTotalAllocatedBytes(false) - before : 0;

    private delegate VirtualNodeTree ScenarioTreeFactory(int frameIndex, DisplayScale displayScale, out TreeAllocationAttribution attribution);

    private static VirtualNodeTree BuildScenarioTree(VirtualTextArena arena, string text, int scrollY)
        => BuildScenarioTree(arena, text, scrollY, measureAllocation: false, out _);

    private static VirtualNodeTree BuildScenarioTree(
        VirtualTextArena arena,
        string text,
        int scrollY,
        bool measureAllocation,
        out TreeAllocationAttribution attribution)
    {
        attribution = default;
        var beforeBeginFrame = GetAllocatedBytes(measureAllocation);
        arena.BeginFrame();
        attribution = attribution.WithBeginFrame(AllocatedDelta(measureAllocation, beforeBeginFrame));

        var beforeBuildRoot = GetAllocatedBytes(measureAllocation);
        var root = BuildRoot(arena, text, scrollY);
        attribution = attribution.WithBuildRoot(AllocatedDelta(measureAllocation, beforeBuildRoot));

        var beforeSnapshot = GetAllocatedBytes(measureAllocation);
        var snapshot = arena.GetOrCreateSnapshot();
        attribution = attribution.WithSnapshot(AllocatedDelta(measureAllocation, beforeSnapshot));
        return new VirtualNodeTree(root, snapshot);
    }

    private static VirtualNode BuildRoot(VirtualTextArena arena, string text, int scrollY)
    {
        return new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(scrollY)],
            children:
            [
                VirtualNodeBuilder.Button(arena, "Cache A", new NodeKey(2), VirtualNodeProperty.Action(new ActionId(200))),
                VirtualNodeBuilder.Text(arena, text, new NodeKey(4)),
                VirtualNodeBuilder.Button(arena, "Cache B", new NodeKey(5), VirtualNodeProperty.Action(new ActionId(201))),
            ]);
    }

    private static ScreenRegion CreatePrimaryWindowRegion(IScreenInfo screen)
    {
        const int windowWidth = 960;
        const int windowHeight = 540;
        var bounds = screen.PhysicalBounds;
        var x = bounds.X + Math.Max((bounds.Width - windowWidth) / 2, 0);
        var y = bounds.Y + Math.Max((bounds.Height - windowHeight) / 2, 0);
        return new ScreenRegion(screen.Id, new PixelRectangle(x, y, windowWidth, windowHeight));
    }

    internal readonly struct AllocationAttribution(
        long TreeBytes,
        long DiffBytes,
        long TranslateBytes,
        long RenderBytes) : IEquatable<AllocationAttribution>
    {
        public long TreeBytes { get; } = TreeBytes;
        public long DiffBytes { get; } = DiffBytes;
        public long TranslateBytes { get; } = TranslateBytes;
        public long RenderBytes { get; } = RenderBytes;

        public AllocationAttribution AddTree(long bytes) => new(TreeBytes + bytes, DiffBytes, TranslateBytes, RenderBytes);

        public AllocationAttribution AddDiff(long bytes) => new(TreeBytes, DiffBytes + bytes, TranslateBytes, RenderBytes);

        public AllocationAttribution AddTranslate(long bytes) => new(TreeBytes, DiffBytes, TranslateBytes + bytes, RenderBytes);

        public AllocationAttribution AddRender(long bytes) => new(TreeBytes, DiffBytes, TranslateBytes, RenderBytes + bytes);

        public bool Equals(AllocationAttribution other)
        {
            return TreeBytes == other.TreeBytes
                && DiffBytes == other.DiffBytes
                && TranslateBytes == other.TranslateBytes
                && RenderBytes == other.RenderBytes;
        }

        public override bool Equals(object? obj) => obj is AllocationAttribution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TreeBytes, DiffBytes, TranslateBytes, RenderBytes);
    }

    internal readonly struct TreeAllocationAttribution(
        long BeginFrameBytes,
        long BuildRootBytes,
        long SnapshotBytes) : IEquatable<TreeAllocationAttribution>
    {
        public long BeginFrameBytes { get; } = BeginFrameBytes;
        public long BuildRootBytes { get; } = BuildRootBytes;
        public long SnapshotBytes { get; } = SnapshotBytes;
        public long TotalBytes => BeginFrameBytes + BuildRootBytes + SnapshotBytes;

        public TreeAllocationAttribution Add(TreeAllocationAttribution other) =>
            new(
                BeginFrameBytes + other.BeginFrameBytes,
                BuildRootBytes + other.BuildRootBytes,
                SnapshotBytes + other.SnapshotBytes);

        public TreeAllocationAttribution WithBeginFrame(long bytes) => new(BeginFrameBytes + bytes, BuildRootBytes, SnapshotBytes);

        public TreeAllocationAttribution WithBuildRoot(long bytes) => new(BeginFrameBytes, BuildRootBytes + bytes, SnapshotBytes);

        public TreeAllocationAttribution WithSnapshot(long bytes) => new(BeginFrameBytes, BuildRootBytes, SnapshotBytes + bytes);

        public bool Equals(TreeAllocationAttribution other)
        {
            return BeginFrameBytes == other.BeginFrameBytes
                && BuildRootBytes == other.BuildRootBytes
                && SnapshotBytes == other.SnapshotBytes;
        }

        public override bool Equals(object? obj) => obj is TreeAllocationAttribution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(BeginFrameBytes, BuildRootBytes, SnapshotBytes);
    }
}
