using Irix.Rendering;

namespace Irix.Poc;

internal interface IScrollPresentationRuntimeAdapter
{
    ScrollState CurrentScroll { get; }

    Task DispatchScrollPresentationInterruptedAsync(
        ScrollPresentationInterruptDecision decision,
        CancellationToken cancellationToken = default);
}

internal interface IScrollPresentationCompositorAdapter
{
    ValueTask<ScrollPresentationSample> SampleAndCancelAsync(
        NodeKey targetKey,
        CancellationToken cancellationToken = default);

    ValueTask StartAsync(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot,
        CancellationToken cancellationToken = default);

    bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY);
}

internal interface IScrollPresentationRetainedSnapshotProvider
{
    RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot { get; }
}

internal readonly struct ScrollPresentationSample(
    bool HasValue,
    double PresentedScrollY) : IEquatable<ScrollPresentationSample>
{
    public bool HasValue { get; } = HasValue;
    public double PresentedScrollY { get; } = PresentedScrollY;

    public bool Equals(ScrollPresentationSample other)
    {
        return HasValue == other.HasValue
            && PresentedScrollY.Equals(other.PresentedScrollY);
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationSample other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(HasValue, PresentedScrollY);

    public static bool operator ==(ScrollPresentationSample left, ScrollPresentationSample right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationSample left, ScrollPresentationSample right) => !left.Equals(right);
}

internal readonly struct CounterScrollPresentationRuntimeAdapter(
    Runtime<CounterModel, CounterMessage> Runtime) : IScrollPresentationRuntimeAdapter
{
    public ScrollState CurrentScroll => Runtime.CurrentModel.Scroll;

    public Task DispatchScrollPresentationInterruptedAsync(
        ScrollPresentationInterruptDecision decision,
        CancellationToken cancellationToken = default)
    {
        return Runtime.DispatchAndStageRetainedFrameAsync(
            new CounterMessage.ScrollPresentationInterrupted(decision),
            cancellationToken);
    }
}

internal readonly struct CompositorLoopScrollPresentationAdapter(
    CompositorLoop CompositorLoop) : IScrollPresentationCompositorAdapter
{
    public async ValueTask<ScrollPresentationSample> SampleAndCancelAsync(
        NodeKey targetKey,
        CancellationToken cancellationToken = default)
    {
        var sample = await CompositorLoop.SampleAndCancelCompositionScrollPresentationAsync(targetKey, cancellationToken);
        return new ScrollPresentationSample(sample.HasValue, sample.PresentedScrollY);
    }

    public ValueTask StartAsync(
        in CompositionScrollPresentationDeclaration declaration,
        RenderPipelineRetainedInputSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        return CompositorLoop.StartCompositionScrollPresentationAsync(declaration, snapshot, cancellationToken);
    }

    public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
    {
        return CompositorLoop.TryGetPresentedScrollY(targetKey, out presentedScrollY);
    }
}

internal readonly struct WindowDrawCommandTranslatorRetainedSnapshotProvider(
    WindowDrawCommandTranslator Translator) : IScrollPresentationRetainedSnapshotProvider
{
    public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot => Translator.LastRetainedInputSnapshot;
}
