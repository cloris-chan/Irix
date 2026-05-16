using Irix.Drawing;

namespace Irix.Rendering;

internal enum SegmentedBackendExecutionStrategy : byte
{
    PerSegmentExecute,
    CompositeResolverAdapter,
    ResourceRebase
}

internal readonly struct SegmentedBackendExecutionAdapterDecision(
    SegmentedBackendExecutionStrategy PreferredStrategy,
    string Rationale,
    string BackendContractImpact,
    string BlockedAlternatives) : IEquatable<SegmentedBackendExecutionAdapterDecision>
{
    public SegmentedBackendExecutionStrategy PreferredStrategy { get; } = PreferredStrategy;
    public string Rationale { get; } = Rationale;
    public string BackendContractImpact { get; } = BackendContractImpact;
    public string BlockedAlternatives { get; } = BlockedAlternatives;

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
        "Execute each retained resource segment with its owning resolver; this is the smallest shape that preserves current local resource handles.",
        "No IDrawingBackend.Execute signature change; an adapter would call the existing Execute once per contiguous resource segment.",
        "Composite resolver cannot identify a command's resource owner without segment metadata; resource rebase requires text/style copying and command rewriting; stable global handles remain postponed.");
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
