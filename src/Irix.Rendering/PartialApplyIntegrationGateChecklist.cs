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
    string BlockingCondition);

internal static class PartialApplyIntegrationGateChecklist
{
    public static IReadOnlyList<PartialApplyIntegrationGateStatus> RequiredGates { get; } =
    [
        new(PartialApplyIntegrationGate.ResourceResolverOwnership, false, "Old retained commands and replacement commands must resolve through their owning snapshots."),
        new(PartialApplyIntegrationGate.ResourceDisposePolicy, false, "Retained resource snapshots must release exactly once across partial, full fallback, invalidate, and dispose."),
        new(PartialApplyIntegrationGate.CommandRangeStability, false, "Dirty element ranges must map to stable contiguous command ranges before replacement."),
        new(PartialApplyIntegrationGate.HitTargetMetadataProjection, false, "Action metadata must project from next VirtualNode without consuming next layout output."),
        new(PartialApplyIntegrationGate.RetainedRootUpdate, false, "Accepted partial updates must advance retained root metadata for the next diff."),
        new(PartialApplyIntegrationGate.FallbackReporting, false, "Local AppliedPartial/FallbackFull/Rejected reporting must remain available without diagnostics expansion."),
        new(PartialApplyIntegrationGate.CompositorOwnership, false, "Compositor ownership of multiple retained snapshots must be explicit before hookup."),
        new(PartialApplyIntegrationGate.RegressionCoverage, false, "Planner, retained frame, compositor, diagnostics, and hit-test no-change coverage must remain green.")
    ];

    public static bool CanHookUpPartialApply => false;
}