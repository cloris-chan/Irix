#if IRIX_DIAGNOSTICS
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class StyleOnlyPatchPlanSmokeDiagnostics
{
    internal static string[] BuildDiagnosticLines()
    {
        var hoverOnlySnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan(StyleOnlyPatchPlanCase.HoverOnly, BuildHoverOnlyStyleOnlyPatchPlan());
        var layoutAffectingSnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan(StyleOnlyPatchPlanCase.LayoutAffecting, BuildLayoutAffectingStyleOnlyPatchPlan());

        return [
            "=== StyleOnly Patch Plan Diagnostics ===",
            DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(hoverOnlySnapshot),
            DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(layoutAffectingSnapshot)
        ];
    }

    private static StyleOnlyPatchPlan BuildHoverOnlyStyleOnlyPatchPlan()
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = VirtualNodeFactory.Container(new NodeKey(1),
            ControlNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                ActionIdRegistry.Increment,
                new ControlVisualState(IsHovered: false, IsPressed: false, IsFocused: false)));
        var root2 = VirtualNodeFactory.Container(new NodeKey(1),
            ControlNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                ActionIdRegistry.Increment,
                new ControlVisualState(IsHovered: true, IsPressed: false, IsFocused: false)));

        var snapshot = arena.GetOrCreateSnapshot();
        using var frame1 = pipeline.Build(root1, viewport, snapshot);
        var retainedLayout = pipeline.LastLayoutResult;
        var retainedCommandRanges = pipeline.LastElementCommandRanges;
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, snapshot, [1]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Value.ElementSpan,
            pipeline.LastDirtyElementRanges);
    }

    private static StyleOnlyPatchPlan BuildLayoutAffectingStyleOnlyPatchPlan()
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.Container,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(0)],
            children: [ControlNodeBuilder.Button(arena, "Increment", new NodeKey(2), ControlActionPropertyAdapter.ToProperty(ActionIdRegistry.Increment))]);
        var root2 = new VirtualNode(
            VirtualNodeKind.Container,
            key: 1,
            properties: [VirtualNodeProperty.ScrollY(24)],
            children: [ControlNodeBuilder.Button(arena, "Increment", new NodeKey(2), ControlActionPropertyAdapter.ToProperty(ActionIdRegistry.Increment))]);

        var snapshot = arena.GetOrCreateSnapshot();
        using var frame1 = pipeline.Build(root1, viewport, snapshot);
        var retainedLayout = pipeline.LastLayoutResult;
        var retainedCommandRanges = pipeline.LastElementCommandRanges;
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, snapshot, [0]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Value.ElementSpan,
            pipeline.LastDirtyElementRanges);
    }
}
#endif
