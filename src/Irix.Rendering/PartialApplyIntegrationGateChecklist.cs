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
            "Default-off RetainedRenderFrameSegmentOwnership sits beside the production RetainedRenderFrame, can opt in a segmented owner that follows normal batches, and DrawingBackendCompositor's internal handoff selector can invoke RetainedRenderFrameHandoffHarness for a fresh accepted partial owner while TryReadFrame remains unchanged.",
            "Default-off selected render-source path in DrawingBackendCompositor can execute fresh accepted RuntimeOwner.ReadSegments through the real backend for one frame; disabled, missing, stale, rejected, fallback, and empty-read cases still use RetainedRenderFrame.TryReadFrame.",
            "Backend contract and D3D12 execution tests remain unchanged.",
            "Runtime retained frame must expose segmented resolver reads before hookup.",
            "Promote only after an internal retained-frame owner exposes segment reads from real retained state without changing the existing TryReadFrame contract."),
        new(
            PartialApplyIntegrationGate.ResourceDisposePolicy,
            false,
            "Segment table and segmented retained-frame owner lifecycle tests cover partial accept, full fallback, invalidate, dispose, replaced old ranges, and invalid segment reads.",
            "SegmentedRetainedFrameOwner, SegmentedRetainedFrameRuntimeOwner, and the opt-in diagnostic harness cover full apply, accepted partial rehearsal, explicit full fallback rehearsal, rebuild, disabled mode, invalidate, and dispose with multiple snapshots.",
            "Retained-frame segment ownership and handoff harness tests cover default-off no segmented owner, enabled full apply, accepted partial, rejected partial before fallback, fallback full, empty batch, malformed dirty ranges, invalidate, disposal, repeated replacement, throw path, and multiple FrameDrawingResources retain/release exactly-once while TryReadFrame survives.",
            "None; production RetainedRenderFrame is still not the segmented render-source owner of multiple retained snapshots across command ranges.",
            "Existing retained frame resource ownership tests keep same-frame behavior sealed.",
            "Production render-source ownership must release retained snapshots exactly once across every path.",
            "Promote only after production render-source ownership paths cover accepted partial, rejected partial, full fallback, empty frame, invalidate, dispose, and repeated replacement with multiple FrameDrawingResources snapshots."),
        new(
            PartialApplyIntegrationGate.CommandRangeStability,
            false,
            "Planner, segment table, and segmented reader tests cover unstable, invalid, overlapping, and non-contiguous ranges.",
            "SegmentedRetainedFrameOwner reports ShadowRejected and leaves command, segment, and retained-root state unchanged when an applied plan cannot be accepted by the shadow owner.",
            "RetainedRenderFrameSegmentOwnership runs planner to hit target projection to root patch to owner partial accept, reports fallback while preserving commands, resources, retained root, and hit targets before fallback when projection or malformed dirty ranges make rehearsal fail, and then explicitly full-applies fallback state.",
            "None; production RetainedRenderFrame still does not replace cross-frame command ranges from segmented ownership.",
            "No-change compositor tests keep current full/guarded partial behavior sealed.",
            "Runtime command replacement must require stable contiguous dirty command ranges.",
            "Promote only after runtime rejects mismatched command counts and malformed dirty ranges before any command buffer or segment table mutation."),
        new(
            PartialApplyIntegrationGate.HitTargetMetadataProjection,
            false,
            "Hit target projector tests cover action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls.",
            "None; shadow owner consumes projector output but hit-test runtime remains unchanged.",
            "Retained-frame segment ownership stores secondary hit target metadata; handoff harness uses owner-side hit targets for normal hit, clipped hit, no-hit, dirty action id projection, and projection-failure fallback without changing production hit-test runtime.",
            "None; DrawingBackendCompositor hit-test runtime still consumes the existing retained frame hit targets from full layout output.",
            "Hit-test behavior tests keep retained geometry and compositor lookup unchanged.",
            "Runtime projector must reproject action metadata without next layout output.",
            "Promote only after the runtime partial path consumes retained geometry plus next-root action metadata without reading next layout output."),
        new(
            PartialApplyIntegrationGate.RetainedRootUpdate,
            false,
            "Retained root metadata patcher tests cover dirty control metadata projection and fallback cases.",
            "Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal atomically advances retained root metadata with command/resource segments.",
            "Retained-frame segment ownership accepted partial rehearsal advances secondary retained root metadata and hit target metadata while RenderPipeline's production retained root baseline remains unchanged.",
            "None; RenderPipeline retained root baseline is not patched by a production partial path.",
            "RenderPipeline.Build tests keep the current diff/layout baseline unchanged.",
            "Accepted partial updates must advance retained root metadata for the next diff.",
            "Promote only after accepted runtime partials atomically advance retained root metadata with command segments and hit targets, while failed partials leave the old baseline intact."),
        new(
            PartialApplyIntegrationGate.FallbackReporting,
            false,
            "Planner result tests cover AppliedPartial, FallbackFull, Rejected, and every local reason.",
            "Shadow result vocabulary reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, and ShadowRejected for tests/internal diagnostics only.",
            "Production-owner feed result reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, fallback-applied, owner-state-preserved-before-fallback flags, and batch freshness stamps; DrawingBackendCompositor.LastHandoffResult maps selector outcomes to Disabled, MissingOwner, Executed, FallbackFull, and Rejected for tests/internal callers only.",
            "None; no runtime/compositor reporting hookup exists.",
            "Diagnostics formatter tests keep CLI output unchanged.",
            "Runtime hookup must preserve local AppliedPartial/FallbackFull/Rejected reporting without diagnostics expansion.",
            "Promote only after runtime callers can observe local result vocabulary without changing CLI diagnostics output."),
        new(
            PartialApplyIntegrationGate.CompositorOwnership,
            false,
            "Dry-run and adapter preflight tests keep segment ownership/execution outside compositor mutation.",
            "DrawingBackendCompositorShadowProbe executes shadow segmented reads through the adapter outside DrawingBackendCompositor and verifies execute order, resolver ownership, and hit-test no-change.",
            "Production-adjacent no-change tests prove retained-frame segment ownership, the feed, and DrawingBackendCompositor's default-off selector do not alter DrawingBackendCompositor counters, backend Execute calls, dirty range propagation, hit-test results, or diagnostics text when disabled or enabled; enabled selector executes only a fresh accepted partial owner, rejects stale ownership, and uses segment-local dirty ranges and internal handoff counter semantics only.",
            "DrawingBackendCompositor's internal selector can make fresh accepted segmented reads the selected render source for a frame, route segment-local dirty ranges through the real backend, report LastHandoffResult, and use owner-side hit targets only after selected execution succeeds.",
            "Compositor no-mutation tests keep current counters, retained frame, and backend execution unchanged.",
            "Compositor ownership of multiple retained snapshots must be explicit before hookup.",
            "Promote only after compositor integration is explicitly opt-in and proves segmented execution does not change existing full-frame behavior or hit-test lookup."),
        new(
            PartialApplyIntegrationGate.RegressionCoverage,
            false,
            "Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds.",
            "Shadow owner, runtime owner seam, compositor shadow probe, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path.",
            "Production-adjacent tests cover retained-frame segment ownership, main-compositor selector default-off lazy allocation, same-frame freshness guard, missing-owner, stale-owner, fallback, rejected, empty-read fallback, enabled-secondary equivalence, internal result reporting, candidate render-source backend call order, accepted partial rehearsal, owner-side hit target lookup, projection failure fallback, multiple FrameDrawingResources lifecycle exact-once, repeated replacement, handoff counter semantics, segment-local dirty-range handoff, dirty-range mismatch routing, four-piece atomicity, rebuild, malformed dirty ranges, dispose, compositor probe hardening, dirty-range isolation, throw path, clipped/no-hit hit-test, and diagnostics text no-change.",
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