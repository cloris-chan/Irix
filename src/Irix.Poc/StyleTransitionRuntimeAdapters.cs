using Irix.Rendering;

namespace Irix.Poc;

internal readonly struct DrawingBackendStyleTransitionCompositorAdapter(
    DrawingBackendCompositor Compositor) : IStyleTransitionCompositorAdapter
{
    public ValueTask StartAsync(
        in CompositionAnimationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ((ICompositionAnimationCompositor)Compositor).SetCompositionAnimationDeclaration(declaration, snapshot);
        return ValueTask.CompletedTask;
    }

    public ValueTask CancelAsync(NodeKey targetKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ((ICompositionAnimationCompositor)Compositor).ClearCompositionAnimation();
        return ValueTask.CompletedTask;
    }
}

internal readonly struct DrawingBackendStyleTransitionPresentationActivationCompositorAdapter(
    DrawingBackendCompositor Compositor) : IStyleTransitionPresentationActivationCompositorAdapter
{
    public CompositionAnimationPresentationSetActivationPreflightResult PreparePresentationActivation(
        ReadOnlySpan<CompositionAnimationDeclaration> declarations,
        RenderPipelineRetainedInputSnapshot? snapshot)
    {
        return Compositor.PrepareCompositionAnimationPresentationSetActivation(declarations, snapshot);
    }

    public void ActivatePresentationPlan(in CompositionAnimationPresentationSetPlan plan)
    {
        Compositor.ActivateCompositionAnimationPresentationPlan(plan);
    }
}

internal readonly struct FixedStyleTransitionRetainedSnapshotProvider(
    RenderPipelineRetainedInputSnapshot? Snapshot) : IStyleTransitionRetainedSnapshotProvider
{
    public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot => Snapshot;
}

internal sealed class SingleStyleTransitionRuntimeAdapter(
    StyleTransitionRuntimeDecision Decision) : IStyleTransitionRuntimeAdapter
{
    private StyleTransitionRuntimeDecision _decision = Decision;

    public StyleTransitionRuntimeResult LastResult { get; private set; }

    public StyleTransitionRuntimeDecision ConsumeStyleTransitionDecision()
    {
        var decision = _decision;
        _decision = default;
        return decision;
    }

    public void PublishStyleTransitionResult(in StyleTransitionRuntimeResult result)
    {
        LastResult = result;
    }
}
