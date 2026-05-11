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
    string ProductionOffRuntimeEvidence,
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
            "SegmentedRetainedFrameDiagnosticHarness and SegmentedRetainedFrameRuntimeOwner feed real batches into SegmentedRetainedFrameOwner, and DrawingBackendCompositorShadowProbe verifies segmented resolver ownership outside the compositor.",
            "Default-off SegmentedRetainedFrameProductionOwnerFeed can opt in a runtime owner that follows normal RenderPipeline.Build batches and exposes segmented reads while rendering still uses RetainedRenderFrame.",
            "None; production retained frame still exposes a single resolver boundary.",
            "Backend contract and D3D12 execution tests remain unchanged.",
            "Runtime retained frame must expose segmented resolver reads before hookup.",
            "Promote only after an internal retained-frame owner exposes segment reads from real retained state without changing the existing TryReadFrame contract."),
        new(
            PartialApplyIntegrationGate.ResourceDisposePolicy,
            false,
            "Segment table and segmented retained-frame owner lifecycle tests cover partial accept, full fallback, invalidate, dispose, replaced old ranges, and invalid segment reads.",
            "SegmentedRetainedFrameOwner, SegmentedRetainedFrameRuntimeOwner, and the opt-in diagnostic harness cover full apply, accepted partial rehearsal, explicit full fallback rehearsal, rebuild, disabled mode, invalidate, and dispose with multiple snapshots.",
            "Production-owner feed tests cover default-off no allocation, enabled full apply, accepted partial, fallback full, rebuild, empty batch, malformed dirty ranges, and runtime-owner disposal without making the owner a render source.",
            "None; production retained frame does not own multiple retained snapshots.",
            "Existing retained frame resource ownership tests keep same-frame behavior sealed.",
            "Production retained frame must release retained snapshots exactly once across every path.",
            "Promote only after production ownership paths cover accepted partial, rejected partial, full fallback, empty frame, invalidate, and dispose with multiple FrameDrawingResources snapshots."),
        new(
            PartialApplyIntegrationGate.CommandRangeStability,
            false,
            "Planner, segment table, and segmented reader tests cover unstable, invalid, overlapping, and non-contiguous ranges.",
            "SegmentedRetainedFrameOwner reports ShadowRejected and leaves command, segment, and retained-root state unchanged when an applied plan cannot be accepted by the shadow owner.",
            "Production-owner feed runs planner to root patch to owner partial accept, reports fallback while preserving owner state before fallback when malformed dirty ranges make rehearsal fail, and then explicitly full-applies fallback state.",
            "None; production runtime still does not replace cross-frame command ranges.",
            "No-change compositor tests keep current full/guarded partial behavior sealed.",
            "Runtime command replacement must require stable contiguous dirty command ranges.",
            "Promote only after runtime rejects mismatched command counts and malformed dirty ranges before any command buffer or segment table mutation."),
        new(
            PartialApplyIntegrationGate.HitTargetMetadataProjection,
            false,
            "Hit target projector tests cover action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls.",
            "None; shadow owner consumes projector output but hit-test runtime remains unchanged.",
            "Production-off feed does not change hit-test runtime; compositor shadow probe covers hit-test no-change including clipped target rejection.",
            "None; hit-test runtime still consumes full layout output only.",
            "Hit-test behavior tests keep retained geometry and compositor lookup unchanged.",
            "Runtime projector must reproject action metadata without next layout output.",
            "Promote only after the runtime partial path consumes retained geometry plus next-root action metadata without reading next layout output."),
        new(
            PartialApplyIntegrationGate.RetainedRootUpdate,
            false,
            "Retained root metadata patcher tests cover dirty control metadata projection and fallback cases.",
            "Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal atomically advances retained root metadata with command/resource segments.",
            "Production-owner feed accepted partial rehearsal advances the runtime owner's retained root metadata while RenderPipeline's production retained root baseline remains unchanged.",
            "None; RenderPipeline retained root baseline is not patched by a partial path.",
            "RenderPipeline.Build tests keep the current diff/layout baseline unchanged.",
            "Accepted partial updates must advance retained root metadata for the next diff.",
            "Promote only after accepted runtime partials atomically advance retained root metadata with command segments and hit targets, while failed partials leave the old baseline intact."),
        new(
            PartialApplyIntegrationGate.FallbackReporting,
            false,
            "Planner result tests cover AppliedPartial, FallbackFull, Rejected, and every local reason.",
            "Shadow result vocabulary reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, and ShadowRejected for tests/internal diagnostics only.",
            "Production-owner feed result reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, fallback-applied, and owner-state-preserved-before-fallback flags for tests/internal diagnostics only.",
            "None; no runtime/compositor reporting hookup exists.",
            "Diagnostics formatter tests keep CLI output unchanged.",
            "Runtime hookup must preserve local AppliedPartial/FallbackFull/Rejected reporting without diagnostics expansion.",
            "Promote only after runtime callers can observe local result vocabulary without changing CLI diagnostics output."),
        new(
            PartialApplyIntegrationGate.CompositorOwnership,
            false,
            "Dry-run and adapter preflight tests keep segment ownership/execution outside compositor mutation.",
            "DrawingBackendCompositorShadowProbe executes shadow segmented reads through the adapter outside DrawingBackendCompositor and verifies execute order, resolver ownership, and hit-test no-change.",
            "Production-off no-change tests prove the feed does not alter DrawingBackendCompositor counters, backend Execute calls, dirty range propagation, hit-test results, or diagnostics text in default-off or enabled-secondary modes; enabled tests prove rendering still executes the normal full-frame batch.",
            "None; DrawingBackendCompositor owns one retained frame with one resolver boundary.",
            "Compositor no-mutation tests keep current counters, retained frame, and backend execution unchanged.",
            "Compositor ownership of multiple retained snapshots must be explicit before hookup.",
            "Promote only after compositor integration is explicitly opt-in and proves segmented execution does not change existing full-frame behavior or hit-test lookup."),
        new(
            PartialApplyIntegrationGate.RegressionCoverage,
            false,
            "Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds.",
            "Shadow owner, runtime owner seam, compositor shadow probe, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path.",
            "Production-off tests cover default-off equivalence, enabled-secondary equivalence, accepted partial rehearsal, fallback, rebuild, empty batch, malformed dirty ranges, compositor probe hardening, dirty-range isolation, throw path, clipped hit-test, and diagnostics text no-change.",
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