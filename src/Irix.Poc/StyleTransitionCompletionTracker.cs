using Irix.Rendering;

namespace Irix.Poc;

internal enum StyleTransitionCompletionResultKind : byte
{
    None,
    NotTracked,
    TrackingStarted,
    TrackingRetargeted,
    Completed,
    Cleared,
    Aborted,
}

internal readonly struct StyleTransitionCompletionResult(
    StyleTransitionCompletionResultKind Kind,
    NodeKey TargetKey = default,
    CompositionAnimationInstanceId InstanceId = default,
    StyleTransitionRuntimeDecision Decision = default) : IEquatable<StyleTransitionCompletionResult>
{
    public StyleTransitionCompletionResultKind Kind { get; } = Kind;
    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public StyleTransitionRuntimeDecision Decision { get; } = Decision;
    public bool HasDecision => Decision.Kind != StyleTransitionRuntimeDecisionKind.None;

    public static StyleTransitionCompletionResult NotTracked() =>
        new(StyleTransitionCompletionResultKind.NotTracked);

    public static StyleTransitionCompletionResult TrackingStarted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingStarted, targetKey, instanceId);

    public static StyleTransitionCompletionResult TrackingRetargeted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingRetargeted, targetKey, instanceId);

    public static StyleTransitionCompletionResult Completed(NodeKey targetKey, CompositionAnimationInstanceId instanceId, StyleTransitionRuntimeDecision decision) =>
        new(StyleTransitionCompletionResultKind.Completed, targetKey, instanceId, decision);

    public static StyleTransitionCompletionResult Cleared(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Cleared, targetKey, instanceId);

    public static StyleTransitionCompletionResult Aborted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Aborted, targetKey, instanceId);

    public bool Equals(StyleTransitionCompletionResult other)
    {
        return Kind == other.Kind
            && TargetKey == other.TargetKey
            && InstanceId == other.InstanceId
            && Decision == other.Decision;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionCompletionResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetKey, InstanceId, Decision);

    public static bool operator ==(StyleTransitionCompletionResult left, StyleTransitionCompletionResult right) => left.Equals(right);

    public static bool operator !=(StyleTransitionCompletionResult left, StyleTransitionCompletionResult right) => !left.Equals(right);
}

internal sealed class StyleTransitionCompletionTracker
{
    internal static readonly CompositionAnimationMarkerId CompletionMarkerId = new(0xffff_fffe);
    internal static readonly CompositionRuntimeEventId CompletionRuntimeEventId = new(0xffff_fffe);

    private static readonly CompositionAnimationMarker CompletionMarker = new(
        CompletionMarkerId,
        CompletionRuntimeEventId,
        CompositionAnimationMarkerTrigger.AtProgress(1f));

    private readonly Lock _gate = new();
    private TrackedTransition _active;
    private StyleTransitionCompletionResult _lastResult;

    internal StyleTransitionCompletionResult LastResult
    {
        get
        {
            lock (_gate)
            {
                return _lastResult;
            }
        }
    }

    internal bool HasActiveTransition
    {
        get
        {
            lock (_gate)
            {
                return _active.HasValue;
            }
        }
    }

    internal NodeKey ActiveTargetKey
    {
        get
        {
            lock (_gate)
            {
                return _active.TargetKey;
            }
        }
    }

    internal CompositionAnimationInstanceId ActiveInstanceId
    {
        get
        {
            lock (_gate)
            {
                return _active.InstanceId;
            }
        }
    }

    internal (
        bool HasActiveTransition,
        NodeKey ActiveTargetKey,
        CompositionAnimationInstanceId ActiveInstanceId,
        StyleTransitionCompletionResult LastResult) CaptureDiagnosticState()
    {
        lock (_gate)
        {
            return (
                _active.HasValue,
                _active.TargetKey,
                _active.InstanceId,
                _lastResult);
        }
    }

    internal StyleTransitionRuntimeDecision AttachCompletionMarker(StyleTransitionRuntimeDecision decision)
    {
        if (!IsTrackableDecision(decision))
        {
            SetLastResult(StyleTransitionCompletionResult.NotTracked());
            return decision;
        }

        if (HasCompletionMarker(decision.Markers))
        {
            return decision;
        }

        var sourceMarkers = decision.Markers;
        var markers = new CompositionAnimationMarker[sourceMarkers.Length + 1];
        for (var i = 0; i < sourceMarkers.Length; i++)
        {
            markers[i] = sourceMarkers[i];
        }

        markers[^1] = CompletionMarker;
        return new StyleTransitionRuntimeDecision(
            decision.Kind,
            decision.TargetKey,
            decision.PreviousProperties,
            decision.NextProperties,
            decision.StartTimestamp,
            decision.Duration,
            decision.Easing,
            decision.RepeatMode,
            decision.InstanceId,
            markers);
    }

    internal StyleTransitionCompletionResult PublishRuntimeResult(
        StyleTransitionRuntimeDecision decision,
        StyleTransitionRuntimeResult runtimeResult)
    {
        if (runtimeResult.Kind is StyleTransitionRuntimeResultKind.Canceled
            or StyleTransitionRuntimeResultKind.Committed
            or StyleTransitionRuntimeResultKind.Fallback)
        {
            return ClearIfActiveTarget(runtimeResult.TargetKey);
        }

        if (!runtimeResult.HasDeclaration
            || runtimeResult.Kind is not (StyleTransitionRuntimeResultKind.Started or StyleTransitionRuntimeResultKind.Retargeted)
            || !IsTrackableDecision(decision)
            || !HasCompletionMarker(decision.Markers))
        {
            return SetLastResult(StyleTransitionCompletionResult.NotTracked());
        }

        lock (_gate)
        {
            _active = new TrackedTransition(decision.TargetKey, decision.InstanceId);
            _lastResult = runtimeResult.Kind == StyleTransitionRuntimeResultKind.Retargeted
                ? StyleTransitionCompletionResult.TrackingRetargeted(decision.TargetKey, decision.InstanceId)
                : StyleTransitionCompletionResult.TrackingStarted(decision.TargetKey, decision.InstanceId);
            return _lastResult;
        }
    }

    internal bool TryCreateCompletionDecision(
        in CompositionAnimationMarkerEvent markerEvent,
        out StyleTransitionRuntimeDecision decision)
    {
        lock (_gate)
        {
            if (!IsActiveCompletionEvent(markerEvent))
            {
                decision = default;
                _lastResult = StyleTransitionCompletionResult.NotTracked();
                return false;
            }

            var targetKey = _active.TargetKey;
            var instanceId = _active.InstanceId;
            decision = StyleTransitionRuntimeDecision.Commit(targetKey);
            _active = default;
            _lastResult = StyleTransitionCompletionResult.Completed(targetKey, instanceId, decision);
            return true;
        }
    }

    internal StyleTransitionCompletionResult AbortActiveTransition()
    {
        lock (_gate)
        {
            if (!_active.HasValue)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked();
                return _lastResult;
            }

            var previous = _active;
            _active = default;
            _lastResult = StyleTransitionCompletionResult.Aborted(previous.TargetKey, previous.InstanceId);
            return _lastResult;
        }
    }

    private StyleTransitionCompletionResult ClearIfActiveTarget(NodeKey targetKey)
    {
        lock (_gate)
        {
            if (!_active.HasValue || _active.TargetKey != targetKey)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked();
                return _lastResult;
            }

            var previous = _active;
            _active = default;
            _lastResult = StyleTransitionCompletionResult.Cleared(previous.TargetKey, previous.InstanceId);
            return _lastResult;
        }
    }

    private bool IsActiveCompletionEvent(in CompositionAnimationMarkerEvent markerEvent)
    {
        return _active.HasValue
            && markerEvent.OwnerKind == CompositionAnimationMarkerOwnerKind.TransformOpacity
            && markerEvent.TargetKey == _active.TargetKey
            && markerEvent.InstanceId == _active.InstanceId
            && markerEvent.MarkerId == CompletionMarkerId
            && markerEvent.RuntimeEventId == CompletionRuntimeEventId;
    }

    private StyleTransitionCompletionResult SetLastResult(StyleTransitionCompletionResult result)
    {
        lock (_gate)
        {
            _lastResult = result;
            return _lastResult;
        }
    }

    private static bool IsTrackableDecision(in StyleTransitionRuntimeDecision decision)
    {
        return decision.Kind is StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget
            && decision.TargetKey != NodeKey.None
            && decision.InstanceId.IsValid
            && decision.RepeatMode == CompositionAnimationRepeatMode.Once;
    }

    private static bool HasCompletionMarker(ReadOnlySpan<CompositionAnimationMarker> markers)
    {
        for (var i = 0; i < markers.Length; i++)
        {
            if (markers[i].Id == CompletionMarkerId
                && markers[i].RuntimeEventId == CompletionRuntimeEventId)
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct TrackedTransition(NodeKey TargetKey, CompositionAnimationInstanceId InstanceId)
    {
        public NodeKey TargetKey { get; } = TargetKey;
        public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
        public bool HasValue => TargetKey != NodeKey.None && InstanceId.IsValid;
    }
}
