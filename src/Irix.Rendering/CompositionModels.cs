using Irix.Drawing;

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
    SineInOut
}

internal readonly struct CompositionAnimationTimeline(
    long StartTimestamp,
    long DurationTicks,
    CompositionAnimationRepeatMode RepeatMode = CompositionAnimationRepeatMode.Once) : IEquatable<CompositionAnimationTimeline>
{
    public long StartTimestamp { get; } = StartTimestamp;
    public long DurationTicks { get; } = DurationTicks;
    public CompositionAnimationRepeatMode RepeatMode { get; } = RepeatMode;

    public float ProgressAt(long timestamp)
    {
        if (DurationTicks <= 0)
        {
            return 1f;
        }

        var elapsed = timestamp - StartTimestamp;
        if (elapsed <= 0)
        {
            return 0f;
        }

        return RepeatMode switch
        {
            CompositionAnimationRepeatMode.Loop => (elapsed % DurationTicks) / (float)DurationTicks,
            CompositionAnimationRepeatMode.Alternate => ResolveAlternateProgress(elapsed),
            _ => Math.Clamp(elapsed / (float)DurationTicks, 0f, 1f)
        };
    }

    public bool Equals(CompositionAnimationTimeline other)
    {
        return StartTimestamp == other.StartTimestamp
            && DurationTicks == other.DurationTicks
            && RepeatMode == other.RepeatMode;
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationTimeline other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(StartTimestamp, DurationTicks, RepeatMode);

    public static bool operator ==(CompositionAnimationTimeline left, CompositionAnimationTimeline right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationTimeline left, CompositionAnimationTimeline right) => !left.Equals(right);

    private float ResolveAlternateProgress(long elapsed)
    {
        var cycle = elapsed / DurationTicks;
        var progress = (elapsed % DurationTicks) / (float)DurationTicks;
        return (cycle & 1L) == 0 ? progress : 1f - progress;
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
    CompositionScalarAnimation Opacity) : IEquatable<CompositionLayerAnimation>
{
    public CompositionLayerId LayerId { get; } = LayerId;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionAnimationTimeline Timeline { get; } = Timeline;
    public CompositionTransformAnimation Transform { get; } = Transform;
    public CompositionScalarAnimation Opacity { get; } = Opacity;

    public CompositionLayer Evaluate(long timestamp)
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
            && Opacity == other.Opacity;
    }

    public override bool Equals(object? obj) => obj is CompositionLayerAnimation other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(LayerId, CommandStart, CommandCount, Timeline, Transform, Opacity);

    public static bool operator ==(CompositionLayerAnimation left, CompositionLayerAnimation right) => left.Equals(right);

    public static bool operator !=(CompositionLayerAnimation left, CompositionLayerAnimation right) => !left.Equals(right);
}

internal readonly struct CompositionAnimationPlan(CompositionLayerAnimation LayerAnimation) : IEquatable<CompositionAnimationPlan>
{
    public CompositionLayerAnimation LayerAnimation { get; } = LayerAnimation;

    public bool IsValidForCommandCount(int commandCount) => LayerAnimation.IsValidForCommandCount(commandCount);

    public CompositionFrame Evaluate(int commandCount, long timestamp)
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

internal readonly struct CompositionLayer(
    CompositionLayerId Id,
    int CommandStart,
    int CommandCount,
    CompositionTransform Transform,
    CompositionOpacity Opacity) : IEquatable<CompositionLayer>
{
    public CompositionLayerId Id { get; } = Id;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;
    public CompositionTransform Transform { get; } = Transform;
    public CompositionOpacity Opacity { get; } = Opacity;

    public bool IsValidForCommandCount(int commandCount)
    {
        return Id.IsValid
            && CommandStart >= 0
            && CommandCount > 0
            && CommandStart <= commandCount
            && CommandStart + CommandCount <= commandCount;
    }

    public bool Equals(CompositionLayer other)
    {
        return Id == other.Id
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount
            && Transform == other.Transform
            && Opacity == other.Opacity;
    }

    public override bool Equals(object? obj) => obj is CompositionLayer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, CommandStart, CommandCount, Transform, Opacity);

    public static bool operator ==(CompositionLayer left, CompositionLayer right) => left.Equals(right);

    public static bool operator !=(CompositionLayer left, CompositionLayer right) => !left.Equals(right);
}

internal readonly struct CompositionFrame(CompositionLayer Layer) : IEquatable<CompositionFrame>
{
    public CompositionLayer Layer { get; } = Layer;

    public bool IsValidForCommandCount(int commandCount) => Layer.IsValidForCommandCount(commandCount);

    public bool Equals(CompositionFrame other) => Layer == other.Layer;

    public override bool Equals(object? obj) => obj is CompositionFrame other && Equals(other);

    public override int GetHashCode() => Layer.GetHashCode();

    public static bool operator ==(CompositionFrame left, CompositionFrame right) => left.Equals(right);

    public static bool operator !=(CompositionFrame left, CompositionFrame right) => !left.Equals(right);
}

[Flags]
internal enum CompositionBackendCapabilities : byte
{
    None = 0,
    Transform = 1,
    Opacity = 2,
    TransformOpacity = Transform | Opacity
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
