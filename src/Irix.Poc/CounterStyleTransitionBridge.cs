using Irix.Rendering;

namespace Irix.Poc;

internal static class CounterStyleTransitionBridge
{
    private static readonly CompositionDuration ButtonStateDuration = CompositionDuration.FromMilliseconds(120);

    internal static CounterStyleTransitionLifecycleResult EvaluateInputTransition(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        bool hasActiveScrollPresentation,
        CompositionTimestamp startTimestamp)
    {
        if (previous == next)
        {
            return CounterStyleTransitionLifecycleResult.DispatchNormally(
                CounterStyleTransitionLifecycleReason.NoOwnershipDelta);
        }

        if (hasActiveScrollPresentation)
        {
            return CounterStyleTransitionLifecycleResult.DispatchNormally(
                CounterStyleTransitionLifecycleReason.ActiveScrollPresentation);
        }

        var reason = CreateDecisionCore(previous, next, startTimestamp, out var decision);
        return reason == CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta
            ? CounterStyleTransitionLifecycleResult.ApplyTransition(decision)
            : CounterStyleTransitionLifecycleResult.DispatchNormally(reason);
    }

    internal static bool TryCreateDecision(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        CompositionTimestamp startTimestamp,
        out StyleTransitionRuntimeDecision decision)
    {
        return CreateDecisionCore(previous, next, startTimestamp, out decision)
            == CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta;
    }

    private static CounterStyleTransitionLifecycleReason CreateDecisionCore(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        CompositionTimestamp startTimestamp,
        out StyleTransitionRuntimeDecision decision)
    {
        var changed = false;
        var selectedTarget = default(CounterButtonTransitionTarget);
        var selectedPreviousState = default(ControlVisualState);
        var selectedNextState = default(ControlVisualState);
        foreach (var target in CounterButtonTransitionTarget.All)
        {
            var previousState = ControlVisualStateProjection.Project(previous, target.ActionId);
            var nextState = ControlVisualStateProjection.Project(next, target.ActionId);
            if (previousState == nextState)
            {
                continue;
            }

            if (changed)
            {
                decision = default;
                return CounterStyleTransitionLifecycleReason.MultiTargetUnsupported;
            }

            changed = true;
            selectedTarget = target;
            selectedPreviousState = previousState;
            selectedNextState = nextState;
        }

        if (!changed)
        {
            decision = default;
            return CounterStyleTransitionLifecycleReason.NoControlStateDelta;
        }

        var previousProperties = ToCompositionProperties(selectedPreviousState);
        var nextProperties = ToCompositionProperties(selectedNextState);
        var kind = RequiresRetarget(selectedPreviousState, selectedNextState)
            ? StyleTransitionRuntimeDecisionKind.Retarget
            : StyleTransitionRuntimeDecisionKind.Start;
        decision = new StyleTransitionRuntimeDecision(
            kind,
            selectedTarget.NodeKey,
            previousProperties,
            nextProperties,
            startTimestamp,
            ButtonStateDuration,
            CompositionAnimationEasing.SineOut,
            CompositionAnimationRepeatMode.Once,
            CreateInstanceId(selectedTarget.ActionId, next.HoverChangeCount));
        return CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta;
    }

    internal static bool TryMapActionToNodeKey(ActionId actionId, out NodeKey nodeKey)
    {
        foreach (var target in CounterButtonTransitionTarget.All)
        {
            if (target.ActionId == actionId)
            {
                nodeKey = target.NodeKey;
                return true;
            }
        }

        nodeKey = NodeKey.None;
        return false;
    }

    private static bool RequiresRetarget(ControlVisualState previous, ControlVisualState next) =>
        previous.IsHovered || previous.IsPressed || previous.IsFocused || next.IsPressed || next.IsFocused;

    private static VirtualNodeProperty[] ToCompositionProperties(ControlVisualState state)
    {
        var translateY = state.IsPressed ? 2 : 0;
        var opacity = state.IsPressed ? 0.92 : state.IsHovered ? 0.98 : 1.0;
        return StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.TranslationX(0),
            StyleDeclaration.TranslationY(translateY),
            StyleDeclaration.Opacity(opacity)
        ]);
    }

    private static CompositionAnimationInstanceId CreateInstanceId(ActionId actionId, long hoverChangeCount)
    {
        var value = actionId.Value * 1_000u + (uint)(hoverChangeCount & 0x3ff);
        return new CompositionAnimationInstanceId(value == 0 ? 1 : value);
    }
}

internal enum CounterStyleTransitionLifecycleAction : byte
{
    None,
    DispatchNormally,
    ApplyTransition,
}

internal enum CounterStyleTransitionLifecyclePresentationPolicy : byte
{
    None,
    Preserve,
    AbortActiveStyleTransition,
}

internal enum CounterStyleTransitionLifecycleReason : byte
{
    None,
    NoOwnershipDelta,
    NoControlStateDelta,
    ActiveScrollPresentation,
    MultiTargetUnsupported,
    SingleTargetControlStateDelta,
}

internal enum CounterStyleTransitionLifecycleCompletionPolicy : byte
{
    None,
    RequiresExplicitRuntimeDecision,
}

internal readonly struct CounterStyleTransitionLifecycleResult(
    CounterStyleTransitionLifecycleAction Action,
    CounterStyleTransitionLifecycleReason Reason,
    StyleTransitionRuntimeDecision Decision = default,
    CounterStyleTransitionLifecycleCompletionPolicy CompletionPolicy = CounterStyleTransitionLifecycleCompletionPolicy.None,
    CounterStyleTransitionLifecyclePresentationPolicy PresentationPolicy = CounterStyleTransitionLifecyclePresentationPolicy.None) : IEquatable<CounterStyleTransitionLifecycleResult>
{
    public CounterStyleTransitionLifecycleAction Action { get; } = Action;
    public CounterStyleTransitionLifecycleReason Reason { get; } = Reason;
    public StyleTransitionRuntimeDecision Decision { get; } = Decision;
    public CounterStyleTransitionLifecycleCompletionPolicy CompletionPolicy { get; } = CompletionPolicy;
    public CounterStyleTransitionLifecyclePresentationPolicy PresentationPolicy { get; } = PresentationPolicy;
    public bool HasTransitionDecision => Action == CounterStyleTransitionLifecycleAction.ApplyTransition
        && Decision.Kind is StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget;
    public bool RequiresNormalDispatch => Action == CounterStyleTransitionLifecycleAction.DispatchNormally;
    public bool RequiresStyleTransitionAbort => PresentationPolicy == CounterStyleTransitionLifecyclePresentationPolicy.AbortActiveStyleTransition;

    public static CounterStyleTransitionLifecycleResult DispatchNormally(CounterStyleTransitionLifecycleReason reason) =>
        new(
            CounterStyleTransitionLifecycleAction.DispatchNormally,
            reason,
            PresentationPolicy: RequiresPresentationAbort(reason)
                ? CounterStyleTransitionLifecyclePresentationPolicy.AbortActiveStyleTransition
                : CounterStyleTransitionLifecyclePresentationPolicy.Preserve);

    public static CounterStyleTransitionLifecycleResult ApplyTransition(StyleTransitionRuntimeDecision decision) =>
        new(
            CounterStyleTransitionLifecycleAction.ApplyTransition,
            CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta,
            decision,
            CounterStyleTransitionLifecycleCompletionPolicy.RequiresExplicitRuntimeDecision,
            CounterStyleTransitionLifecyclePresentationPolicy.Preserve);

    public bool Equals(CounterStyleTransitionLifecycleResult other)
    {
        return Action == other.Action
            && Reason == other.Reason
            && Decision == other.Decision
            && CompletionPolicy == other.CompletionPolicy
            && PresentationPolicy == other.PresentationPolicy;
    }

    public override bool Equals(object? obj) => obj is CounterStyleTransitionLifecycleResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Action, Reason, Decision, CompletionPolicy, PresentationPolicy);

    public static bool operator ==(CounterStyleTransitionLifecycleResult left, CounterStyleTransitionLifecycleResult right) => left.Equals(right);

    public static bool operator !=(CounterStyleTransitionLifecycleResult left, CounterStyleTransitionLifecycleResult right) => !left.Equals(right);

    private static bool RequiresPresentationAbort(CounterStyleTransitionLifecycleReason reason) =>
        reason is CounterStyleTransitionLifecycleReason.ActiveScrollPresentation
            or CounterStyleTransitionLifecycleReason.MultiTargetUnsupported;
}

internal readonly struct CounterButtonTransitionTarget(ActionId ActionId, NodeKey NodeKey) : IEquatable<CounterButtonTransitionTarget>
{
    private static readonly CounterButtonTransitionTarget[] Targets =
    [
        new(ActionIdRegistry.Increment, new NodeKey(6)),
        new(ActionIdRegistry.Decrement, new NodeKey(7)),
        new(ActionIdRegistry.Reset, new NodeKey(8))
    ];

    public ActionId ActionId { get; } = ActionId;
    public NodeKey NodeKey { get; } = NodeKey;

    public static ReadOnlySpan<CounterButtonTransitionTarget> All => Targets;

    public bool Equals(CounterButtonTransitionTarget other) =>
        ActionId == other.ActionId && NodeKey == other.NodeKey;

    public override bool Equals(object? obj) => obj is CounterButtonTransitionTarget other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ActionId, NodeKey);

    public static bool operator ==(CounterButtonTransitionTarget left, CounterButtonTransitionTarget right) => left.Equals(right);

    public static bool operator !=(CounterButtonTransitionTarget left, CounterButtonTransitionTarget right) => !left.Equals(right);
}

internal sealed class CounterStyleTransitionRuntimeAdapter(
    OwnershipSnapshot Previous,
    OwnershipSnapshot Next,
    CompositionTimestamp StartTimestamp) : IStyleTransitionRuntimeAdapter
{
    private readonly OwnershipSnapshot _previous = Previous;
    private readonly OwnershipSnapshot _next = Next;
    private readonly CompositionTimestamp _startTimestamp = StartTimestamp;

    public StyleTransitionRuntimeResult LastResult { get; private set; }

    public StyleTransitionRuntimeDecision ConsumeStyleTransitionDecision()
    {
        return CounterStyleTransitionBridge.TryCreateDecision(
            _previous,
            _next,
            _startTimestamp,
            out var decision)
            ? decision
            : default;
    }

    public void PublishStyleTransitionResult(in StyleTransitionRuntimeResult result)
    {
        LastResult = result;
    }
}

internal static class CounterStyleTransitionRuntimeBridge
{
    internal static async ValueTask<StyleTransitionRuntimeResult> DispatchAndApplyInputTransitionAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        OwnershipSnapshot previous,
        OwnershipSnapshot next,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CompositionTimestamp startTimestamp,
        CancellationToken cancellationToken = default,
        StyleTransitionCompletionTracker? completionTracker = null)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var adapter = new CounterStyleTransitionRuntimeAdapter(previous, next, startTimestamp);
        return await ApplyNextWithOptionalCompletionTrackerAsync(
            adapter,
            compositor,
            snapshotProvider,
            completionTracker,
            cancellationToken);
    }

    internal static async ValueTask<StyleTransitionRuntimeResult> DispatchAndApplyInputTransitionAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        StyleTransitionRuntimeDecision decision,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CancellationToken cancellationToken = default,
        StyleTransitionCompletionTracker? completionTracker = null)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var adapter = new SingleStyleTransitionRuntimeAdapter(decision);
        return await ApplyNextWithOptionalCompletionTrackerAsync(
            adapter,
            compositor,
            snapshotProvider,
            completionTracker,
            cancellationToken);
    }

    private static async ValueTask<StyleTransitionRuntimeResult> ApplyNextWithOptionalCompletionTrackerAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker? completionTracker,
        CancellationToken cancellationToken)
        where TRuntime : IStyleTransitionRuntimeAdapter
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        if (completionTracker is null)
        {
            var coordinator = new StyleTransitionRuntimeCoordinator();
            return await coordinator.ApplyNextAsync(runtime, compositor, snapshotProvider, cancellationToken);
        }

        var decision = runtime.ConsumeStyleTransitionDecision();
        var trackedDecision = completionTracker.AttachCompletionMarker(decision);
        var result = await StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
            trackedDecision,
            compositor,
            snapshotProvider,
            cancellationToken);
        runtime.PublishStyleTransitionResult(result);
        completionTracker.PublishRuntimeResult(trackedDecision, result);
        return result;
    }
}
