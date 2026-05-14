using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class StyleOnlyPatchPlanSmokeDiagnostics
{
    internal static string[] BuildDiagnosticLines()
    {
        var hoverOnlySnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("hoverOnly", BuildHoverOnlyStyleOnlyPatchPlan());
        var layoutAffectingSnapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("layoutAffecting", BuildLayoutAffectingStyleOnlyPatchPlan());

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
        var root1 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                ButtonAttributeBundle.Create(ActionIdRegistry.Increment, new ControlVisualState(IsHovered: false, IsPressed: false, IsFocused: false))));
        var root2 = VirtualNodeFactory.ScrollContainer(new NodeKey(1),
            VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2),
                ButtonAttributeBundle.Create(ActionIdRegistry.Increment, new ControlVisualState(IsHovered: true, IsPressed: false, IsFocused: false))));

        using var frame1 = pipeline.Build(root1, viewport);
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, [1]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);
    }

    private static StyleOnlyPatchPlan BuildLayoutAffectingStyleOnlyPatchPlan()
    {
        var arena = new VirtualTextArena();
        var pipeline = new RenderPipeline();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var root1 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(0))],
            children: [VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2), ControlActionAttributeAdapter.ToAttribute(ActionIdRegistry.Increment))]);
        var root2 = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute(VirtualAttributeKey.ScrollY, AttributeValue.FromNumber(24))],
            children: [VirtualNodeBuilder.Button(arena, "Increment", new NodeKey(2), ControlActionAttributeAdapter.ToAttribute(ActionIdRegistry.Increment))]);

        using var frame1 = pipeline.Build(root1, viewport);
        var retainedLayout = pipeline.LastLayoutResult;
        ElementCommandRange[] retainedCommandRanges = [.. pipeline.LastElementCommandRanges];
        HitTestTarget[] retainedHitTargets = [.. frame1.HitTargets];

        using var frame2 = pipeline.Build(root2, viewport, [0]);
        return StyleOnlyPatchPlanBuilder.Build(
            pipeline.LastDirtyClassifications,
            viewportChanged: false,
            retainedLayout,
            retainedCommandRanges,
            retainedHitTargets,
            pipeline.LastLayoutResult!.Elements,
            pipeline.LastDirtyElementRanges);
    }
}
