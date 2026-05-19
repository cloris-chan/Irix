using Irix.Drawing;

namespace Irix.Rendering;

internal enum SegmentedBackendExecutionStrategy : byte
{
    PerSegmentExecute,
    CompositeResolverAdapter,
    ResourceRebase
}

internal enum SegmentedBackendExecutionRationale : byte
{
    SmallestShapeThatPreservesCurrentLocalResourceHandles
}

internal enum SegmentedBackendExecutionContractImpact : byte
{
    ExistingIDrawingBackendExecuteSignatureRemainsUnchanged
}

[Flags]
internal enum SegmentedBackendExecutionBlockedAlternative : byte
{
    None = 0,
    CompositeResolverNeedsSegmentMetadata = 1 << 0,
    ResourceRebaseNeedsTextStyleCopyingAndCommandRewriting = 1 << 1,
    StableGlobalHandlesRemainDeferred = 1 << 2
}

internal readonly struct SegmentedBackendExecutionAdapterDecision(
    SegmentedBackendExecutionStrategy PreferredStrategy,
    SegmentedBackendExecutionRationale Rationale,
    SegmentedBackendExecutionContractImpact BackendContractImpact,
    SegmentedBackendExecutionBlockedAlternative BlockedAlternatives) : IEquatable<SegmentedBackendExecutionAdapterDecision>
{
    public SegmentedBackendExecutionStrategy PreferredStrategy { get; } = PreferredStrategy;
    public SegmentedBackendExecutionRationale Rationale { get; } = Rationale;
    public SegmentedBackendExecutionContractImpact BackendContractImpact { get; } = BackendContractImpact;
    public SegmentedBackendExecutionBlockedAlternative BlockedAlternatives { get; } = BlockedAlternatives;

    public bool Equals(SegmentedBackendExecutionAdapterDecision other)
    {
        return PreferredStrategy == other.PreferredStrategy
            && Rationale == other.Rationale
            && BackendContractImpact == other.BackendContractImpact
            && BlockedAlternatives == other.BlockedAlternatives;
    }

    public override bool Equals(object? obj) => obj is SegmentedBackendExecutionAdapterDecision other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(PreferredStrategy, Rationale, BackendContractImpact, BlockedAlternatives);

    public static bool operator ==(SegmentedBackendExecutionAdapterDecision left, SegmentedBackendExecutionAdapterDecision right) => left.Equals(right);

    public static bool operator !=(SegmentedBackendExecutionAdapterDecision left, SegmentedBackendExecutionAdapterDecision right) => !left.Equals(right);
}

internal static class SegmentedBackendExecutionAdapterDesign
{
    public static SegmentedBackendExecutionAdapterDecision Preferred { get; } = new(
        SegmentedBackendExecutionStrategy.PerSegmentExecute,
        SegmentedBackendExecutionRationale.SmallestShapeThatPreservesCurrentLocalResourceHandles,
        SegmentedBackendExecutionContractImpact.ExistingIDrawingBackendExecuteSignatureRemainsUnchanged,
        SegmentedBackendExecutionBlockedAlternative.CompositeResolverNeedsSegmentMetadata
        | SegmentedBackendExecutionBlockedAlternative.ResourceRebaseNeedsTextStyleCopyingAndCommandRewriting
        | SegmentedBackendExecutionBlockedAlternative.StableGlobalHandlesRemainDeferred);
}

internal sealed class SegmentedBackendExecutionAdapter(IDrawingBackend backend)
{
    public void Execute(in FrameContext frameContext, IReadOnlyList<SegmentedFrameRead> segments)
    {
        backend.BeginFrame(frameContext);
        try
        {
            foreach (var segment in segments)
            {
                backend.Execute(segment.Commands, segment.Resolver);
            }
        }
        finally
        {
            backend.EndFrame();
        }
    }
}
