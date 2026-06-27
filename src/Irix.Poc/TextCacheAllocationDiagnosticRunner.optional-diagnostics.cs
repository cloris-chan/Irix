#if IRIX_DIAGNOSTICS
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
                using var batch = translator.TranslateWithAllocationAttribution(patch, out var translateFrameAttribution);
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
        output.WriteLine(FormatTreeSnapshotAllocationAttribution(treeAttribution.SnapshotAttribution, frameCount));
        output.WriteLine(FormatBuildRootAllocationAttribution(treeAttribution.BuildRootAttribution, frameCount));
        output.WriteLine(FormatButtonAllocationAttribution(treeAttribution.BuildRootAttribution.ButtonAttribution, frameCount));
        output.WriteLine(FormatTranslateAllocationAttribution(translateAttribution, frameCount));
        output.WriteLine(FormatPipelineAllocationAttribution(translateAttribution.PipelineAttribution, frameCount));
        output.WriteLine(FormatPipelineSnapshotAllocationAttribution(translateAttribution.PipelineAttribution.SnapshotAttribution, frameCount));
        output.WriteLine(FormatLayoutAllocationAttribution(translateAttribution.PipelineAttribution.LayoutAttribution, frameCount));
        output.WriteLine(FormatRecordAllocationAttribution(translateAttribution.PipelineAttribution.RecordAttribution, frameCount));
        output.WriteLine(FormatAllocationFocus(attribution, treeAttribution, translateAttribution, frameCount));
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

    internal static string FormatTreeSnapshotAllocationAttribution(TextBufferSnapshotAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return "Tree snapshot allocation: "
            + $"textBuffer={attribution.CharBufferBytes} bytes ({PerFrame(attribution.CharBufferBytes, divisor)}/frame), "
            + $"snapshotShell={attribution.SnapshotShellBytes} bytes ({PerFrame(attribution.SnapshotShellBytes, divisor)}/frame), "
            + $"detailGap={attribution.DetailGapBytes} bytes ({PerFrame(attribution.DetailGapBytes, divisor)}/frame), "
            + $"measuredTotal={attribution.MeasuredBytes} bytes ({PerFrame(attribution.MeasuredBytes, divisor)}/frame)";
    }

    internal static string FormatBuildRootAllocationAttribution(BuildRootAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return "BuildRoot allocation: "
            + $"buttons={attribution.ButtonBytes} bytes ({PerFrame(attribution.ButtonBytes, divisor)}/frame), "
            + $"text={attribution.TextBytes} bytes ({PerFrame(attribution.TextBytes, divisor)}/frame), "
            + $"scrollProperty={attribution.ScrollPropertyBytes} bytes ({PerFrame(attribution.ScrollPropertyBytes, divisor)}/frame), "
            + $"children={attribution.ChildrenBytes} bytes ({PerFrame(attribution.ChildrenBytes, divisor)}/frame), "
            + $"container={attribution.ContainerBytes} bytes ({PerFrame(attribution.ContainerBytes, divisor)}/frame), "
            + $"measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatButtonAllocationAttribution(ButtonAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return "Button allocation: "
            + $"actionProperty={attribution.ActionPropertyBytes} bytes ({PerFrame(attribution.ActionPropertyBytes, divisor)}/frame), "
            + $"labelText={attribution.LabelTextBytes} bytes ({PerFrame(attribution.LabelTextBytes, divisor)}/frame), "
            + $"labelNode={attribution.LabelNodeBytes} bytes ({PerFrame(attribution.LabelNodeBytes, divisor)}/frame), "
            + $"childrenArray={attribution.ChildrenArrayBytes} bytes ({PerFrame(attribution.ChildrenArrayBytes, divisor)}/frame), "
            + $"propertyArray={attribution.PropertyArrayBytes} bytes ({PerFrame(attribution.PropertyArrayBytes, divisor)}/frame), "
            + $"buttonNode={attribution.ButtonNodeBytes} bytes ({PerFrame(attribution.ButtonNodeBytes, divisor)}/frame), "
            + $"detailGap={attribution.DetailGapBytes} bytes ({PerFrame(attribution.DetailGapBytes, divisor)}/frame), "
            + $"measuredTotal={attribution.MeasuredBytes} bytes ({PerFrame(attribution.MeasuredBytes, divisor)}/frame)";
    }

    internal static string FormatTranslateAllocationAttribution(WindowTranslateAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Translate allocation currentThread: retainedApply={attribution.RetainedApplyBytes} bytes ({PerFrame(attribution.RetainedApplyBytes, divisor)}/frame), viewport={attribution.ViewportBytes} bytes ({PerFrame(attribution.ViewportBytes, divisor)}/frame), pipeline={attribution.PipelineBuildBytes} bytes ({PerFrame(attribution.PipelineBuildBytes, divisor)}/frame), feedback={attribution.FeedbackBytes} bytes ({PerFrame(attribution.FeedbackBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatPipelineAllocationAttribution(RenderPipelineBuildAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Pipeline allocation currentThread: classify={attribution.ClassificationBytes} bytes ({PerFrame(attribution.ClassificationBytes, divisor)}/frame), layout={attribution.LayoutBytes} bytes ({PerFrame(attribution.LayoutBytes, divisor)}/frame), record={attribution.RecordBytes} bytes ({PerFrame(attribution.RecordBytes, divisor)}/frame), hitTargets={attribution.HitTargetsBytes} bytes ({PerFrame(attribution.HitTargetsBytes, divisor)}/frame), snapshot={attribution.SnapshotBytes} bytes ({PerFrame(attribution.SnapshotBytes, divisor)}/frame), retainedFrame={attribution.RetainedFrameBytes} bytes ({PerFrame(attribution.RetainedFrameBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatPipelineSnapshotAllocationAttribution(RenderPipelineSnapshotAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return "Pipeline snapshot allocation currentThread: "
            + $"frameBatch={attribution.FrameBatchBytes} bytes ({PerFrame(attribution.FrameBatchBytes, divisor)}/frame), "
            + $"retainedInput={attribution.RetainedInputBytes} bytes ({PerFrame(attribution.RetainedInputBytes, divisor)}/frame), "
            + $"detailGap={attribution.DetailGapBytes} bytes ({PerFrame(attribution.DetailGapBytes, divisor)}/frame), "
            + $"measuredTotal={attribution.MeasuredBytes} bytes ({PerFrame(attribution.MeasuredBytes, divisor)}/frame)";
    }

    internal static string FormatLayoutAllocationAttribution(LayoutBuildAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Layout allocation currentThread: nodeWalk={attribution.NodeWalkBytes} bytes ({PerFrame(attribution.NodeWalkBytes, divisor)}/frame), dirtyRanges={attribution.DirtyRangeBytes} bytes ({PerFrame(attribution.DirtyRangeBytes, divisor)}/frame), elementsArray={attribution.ElementArrayBytes} bytes ({PerFrame(attribution.ElementArrayBytes, divisor)}/frame), treeNodesArray={attribution.TreeNodeArrayBytes} bytes ({PerFrame(attribution.TreeNodeArrayBytes, divisor)}/frame), scrollDiagnosticsArray={attribution.ScrollDiagnosticsArrayBytes} bytes ({PerFrame(attribution.ScrollDiagnosticsArrayBytes, divisor)}/frame), result={attribution.ResultBytes} bytes ({PerFrame(attribution.ResultBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatRecordAllocationAttribution(DrawCommandRecordAllocationAttribution attribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        return $"Record allocation currentThread: resources={attribution.ResourcesBytes} bytes ({PerFrame(attribution.ResourcesBytes, divisor)}/frame), styles={attribution.StylesBytes} bytes ({PerFrame(attribution.StylesBytes, divisor)}/frame), commandBuild={attribution.CommandBuildBytes} bytes ({PerFrame(attribution.CommandBuildBytes, divisor)}/frame), dirtyRanges={attribution.DirtyRangesBytes} bytes ({PerFrame(attribution.DirtyRangesBytes, divisor)}/frame), measuredTotal={attribution.TotalBytes} bytes ({PerFrame(attribution.TotalBytes, divisor)}/frame)";
    }

    internal static string FormatAllocationFocus(AllocationAttribution attribution, TreeAllocationAttribution treeAttribution, WindowTranslateAllocationAttribution translateAttribution, int frameCount)
    {
        var divisor = frameCount > 0 ? frameCount : 0;
        var pipelineAttribution = translateAttribution.PipelineAttribution;
        var buildRootAttribution = treeAttribution.BuildRootAttribution;
        var buttonAttribution = buildRootAttribution.ButtonAttribution;
        var treeSnapshotAttribution = treeAttribution.SnapshotAttribution;
        var pipelineSnapshotAttribution = pipelineAttribution.SnapshotAttribution;
        var largestName = buttonAttribution.MeasuredBytes > 0 ? "tree.buildRoot.button.childrenArray" : buildRootAttribution.TotalBytes > 0 ? "tree.buildRoot.buttons" : "tree.buildRoot";
        var largestBytes = buttonAttribution.MeasuredBytes > 0 ? buttonAttribution.ChildrenArrayBytes : buildRootAttribution.TotalBytes > 0 ? buildRootAttribution.ButtonBytes : treeAttribution.BuildRootBytes;
        var nextName = "tree.snapshot";
        var nextBytes = treeAttribution.SnapshotBytes;
        if (treeSnapshotAttribution.MeasuredBytes > 0)
        {
            UpdateLargest("tree.snapshot.textBuffer", treeSnapshotAttribution.CharBufferBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("tree.snapshot.snapshotShell", treeSnapshotAttribution.SnapshotShellBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        if (buildRootAttribution.TotalBytes > 0)
        {
            if (buttonAttribution.MeasuredBytes > 0)
            {
                UpdateLargest("tree.buildRoot.button.actionProperty", buttonAttribution.ActionPropertyBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
                UpdateLargest("tree.buildRoot.button.labelText", buttonAttribution.LabelTextBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
                UpdateLargest("tree.buildRoot.button.labelNode", buttonAttribution.LabelNodeBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
                UpdateLargest("tree.buildRoot.button.childrenArray", buttonAttribution.ChildrenArrayBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
                UpdateLargest("tree.buildRoot.button.propertyArray", buttonAttribution.PropertyArrayBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
                UpdateLargest("tree.buildRoot.button.buttonNode", buttonAttribution.ButtonNodeBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            }

            UpdateLargest("tree.buildRoot.text", buildRootAttribution.TextBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("tree.buildRoot.scrollProperty", buildRootAttribution.ScrollPropertyBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("tree.buildRoot.children", buildRootAttribution.ChildrenBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("tree.buildRoot.container", buildRootAttribution.ContainerBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        var layoutAttribution = pipelineAttribution.LayoutAttribution;
        if (layoutAttribution.TotalBytes > 0)
        {
            UpdateLargest("layout.nodeWalk", layoutAttribution.NodeWalkBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("layout.dirtyRanges", layoutAttribution.DirtyRangeBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("layout.elementsArray", layoutAttribution.ElementArrayBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("layout.treeNodesArray", layoutAttribution.TreeNodeArrayBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("layout.scrollDiagnosticsArray", layoutAttribution.ScrollDiagnosticsArrayBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("layout.result", layoutAttribution.ResultBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        else
        {
            UpdateLargest("pipeline.layout", pipelineAttribution.LayoutBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        if (pipelineSnapshotAttribution.MeasuredBytes > 0)
        {
            UpdateLargest("pipeline.snapshot.frameBatch", pipelineSnapshotAttribution.FrameBatchBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
            UpdateLargest("pipeline.snapshot.retainedInput", pipelineSnapshotAttribution.RetainedInputBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        else
        {
            UpdateLargest("pipeline.snapshot", pipelineAttribution.SnapshotBytes, ref largestName, ref largestBytes, ref nextName, ref nextBytes);
        }
        var treeDetailGap = attribution.TreeBytes - treeAttribution.TotalBytes;
        var pipelineDetailGap = translateAttribution.PipelineBuildBytes - pipelineAttribution.TotalBytes;
        return $"Allocation focus: largestCandidate={largestName}={largestBytes} bytes ({PerFrame(largestBytes, divisor)}/frame), nextCandidate={nextName}={nextBytes} bytes ({PerFrame(nextBytes, divisor)}/frame), treeDetailGap={treeDetailGap} bytes ({PerFrame(treeDetailGap, divisor)}/frame), pipelineDetailGap={pipelineDetailGap} bytes ({PerFrame(pipelineDetailGap, divisor)}/frame), drawRecord={pipelineAttribution.RecordBytes} bytes ({PerFrame(pipelineAttribution.RecordBytes, divisor)}/frame)";
    }

    private static void UpdateLargest(string candidateName, long candidateBytes, ref string largestName, ref long largestBytes, ref string nextName, ref long nextBytes)
    {
        if (candidateName == largestName)
        {
            return;
        }

        if (candidateBytes > largestBytes)
        {
            nextName = largestName;
            nextBytes = largestBytes;
            largestName = candidateName;
            largestBytes = candidateBytes;
        }
        else if (candidateBytes > nextBytes)
        {
            nextName = candidateName;
            nextBytes = candidateBytes;
        }
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
        var root = BuildRoot(arena, text, scrollY, measureAllocation, out var buildRootAttribution);
        attribution = attribution.WithBuildRoot(AllocatedDelta(measureAllocation, beforeBuildRoot), buildRootAttribution);

        var beforeSnapshot = GetAllocatedBytes(measureAllocation);
        var snapshot = arena.GetOrCreateSnapshotWithAllocationAttribution(out var snapshotAttribution);
        attribution = attribution.WithSnapshot(AllocatedDelta(measureAllocation, beforeSnapshot), snapshotAttribution);
        return new VirtualNodeTree(root, snapshot);
    }

    private static VirtualNode BuildRoot(VirtualTextArena arena, string text, int scrollY)
        => BuildRoot(arena, text, scrollY, measureAllocation: false, out _);

    private static VirtualNode BuildRoot(
        VirtualTextArena arena,
        string text,
        int scrollY,
        bool measureAllocation,
        out BuildRootAllocationAttribution attribution)
    {
        attribution = default;

        var beforeButtonA = GetAllocatedBytes(measureAllocation);
        var buttonA = BuildMeasuredButton(arena, "Cache A".AsSpan(), new NodeKey(2), new ActionId(200), measureAllocation, out var buttonAAttribution);
        attribution = attribution.WithButton(AllocatedDelta(measureAllocation, beforeButtonA), buttonAAttribution);

        var beforeText = GetAllocatedBytes(measureAllocation);
        var textNode = VirtualNodeBuilder.Text(arena, text, new NodeKey(4));
        attribution = attribution.WithText(AllocatedDelta(measureAllocation, beforeText));

        var beforeButtonB = GetAllocatedBytes(measureAllocation);
        var buttonB = BuildMeasuredButton(arena, "Cache B".AsSpan(), new NodeKey(5), new ActionId(201), measureAllocation, out var buttonBAttribution);
        attribution = attribution.WithButton(AllocatedDelta(measureAllocation, beforeButtonB), buttonBAttribution);

        var beforeScrollProperty = GetAllocatedBytes(measureAllocation);
        ReadOnlySpan<VirtualNodeProperty> properties = [VirtualNodeProperty.ScrollY(scrollY)];
        attribution = attribution.WithScrollProperty(AllocatedDelta(measureAllocation, beforeScrollProperty));

        var beforeChildren = GetAllocatedBytes(measureAllocation);
        ReadOnlySpan<VirtualNode> children = [buttonA, textNode, buttonB];
        attribution = attribution.WithChildren(AllocatedDelta(measureAllocation, beforeChildren));

        var beforeContainer = GetAllocatedBytes(measureAllocation);
        var root = new VirtualNode(
            VirtualNodeKind.Container,
            key: 1,
            properties: properties,
            children: children);
        attribution = attribution.WithContainer(AllocatedDelta(measureAllocation, beforeContainer));
        return root;
    }

    private static VirtualNode BuildMeasuredButton(
        VirtualTextArena arena,
        ReadOnlySpan<char> label,
        NodeKey key,
        ActionId actionId,
        bool measureAllocation,
        out ButtonAllocationAttribution attribution)
    {
        attribution = default;

        var beforeActionProperty = GetAllocatedBytes(measureAllocation);
        var actionProperty = VirtualNodeProperty.Action(actionId);
        attribution = attribution.WithActionProperty(AllocatedDelta(measureAllocation, beforeActionProperty));

        var beforeLabelText = GetAllocatedBytes(measureAllocation);
        var labelContent = arena.AddText(label);
        attribution = attribution.WithLabelText(AllocatedDelta(measureAllocation, beforeLabelText));

        ReadOnlySpan<VirtualNodeProperty> properties = [actionProperty];
        ControlNodeBuilder.CountButtonProperties(properties, out var containerCount, out var rectangleCount, out var textCount);

        var beforePropertyArray = GetAllocatedBytes(measureAllocation);
        var propertyArray = ControlNodeBuilder.CreateButtonPropertyArray(properties, containerCount, ControlNodeBuilder.ButtonPropertyTarget.Container);
        var rectanglePropertyArray = ControlNodeBuilder.CreateButtonPropertyArray(properties, rectangleCount, ControlNodeBuilder.ButtonPropertyTarget.Rectangle);
        var textPropertyArray = ControlNodeBuilder.CreateButtonPropertyArray(properties, textCount, ControlNodeBuilder.ButtonPropertyTarget.Text);
        attribution = attribution.WithPropertyArray(AllocatedDelta(measureAllocation, beforePropertyArray));

        var beforeChildrenArray = GetAllocatedBytes(measureAllocation);
        var childArray = ControlNodeBuilder.CreateButtonChildrenFromOwnedPropertyArraysUnsafe(labelContent, rectanglePropertyArray, textPropertyArray);
        attribution = attribution.WithChildrenArray(AllocatedDelta(measureAllocation, beforeChildrenArray));

        var beforeButtonNode = GetAllocatedBytes(measureAllocation);
        var button = VirtualNode.CreateFromOwnedArraysUnsafe(VirtualNodeKind.Container, key, default, propertyArray, childArray);
        attribution = attribution.WithButtonNode(AllocatedDelta(measureAllocation, beforeButtonNode));
        return button;
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
        long SnapshotBytes,
        BuildRootAllocationAttribution BuildRootAttribution = default,
        TextBufferSnapshotAllocationAttribution SnapshotAttribution = default) : IEquatable<TreeAllocationAttribution>
    {
        public long BeginFrameBytes { get; } = BeginFrameBytes;
        public long BuildRootBytes { get; } = BuildRootBytes;
        public long SnapshotBytes { get; } = SnapshotBytes;
        public BuildRootAllocationAttribution BuildRootAttribution { get; } = BuildRootAttribution;
        public TextBufferSnapshotAllocationAttribution SnapshotAttribution { get; } = SnapshotAttribution;
        public long TotalBytes => BeginFrameBytes + BuildRootBytes + SnapshotBytes;

        public TreeAllocationAttribution Add(TreeAllocationAttribution other) =>
            new(
                BeginFrameBytes + other.BeginFrameBytes,
                BuildRootBytes + other.BuildRootBytes,
                SnapshotBytes + other.SnapshotBytes,
                BuildRootAttribution.Add(other.BuildRootAttribution),
                SnapshotAttribution.Add(other.SnapshotAttribution));

        public TreeAllocationAttribution WithBeginFrame(long bytes) => new(BeginFrameBytes + bytes, BuildRootBytes, SnapshotBytes, BuildRootAttribution, SnapshotAttribution);

        public TreeAllocationAttribution WithBuildRoot(long bytes) => new(BeginFrameBytes, BuildRootBytes + bytes, SnapshotBytes, BuildRootAttribution, SnapshotAttribution);

        public TreeAllocationAttribution WithBuildRoot(long bytes, BuildRootAllocationAttribution attribution) =>
            new(BeginFrameBytes, BuildRootBytes + bytes, SnapshotBytes, BuildRootAttribution.Add(attribution), SnapshotAttribution);

        public TreeAllocationAttribution WithSnapshot(long bytes) => new(BeginFrameBytes, BuildRootBytes, SnapshotBytes + bytes, BuildRootAttribution, SnapshotAttribution);

        public TreeAllocationAttribution WithSnapshot(long bytes, TextBufferSnapshotAllocationAttribution attribution) =>
            new(BeginFrameBytes, BuildRootBytes, SnapshotBytes + bytes, BuildRootAttribution, SnapshotAttribution.Add(attribution));

        public bool Equals(TreeAllocationAttribution other)
        {
            return BeginFrameBytes == other.BeginFrameBytes
                && BuildRootBytes == other.BuildRootBytes
                && SnapshotBytes == other.SnapshotBytes
                && BuildRootAttribution.Equals(other.BuildRootAttribution)
                && SnapshotAttribution.Equals(other.SnapshotAttribution);
        }

        public override bool Equals(object? obj) => obj is TreeAllocationAttribution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(BeginFrameBytes, BuildRootBytes, SnapshotBytes, BuildRootAttribution, SnapshotAttribution);
    }

    internal readonly struct BuildRootAllocationAttribution(
        long ButtonBytes,
        long TextBytes,
        long ScrollPropertyBytes,
        long ChildrenBytes,
        long ContainerBytes,
        ButtonAllocationAttribution ButtonAttribution = default) : IEquatable<BuildRootAllocationAttribution>
    {
        public long ButtonBytes { get; } = ButtonBytes;
        public long TextBytes { get; } = TextBytes;
        public long ScrollPropertyBytes { get; } = ScrollPropertyBytes;
        public long ChildrenBytes { get; } = ChildrenBytes;
        public long ContainerBytes { get; } = ContainerBytes;
        public ButtonAllocationAttribution ButtonAttribution { get; } = ButtonAttribution;
        public long TotalBytes => ButtonBytes + TextBytes + ScrollPropertyBytes + ChildrenBytes + ContainerBytes;

        public BuildRootAllocationAttribution Add(BuildRootAllocationAttribution other) =>
            new(
                ButtonBytes + other.ButtonBytes,
                TextBytes + other.TextBytes,
                ScrollPropertyBytes + other.ScrollPropertyBytes,
                ChildrenBytes + other.ChildrenBytes,
                ContainerBytes + other.ContainerBytes,
                ButtonAttribution.Add(other.ButtonAttribution));

        public BuildRootAllocationAttribution WithButton(long bytes, ButtonAllocationAttribution attribution) =>
            new(ButtonBytes + bytes, TextBytes, ScrollPropertyBytes, ChildrenBytes, ContainerBytes, ButtonAttribution.Add(attribution.WithMeasured(bytes)));

        public BuildRootAllocationAttribution WithText(long bytes) => new(ButtonBytes, TextBytes + bytes, ScrollPropertyBytes, ChildrenBytes, ContainerBytes, ButtonAttribution);

        public BuildRootAllocationAttribution WithScrollProperty(long bytes) => new(ButtonBytes, TextBytes, ScrollPropertyBytes + bytes, ChildrenBytes, ContainerBytes, ButtonAttribution);

        public BuildRootAllocationAttribution WithChildren(long bytes) => new(ButtonBytes, TextBytes, ScrollPropertyBytes, ChildrenBytes + bytes, ContainerBytes, ButtonAttribution);

        public BuildRootAllocationAttribution WithContainer(long bytes) => new(ButtonBytes, TextBytes, ScrollPropertyBytes, ChildrenBytes, ContainerBytes + bytes, ButtonAttribution);

        public bool Equals(BuildRootAllocationAttribution other)
        {
            return ButtonBytes == other.ButtonBytes
                && TextBytes == other.TextBytes
                && ScrollPropertyBytes == other.ScrollPropertyBytes
                && ChildrenBytes == other.ChildrenBytes
                && ContainerBytes == other.ContainerBytes
                && ButtonAttribution.Equals(other.ButtonAttribution);
        }

        public override bool Equals(object? obj) => obj is BuildRootAllocationAttribution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ButtonBytes, TextBytes, ScrollPropertyBytes, ChildrenBytes, ContainerBytes, ButtonAttribution);
    }

    internal readonly struct ButtonAllocationAttribution(
        long ActionPropertyBytes,
        long LabelTextBytes,
        long LabelNodeBytes,
        long ChildrenArrayBytes,
        long PropertyArrayBytes,
        long ButtonNodeBytes,
        long MeasuredBytes) : IEquatable<ButtonAllocationAttribution>
    {
        public long ActionPropertyBytes { get; } = ActionPropertyBytes;
        public long LabelTextBytes { get; } = LabelTextBytes;
        public long LabelNodeBytes { get; } = LabelNodeBytes;
        public long ChildrenArrayBytes { get; } = ChildrenArrayBytes;
        public long PropertyArrayBytes { get; } = PropertyArrayBytes;
        public long ButtonNodeBytes { get; } = ButtonNodeBytes;
        public long MeasuredBytes { get; } = MeasuredBytes;
        public long DetailBytes => ActionPropertyBytes + LabelTextBytes + LabelNodeBytes + ChildrenArrayBytes + PropertyArrayBytes + ButtonNodeBytes;
        public long DetailGapBytes => MeasuredBytes - DetailBytes;

        public ButtonAllocationAttribution Add(ButtonAllocationAttribution other) =>
            new(
                ActionPropertyBytes + other.ActionPropertyBytes,
                LabelTextBytes + other.LabelTextBytes,
                LabelNodeBytes + other.LabelNodeBytes,
                ChildrenArrayBytes + other.ChildrenArrayBytes,
                PropertyArrayBytes + other.PropertyArrayBytes,
                ButtonNodeBytes + other.ButtonNodeBytes,
                MeasuredBytes + other.MeasuredBytes);

        public ButtonAllocationAttribution WithActionProperty(long bytes) =>
            new(ActionPropertyBytes + bytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes);

        public ButtonAllocationAttribution WithLabelText(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes + bytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes);

        public ButtonAllocationAttribution WithLabelNode(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes + bytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes);

        public ButtonAllocationAttribution WithChildrenArray(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes + bytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes);

        public ButtonAllocationAttribution WithPropertyArray(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes + bytes, ButtonNodeBytes, MeasuredBytes);

        public ButtonAllocationAttribution WithButtonNode(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes + bytes, MeasuredBytes);

        public ButtonAllocationAttribution WithMeasured(long bytes) =>
            new(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes + bytes);

        public bool Equals(ButtonAllocationAttribution other)
        {
            return ActionPropertyBytes == other.ActionPropertyBytes
                && LabelTextBytes == other.LabelTextBytes
                && LabelNodeBytes == other.LabelNodeBytes
                && ChildrenArrayBytes == other.ChildrenArrayBytes
                && PropertyArrayBytes == other.PropertyArrayBytes
                && ButtonNodeBytes == other.ButtonNodeBytes
                && MeasuredBytes == other.MeasuredBytes;
        }

        public override bool Equals(object? obj) => obj is ButtonAllocationAttribution other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ActionPropertyBytes, LabelTextBytes, LabelNodeBytes, ChildrenArrayBytes, PropertyArrayBytes, ButtonNodeBytes, MeasuredBytes);
    }
}
#endif
