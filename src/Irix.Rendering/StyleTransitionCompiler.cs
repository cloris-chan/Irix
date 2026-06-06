namespace Irix.Rendering;

internal readonly struct StyleTransitionState(
    float TranslateX,
    float TranslateY,
    float Opacity) : IEquatable<StyleTransitionState>
{
    public float TranslateX { get; } = TranslateX;
    public float TranslateY { get; } = TranslateY;
    public float Opacity { get; } = Opacity;

    public static StyleTransitionState Default => new(0f, 0f, 1f);

    public bool Equals(StyleTransitionState other)
    {
        return TranslateX.Equals(other.TranslateX)
            && TranslateY.Equals(other.TranslateY)
            && Opacity.Equals(other.Opacity);
    }

    public override bool Equals(object? obj) => obj is StyleTransitionState other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TranslateX, TranslateY, Opacity);

    public static bool operator ==(StyleTransitionState left, StyleTransitionState right) => left.Equals(right);

    public static bool operator !=(StyleTransitionState left, StyleTransitionState right) => !left.Equals(right);
}

internal readonly struct StyleTransitionCompileRequest(
    NodeKey TargetKey,
    ReadOnlyMemory<VirtualNodeProperty> PreviousProperties,
    ReadOnlyMemory<VirtualNodeProperty> NextProperties,
    CompositionTimestamp StartTimestamp,
    CompositionDuration Duration,
    CompositionAnimationEasing Easing = CompositionAnimationEasing.Linear,
    CompositionAnimationInstanceId InstanceId = default,
    CompositionAnimationMarker[]? Markers = null,
    CompositionAnimationRepeatMode RepeatMode = CompositionAnimationRepeatMode.Once)
{
    private readonly CompositionAnimationMarker[]? _markers = Markers;

    public NodeKey TargetKey { get; } = TargetKey;
    public ReadOnlyMemory<VirtualNodeProperty> PreviousProperties { get; } = PreviousProperties;
    public ReadOnlyMemory<VirtualNodeProperty> NextProperties { get; } = NextProperties;
    public CompositionTimestamp StartTimestamp { get; } = StartTimestamp;
    public CompositionDuration Duration { get; } = Duration;
    public CompositionAnimationEasing Easing { get; } = Easing;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;
    public CompositionAnimationRepeatMode RepeatMode { get; } = RepeatMode;
    internal CompositionAnimationMarker[]? MarkerArray => _markers;
}

internal enum StyleTransitionCompileStatus : byte
{
    None,
    CompiledCompositionDeclaration,
    NoChangedProperties,
    MissingTargetKey,
    NonPositiveDuration,
    RequiresLayout,
    RequiresDrawUpdate,
    NoCompositePropertyChanged,
    InvalidCompositeValue,
}

internal readonly struct StyleTransitionCompileResult(
    StyleTransitionCompileStatus Status,
    StyleDeltaPlan DeltaPlan,
    CompositionAnimationDeclaration Declaration = default,
    StyleTransitionState From = default,
    StyleTransitionState To = default) : IEquatable<StyleTransitionCompileResult>
{
    public StyleTransitionCompileStatus Status { get; } = Status;
    public StyleDeltaPlan DeltaPlan { get; } = DeltaPlan;
    public CompositionAnimationDeclaration Declaration { get; } = Declaration;
    public StyleTransitionState From { get; } = From;
    public StyleTransitionState To { get; } = To;

    public bool HasDeclaration => Status == StyleTransitionCompileStatus.CompiledCompositionDeclaration;

    public bool Equals(StyleTransitionCompileResult other)
    {
        return Status == other.Status
            && DeltaPlan == other.DeltaPlan
            && Declaration == other.Declaration
            && From == other.From
            && To == other.To;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionCompileResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Status, DeltaPlan, Declaration, From, To);

    public static bool operator ==(StyleTransitionCompileResult left, StyleTransitionCompileResult right) => left.Equals(right);

    public static bool operator !=(StyleTransitionCompileResult left, StyleTransitionCompileResult right) => !left.Equals(right);
}

internal static class StyleTransitionCompiler
{
    public static StyleTransitionCompileResult Compile(in StyleTransitionCompileRequest request)
    {
        var deltaPlan = StyleDeltaPlanner.Plan(request.PreviousProperties.Span, request.NextProperties.Span);
        if (deltaPlan.Changes.IsEmpty)
        {
            return CreateFallback(StyleTransitionCompileStatus.NoChangedProperties, deltaPlan);
        }

        if (request.TargetKey == NodeKey.None)
        {
            return CreateFallback(StyleTransitionCompileStatus.MissingTargetKey, deltaPlan);
        }

        if (!request.Duration.IsPositive)
        {
            return CreateFallback(StyleTransitionCompileStatus.NonPositiveDuration, deltaPlan);
        }

        if (!deltaPlan.CanReuseLayout)
        {
            return CreateFallback(StyleTransitionCompileStatus.RequiresLayout, deltaPlan);
        }

        if (deltaPlan.RequiresDrawUpdate)
        {
            return CreateFallback(StyleTransitionCompileStatus.RequiresDrawUpdate, deltaPlan);
        }

        if (!deltaPlan.RequiresCompositionUpdate || deltaPlan.Changes.CompositionMask == 0)
        {
            return CreateFallback(StyleTransitionCompileStatus.NoCompositePropertyChanged, deltaPlan);
        }

        if (!TryReadState(request.PreviousProperties.Span, out var from)
            || !TryReadState(request.NextProperties.Span, out var to))
        {
            return CreateFallback(StyleTransitionCompileStatus.InvalidCompositeValue, deltaPlan);
        }

        var declaration = new CompositionAnimationDeclaration(
            request.TargetKey,
            new CompositionAnimationTimeline(request.StartTimestamp, request.Duration, request.RepeatMode),
            new CompositionTransformAnimation(
                new CompositionScalarAnimation(from.TranslateX, to.TranslateX, request.Easing),
                new CompositionScalarAnimation(from.TranslateY, to.TranslateY, request.Easing)),
            new CompositionScalarAnimation(from.Opacity, to.Opacity, request.Easing),
            request.InstanceId,
            request.MarkerArray);
        return new StyleTransitionCompileResult(
            StyleTransitionCompileStatus.CompiledCompositionDeclaration,
            deltaPlan,
            declaration,
            from,
            to);
    }

    private static StyleTransitionCompileResult CreateFallback(StyleTransitionCompileStatus status, StyleDeltaPlan deltaPlan)
    {
        return new StyleTransitionCompileResult(status, deltaPlan);
    }

    private static bool TryReadState(ReadOnlySpan<VirtualNodeProperty> properties, out StyleTransitionState state)
    {
        var translateX = 0f;
        var translateY = 0f;
        var opacity = 1f;

        foreach (var property in properties)
        {
            if (property.Key == VirtualPropertyKey.TranslateX)
            {
                if (!TryReadFiniteSingle(property.Value, out translateX))
                {
                    state = default;
                    return false;
                }
            }
            else if (property.Key == VirtualPropertyKey.TranslateY)
            {
                if (!TryReadFiniteSingle(property.Value, out translateY))
                {
                    state = default;
                    return false;
                }
            }
            else if (property.Key == VirtualPropertyKey.LayerOpacity)
            {
                if (!TryReadFiniteSingle(property.Value, out opacity))
                {
                    state = default;
                    return false;
                }
            }
        }

        state = new StyleTransitionState(translateX, translateY, opacity);
        return true;
    }

    private static bool TryReadFiniteSingle(PropertyValue value, out float result)
    {
        if (!value.TryGetNumber(out var number) || !double.IsFinite(number))
        {
            result = default;
            return false;
        }

        result = (float)number;
        return float.IsFinite(result);
    }
}
