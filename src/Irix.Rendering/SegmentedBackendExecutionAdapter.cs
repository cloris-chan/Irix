using Irix.Drawing;

namespace Irix.Rendering;

internal enum SegmentedBackendExecutionStrategy : byte
{
    PerSegmentExecute,
    CompositeResolverAdapter,
    ResourceRebase
}

internal readonly record struct SegmentedBackendExecutionAdapterDecision(
    SegmentedBackendExecutionStrategy PreferredStrategy,
    string Rationale,
    string BackendContractImpact,
    string BlockedAlternatives);

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