namespace Irix.Rendering;

public interface ICompositor
{
    ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default);
}

internal interface IRetainedFrameStagingCompositor
{
    ValueTask StageRetainedFrameAsync(
        RenderFrameBatch renderFrameBatch,
        RetainedRenderFrameSegmentOwnership? ownership,
        CancellationToken cancellationToken = default);
}

internal interface ICompositionScrollPresentationCompositor
{
    void SetCompositionScrollPresentationDeclaration(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot);

    void ClearCompositionScrollPresentation();

    ValueTask<CompositionBackendExecutionResult> RenderCompositionScrollPresentationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default);

    bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY);
}

internal interface ICompositionFramePacingProvider
{
    CompositionFramePacing FramePacing { get; }
}

internal readonly struct CompositionRenderInvalidation(bool CancelsScrollPresentation) : IEquatable<CompositionRenderInvalidation>
{
    public bool CancelsScrollPresentation { get; } = CancelsScrollPresentation;

    public static CompositionRenderInvalidation None => default;

    public static CompositionRenderInvalidation ScrollPresentation => new(CancelsScrollPresentation: true);

    public static CompositionRenderInvalidation FromLayoutRebuildReason(LayoutRebuildReason reason)
    {
        return reason is LayoutRebuildReason.ViewportChanged
            or LayoutRebuildReason.TreeStructure
            or LayoutRebuildReason.LayoutAffecting
            or LayoutRebuildReason.TextSizeAffecting
            ? ScrollPresentation
            : None;
    }

    public bool Equals(CompositionRenderInvalidation other) => CancelsScrollPresentation == other.CancelsScrollPresentation;

    public override bool Equals(object? obj) => obj is CompositionRenderInvalidation other && Equals(other);

    public override int GetHashCode() => CancelsScrollPresentation.GetHashCode();

    public static bool operator ==(CompositionRenderInvalidation left, CompositionRenderInvalidation right) => left.Equals(right);

    public static bool operator !=(CompositionRenderInvalidation left, CompositionRenderInvalidation right) => !left.Equals(right);
}

internal interface ICompositionInvalidationProvider
{
    CompositionRenderInvalidation LastCompositionInvalidation { get; }
}
