namespace Irix.Rendering;

internal readonly struct CompositionAnimationInstanceId(uint Value) : IEquatable<CompositionAnimationInstanceId>
{
    public uint Value { get; } = Value;

    public bool IsValid => Value != 0;

    public bool Equals(CompositionAnimationInstanceId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CompositionAnimationInstanceId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(CompositionAnimationInstanceId left, CompositionAnimationInstanceId right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationInstanceId left, CompositionAnimationInstanceId right) => !left.Equals(right);
}

internal readonly struct CompositionAnimationMarkerId(uint Value) : IEquatable<CompositionAnimationMarkerId>
{
    public uint Value { get; } = Value;

    public bool IsValid => Value != 0;

    public bool Equals(CompositionAnimationMarkerId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CompositionAnimationMarkerId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(CompositionAnimationMarkerId left, CompositionAnimationMarkerId right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationMarkerId left, CompositionAnimationMarkerId right) => !left.Equals(right);
}

internal readonly struct CompositionRuntimeEventId(uint Value) : IEquatable<CompositionRuntimeEventId>
{
    public uint Value { get; } = Value;

    public bool IsValid => Value != 0;

    public bool Equals(CompositionRuntimeEventId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CompositionRuntimeEventId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(CompositionRuntimeEventId left, CompositionRuntimeEventId right) => left.Equals(right);

    public static bool operator !=(CompositionRuntimeEventId left, CompositionRuntimeEventId right) => !left.Equals(right);
}

internal enum CompositionPlaybackDirection : byte
{
    Forward,
    Reverse
}

internal enum CompositionAnimationMarkerTriggerKind : byte
{
    Progress,
    ElapsedTime,
    ProgressRange,
    EveryTick
}

internal enum CompositionAnimationMarkerRepeatPolicy : byte
{
    Once,
    OncePerIteration
}

internal enum CompositionAnimationMarkerEventKind : byte
{
    Progress,
    ElapsedTime,
    ProgressRangeEntered,
    Tick
}

internal enum CompositionAnimationMarkerOwnerKind : byte
{
    TransformOpacity,
    ScrollPresentation
}

internal readonly struct CompositionTimelineSample(
    CompositionTimestamp Timestamp,
    CompositionDuration Elapsed,
    float Progress,
    int Iteration,
    CompositionPlaybackDirection Direction) : IEquatable<CompositionTimelineSample>
{
    public CompositionTimestamp Timestamp { get; } = Timestamp;
    public CompositionDuration Elapsed { get; } = Elapsed;
    public float Progress { get; } = Progress;
    public int Iteration { get; } = Iteration;
    public CompositionPlaybackDirection Direction { get; } = Direction;

    public bool Equals(CompositionTimelineSample other)
    {
        return Timestamp == other.Timestamp
            && Elapsed == other.Elapsed
            && Progress.Equals(other.Progress)
            && Iteration == other.Iteration
            && Direction == other.Direction;
    }

    public override bool Equals(object? obj) => obj is CompositionTimelineSample other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Timestamp, Elapsed, Progress, Iteration, Direction);

    public static bool operator ==(CompositionTimelineSample left, CompositionTimelineSample right) => left.Equals(right);

    public static bool operator !=(CompositionTimelineSample left, CompositionTimelineSample right) => !left.Equals(right);
}

internal readonly struct CompositionAnimationMarkerTrigger(
    CompositionAnimationMarkerTriggerKind Kind,
    float Progress,
    CompositionDuration Elapsed,
    float RangeStart,
    float RangeEnd) : IEquatable<CompositionAnimationMarkerTrigger>
{
    public CompositionAnimationMarkerTriggerKind Kind { get; } = Kind;
    public float Progress { get; } = Progress;
    public CompositionDuration Elapsed { get; } = Elapsed;
    public float RangeStart { get; } = RangeStart;
    public float RangeEnd { get; } = RangeEnd;

    public bool IsValid => Kind switch
    {
        CompositionAnimationMarkerTriggerKind.Progress => IsNormalized(Progress),
        CompositionAnimationMarkerTriggerKind.ElapsedTime => Elapsed.StopwatchTicks >= 0,
        CompositionAnimationMarkerTriggerKind.ProgressRange => IsNormalized(RangeStart) && IsNormalized(RangeEnd) && RangeStart <= RangeEnd,
        CompositionAnimationMarkerTriggerKind.EveryTick => true,
        _ => false
    };

    public static CompositionAnimationMarkerTrigger AtProgress(float progress)
    {
        return new CompositionAnimationMarkerTrigger(CompositionAnimationMarkerTriggerKind.Progress, ClampNormalized(progress), default, default, default);
    }

    public static CompositionAnimationMarkerTrigger AtElapsedTime(CompositionDuration elapsed)
    {
        return new CompositionAnimationMarkerTrigger(CompositionAnimationMarkerTriggerKind.ElapsedTime, default, elapsed, default, default);
    }

    public static CompositionAnimationMarkerTrigger EnterProgressRange(float rangeStart, float rangeEnd)
    {
        rangeStart = ClampNormalized(rangeStart);
        rangeEnd = ClampNormalized(rangeEnd);
        return rangeStart <= rangeEnd
            ? new CompositionAnimationMarkerTrigger(CompositionAnimationMarkerTriggerKind.ProgressRange, default, default, rangeStart, rangeEnd)
            : new CompositionAnimationMarkerTrigger(CompositionAnimationMarkerTriggerKind.ProgressRange, default, default, rangeEnd, rangeStart);
    }

    public static CompositionAnimationMarkerTrigger EveryTick()
    {
        return new CompositionAnimationMarkerTrigger(CompositionAnimationMarkerTriggerKind.EveryTick, default, default, default, default);
    }

    public bool Equals(CompositionAnimationMarkerTrigger other)
    {
        return Kind == other.Kind
            && Progress.Equals(other.Progress)
            && Elapsed == other.Elapsed
            && RangeStart.Equals(other.RangeStart)
            && RangeEnd.Equals(other.RangeEnd);
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationMarkerTrigger other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Progress, Elapsed, RangeStart, RangeEnd);

    public static bool operator ==(CompositionAnimationMarkerTrigger left, CompositionAnimationMarkerTrigger right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationMarkerTrigger left, CompositionAnimationMarkerTrigger right) => !left.Equals(right);

    internal static float ClampNormalized(float value)
    {
        return float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
    }

    private static bool IsNormalized(float value)
    {
        return float.IsFinite(value) && value >= 0f && value <= 1f;
    }
}

internal readonly struct CompositionAnimationMarker(
    CompositionAnimationMarkerId Id,
    CompositionRuntimeEventId RuntimeEventId,
    CompositionAnimationMarkerTrigger Trigger,
    CompositionAnimationMarkerRepeatPolicy RepeatPolicy = CompositionAnimationMarkerRepeatPolicy.Once) : IEquatable<CompositionAnimationMarker>
{
    public CompositionAnimationMarkerId Id { get; } = Id;
    public CompositionRuntimeEventId RuntimeEventId { get; } = RuntimeEventId;
    public CompositionAnimationMarkerTrigger Trigger { get; } = Trigger;
    public CompositionAnimationMarkerRepeatPolicy RepeatPolicy { get; } = RepeatPolicy;

    public bool IsValid => Id.IsValid
        && RuntimeEventId.IsValid
        && Trigger.IsValid
        && (RepeatPolicy == CompositionAnimationMarkerRepeatPolicy.Once || RepeatPolicy == CompositionAnimationMarkerRepeatPolicy.OncePerIteration);

    public bool Equals(CompositionAnimationMarker other)
    {
        return Id == other.Id
            && RuntimeEventId == other.RuntimeEventId
            && Trigger == other.Trigger
            && RepeatPolicy == other.RepeatPolicy;
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationMarker other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, RuntimeEventId, Trigger, RepeatPolicy);

    public static bool operator ==(CompositionAnimationMarker left, CompositionAnimationMarker right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationMarker left, CompositionAnimationMarker right) => !left.Equals(right);

    internal static CompositionAnimationMarker[] NormalizeArray(CompositionAnimationMarker[]? markers)
    {
        if (markers is null || markers.Length == 0)
        {
            return [];
        }

        var normalized = new CompositionAnimationMarker[markers.Length];
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            if (!marker.IsValid)
            {
                throw new ArgumentException("Composition animation markers must have non-zero ids, non-zero runtime event ids, and valid trigger values.", nameof(markers));
            }

            normalized[i] = marker;
        }

        return normalized;
    }

    internal static bool SequenceEqual(ReadOnlySpan<CompositionAnimationMarker> left, ReadOnlySpan<CompositionAnimationMarker> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    internal static void AddHashCode(ref HashCode hashCode, ReadOnlySpan<CompositionAnimationMarker> markers)
    {
        for (var i = 0; i < markers.Length; i++)
        {
            hashCode.Add(markers[i]);
        }
    }
}

internal readonly struct CompositionAnimationMarkerEvent(
    CompositionAnimationInstanceId InstanceId,
    CompositionAnimationMarkerId MarkerId,
    CompositionRuntimeEventId RuntimeEventId,
    CompositionAnimationMarkerEventKind Kind,
    CompositionAnimationMarkerOwnerKind OwnerKind,
    CompositionLayerId LayerId,
    NodeKey TargetKey,
    CompositionTimestamp Timestamp,
    CompositionDuration Elapsed,
    float Progress,
    int Iteration,
    CompositionPlaybackDirection Direction) : IEquatable<CompositionAnimationMarkerEvent>
{
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public CompositionAnimationMarkerId MarkerId { get; } = MarkerId;
    public CompositionRuntimeEventId RuntimeEventId { get; } = RuntimeEventId;
    public CompositionAnimationMarkerEventKind Kind { get; } = Kind;
    public CompositionAnimationMarkerOwnerKind OwnerKind { get; } = OwnerKind;
    public CompositionLayerId LayerId { get; } = LayerId;
    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionTimestamp Timestamp { get; } = Timestamp;
    public CompositionDuration Elapsed { get; } = Elapsed;
    public float Progress { get; } = Progress;
    public int Iteration { get; } = Iteration;
    public CompositionPlaybackDirection Direction { get; } = Direction;

    public bool Equals(CompositionAnimationMarkerEvent other)
    {
        return InstanceId == other.InstanceId
            && MarkerId == other.MarkerId
            && RuntimeEventId == other.RuntimeEventId
            && Kind == other.Kind
            && OwnerKind == other.OwnerKind
            && LayerId == other.LayerId
            && TargetKey == other.TargetKey
            && Timestamp == other.Timestamp
            && Elapsed == other.Elapsed
            && Progress.Equals(other.Progress)
            && Iteration == other.Iteration
            && Direction == other.Direction;
    }

    public override bool Equals(object? obj) => obj is CompositionAnimationMarkerEvent other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            InstanceId,
            MarkerId,
            RuntimeEventId,
            Kind,
            OwnerKind,
            LayerId,
            TargetKey,
            HashCode.Combine(Timestamp, Elapsed, Progress, Iteration, Direction));
    }

    public static bool operator ==(CompositionAnimationMarkerEvent left, CompositionAnimationMarkerEvent right) => left.Equals(right);

    public static bool operator !=(CompositionAnimationMarkerEvent left, CompositionAnimationMarkerEvent right) => !left.Equals(right);
}

internal struct CompositionAnimationMarkerPlaybackState(CompositionAnimationMarkerId MarkerId)
{
    public CompositionAnimationMarkerId MarkerId { get; private set; } = MarkerId;
    public bool HasFired { get; private set; }
    public int LastFiredIteration { get; private set; } = -1;

    public readonly bool Allows(in CompositionAnimationMarker marker, in CompositionTimelineSample sample)
    {
        if (marker.Trigger.Kind == CompositionAnimationMarkerTriggerKind.EveryTick)
        {
            return true;
        }

        return marker.RepeatPolicy == CompositionAnimationMarkerRepeatPolicy.Once
            ? !HasFired
            : !HasFired || LastFiredIteration != sample.Iteration;
    }

    public void Record(in CompositionTimelineSample sample)
    {
        HasFired = true;
        LastFiredIteration = sample.Iteration;
    }
}
