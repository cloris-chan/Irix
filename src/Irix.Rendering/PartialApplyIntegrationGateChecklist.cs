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
    string ShadowRuntimeEvidence,
    string ProductionRuntimeEvidence,
    string NoChangeRegressionEvidence,
    string BlockingCondition,
    string RuntimePromotionCondition);

internal static class PartialApplyIntegrationGateChecklist
{
    public static IReadOnlyList<PartialApplyIntegrationGateStatus> RequiredGates { get; } =
    [
        new(
            PartialApplyIntegrationGate.ResourceResolverOwnership,
            false,
            "Segmented reader and per-segment backend adapter preflight tests prove old/new resolver ownership by command segment.",
            "SegmentedRetainedFrameDiagnosticHarness opt-in feeds real RenderPipeline.Build batches into SegmentedRetainedFrameOwner and exposes segmented reads from shadow state.",
            "None; production retained frame still exposes a single resolver boundary.",
            "Backend contract and D3D12 execution tests remain unchanged.",
            "Runtime retained frame must expose segmented resolver reads before hookup.",
            "Promote only after an internal retained-frame owner exposes segment reads from real retained state without changing the existing TryReadFrame contract."),
        new(
            PartialApplyIntegrationGate.ResourceDisposePolicy,
            false,
            "Segment table and segmented retained-frame owner lifecycle tests cover partial accept, full fallback, invalidate, dispose, replaced old ranges, and invalid segment reads.",
            "SegmentedRetainedFrameOwner and the opt-in diagnostic harness cover full apply, accepted partial rehearsal, explicit full fallback rehearsal, disabled mode, invalidate, and dispose with multiple snapshots.",
            "None; production retained frame does not own multiple retained snapshots.",
            "Existing retained frame resource ownership tests keep same-frame behavior sealed.",
            "Production retained frame must release retained snapshots exactly once across every path.",
            "Promote only after production ownership paths cover accepted partial, rejected partial, full fallback, empty frame, invalidate, and dispose with multiple FrameDrawingResources snapshots."),
        new(
            PartialApplyIntegrationGate.CommandRangeStability,
            false,
            "Planner, segment table, and segmented reader tests cover unstable, invalid, overlapping, and non-contiguous ranges.",
            "SegmentedRetainedFrameOwner reports ShadowRejected and leaves command, segment, and retained-root state unchanged when an applied plan cannot be accepted by the shadow owner.",
            "None; production runtime still does not replace cross-frame command ranges.",
            "No-change compositor tests keep current full/guarded partial behavior sealed.",
            "Runtime command replacement must require stable contiguous dirty command ranges.",
            "Promote only after runtime rejects mismatched command counts and malformed dirty ranges before any command buffer or segment table mutation."),
        new(
            PartialApplyIntegrationGate.HitTargetMetadataProjection,
            false,
            "Hit target projector tests cover action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls.",
            "None; shadow owner consumes projector output but hit-test runtime remains unchanged.",
            "None; hit-test runtime still consumes full layout output only.",
            "Hit-test behavior tests keep retained geometry and compositor lookup unchanged.",
            "Runtime projector must reproject action metadata without next layout output.",
            "Promote only after the runtime partial path consumes retained geometry plus next-root action metadata without reading next layout output."),
        new(
            PartialApplyIntegrationGate.RetainedRootUpdate,
            false,
            "Retained root metadata patcher tests cover dirty control metadata projection and fallback cases.",
            "Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal atomically advances retained root metadata with command/resource segments.",
            "None; RenderPipeline retained root baseline is not patched by a partial path.",
            "RenderPipeline.Build tests keep the current diff/layout baseline unchanged.",
            "Accepted partial updates must advance retained root metadata for the next diff.",
            "Promote only after accepted runtime partials atomically advance retained root metadata with command segments and hit targets, while failed partials leave the old baseline intact."),
        new(
            PartialApplyIntegrationGate.FallbackReporting,
            false,
            "Planner result tests cover AppliedPartial, FallbackFull, Rejected, and every local reason.",
            "Shadow result vocabulary reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, and ShadowRejected for tests/internal diagnostics only.",
            "None; no runtime/compositor reporting hookup exists.",
            "Diagnostics formatter tests keep CLI output unchanged.",
            "Runtime hookup must preserve local AppliedPartial/FallbackFull/Rejected reporting without diagnostics expansion.",
            "Promote only after runtime callers can observe local result vocabulary without changing CLI diagnostics output."),
        new(
            PartialApplyIntegrationGate.CompositorOwnership,
            false,
            "Dry-run and adapter preflight tests keep segment ownership/execution outside compositor mutation.",
            "Disabled sentinel tests compare production pipeline/compositor/backend/hit-test behavior, and opt-in tests execute shadow owner reads through the adapter without compositor involvement.",
            "None; DrawingBackendCompositor owns one retained frame with one resolver boundary.",
            "Compositor no-mutation tests keep current counters, retained frame, and backend execution unchanged.",
            "Compositor ownership of multiple retained snapshots must be explicit before hookup.",
            "Promote only after compositor integration is explicitly opt-in and proves segmented execution does not change existing full-frame behavior or hit-test lookup."),
        new(
            PartialApplyIntegrationGate.RegressionCoverage,
            false,
            "Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds.",
            "Shadow owner, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path.",
            "None; no partial apply runtime hookup exists to cover.",
            "Focused and full test suites act as the no-change regression guard.",
            "Planner, retained frame, compositor, diagnostics, and hit-test no-change coverage must remain green.",
            "Promote only after runtime-seam tests and existing no-change suites pass together on every supported validation path.")
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