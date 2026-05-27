using System.Diagnostics;
using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

internal readonly struct CompositionLayerId(int Value) : IEquatable<CompositionLayerId>
{
    public int Value { get; } = Value;

    public bool IsValid => Value > 0;

    public bool Equals(CompositionLayerId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CompositionLayerId other && Equals(other);

    public override int GetHashCode() => Value;

    public static bool operator ==(CompositionLayerId left, CompositionLayerId right) => left.Equals(right);

    public static bool operator !=(CompositionLayerId left, CompositionLayerId right) => !left.Equals(right);
}

internal readonly struct CompositionTransform(float TranslateX, float TranslateY) : IEquatable<CompositionTransform>
{
    public float TranslateX { get; } = TranslateX;
    public float TranslateY { get; } = TranslateY;

    public static CompositionTransform Identity => default;

    public bool IsIdentity => TranslateX == 0f && TranslateY == 0f;

    public CompositionTransform Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        return scale.IsIdentity ? this : new CompositionTransform(TranslateX * scale.ScaleX, TranslateY * scale.ScaleY);
    }

    public bool Equals(CompositionTransform other) => TranslateX.Equals(other.TranslateX) && TranslateY.Equals(other.TranslateY);

    public override bool Equals(object? obj) => obj is CompositionTransform other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TranslateX, TranslateY);

    public static bool operator ==(CompositionTransform left, CompositionTransform right) => left.Equals(right);

    public static bool operator !=(CompositionTransform left, CompositionTransform right) => !left.Equals(right);
}

internal readonly struct CompositionOpacity(float Value) : IEquatable<CompositionOpacity>
{
    public float Value { get; } = Value;

    public static CompositionOpacity Opaque => new(1f);

    public float Normalized => float.IsFinite(Value) ? Math.Clamp(Value, 0f, 1f) : 1f;

    public bool IsOpaque => Normalized == 1f;

    public bool Equals(CompositionOpacity other) => Value.Equals(other.Value);

    public override bool Equals(object? obj) => obj is CompositionOpacity other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(CompositionOpacity left, CompositionOpacity right) => left.Equals(right);

    public static bool operator !=(CompositionOpacity left, CompositionOpacity right) => !left.Equals(right);
}

internal enum CompositionAnimationRepeatMode : byte
{
    Once,
    Loop,
    Alternate
}

internal enum CompositionAnimationEasing : byte
{
    Linear,
    SineInOut,
    SineOut
}

internal enum CompositionClipMode : byte
{
    TransformWithContent,
    Fixed
}

internal readonly struct CompositionTimestamp(long StopwatchTicks) : IEquatable<CompositionTimestamp>, IComparable<CompositionTimestamp>
{
    public long StopwatchTicks { get; } = StopwatchTicks;

    public static CompositionTimestamp Zero => default;

    public static CompositionTimestamp Now() => new(Stopwatch.GetTimestamp());

    public static CompositionTimestamp FromStopwatchTicks(long ticks) => new(ticks);

    public CompositionTimestamp Add(CompositionDuration duration) => new(StopwatchTicks + duration.StopwatchTicks);

    public CompositionDuration ElapsedSince(CompositionTimestamp start) => new(StopwatchTicks - start.StopwatchTicks);

    public int CompareTo(CompositionTimestamp other) => StopwatchTicks.CompareTo(other.StopwatchTicks);

    public bool Equals(CompositionTimestamp other) => StopwatchTicks == other.StopwatchTicks;

    public override bool Equals(object? obj) => obj is CompositionTimestamp other && Equals(other);

    public override int GetHashCode() => StopwatchTicks.GetHashCode();

    public static CompositionTimestamp operator +(CompositionTimestamp timestamp, CompositionDuration duration) => timestamp.Add(duration);

    public static CompositionDuration operator -(CompositionTimestamp timestamp, CompositionTimestamp start) => timestamp.ElapsedSince(start);

    public static bool operator ==(CompositionTimestamp left, CompositionTimestamp right) => left.Equals(right);

    public static bool operator !=(CompositionTimestamp left, CompositionTimestamp right) => !left.Equals(right);

    public static bool operator <(CompositionTimestamp left, CompositionTimestamp right) => left.StopwatchTicks < right.StopwatchTicks;

    public static bool operator <=(CompositionTimestamp left, CompositionTimestamp right) => left.StopwatchTicks <= right.StopwatchTicks;

    public static bool operator >(CompositionTimestamp left, CompositionTimestamp right) => left.StopwatchTicks > right.StopwatchTicks;

    public static bool operator >=(CompositionTimestamp left, CompositionTimestamp right) => left.StopwatchTicks >= right.StopwatchTicks;
}

internal readonly struct CompositionDuration(long StopwatchTicks) : IEquatable<CompositionDuration>, IComparable<CompositionDuration>
{
    public long StopwatchTicks { get; } = StopwatchTicks;

    public static CompositionDuration Zero => default;

    public bool IsPositive => StopwatchTicks > 0;

    public static CompositionDuration FromStopwatchTicks(long ticks) => new(ticks);

    public static CompositionDuration FromMilliseconds(int milliseconds)
    {
        var ticks = Stopwatch.Frequency * (long)Math.Max(1, milliseconds) / 1000;
        return new CompositionDuration(Math.Max(1, ticks));
    }

    public int ToPositiveMillisecondsCeiling()
    {
        if (StopwatchTicks <= 0)
        {
            return 0;
        }

        var wholeMilliseconds = StopwatchTicks / Stopwatch.Frequency * 1000;
        var remainderTicks = StopwatchTicks % Stopwatch.Frequency;
        var milliseconds = wholeMilliseconds + (remainderTicks > 0 ? 1 : 0);
        return milliseconds > int.MaxValue ? int.MaxValue : Math.Max(1, (int)milliseconds);
    }

    public int CompareTo(CompositionDuration other) => StopwatchTicks.CompareTo(other.StopwatchTicks);

    public bool Equals(CompositionDuration other) => StopwatchTicks == other.StopwatchTicks;

    public override bool Equals(object? obj) => obj is CompositionDuration other && Equals(other);

    public override int GetHashCode() => StopwatchTicks.GetHashCode();

    public static bool operator ==(CompositionDuration left, CompositionDuration right) => left.Equals(right);

    public static bool operator !=(CompositionDuration left, CompositionDuration right) => !left.Equals(right);

    public static bool operator <(CompositionDuration left, CompositionDuration right) => left.StopwatchTicks < right.StopwatchTicks;

    public static bool operator <=(CompositionDuration left, CompositionDuration right) => left.StopwatchTicks <= right.StopwatchTicks;

    public static bool operator >(CompositionDuration left, CompositionDuration right) => left.StopwatchTicks > right.StopwatchTicks;

    public static bool operator >=(CompositionDuration left, CompositionDuration right) => left.StopwatchTicks >= right.StopwatchTicks;
}

internal readonly struct CompositionAnimationTimeline(
    CompositionTimestamp StartTimestamp,
    CompositionDuration Duration,
    CompositionAnimationRepeatMode RepeatMode = CompositionAnimationRepeatMode.Once) : IEquatable<CompositionAnimationTimeline>
{
    public CompositionTimestamp StartTimestamp { get; } = StartTimestamp;
    public CompositionDuration Duration { get; } = Duration;
    public CompositionAnimationRepeatMode RepeatMode { get; } = RepeatMode;

    public float ProgressAt(CompositionTimestamp timestamp)
    {
        return SampleAt(timestamp).Progress;
    }

    public CompositionTimelineSample SampleAt(CompositionTimestamp timestamp)
    {
        var durationTicks = Duration.StopwatchTicks;
        var rawElapsed = (timestamp - StartTimestamp).StopwatchTicks;
        var elapsedTicks = Math.Max(0, rawElapsed);
        var elapsed = CompositionDuration.FromStopwatchTicks(elapsedTicks);
        if (durationTicks <= 0)
        {
            return new CompositionTimelineSample(timestamp, elapsed, 1f, 0, CompositionPlaybackDirection.Forward);
        }

        if (elapsedTicks <= 0)
        {
            return new CompositionTimelineSample(timestamp, elapsed, 0f, 0, CompositionPlaybackDirection.Forward);
        }

        if (RepeatMode == CompositionAnimationRepeatMode.Loop)
        {
            var iteration = ClampIteration(elapsedTicks / durationTicks);
            var progress = (elapsedTicks % durationTicks) / (float)durationTicks;
            return new CompositionTimelineSample(timestamp, elapsed, progress, iteration, CompositionPlaybackDirection.Forward);
        }

        if (RepeatMode == CompositionAnimationRepeatMode.Alternate)
        {
            var iteration = ClampIteration(elapsedTicks / durationTicks);
            var direction = (iteration & 1) == 0 ? CompositionPlaybackDirection.Forward : CompositionPlaybackDirection.Reverse;
            return new CompositionTimelineSample(timestamp, elapsed, ResolveAlternateProgress(elapsedTicks, durationTicks), iteration, direction);
        }

        return new CompositionTimelineSample(
            timestamp,
            elapsed,
            Math.Clamp(elapsedTicks / (float)durationTicks, 0f, 1f),
            0,
            CompositionPlaybackDirection.Forward);
    }

    public bool Equals(CompositionAnimationTimeline other)
    {
        return StartTimestamp == other.StartTimestamp
            && Duration == other.Duration
            && RepeatMode == other.RepeatMode;
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationTimeline other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StartTimestamp, Duration, RepeatMode);

    public static bool operator ==(CompositionAnimationTimeline left, CompositionAnimationTimeline right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationTimeline left, CompositionAnimationTimeline right) => !left.Equals(right);

    private static float ResolveAlternateProgress(long elapsed, long durationTicks)
    {
        var cycle = elapsed / durationTicks;
        var progress = (elapsed % durationTicks) / (float)durationTicks;
        return (cycle & 1L) == 0 ? progress : 1f - progress;
    }

    private static int ClampIteration(long iteration)
    {
        return iteration > int.MaxValue ? int.MaxValue : (int)iteration;
    }
}

internal readonly struct CompositionScalarAnimation(
    float From,
    float To,
    CompositionAnimationEasing Easing = CompositionAnimationEasing.Linear) : IEquatable<CompositionScalarAnimation>
{
    public float From { get; } = From;
    public float To { get; } = To;
    public CompositionAnimationEasing Easing { get; } = Easing;

    public static CompositionScalarAnimation Constant(float value) => new(value, value);

    public float Evaluate(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);
        if (Easing == CompositionAnimationEasing.SineInOut)
        {
            progress = 0.5f - MathF.Cos(progress * MathF.PI) * 0.5f;
        }
        else if (Easing == CompositionAnimationEasing.SineOut)
        {
            progress = MathF.Sin(progress * MathF.PI * 0.5f);
        }

        return From + (To - From) * progress;
    }

    public bool Equals(CompositionScalarAnimation other)
    {
        return From.Equals(other.From)
            && To.Equals(other.To)
            && Easing == other.Easing;
    }

    public override bool Equals(object? obj) => obj is CompositionScalarAnimation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(From, To, Easing);

    public static bool operator ==(CompositionScalarAnimation left, CompositionScalarAnimation right) => left.Equals(right);

    public static bool operator !=(CompositionScalarAnimation left, CompositionScalarAnimation right) => !left.Equals(right);
}

internal readonly struct CompositionTransformAnimation(
    CompositionScalarAnimation TranslateX,
    CompositionScalarAnimation TranslateY) : IEquatable<CompositionTransformAnimation>
{
    public CompositionScalarAnimation TranslateX { get; } = TranslateX;
    public CompositionScalarAnimation TranslateY { get; } = TranslateY;

    public static CompositionTransformAnimation Identity => new(
        CompositionScalarAnimation.Constant(0f),
        CompositionScalarAnimation.Constant(0f));

    public CompositionTransform Evaluate(float progress)
    {
        return new CompositionTransform(TranslateX.Evaluate(progress), TranslateY.Evaluate(progress));
    }

    public bool Equals(CompositionTransformAnimation other)
    {
        return TranslateX == other.TranslateX
            && TranslateY == other.TranslateY;
    }

    public override bool Equals(object? obj) => obj is CompositionTransformAnimation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TranslateX, TranslateY);

    public static bool operator ==(CompositionTransformAnimation left, CompositionTransformAnimation right) => left.Equals(right);

    public static bool operator !=(CompositionTransformAnimation left, CompositionTransformAnimation right) => !left.Equals(right);
}

internal readonly struct CompositionLayerAnimation(
    CompositionLayerId LayerId,
    int CommandStart,
    int CommandCount,
    CompositionAnimationTimeline Timeline,
    CompositionTransformAnimation Transform,
    CompositionScalarAnimation Opacity,
    CompositionAnimationInstanceId InstanceId = default,
    NodeKey TargetKey = default,
    CompositionAnimationMarker[]? Markers = null) : IEquatable<CompositionLayerAnimation>
{
    private readonly CompositionAnimationMarker[] _markers = CompositionAnimationMarker.NormalizeArray(Markers);

    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionAnimationTimeline Timeline { get; } = Timeline;
    public CompositionTransformAnimation Transform { get; } = Transform;
    public CompositionScalarAnimation Opacity { get; } = Opacity;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public NodeKey TargetKey { get; } = TargetKey;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;
    public bool HasMarkers => Markers.Length != 0;

    public CompositionLayer Evaluate(CompositionTimestamp timestamp)
    {
        var progress = Timeline.ProgressAt(timestamp);
        return new CompositionLayer(
            LayerId,
            CommandStart,
            CommandCount,
            Transform.Evaluate(progress),
            new CompositionOpacity(Opacity.Evaluate(progress)));
    }

    public bool IsValidForCommandCount(int commandCount)
    {
        return LayerId.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount;
    }

    public bool Equals(CompositionLayerAnimation other)
    {
        return LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && Timeline == other.Timeline
            && Transform == other.Transform
            && Opacity == other.Opacity
            && InstanceId == other.InstanceId
            && TargetKey == other.TargetKey
            && CompositionAnimationMarker.SequenceEqual(Markers, other.Markers);
    }

    public override bool Equals(object? obj) => obj is CompositionLayerAnimation other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(LayerId);
        hashCode.Add(CommandStart);
        hashCode.Add(CommandCount);
        hashCode.Add(Timeline);
        hashCode.Add(Transform);
        hashCode.Add(Opacity);
        hashCode.Add(InstanceId);
        hashCode.Add(TargetKey);
        CompositionAnimationMarker.AddHashCode(ref hashCode, Markers);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionLayerAnimation left, CompositionLayerAnimation right) => left.Equals(right);

    public static bool operator !=(CompositionLayerAnimation left, CompositionLayerAnimation right) => !left.Equals(right);
}

internal readonly struct CompositionScrollLayerAnimation(
    CompositionLayerId LayerId,
    int CommandStart,
    int CommandCount,
    PixelRectangle ClipBounds,
    float RetainedScrollY,
    float MaxScrollY,
    CompositionAnimationTimeline Timeline,
    CompositionScalarAnimation PresentedScrollY,
    CompositionAnimationInstanceId InstanceId = default,
    NodeKey TargetKey = default,
    CompositionAnimationMarker[]? Markers = null) : IEquatable<CompositionScrollLayerAnimation>
{
    private readonly CompositionAnimationMarker[] _markers = CompositionAnimationMarker.NormalizeArray(Markers);

    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public PixelRectangle ClipBounds { get; } = ClipBounds;
    public float RetainedScrollY { get; } = RetainedScrollY;
    public float MaxScrollY { get; } = MaxScrollY;
    public CompositionAnimationTimeline Timeline { get; } = Timeline;
    public CompositionScalarAnimation PresentedScrollY { get; } = PresentedScrollY;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public NodeKey TargetKey { get; } = TargetKey;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;
    public bool HasMarkers => Markers.Length != 0;

    public CompositionLayer Evaluate(CompositionTimestamp timestamp)
    {
        var progress = Timeline.ProgressAt(timestamp);
        var presentedScrollY = PresentedScrollY.Evaluate(progress);
        return new CompositionLayer(
            LayerId,
            CommandStart,
            CommandCount,
            new CompositionTransform(0f, RetainedScrollY - presentedScrollY),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            ToDrawRect(ClipBounds));
    }

    public bool IsValidForCommandCount(int commandCount)
    {
        return LayerId.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount
            && ClipBounds.Width > 0
            && ClipBounds.Height > 0
            && float.IsFinite(RetainedScrollY)
            && RetainedScrollY >= 0f
            && float.IsFinite(MaxScrollY)
            && MaxScrollY >= 0f
            && RetainedScrollY <= MaxScrollY
            && IsPresentedScrollInRange(PresentedScrollY, MaxScrollY);
    }

    public bool Equals(CompositionScrollLayerAnimation other)
    {
        return LayerId == other.LayerId
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && ClipBounds == other.ClipBounds
            && RetainedScrollY.Equals(other.RetainedScrollY)
            && MaxScrollY.Equals(other.MaxScrollY)
            && Timeline == other.Timeline
            && PresentedScrollY == other.PresentedScrollY
            && InstanceId == other.InstanceId
            && TargetKey == other.TargetKey
            && CompositionAnimationMarker.SequenceEqual(Markers, other.Markers);
    }

    public override bool Equals(object? obj) => obj is CompositionScrollLayerAnimation other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(LayerId);
        hashCode.Add(CommandStart);
        hashCode.Add(CommandCount);
        hashCode.Add(ClipBounds);
        hashCode.Add(RetainedScrollY);
        hashCode.Add(MaxScrollY);
        hashCode.Add(Timeline);
        hashCode.Add(PresentedScrollY);
        hashCode.Add(InstanceId);
        hashCode.Add(TargetKey);
        CompositionAnimationMarker.AddHashCode(ref hashCode, Markers);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionScrollLayerAnimation left, CompositionScrollLayerAnimation right) => left.Equals(right);

    public static bool operator !=(CompositionScrollLayerAnimation left, CompositionScrollLayerAnimation right) => !left.Equals(right);

    private static bool IsPresentedScrollInRange(in CompositionScalarAnimation animation, float maxScrollY)
    {
        return float.IsFinite(animation.From)
            && float.IsFinite(animation.To)
            && animation.From >= 0f
            && animation.To >= 0f
            && animation.From <= maxScrollY
            && animation.To <= maxScrollY;
    }

    private static DrawRect ToDrawRect(in PixelRectangle bounds)
    {
        return new DrawRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}

internal readonly struct CompositionAnimationPlan(CompositionLayerAnimation LayerAnimation) : IEquatable<CompositionAnimationPlan>
{
    public CompositionLayerAnimation LayerAnimation { get; } = LayerAnimation;

    public bool IsValidForCommandCount(int commandCount) => LayerAnimation.IsValidForCommandCount(commandCount);

    public CompositionFrame Evaluate(int commandCount, CompositionTimestamp timestamp)
    {
        if (!IsValidForCommandCount(commandCount))
        {
            throw new ArgumentException("Composition animation layer range must reference a non-empty range inside the command span.", nameof(commandCount));
        }

        return new CompositionFrame(LayerAnimation.Evaluate(timestamp));
    }

    public bool Equals(CompositionAnimationPlan other) => LayerAnimation == other.LayerAnimation;

    public override bool Equals(object? obj) => obj is CompositionAnimationPlan other && Equals(other);

    public override int GetHashCode() => LayerAnimation.GetHashCode();

    public static bool operator ==(CompositionAnimationPlan left, CompositionAnimationPlan right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationPlan left, CompositionAnimationPlan right) => !left.Equals(right);
}

internal readonly struct CompositionScrollPresentationPlan : IEquatable<CompositionScrollPresentationPlan>
{
    private readonly CompositionScrollLayerAnimation[]? _additionalLayerAnimations;

    public CompositionScrollPresentationPlan(CompositionScrollLayerAnimation layerAnimation)
    {
        LayerAnimation = layerAnimation;
        _additionalLayerAnimations = null;
    }

    public CompositionScrollPresentationPlan(CompositionScrollLayerAnimation layerAnimation, ReadOnlySpan<CompositionScrollLayerAnimation> additionalLayerAnimations)
    {
        LayerAnimation = layerAnimation;
        _additionalLayerAnimations = additionalLayerAnimations.IsEmpty ? null : additionalLayerAnimations.ToArray();
    }

    public CompositionScrollLayerAnimation LayerAnimation { get; }
    public int LayerCount => LayerAnimation.LayerId.IsValid ? 1 + (_additionalLayerAnimations?.Length ?? 0) : 0;

    public CompositionScrollLayerAnimation GetLayerAnimation(int index)
    {
        if (index == 0 && LayerCount > 0)
        {
            return LayerAnimation;
        }

        if (_additionalLayerAnimations is not null && (uint)(index - 1) < (uint)_additionalLayerAnimations.Length)
        {
            return _additionalLayerAnimations[index - 1];
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public bool IsValidForCommandCount(int commandCount)
    {
        var layerCount = LayerCount;
        if (layerCount == 0)
        {
            return false;
        }

        for (var i = 0; i < layerCount; i++)
        {
            var layer = GetLayerAnimation(i);
            if (!layer.IsValidForCommandCount(commandCount) || HasDuplicateLayerId(i, layer.LayerId))
            {
                return false;
            }
        }

        return true;
    }

    public CompositionFrame Evaluate(int commandCount, CompositionTimestamp timestamp)
    {
        if (!IsValidForCommandCount(commandCount))
        {
            throw new ArgumentException("Composition scroll presentation layer range must reference a non-empty range inside the command span.", nameof(commandCount));
        }

        var layerCount = LayerCount;
        if (layerCount == 1)
        {
            return new CompositionFrame(LayerAnimation.Evaluate(timestamp));
        }

        Span<CompositionLayer> layers = layerCount <= 8 ? stackalloc CompositionLayer[layerCount] : new CompositionLayer[layerCount];
        for (var i = 0; i < layerCount; i++)
        {
            layers[i] = GetLayerAnimation(i).Evaluate(timestamp);
        }

        return CompositionFrame.FromLayers(layers);
    }

    public bool Equals(CompositionScrollPresentationPlan other)
    {
        if (LayerCount != other.LayerCount)
        {
            return false;
        }

        for (var i = 0; i < LayerCount; i++)
        {
            if (GetLayerAnimation(i) != other.GetLayerAnimation(i))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is CompositionScrollPresentationPlan other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        for (var i = 0; i < LayerCount; i++)
        {
            hashCode.Add(GetLayerAnimation(i));
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionScrollPresentationPlan left, CompositionScrollPresentationPlan right) => left.Equals(right);

    public static bool operator !=(CompositionScrollPresentationPlan left, CompositionScrollPresentationPlan right) => !left.Equals(right);

    private bool HasDuplicateLayerId(int currentIndex, CompositionLayerId layerId)
    {
        for (var i = 0; i < currentIndex; i++)
        {
            if (GetLayerAnimation(i).LayerId == layerId)
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly struct CompositionAnimationDeclaration(
    NodeKey TargetKey,
    CompositionAnimationTimeline Timeline,
    CompositionTransformAnimation Transform,
    CompositionScalarAnimation Opacity,
    CompositionAnimationInstanceId InstanceId = default,
    CompositionAnimationMarker[]? Markers = null) : IEquatable<CompositionAnimationDeclaration>
{
    private readonly CompositionAnimationMarker[] _markers = CompositionAnimationMarker.NormalizeArray(Markers);

    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionAnimationTimeline Timeline { get; } = Timeline;
    public CompositionTransformAnimation Transform { get; } = Transform;
    public CompositionScalarAnimation Opacity { get; } = Opacity;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;

    public bool TryResolve(RenderPipelineRetainedInputSnapshot snapshot, out CompositionAnimationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return TryResolve(snapshot, snapshot.CommandCount, out plan);
    }

    public bool TryResolve(RenderPipelineRetainedInputSnapshot snapshot, int commandCount, out CompositionAnimationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TargetKey == NodeKey.None
            || !snapshot.TryGetCompositionTarget(TargetKey, commandCount, out var target)
            || !target.IsValidForCommandCount(commandCount))
        {
            plan = default;
            return false;
        }

        plan = new CompositionAnimationPlan(new CompositionLayerAnimation(
            target.LayerId,
            target.CommandStart,
            target.CommandCount,
            Timeline,
            Transform,
            Opacity,
            InstanceId,
            TargetKey,
            _markers));
        return true;
    }

    public bool Equals(CompositionAnimationDeclaration other)
    {
        return TargetKey == other.TargetKey
            && Timeline == other.Timeline
            && Transform == other.Transform
            && Opacity == other.Opacity
            && InstanceId == other.InstanceId
            && CompositionAnimationMarker.SequenceEqual(Markers, other.Markers);
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationDeclaration other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(TargetKey);
        hashCode.Add(Timeline);
        hashCode.Add(Transform);
        hashCode.Add(Opacity);
        hashCode.Add(InstanceId);
        CompositionAnimationMarker.AddHashCode(ref hashCode, Markers);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionAnimationDeclaration left, CompositionAnimationDeclaration right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationDeclaration left, CompositionAnimationDeclaration right) => !left.Equals(right);
}

internal readonly struct CompositionScrollPresentationDeclaration(
    NodeKey TargetKey,
    CompositionAnimationTimeline Timeline,
    CompositionScalarAnimation PresentedScrollY,
    CompositionAnimationInstanceId InstanceId = default,
    CompositionAnimationMarker[]? Markers = null) : IEquatable<CompositionScrollPresentationDeclaration>
{
    private readonly CompositionAnimationMarker[] _markers = CompositionAnimationMarker.NormalizeArray(Markers);

    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionAnimationTimeline Timeline { get; } = Timeline;
    public CompositionScalarAnimation PresentedScrollY { get; } = PresentedScrollY;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public ReadOnlySpan<CompositionAnimationMarker> Markers => _markers;

    public bool TryResolve(RenderPipelineRetainedInputSnapshot snapshot, out CompositionScrollPresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return TryResolve(snapshot, snapshot.CommandCount, out plan);
    }

    public bool TryResolve(RenderPipelineRetainedInputSnapshot snapshot, int commandCount, out CompositionScrollPresentationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TargetKey == NodeKey.None
            || !snapshot.TryGetScrollCompositionTarget(TargetKey, commandCount, out var target)
            || !target.IsValidForCommandCount(commandCount))
        {
            plan = default;
            return false;
        }

        var layerCount = target.LayerCount;
        if (layerCount <= 0)
        {
            plan = default;
            return false;
        }

        var firstAnimation = CreateLayerAnimation(target.GetLayer(0), target, _markers);
        if (!firstAnimation.IsValidForCommandCount(commandCount))
        {
            plan = default;
            return false;
        }

        if (layerCount == 1)
        {
            plan = new CompositionScrollPresentationPlan(firstAnimation);
            return true;
        }

        var additionalAnimations = new CompositionScrollLayerAnimation[layerCount - 1];
        for (var i = 1; i < layerCount; i++)
        {
            additionalAnimations[i - 1] = CreateLayerAnimation(target.GetLayer(i), target, null);
        }

        plan = new CompositionScrollPresentationPlan(firstAnimation, additionalAnimations);
        if (!plan.IsValidForCommandCount(commandCount))
        {
            plan = default;
            return false;
        }

        return true;
    }

    public bool Equals(CompositionScrollPresentationDeclaration other)
    {
        return TargetKey == other.TargetKey
            && Timeline == other.Timeline
            && PresentedScrollY == other.PresentedScrollY
            && InstanceId == other.InstanceId
            && CompositionAnimationMarker.SequenceEqual(Markers, other.Markers);
    }

    public override bool Equals(object? obj) => obj is CompositionScrollPresentationDeclaration other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(TargetKey);
        hashCode.Add(Timeline);
        hashCode.Add(PresentedScrollY);
        hashCode.Add(InstanceId);
        CompositionAnimationMarker.AddHashCode(ref hashCode, Markers);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionScrollPresentationDeclaration left, CompositionScrollPresentationDeclaration right) => left.Equals(right);

    public static bool operator !=(CompositionScrollPresentationDeclaration left, CompositionScrollPresentationDeclaration right) => !left.Equals(right);

    private CompositionScrollLayerAnimation CreateLayerAnimation(
        in ScrollCompositionLayerTarget layer,
        in ScrollCompositionTarget target,
        CompositionAnimationMarker[]? markers)
    {
        return new CompositionScrollLayerAnimation(
            layer.LayerId,
            layer.CommandStart,
            layer.CommandCount,
            layer.ClipBounds,
            target.RetainedScrollY,
            target.MaxScrollY,
            Timeline,
            PresentedScrollY,
            InstanceId,
            TargetKey,
            markers);
    }
}

internal readonly struct CompositionLayer(
    CompositionLayerId Id,
    int CommandStart,
    int CommandCount,
    CompositionTransform Transform,
    CompositionOpacity Opacity,
    CompositionClipMode ClipMode = CompositionClipMode.TransformWithContent,
    DrawRect ClipBounds = default) : IEquatable<CompositionLayer>
{
    public CompositionLayerId Id { get; } = Id;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionTransform Transform { get; } = Transform;
    public CompositionOpacity Opacity { get; } = Opacity;
    public CompositionClipMode ClipMode { get; } = ClipMode;
    public DrawRect ClipBounds { get; } = ClipBounds;

    public bool HasFixedClip => ClipMode == CompositionClipMode.Fixed && ClipBounds.Width > 0f && ClipBounds.Height > 0f;

    public bool IsValidForCommandCount(int commandCount)
    {
        return Id.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount
            && (ClipMode != CompositionClipMode.Fixed || HasFixedClip);
    }

    public bool Equals(CompositionLayer other)
    {
        return Id == other.Id
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && Transform == other.Transform
            && Opacity == other.Opacity
            && ClipMode == other.ClipMode
            && ClipBounds == other.ClipBounds;
    }

    public override bool Equals(object? obj) => obj is CompositionLayer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, CommandStart, CommandCount, Transform, Opacity, ClipMode, ClipBounds);

    public static bool operator ==(CompositionLayer left, CompositionLayer right) => left.Equals(right);

    public static bool operator !=(CompositionLayer left, CompositionLayer right) => !left.Equals(right);
}

internal readonly struct CompositionFrame : IEquatable<CompositionFrame>
{
    private readonly CompositionLayer _layer;
    private readonly CompositionLayer[]? _additionalLayers;

    public CompositionFrame(CompositionLayer layer)
    {
        _layer = layer;
        _additionalLayers = null;
    }

    public CompositionFrame(CompositionLayer layer, ReadOnlySpan<CompositionLayer> additionalLayers)
    {
        _layer = layer;
        _additionalLayers = additionalLayers.IsEmpty ? null : additionalLayers.ToArray();
    }

    public CompositionLayer Layer => _layer;

    public int LayerCount => _layer.Id.IsValid ? 1 + (_additionalLayers?.Length ?? 0) : 0;

    public static CompositionFrame FromLayers(ReadOnlySpan<CompositionLayer> layers)
    {
        return layers.Length switch
        {
            0 => default,
            1 => new CompositionFrame(layers[0]),
            _ => new CompositionFrame(layers[0], layers[1..])
        };
    }

    public CompositionLayer GetLayer(int index)
    {
        if (index == 0 && LayerCount > 0)
        {
            return _layer;
        }

        if (_additionalLayers is not null && (uint)(index - 1) < (uint)_additionalLayers.Length)
        {
            return _additionalLayers[index - 1];
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    public bool IsValidForCommandCount(int commandCount)
    {
        var layerCount = LayerCount;
        if (layerCount == 0)
        {
            return false;
        }

        for (var i = 0; i < layerCount; i++)
        {
            var layer = GetLayer(i);
            if (!layer.IsValidForCommandCount(commandCount) || HasDuplicateLayerId(i, layer.Id))
            {
                return false;
            }
        }

        return true;
    }

    public bool Equals(CompositionFrame other)
    {
        var layerCount = LayerCount;
        if (layerCount != other.LayerCount)
        {
            return false;
        }

        for (var i = 0; i < layerCount; i++)
        {
            if (GetLayer(i) != other.GetLayer(i))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is CompositionFrame other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        var layerCount = LayerCount;
        for (var i = 0; i < layerCount; i++)
        {
            hashCode.Add(GetLayer(i));
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(CompositionFrame left, CompositionFrame right) => left.Equals(right);

    public static bool operator !=(CompositionFrame left, CompositionFrame right) => !left.Equals(right);

    private bool HasDuplicateLayerId(int currentIndex, CompositionLayerId layerId)
    {
        for (var i = 0; i < currentIndex; i++)
        {
            if (GetLayer(i).Id == layerId)
            {
                return true;
            }
        }

        return false;
    }
}

[Flags]
internal enum CompositionBackendCapabilities : byte
{
    None = 0,
    Transform = 1,
    Opacity = 2,
    FixedClip = 4,
    MultiLayer = 8,
    TransformOpacity = Transform | Opacity,
    ScrollPresentation = Transform | FixedClip
}

internal readonly struct CompositionBackendExecutionResult(
    bool D3D12Backed,
    int LayerCount,
    int CommandCount,
    int TranslatedCommands,
    int OpacityAppliedCommands) : IEquatable<CompositionBackendExecutionResult>
{
    public bool D3D12Backed { get; } = D3D12Backed;
    public int LayerCount { get; } = LayerCount;
    public int CommandCount { get; } = CommandCount;
    public int TranslatedCommands { get; } = TranslatedCommands;
    public int OpacityAppliedCommands { get; } = OpacityAppliedCommands;

    public bool Equals(CompositionBackendExecutionResult other)
    {
        return D3D12Backed == other.D3D12Backed
            && LayerCount == other.LayerCount
            && CommandCount == other.CommandCount
            && TranslatedCommands == other.TranslatedCommands
            && OpacityAppliedCommands == other.OpacityAppliedCommands;
    }

    public override bool Equals(object? obj) => obj is CompositionBackendExecutionResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(D3D12Backed, LayerCount, CommandCount, TranslatedCommands, OpacityAppliedCommands);

    public static bool operator ==(CompositionBackendExecutionResult left, CompositionBackendExecutionResult right) => left.Equals(right);

    public static bool operator !=(CompositionBackendExecutionResult left, CompositionBackendExecutionResult right) => !left.Equals(right);
}

internal interface ICompositionDrawingBackend
{
    CompositionBackendCapabilities CompositionCapabilities { get; }

    CompositionBackendExecutionResult ExecuteComposition(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        in CompositionFrame compositionFrame);
}
