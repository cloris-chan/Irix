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
    StyleTransitionRuntimeDecision Decision = default,
    StyleTransitionOwnerKey OwnerKey = default) : IEquatable<StyleTransitionCompletionResult>
{
    public StyleTransitionCompletionResultKind Kind { get; } = Kind;
    public NodeKey TargetKey { get; } = TargetKey;
    public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    public StyleTransitionRuntimeDecision Decision { get; } = Decision;
    public StyleTransitionOwnerKey OwnerKey { get; } = OwnerKey;
    public bool HasDecision => Decision.Kind != StyleTransitionRuntimeDecisionKind.None;
    public bool HasOwner => !OwnerKey.IsNone;

    public static StyleTransitionCompletionResult NotTracked() =>
        new(StyleTransitionCompletionResultKind.NotTracked);

    internal static StyleTransitionCompletionResult NotTracked(StyleTransitionOwnerKey ownerKey) =>
        new(StyleTransitionCompletionResultKind.NotTracked, OwnerKey: ownerKey);

    public static StyleTransitionCompletionResult TrackingStarted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingStarted, targetKey, instanceId);

    internal static StyleTransitionCompletionResult TrackingStarted(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingStarted, targetKey, instanceId, OwnerKey: ownerKey);

    public static StyleTransitionCompletionResult TrackingRetargeted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingRetargeted, targetKey, instanceId);

    internal static StyleTransitionCompletionResult TrackingRetargeted(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.TrackingRetargeted, targetKey, instanceId, OwnerKey: ownerKey);

    public static StyleTransitionCompletionResult Completed(NodeKey targetKey, CompositionAnimationInstanceId instanceId, StyleTransitionRuntimeDecision decision) =>
        new(StyleTransitionCompletionResultKind.Completed, targetKey, instanceId, decision);

    internal static StyleTransitionCompletionResult Completed(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId,
        StyleTransitionRuntimeDecision decision) =>
        new(StyleTransitionCompletionResultKind.Completed, targetKey, instanceId, decision, ownerKey);

    public static StyleTransitionCompletionResult Cleared(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Cleared, targetKey, instanceId);

    internal static StyleTransitionCompletionResult Cleared(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Cleared, targetKey, instanceId, OwnerKey: ownerKey);

    public static StyleTransitionCompletionResult Aborted(NodeKey targetKey, CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Aborted, targetKey, instanceId);

    internal static StyleTransitionCompletionResult Aborted(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId) =>
        new(StyleTransitionCompletionResultKind.Aborted, targetKey, instanceId, OwnerKey: ownerKey);

    public bool Equals(StyleTransitionCompletionResult other)
    {
        return Kind == other.Kind
            && TargetKey == other.TargetKey
            && InstanceId == other.InstanceId
            && Decision == other.Decision
            && OwnerKey == other.OwnerKey;
    }

    public override bool Equals(object? obj) => obj is StyleTransitionCompletionResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, TargetKey, InstanceId, Decision, OwnerKey);

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
    private TrackedTransition[]? _activeOwners;
    private int _activeOwnerCount;
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

    internal int ActiveOwnerCount
    {
        get
        {
            lock (_gate)
            {
                return _activeOwnerCount;
            }
        }
    }

    internal bool HasActiveTransition
    {
        get
        {
            lock (_gate)
            {
                return _activeOwnerCount > 0;
            }
        }
    }

    internal NodeKey ActiveTargetKey
    {
        get
        {
            lock (_gate)
            {
                return GetPrimaryActiveTransition().TargetKey;
            }
        }
    }

    internal CompositionAnimationInstanceId ActiveInstanceId
    {
        get
        {
            lock (_gate)
            {
                return GetPrimaryActiveTransition().InstanceId;
            }
        }
    }

    internal (
        bool HasActiveTransition,
        NodeKey ActiveTargetKey,
        CompositionAnimationInstanceId ActiveInstanceId,
        int ActiveOwnerCount,
        StyleTransitionOwnerKind ActiveOwnerKind,
        StyleTransitionCompletionResult LastResult) CaptureDiagnosticState()
    {
        lock (_gate)
        {
            var active = GetPrimaryActiveTransition();
            return (
                _activeOwnerCount > 0,
                active.TargetKey,
                active.InstanceId,
                _activeOwnerCount,
                active.OwnerKey.Kind,
                _lastResult);
        }
    }

    internal StyleTransitionRuntimeDecision AttachCompletionMarker(StyleTransitionRuntimeDecision decision)
    {
        return AttachCompletionMarker(CreateLegacyOwnerKey(decision.TargetKey), decision);
    }

    internal StyleTransitionRuntimeDecision AttachCompletionMarker(
        StyleTransitionOwnerKey ownerKey,
        StyleTransitionRuntimeDecision decision)
    {
        if (!IsValidOwnerForDecision(ownerKey, decision) || !IsTrackableDecision(decision))
        {
            SetLastResult(StyleTransitionCompletionResult.NotTracked(ownerKey));
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
        return PublishRuntimeResult(CreateLegacyOwnerKey(decision.TargetKey), decision, runtimeResult);
    }

    internal StyleTransitionCompletionResult PublishRuntimeResult(
        StyleTransitionOwnerKey ownerKey,
        StyleTransitionRuntimeDecision decision,
        StyleTransitionRuntimeResult runtimeResult)
    {
        if (runtimeResult.Kind is StyleTransitionRuntimeResultKind.Canceled
            or StyleTransitionRuntimeResultKind.Committed
            or StyleTransitionRuntimeResultKind.Fallback)
        {
            return ClearIfActiveOwner(ownerKey, runtimeResult.TargetKey);
        }

        if (!runtimeResult.HasDeclaration
            || runtimeResult.Kind is not (StyleTransitionRuntimeResultKind.Started or StyleTransitionRuntimeResultKind.Retargeted)
            || !IsValidOwnerForDecision(ownerKey, decision)
            || !IsTrackableDecision(decision)
            || !HasCompletionMarker(decision.Markers))
        {
            return SetLastResult(StyleTransitionCompletionResult.NotTracked(ownerKey));
        }

        lock (_gate)
        {
            UpsertActiveOwner(ownerKey, decision.TargetKey, decision.InstanceId);
            _lastResult = runtimeResult.Kind == StyleTransitionRuntimeResultKind.Retargeted
                ? StyleTransitionCompletionResult.TrackingRetargeted(ownerKey, decision.TargetKey, decision.InstanceId)
                : StyleTransitionCompletionResult.TrackingStarted(ownerKey, decision.TargetKey, decision.InstanceId);
            return _lastResult;
        }
    }

    internal bool TryCreateCompletionDecision(
        in CompositionAnimationMarkerEvent markerEvent,
        out StyleTransitionRuntimeDecision decision)
    {
        return TryCreateCompletionDecision(default, markerEvent, out decision);
    }

    internal bool TryCreateCompletionDecision(
        StyleTransitionOwnerKey ownerKey,
        in CompositionAnimationMarkerEvent markerEvent,
        out StyleTransitionRuntimeDecision decision)
    {
        lock (_gate)
        {
            var activeIndex = FindActiveCompletionEventIndex(ownerKey, markerEvent);
            if (activeIndex < 0)
            {
                decision = default;
                _lastResult = StyleTransitionCompletionResult.NotTracked(ownerKey);
                return false;
            }

            var active = _activeOwners![activeIndex];
            var targetKey = active.TargetKey;
            var instanceId = active.InstanceId;
            decision = StyleTransitionRuntimeDecision.Commit(targetKey);
            RemoveActiveOwnerAt(activeIndex);
            _lastResult = StyleTransitionCompletionResult.Completed(active.OwnerKey, targetKey, instanceId, decision);
            return true;
        }
    }

    internal StyleTransitionCompletionResult AbortActiveTransition()
    {
        lock (_gate)
        {
            if (_activeOwnerCount == 0)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked();
                return _lastResult;
            }

            var previous = GetPrimaryActiveTransition();
            RemoveActiveOwnerAt(0);
            _lastResult = StyleTransitionCompletionResult.Aborted(previous.OwnerKey, previous.TargetKey, previous.InstanceId);
            return _lastResult;
        }
    }

    internal StyleTransitionCompletionResult AbortActiveTransition(StyleTransitionOwnerKey ownerKey)
    {
        lock (_gate)
        {
            var activeIndex = FindActiveOwnerIndex(ownerKey);
            if (activeIndex < 0)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked(ownerKey);
                return _lastResult;
            }

            var previous = _activeOwners![activeIndex];
            RemoveActiveOwnerAt(activeIndex);
            _lastResult = StyleTransitionCompletionResult.Aborted(previous.OwnerKey, previous.TargetKey, previous.InstanceId);
            return _lastResult;
        }
    }

    internal bool TryGetActiveTransition(
        StyleTransitionOwnerKey ownerKey,
        out NodeKey targetKey,
        out CompositionAnimationInstanceId instanceId)
    {
        lock (_gate)
        {
            var activeIndex = FindActiveOwnerIndex(ownerKey);
            if (activeIndex < 0)
            {
                targetKey = default;
                instanceId = default;
                return false;
            }

            var active = _activeOwners![activeIndex];
            targetKey = active.TargetKey;
            instanceId = active.InstanceId;
            return true;
        }
    }

    private StyleTransitionCompletionResult ClearIfActiveOwner(StyleTransitionOwnerKey ownerKey, NodeKey targetKey)
    {
        lock (_gate)
        {
            var activeIndex = FindActiveOwnerIndex(ownerKey);
            if (activeIndex < 0)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked(ownerKey);
                return _lastResult;
            }

            var previous = _activeOwners![activeIndex];
            if (previous.TargetKey != targetKey)
            {
                _lastResult = StyleTransitionCompletionResult.NotTracked(ownerKey);
                return _lastResult;
            }

            RemoveActiveOwnerAt(activeIndex);
            _lastResult = StyleTransitionCompletionResult.Cleared(previous.OwnerKey, previous.TargetKey, previous.InstanceId);
            return _lastResult;
        }
    }

    private int FindActiveCompletionEventIndex(
        StyleTransitionOwnerKey ownerKey,
        in CompositionAnimationMarkerEvent markerEvent)
    {
        if (markerEvent.OwnerKind != CompositionAnimationMarkerOwnerKind.TransformOpacity
            || markerEvent.MarkerId != CompletionMarkerId
            || markerEvent.RuntimeEventId != CompletionRuntimeEventId)
        {
            return -1;
        }

        for (var i = 0; i < _activeOwnerCount; i++)
        {
            var active = _activeOwners![i];
            if ((ownerKey.IsNone || active.OwnerKey == ownerKey)
                && markerEvent.TargetKey == active.TargetKey
                && markerEvent.InstanceId == active.InstanceId)
            {
                return i;
            }
        }

        return -1;
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

    private static bool IsValidOwnerForDecision(
        StyleTransitionOwnerKey ownerKey,
        in StyleTransitionRuntimeDecision decision)
    {
        return !ownerKey.IsNone
            && ownerKey.TargetKey == decision.TargetKey;
    }

    private void UpsertActiveOwner(
        StyleTransitionOwnerKey ownerKey,
        NodeKey targetKey,
        CompositionAnimationInstanceId instanceId)
    {
        var activeIndex = FindActiveOwnerIndex(ownerKey);
        if (activeIndex >= 0)
        {
            _activeOwners![activeIndex] = new TrackedTransition(ownerKey, targetKey, instanceId);
            return;
        }

        EnsureActiveOwnerCapacity();
        _activeOwners![_activeOwnerCount++] = new TrackedTransition(ownerKey, targetKey, instanceId);
    }

    private void EnsureActiveOwnerCapacity()
    {
        if (_activeOwners is null)
        {
            _activeOwners = new TrackedTransition[4];
            return;
        }

        if (_activeOwnerCount < _activeOwners.Length)
        {
            return;
        }

        Array.Resize(ref _activeOwners, _activeOwners.Length * 2);
    }

    private int FindActiveOwnerIndex(StyleTransitionOwnerKey ownerKey)
    {
        if (ownerKey.IsNone)
        {
            return -1;
        }

        for (var i = 0; i < _activeOwnerCount; i++)
        {
            if (_activeOwners![i].OwnerKey == ownerKey)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveActiveOwnerAt(int index)
    {
        if (index < 0 || index >= _activeOwnerCount)
        {
            return;
        }

        var moveCount = _activeOwnerCount - index - 1;
        if (moveCount > 0)
        {
            Array.Copy(_activeOwners!, index + 1, _activeOwners!, index, moveCount);
        }

        _activeOwnerCount--;
        _activeOwners![_activeOwnerCount] = default;
    }

    private TrackedTransition GetPrimaryActiveTransition()
    {
        return _activeOwnerCount == 0
            ? default
            : _activeOwners![0];
    }

    private static StyleTransitionOwnerKey CreateLegacyOwnerKey(NodeKey targetKey)
    {
        return targetKey == NodeKey.None
            ? default
            : StyleTransitionOwnerKey.ControlState(default, targetKey);
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

    private readonly struct TrackedTransition(
        StyleTransitionOwnerKey OwnerKey,
        NodeKey TargetKey,
        CompositionAnimationInstanceId InstanceId)
    {
        public StyleTransitionOwnerKey OwnerKey { get; } = OwnerKey;
        public NodeKey TargetKey { get; } = TargetKey;
        public CompositionAnimationInstanceId InstanceId { get; } = InstanceId;
    }
}
