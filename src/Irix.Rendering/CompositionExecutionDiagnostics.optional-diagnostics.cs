#if IRIX_DIAGNOSTICS
namespace Irix.Rendering;

internal enum CompositionExecutionKind : byte
{
    None,
    TransformOpacityTick,
    ScrollPresentationTick,
    RetainedUpdateScrollPresentation,
    AnimationPresentationTick
}

internal enum CompositionExecutionSkipReason : byte
{
    None,
    NoActivePlan,
    BackendDoesNotImplementComposition,
    MissingBackendCapability,
    MissingRetainedFrame,
    InvalidPlanForRetainedFrame,
    DeviceLostRecovered
}

internal readonly struct CompositionExecutionStatus(
    CompositionExecutionKind Kind,
    CompositionExecutionSkipReason SkipReason,
    CompositionBackendCapabilities RequiredCapabilities,
    CompositionBackendCapabilities BackendCapabilities,
    CompositionFramePacing FramePacing,
    int LayerCount,
    int CommandCount) : IEquatable<CompositionExecutionStatus>
{
    public CompositionExecutionKind Kind { get; } = Kind;
    public CompositionExecutionSkipReason SkipReason { get; } = SkipReason;
    public CompositionBackendCapabilities RequiredCapabilities { get; } = RequiredCapabilities;
    public CompositionBackendCapabilities BackendCapabilities { get; } = BackendCapabilities;
    public CompositionFramePacing FramePacing { get; } = FramePacing;
    public int LayerCount { get; } = LayerCount;
    public int CommandCount { get; } = CommandCount;
    public bool IsSkipped => SkipReason != CompositionExecutionSkipReason.None;

    public static CompositionExecutionStatus Executed(
        CompositionExecutionKind kind,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount,
        int commandCount)
    {
        return new CompositionExecutionStatus(
            kind,
            CompositionExecutionSkipReason.None,
            requiredCapabilities,
            backendCapabilities,
            framePacing,
            layerCount,
            commandCount);
    }

    public static CompositionExecutionStatus Skipped(
        CompositionExecutionKind kind,
        CompositionExecutionSkipReason reason,
        CompositionBackendCapabilities requiredCapabilities,
        CompositionBackendCapabilities backendCapabilities,
        CompositionFramePacing framePacing,
        int layerCount = 0,
        int commandCount = 0)
    {
        return new CompositionExecutionStatus(
            kind,
            reason,
            requiredCapabilities,
            backendCapabilities,
            framePacing,
            layerCount,
            commandCount);
    }

    public bool Equals(CompositionExecutionStatus other)
    {
        return Kind == other.Kind
            && SkipReason == other.SkipReason
            && RequiredCapabilities == other.RequiredCapabilities
            && BackendCapabilities == other.BackendCapabilities
            && FramePacing == other.FramePacing
            && LayerCount == other.LayerCount
            && CommandCount == other.CommandCount;
    }

    public override bool Equals(object? obj) => obj is CompositionExecutionStatus other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, SkipReason, RequiredCapabilities, BackendCapabilities, FramePacing, LayerCount, CommandCount);

    public static bool operator ==(CompositionExecutionStatus left, CompositionExecutionStatus right) => left.Equals(right);

    public static bool operator !=(CompositionExecutionStatus left, CompositionExecutionStatus right) => !left.Equals(right);
}
#endif
