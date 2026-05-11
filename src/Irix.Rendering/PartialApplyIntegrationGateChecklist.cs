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
    string PreflightEvidence,
    string RuntimeEvidence,
    string NoChangeRegressionEvidence,
    string BlockingCondition);

internal static class PartialApplyIntegrationGateChecklist
{
    public static IReadOnlyList<PartialApplyIntegrationGateStatus> RequiredGates { get; } =
    [
        new(
            PartialApplyIntegrationGate.ResourceResolverOwnership,
            false,
            "Segmented reader and per-segment backend adapter preflight tests prove old/new resolver ownership by command segment.",
            "None; runtime retained frame still exposes a single resolver boundary.",
            "Backend contract and D3D12 execution tests remain unchanged.",
            "Runtime retained frame must expose segmented resolver reads before hookup."),
        new(
            PartialApplyIntegrationGate.ResourceDisposePolicy,
            false,
            "Segment table and segmented retained-frame prototype lifecycle tests cover partial accept, full fallback, invalidate, dispose, replaced old ranges, and invalid segment reads.",
            "None; production retained frame does not own multiple retained snapshots.",
            "Existing retained frame resource ownership tests keep same-frame behavior sealed.",
            "Production retained frame must release retained snapshots exactly once across every path."),
        new(
            PartialApplyIntegrationGate.CommandRangeStability,
            false,
            "Planner, segment table, and segmented reader tests cover unstable, invalid, overlapping, and non-contiguous ranges.",
            "None; runtime still does not replace cross-frame command ranges.",
            "No-change compositor tests keep current full/guarded partial behavior sealed.",
            "Runtime command replacement must require stable contiguous dirty command ranges."),
        new(
            PartialApplyIntegrationGate.HitTargetMetadataProjection,
            false,
            "Hit target projector tests cover action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls.",
            "None; hit-test runtime still consumes full layout output only.",
            "Hit-test behavior tests keep retained geometry and compositor lookup unchanged.",
            "Runtime projector must reproject action metadata without next layout output."),
        new(
            PartialApplyIntegrationGate.RetainedRootUpdate,
            false,
            "Retained root metadata patcher tests cover dirty control metadata projection and fallback cases.",
            "None; RenderPipeline retained root baseline is not patched by a partial path.",
            "RenderPipeline.Build tests keep the current diff/layout baseline unchanged.",
            "Accepted partial updates must advance retained root metadata for the next diff."),
        new(
            PartialApplyIntegrationGate.FallbackReporting,
            false,
            "Planner result tests cover AppliedPartial, FallbackFull, Rejected, and every local reason.",
            "None; no runtime/compositor reporting hookup exists.",
            "Diagnostics formatter tests keep CLI output unchanged.",
            "Runtime hookup must preserve local AppliedPartial/FallbackFull/Rejected reporting without diagnostics expansion."),
        new(
            PartialApplyIntegrationGate.CompositorOwnership,
            false,
            "Dry-run and adapter preflight tests keep segment ownership/execution outside compositor mutation.",
            "None; DrawingBackendCompositor owns one retained frame with one resolver boundary.",
            "Compositor no-mutation tests keep current counters, retained frame, and backend execution unchanged.",
            "Compositor ownership of multiple retained snapshots must be explicit before hookup."),
        new(
            PartialApplyIntegrationGate.RegressionCoverage,
            false,
            "Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds.",
            "None; no partial apply runtime hookup exists to cover.",
            "Focused and full test suites act as the no-change regression guard.",
            "Planner, retained frame, compositor, diagnostics, and hit-test no-change coverage must remain green.")
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