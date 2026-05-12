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
            true,
            "Segmented reader and per-segment backend adapter preflight tests prove old/new resolver ownership by command segment.",
            "SegmentedRetainedFrameDiagnosticHarness and SegmentedRetainedFrameRuntimeOwner feed real batches into SegmentedRetainedFrameOwner, and DrawingBackendCompositorShadowProbe verifies segmented resolver ownership outside the compositor.",
            "Default-off RetainedRenderFrameSegmentOwnership sits beside the production RetainedRenderFrame, can opt in a segmented owner that follows normal batches, and DrawingBackendCompositor's internal handoff selector can invoke RetainedRenderFrameHandoffHarness for a fresh accepted partial owner while TryReadFrame remains unchanged.",
            "Default-off selected render-source path in DrawingBackendCompositor can execute fresh accepted RuntimeOwner.ReadSegments through the real backend for one frame; disabled, missing, stale, rejected, fallback, and empty-read cases still use RetainedRenderFrame.TryReadFrame.",
            "Backend contract and D3D12 execution tests remain unchanged.",
            "Satisfied for V1 core; public/default-on hookup remains outside this gate.",
            "Internal/default-off selected path is promoted; public API and default-on rollout remain postponed."),
        new(
            PartialApplyIntegrationGate.ResourceDisposePolicy,
            true,
            "Segment table and segmented retained-frame owner lifecycle tests cover partial accept, full fallback, invalidate, dispose, replaced old ranges, and invalid segment reads.",
            "SegmentedRetainedFrameOwner, SegmentedRetainedFrameRuntimeOwner, and the opt-in diagnostic harness cover full apply, accepted partial rehearsal, explicit full fallback rehearsal, rebuild, disabled mode, invalidate, and dispose with multiple snapshots.",
            "Retained-frame segment ownership and handoff harness tests cover default-off no segmented owner, enabled full apply, accepted partial, rejected partial before fallback, fallback full, empty batch, malformed dirty ranges, invalidate, disposal, repeated replacement, throw path, and multiple FrameDrawingResources retain/release exactly-once while TryReadFrame survives.",
            "Selected path keeps segmented snapshots owned by RuntimeOwner, reports partial fallback without mutating owner state, pairs EndFrame on backend throw, preserves hit targets before commit, and releases accepted partial snapshots exactly once on dispose.",
            "Existing retained frame resource ownership tests keep same-frame behavior sealed.",
            "Satisfied for V1 core; resource cache and stable global handles remain postponed.",
            "Internal/default-off selected path is promoted; D3D12-specific segmented ownership remains postponed."),
        new(
            PartialApplyIntegrationGate.CommandRangeStability,
            true,
            "Planner, segment table, and segmented reader tests cover unstable, invalid, overlapping, and non-contiguous ranges.",
            "SegmentedRetainedFrameOwner reports ShadowRejected and leaves command, segment, and retained-root state unchanged when an applied plan cannot be accepted by the shadow owner.",
            "RetainedRenderFrameSegmentOwnership runs planner to hit target projection to root patch to owner partial accept, reports fallback while preserving commands, resources, retained root, and hit targets when projection or malformed dirty ranges make rehearsal fail.",
            "DrawingBackendCompositor validates owner freshness, command owner, resource owner, command count, FrameId, strict dirty ranges, and contiguous segment coverage before selected execution; stale, malformed, overlapping, and command-count mismatch cases fall back before candidate execution.",
            "No-change compositor tests keep current full/guarded partial behavior sealed.",
            "Satisfied for V1 core; broader typed-id range contracts remain postponed.",
            "Internal/default-off selected path is promoted; public range contract changes remain postponed."),
        new(
            PartialApplyIntegrationGate.HitTargetMetadataProjection,
            true,
            "Hit target projector tests cover action metadata projection, dirty DFS mismatch, non-dirty drift, key/path mismatch, and nested controls.",
            "None; shadow owner consumes projector output but hit-test runtime remains unchanged.",
            "Retained-frame segment ownership stores secondary hit target metadata; handoff harness uses owner-side hit targets for normal hit, clipped hit, no-hit, dirty action id projection, and projection-failure fallback without changing production hit-test runtime.",
            "DrawingBackendCompositor uses owner-side hit targets only after selected segmented execution succeeds; disabled, missing, stale, rejected, fallback, projection failure, and backend throw paths keep retained-frame hit targets.",
            "Hit-test behavior tests keep retained geometry and compositor lookup unchanged.",
            "Satisfied for V1 core; future geometry-source changes remain postponed.",
            "Internal/default-off selected path is promoted; public hit-test API remains unchanged."),
        new(
            PartialApplyIntegrationGate.RetainedRootUpdate,
            true,
            "Retained root metadata patcher tests cover dirty control metadata projection and fallback cases.",
            "Opt-in shadow end-to-end tests prove accepted shadow partial rehearsal atomically advances retained root metadata with command/resource segments.",
            "Retained-frame segment ownership accepted partial rehearsal advances secondary retained root metadata and hit target metadata while RenderPipeline's production retained root baseline remains unchanged.",
            "Production-owner feed accepted partials atomically advance command segments, resource snapshots, retained root metadata, and hit target metadata; failed projection, malformed range, and fallback cases report fallback while preserving the previous owner state.",
            "RenderPipeline.Build tests keep the current diff/layout baseline unchanged.",
            "Satisfied for V1 core; RenderPipeline.Build default full layout behavior remains unchanged.",
            "Internal/default-off selected path is promoted; layout-skip default enablement remains postponed."),
        new(
            PartialApplyIntegrationGate.FallbackReporting,
            true,
            "Planner result tests cover AppliedPartial, FallbackFull, Rejected, and every local reason.",
            "Shadow result vocabulary reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, and ShadowRejected for tests/internal diagnostics only.",
            "Production-owner feed result reports Disabled, ShadowAppliedPartial, ShadowFallbackFull, owner-state-preserved-before-fallback flags, and batch freshness stamps; DrawingBackendCompositor.LastHandoffResult maps selector outcomes to Disabled, MissingOwner, Executed, FallbackFull, and Rejected for tests/internal callers only.",
            "DrawingBackendCompositor.LastHandoffResult includes internal reason vocabulary for Disabled, MissingOwner, StaleOwner, OwnerRejected, OwnerFallbackFull, EmptySegmentRead, DirtyRangeMismatch, MalformedSegmentCoverage, and BackendThrewBeforeCommit without changing CLI diagnostics; EmptySegmentRead is wired for empty-segment validation but not directly asserted on LastHandoffResult because the owner reports ShadowFallbackFull before reaching that path.",
            "Diagnostics formatter tests keep CLI output unchanged.",
            "Satisfied for V1 core; unified diagnostics channel remains postponed.",
            "Internal/default-off selected path is promoted; CLI diagnostics text remains unchanged."),
        new(
            PartialApplyIntegrationGate.CompositorOwnership,
            true,
            "Dry-run and adapter preflight tests keep segment ownership/execution outside compositor mutation.",
            "DrawingBackendCompositorShadowProbe executes shadow segmented reads through the adapter outside DrawingBackendCompositor and verifies execute order, resolver ownership, and hit-test no-change.",
            "Production-adjacent no-change tests prove retained-frame segment ownership, the feed, and DrawingBackendCompositor's default-off selector do not alter DrawingBackendCompositor counters, backend Execute calls, dirty range propagation, hit-test results, or diagnostics text when disabled or enabled; enabled selector executes only a fresh accepted partial owner, rejects stale ownership, and uses segment-local dirty ranges and internal handoff counter semantics only.",
            "DrawingBackendCompositor's internal selector can make fresh accepted segmented reads the selected render source for a frame, route segment-local dirty ranges through the real backend, report LastHandoffResult, and use owner-side hit targets only after selected execution succeeds.",
            "Compositor no-mutation tests keep current counters, retained frame, and backend execution unchanged.",
            "Satisfied for V1 core; public/default-on rollout remains outside this gate.",
            "Internal/default-off selected path is promoted; IDrawingBackend.Execute remains unchanged."),
        new(
            PartialApplyIntegrationGate.RegressionCoverage,
            true,
            "Preflight tests cover planner, projector, root patch, segment table, and segmented reader scaffolds.",
            "Shadow owner, runtime owner seam, compositor shadow probe, diagnostic harness, result vocabulary, disabled no-change sentinel, opt-in end-to-end, fallback, rejection, and adapter tests cover the current shadow path.",
            "Production-adjacent tests cover retained-frame segment ownership, main-compositor selector default-off lazy allocation, same-frame freshness guard, missing-owner, stale-owner, fallback, rejected, empty-read fallback, enabled-secondary equivalence, internal result reporting, candidate render-source backend call order, accepted partial rehearsal, owner-side hit target lookup, projection failure fallback, multiple FrameDrawingResources lifecycle exact-once, repeated replacement, handoff counter semantics, segment-local dirty-range handoff, dirty-range mismatch routing, four-piece atomicity, rebuild, malformed dirty ranges, dispose, compositor probe hardening, dirty-range isolation, throw path, clipped/no-hit hit-test, and diagnostics text no-change.",
            "Focused regression tests cover default-off equivalence, enabled selected segmented execution, fallback path, stale owner, backend throw, malformed guard, dirty range routing, hit-test ownership, counter semantics, diagnostics unchanged, and style-only pre-switch behavior.",
            "Focused and full test suites act as the no-change regression guard.",
            "Satisfied for V1 core; platform-specific GA hardening remains postponed.",
            "Internal/default-off selected path is promoted; MVP/GA validation remains separate.")
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
