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

internal interface ICompositionAnimationCompositor
{
    void SetCompositionAnimationDeclaration(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot);

    void ClearCompositionAnimation();

    ValueTask<CompositionBackendExecutionResult> RenderCompositionAnimationTickAtAsync(
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken = default);
}

internal interface ICompositionFramePacingProvider
{
    CompositionFramePacing FramePacing { get; }
}

internal enum CompositionRenderInvalidationKind : byte
{
    None,
    ScrollPresentation,
    ViewportChanged,
    TreeStructure,
    LayoutAffecting,
    TextSizeAffecting,
    MaxScrollChanged
}

internal readonly struct CompositionRenderInvalidation(CompositionRenderInvalidationKind Kind) : IEquatable<CompositionRenderInvalidation>
{
    public CompositionRenderInvalidationKind Kind { get; } = Kind;
    public bool CancelsScrollPresentation => Kind != CompositionRenderInvalidationKind.None;

    public static CompositionRenderInvalidation None => default;

    public static CompositionRenderInvalidation ScrollPresentation => new(CompositionRenderInvalidationKind.ScrollPresentation);

    public static CompositionRenderInvalidation MaxScrollChanged => new(CompositionRenderInvalidationKind.MaxScrollChanged);

    public static CompositionRenderInvalidation FromLayoutRebuildReason(LayoutRebuildReason reason)
    {
        return reason switch
        {
            LayoutRebuildReason.ViewportChanged => new CompositionRenderInvalidation(CompositionRenderInvalidationKind.ViewportChanged),
            LayoutRebuildReason.TreeStructure => new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TreeStructure),
            LayoutRebuildReason.LayoutAffecting => new CompositionRenderInvalidation(CompositionRenderInvalidationKind.LayoutAffecting),
            LayoutRebuildReason.TextSizeAffecting => new CompositionRenderInvalidation(CompositionRenderInvalidationKind.TextSizeAffecting),
            _ => None
        };
    }

    public bool Equals(CompositionRenderInvalidation other) => Kind == other.Kind;

    public override bool Equals(object? obj) => obj is CompositionRenderInvalidation other && Equals(other);

    public override int GetHashCode() => Kind.GetHashCode();

    public static bool operator ==(CompositionRenderInvalidation left, CompositionRenderInvalidation right) => left.Equals(right);

    public static bool operator !=(CompositionRenderInvalidation left, CompositionRenderInvalidation right) => !left.Equals(right);
}

internal interface ICompositionInvalidationProvider
{
    CompositionRenderInvalidation LastCompositionInvalidation { get; }
}
