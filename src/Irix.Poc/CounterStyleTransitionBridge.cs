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

        var reason = CreateDecisionBatchCore(previous, next, startTimestamp, out var decision, out var batch);
        return reason switch
        {
            CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta =>
                CounterStyleTransitionLifecycleResult.ApplyTransition(decision, batch),
            CounterStyleTransitionLifecycleReason.MultiTargetControlStateDelta =>
                CounterStyleTransitionLifecycleResult.ApplyTransitionBatch(batch, reason),
            _ => CounterStyleTransitionLifecycleResult.DispatchNormally(reason),
        };
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

    internal static bool TryCreateDecisionBatch(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        CompositionTimestamp startTimestamp,
        out StyleTransitionDecisionBatch batch)
    {
        var reason = CreateDecisionBatchCore(previous, next, startTimestamp, out _, out batch);
        return reason is CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta
            or CounterStyleTransitionLifecycleReason.MultiTargetControlStateDelta;
    }

    private static CounterStyleTransitionLifecycleReason CreateDecisionCore(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        CompositionTimestamp startTimestamp,
        out StyleTransitionRuntimeDecision decision)
    {
        var reason = CreateDecisionBatchCore(previous, next, startTimestamp, out decision, out var batch);
        if (reason != CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta)
        {
            decision = default;
        }

        _ = batch;
        return reason;
    }

    private static CounterStyleTransitionLifecycleReason CreateDecisionBatchCore(
        in OwnershipSnapshot previous,
        in OwnershipSnapshot next,
        CompositionTimestamp startTimestamp,
        out StyleTransitionRuntimeDecision decision,
        out StyleTransitionDecisionBatch batch)
    {
        decision = default;
        batch = default;
        var entries = new StyleTransitionDecisionBatchEntry[CounterButtonTransitionTarget.All.Length];
        var count = 0;
        foreach (var target in CounterButtonTransitionTarget.All)
        {
            var previousState = ControlVisualStateProjection.Project(previous, target.ActionId);
            var nextState = ControlVisualStateProjection.Project(next, target.ActionId);
            if (previousState == nextState)
            {
                continue;
            }

            var entryDecision = CreateDecision(target, previousState, nextState, startTimestamp, next.HoverChangeCount);
            entries[count++] = new StyleTransitionDecisionBatchEntry(
                StyleTransitionOwnerKey.ControlState(target.ActionId, target.NodeKey),
                entryDecision);
        }

        if (count == 0)
        {
            return CounterStyleTransitionLifecycleReason.NoControlStateDelta;
        }

        decision = count == 1 ? entries[0].Decision : default;
        batch = StyleTransitionDecisionBatch.Create(entries.AsSpan(0, count));
        return count == 1
            ? CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta
            : CounterStyleTransitionLifecycleReason.MultiTargetControlStateDelta;
    }

    private static StyleTransitionRuntimeDecision CreateDecision(
        CounterButtonTransitionTarget target,
        ControlVisualState previousState,
        ControlVisualState nextState,
        CompositionTimestamp startTimestamp,
        long hoverChangeCount)
    {
        var previousProperties = ToCompositionProperties(previousState);
        var nextProperties = ToCompositionProperties(nextState);
        var kind = RequiresRetarget(previousState, nextState)
            ? StyleTransitionRuntimeDecisionKind.Retarget
            : StyleTransitionRuntimeDecisionKind.Start;
        return new StyleTransitionRuntimeDecision(
            kind,
            target.NodeKey,
            previousProperties,
            nextProperties,
            startTimestamp,
            ButtonStateDuration,
            CompositionAnimationEasing.SineOut,
            CompositionAnimationRepeatMode.Once,
            CreateInstanceId(target.ActionId, hoverChangeCount));
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
    ApplyTransitionBatch,
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
    SingleTargetControlStateDelta,
    MultiTargetControlStateDelta,
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
    StyleTransitionDecisionBatch Batch = default,
    CounterStyleTransitionLifecycleCompletionPolicy CompletionPolicy = CounterStyleTransitionLifecycleCompletionPolicy.None,
    CounterStyleTransitionLifecyclePresentationPolicy PresentationPolicy = CounterStyleTransitionLifecyclePresentationPolicy.None) : IEquatable<CounterStyleTransitionLifecycleResult>
{
    public CounterStyleTransitionLifecycleAction Action { get; } = Action;
    public CounterStyleTransitionLifecycleReason Reason { get; } = Reason;
    public StyleTransitionRuntimeDecision Decision { get; } = Decision;
    public StyleTransitionDecisionBatch Batch { get; } = Batch;
    public CounterStyleTransitionLifecycleCompletionPolicy CompletionPolicy { get; } = CompletionPolicy;
    public CounterStyleTransitionLifecyclePresentationPolicy PresentationPolicy { get; } = PresentationPolicy;
    public bool HasTransitionDecision => Action == CounterStyleTransitionLifecycleAction.ApplyTransition
        && Decision.Kind is StyleTransitionRuntimeDecisionKind.Start or StyleTransitionRuntimeDecisionKind.Retarget;
    public bool HasTransitionBatch => Action == CounterStyleTransitionLifecycleAction.ApplyTransitionBatch
        && !Batch.IsEmpty;
    public bool RequiresNormalDispatch => Action == CounterStyleTransitionLifecycleAction.DispatchNormally;
    public bool RequiresStyleTransitionAbort => PresentationPolicy == CounterStyleTransitionLifecyclePresentationPolicy.AbortActiveStyleTransition;

    public static CounterStyleTransitionLifecycleResult DispatchNormally(CounterStyleTransitionLifecycleReason reason) =>
        new(
            CounterStyleTransitionLifecycleAction.DispatchNormally,
            reason,
            PresentationPolicy: RequiresPresentationAbort(reason)
                ? CounterStyleTransitionLifecyclePresentationPolicy.AbortActiveStyleTransition
                : CounterStyleTransitionLifecyclePresentationPolicy.Preserve);

    public static CounterStyleTransitionLifecycleResult ApplyTransition(
        StyleTransitionRuntimeDecision decision,
        StyleTransitionDecisionBatch batch = default) =>
        new(
            CounterStyleTransitionLifecycleAction.ApplyTransition,
            CounterStyleTransitionLifecycleReason.SingleTargetControlStateDelta,
            decision,
            batch,
            CounterStyleTransitionLifecycleCompletionPolicy.RequiresExplicitRuntimeDecision,
            CounterStyleTransitionLifecyclePresentationPolicy.Preserve);

    public static CounterStyleTransitionLifecycleResult ApplyTransitionBatch(
        StyleTransitionDecisionBatch batch,
        CounterStyleTransitionLifecycleReason reason) =>
        new(
            CounterStyleTransitionLifecycleAction.ApplyTransitionBatch,
            reason,
            default,
            batch,
            CounterStyleTransitionLifecycleCompletionPolicy.RequiresExplicitRuntimeDecision,
            CounterStyleTransitionLifecyclePresentationPolicy.Preserve);

    public bool Equals(CounterStyleTransitionLifecycleResult other)
    {
        return Action == other.Action
            && Reason == other.Reason
            && Decision == other.Decision
            && Batch == other.Batch
            && CompletionPolicy == other.CompletionPolicy
            && PresentationPolicy == other.PresentationPolicy;
    }

    public override bool Equals(object? obj) => obj is CounterStyleTransitionLifecycleResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Action, Reason, Decision, Batch, CompletionPolicy, PresentationPolicy);

    public static bool operator ==(CounterStyleTransitionLifecycleResult left, CounterStyleTransitionLifecycleResult right) => left.Equals(right);

    public static bool operator !=(CounterStyleTransitionLifecycleResult left, CounterStyleTransitionLifecycleResult right) => !left.Equals(right);

    private static bool RequiresPresentationAbort(CounterStyleTransitionLifecycleReason reason) =>
        reason is CounterStyleTransitionLifecycleReason.ActiveScrollPresentation;
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

    internal static async ValueTask<StyleTransitionBatchPresentationActivationResult> DispatchAndActivateInputTransitionBatchAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        StyleTransitionDecisionBatch batch,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker,
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionPresentationActivationCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);
        ArgumentNullException.ThrowIfNull(completionTracker);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var coordinator = new StyleTransitionRuntimeCoordinator();
        return coordinator.ActivateBatchPresentation(
            batch,
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
