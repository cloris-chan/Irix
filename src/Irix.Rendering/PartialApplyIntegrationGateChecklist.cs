namespace Irix.Rendering;

internal enum PartialApplyIntegrationGate : byte
{
    ResourceResolverOwnership,
    ResourceDisposePolicy,
    CommandRangeStability,
    HitTargetMetadataProjection,
    RetainedRootUpdate,
    FallbackReporting,
    CompositorOwnership,
    RegressionCoverage
}

internal readonly record struct PartialApplyIntegrationGateStatus(
    PartialApplyIntegrationGate Gate,
    bool Satisfied,
    string BlockingCondition,
    string Evidence);

internal static class PartialApplyIntegrationGateChecklist
{
    public static IReadOnlyList<PartialApplyIntegrationGateStatus> RequiredGates { get; } =
    [
        new(PartialApplyIntegrationGate.ResourceResolverOwnership, false, "Runtime retained frame must expose segmented resolver reads before hookup.", "Segmented reader preflight tests cover old/new resolver ownership."),
        new(PartialApplyIntegrationGate.ResourceDisposePolicy, false, "Production retained frame must release retained snapshots exactly once across every path.", "Segment table lifecycle tests cover partial accept, full fallback, invalidate, dispose, and replaced old ranges."),
        new(PartialApplyIntegrationGate.CommandRangeStability, false, "Runtime command replacement must require stable contiguous dirty command ranges.", "Planner and segment table tests cover unstable/invalid ranges."),
        new(PartialApplyIntegrationGate.HitTargetMetadataProjection, false, "Runtime projector must reproject action metadata without next layout output.", "Hit target projector preflight tests cover success and fallback cases."),
        new(PartialApplyIntegrationGate.RetainedRootUpdate, false, "Accepted partial updates must advance retained root metadata for the next diff.", "Design checklist documents metadata update and fallback rules; runtime update is not implemented."),
        new(PartialApplyIntegrationGate.FallbackReporting, false, "Runtime hookup must preserve local AppliedPartial/FallbackFull/Rejected reporting without diagnostics expansion.", "Planner result tests cover every local reason."),
        new(PartialApplyIntegrationGate.CompositorOwnership, false, "Compositor ownership of multiple retained snapshots must be explicit before hookup.", "No compositor integration exists; no-change tests keep current behavior sealed."),
        new(PartialApplyIntegrationGate.RegressionCoverage, false, "Planner, retained frame, compositor, diagnostics, and hit-test no-change coverage must remain green.", "Focused and full test suites act as the regression guard.")
    ];

    public static bool CanHookUpPartialApply
    {
        get
        {
            foreach (var gate in RequiredGates)
            {
                if (!gate.Satisfied)
                {
                    return false;
                }
            }

            return true;
        }
    }
}