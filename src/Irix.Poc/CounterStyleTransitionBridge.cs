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

            if (!HasCompositionPropertyDelta(previousState, nextState))
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

    internal static bool TryMapNodeKeyToAction(NodeKey nodeKey, out ActionId actionId)
    {
        foreach (var target in CounterButtonTransitionTarget.All)
        {
            if (target.NodeKey == nodeKey)
            {
                actionId = target.ActionId;
                return true;
            }
        }

        actionId = default;
        return false;
    }

    private static bool RequiresRetarget(ControlVisualState previous, ControlVisualState next) =>
        previous.IsHovered || previous.IsPressed || previous.IsFocused || next.IsPressed || next.IsFocused;

    private static bool HasCompositionPropertyDelta(ControlVisualState previous, ControlVisualState next) =>
        GetCompositionTranslateY(previous) != GetCompositionTranslateY(next)
        || GetCompositionOpacity(previous) != GetCompositionOpacity(next);

    private static VirtualNodeProperty[] ToCompositionProperties(ControlVisualState state)
    {
        return StyleDeclarationMapper.ToVirtualNodeProperties(
        [
            StyleDeclaration.TranslationX(0),
            StyleDeclaration.TranslationY(GetCompositionTranslateY(state)),
            StyleDeclaration.Opacity(GetCompositionOpacity(state))
        ]);
    }

    private static double GetCompositionTranslateY(ControlVisualState state) =>
        state.IsPressed ? 2 : 0;

    private static double GetCompositionOpacity(ControlVisualState state) =>
        state.IsPressed ? 0.92 : state.IsHovered ? 0.98 : 1.0;

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
    private static readonly CompositionDuration InitialVisibleTransitionLead = CompositionDuration.FromMilliseconds(24);

    internal static async ValueTask<StyleTransitionRuntimeResult> DispatchAndApplyInputTransitionAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        OwnershipSnapshot previous,
        OwnershipSnapshot next,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CompositionTimestamp startTimestamp,
        CancellationToken cancellationToken = default,
        StyleTransitionCompletionTracker? completionTracker = null,
        bool retimestampAfterDispatch = false)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var activationTimestamp = retimestampAfterDispatch ? CompositionTimestamp.Now() : (CompositionTimestamp?)null;
        var postDispatchStartTimestamp = activationTimestamp is { } timestamp
            ? CreatePostDispatchStartTimestamp(timestamp)
            : (CompositionTimestamp?)null;
        var adapter = new CounterStyleTransitionRuntimeAdapter(previous, next, startTimestamp);
        return await ApplyNextWithOptionalCompletionTrackerAsync(
            adapter,
            compositor,
            snapshotProvider,
            completionTracker,
            postDispatchStartTimestamp,
            activationTimestamp,
            cancellationToken);
    }

    internal static async ValueTask<StyleTransitionRuntimeResult> DispatchAndApplyInputTransitionAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        StyleTransitionRuntimeDecision decision,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        CancellationToken cancellationToken = default,
        StyleTransitionCompletionTracker? completionTracker = null,
        bool retimestampAfterDispatch = false)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var activationTimestamp = retimestampAfterDispatch ? CompositionTimestamp.Now() : (CompositionTimestamp?)null;
        var postDispatchStartTimestamp = activationTimestamp is { } timestamp
            ? CreatePostDispatchStartTimestamp(timestamp)
            : (CompositionTimestamp?)null;
        var adapter = new SingleStyleTransitionRuntimeAdapter(decision);
        return await ApplyNextWithOptionalCompletionTrackerAsync(
            adapter,
            compositor,
            snapshotProvider,
            completionTracker,
            postDispatchStartTimestamp,
            activationTimestamp,
            cancellationToken);
    }

    internal static async ValueTask<StyleTransitionBatchPresentationActivationResult> DispatchAndActivateInputTransitionBatchAsync<TCompositor, TSnapshotProvider>(
        Runtime<CounterModel, CounterMessage> runtime,
        CounterMessage appMessage,
        StyleTransitionDecisionBatch batch,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker completionTracker,
        CancellationToken cancellationToken = default,
        bool retimestampAfterDispatch = false)
        where TCompositor : IStyleTransitionPresentationActivationCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);
        ArgumentNullException.ThrowIfNull(completionTracker);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var activationTimestamp = retimestampAfterDispatch ? CompositionTimestamp.Now() : (CompositionTimestamp?)null;
        var activationBatch = activationTimestamp is { } timestamp
            ? batch.WithStartTimestamp(CreatePostDispatchStartTimestamp(timestamp))
            : batch;
        var coordinator = new StyleTransitionRuntimeCoordinator();
        var result = coordinator.ActivateBatchPresentation(
            activationBatch,
            compositor,
            snapshotProvider,
            completionTracker,
            cancellationToken);
        if (result.Kind == StyleTransitionBatchPresentationActivationKind.Activated
            && activationTimestamp is { } initialTickTimestamp)
        {
            await TryRenderInitialPresentationTickAsync(compositor, initialTickTimestamp, cancellationToken);
        }

        return result;
    }

    private static async ValueTask<StyleTransitionRuntimeResult> ApplyNextWithOptionalCompletionTrackerAsync<TRuntime, TCompositor, TSnapshotProvider>(
        TRuntime runtime,
        TCompositor compositor,
        TSnapshotProvider snapshotProvider,
        StyleTransitionCompletionTracker? completionTracker,
        CompositionTimestamp? startTimestamp,
        CompositionTimestamp? initialTickTimestamp,
        CancellationToken cancellationToken)
        where TRuntime : IStyleTransitionRuntimeAdapter
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        var decision = runtime.ConsumeStyleTransitionDecision();
        if (startTimestamp is { } timestamp)
        {
            decision = decision.WithStartTimestamp(timestamp);
        }

        if (completionTracker is null)
        {
            var resultWithoutTracking = await StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
                decision,
                compositor,
                snapshotProvider,
                cancellationToken);
            runtime.PublishStyleTransitionResult(resultWithoutTracking);
            await TryRenderInitialAnimationTickAsync(compositor, resultWithoutTracking, initialTickTimestamp, cancellationToken);
            return resultWithoutTracking;
        }

        var ownerKey = CreateCounterOwnerKey(decision);
        var trackedDecision = ownerKey.IsNone
            ? completionTracker.AttachCompletionMarker(decision)
            : completionTracker.AttachCompletionMarker(ownerKey, decision);
        var result = await StyleTransitionRuntimeCoordinator.ApplyDecisionAsync(
            trackedDecision,
            compositor,
            snapshotProvider,
            cancellationToken);
        runtime.PublishStyleTransitionResult(result);
        if (ownerKey.IsNone)
        {
            completionTracker.PublishRuntimeResult(trackedDecision, result);
        }
        else
        {
            completionTracker.PublishRuntimeResult(ownerKey, trackedDecision, result);
        }

        await TryRenderInitialAnimationTickAsync(compositor, result, initialTickTimestamp, cancellationToken);
        return result;
    }

    private static CompositionTimestamp CreatePostDispatchStartTimestamp(CompositionTimestamp activationTimestamp)
    {
        return activationTimestamp + CompositionDuration.FromStopwatchTicks(-InitialVisibleTransitionLead.StopwatchTicks);
    }

    private static async ValueTask TryRenderInitialAnimationTickAsync<TCompositor>(
        TCompositor compositor,
        StyleTransitionRuntimeResult result,
        CompositionTimestamp? timestamp,
        CancellationToken cancellationToken)
        where TCompositor : IStyleTransitionCompositorAdapter
    {
        if (timestamp is null
            || result.Kind is not (StyleTransitionRuntimeResultKind.Started or StyleTransitionRuntimeResultKind.Retargeted)
            || compositor is not IStyleTransitionAnimationTickCompositorAdapter animationTickCompositor)
        {
            return;
        }

        try
        {
            await animationTickCompositor.RenderAnimationTickAtAsync(timestamp.Value, cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static async ValueTask TryRenderInitialPresentationTickAsync<TCompositor>(
        TCompositor compositor,
        CompositionTimestamp timestamp,
        CancellationToken cancellationToken)
        where TCompositor : IStyleTransitionPresentationActivationCompositorAdapter
    {
        if (compositor is not IStyleTransitionAnimationPresentationTickCompositorAdapter presentationTickCompositor)
        {
            return;
        }

        try
        {
            await presentationTickCompositor.RenderAnimationPresentationTickAtAsync(timestamp, cancellationToken);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static StyleTransitionOwnerKey CreateCounterOwnerKey(in StyleTransitionRuntimeDecision decision)
    {
        return CounterStyleTransitionBridge.TryMapNodeKeyToAction(decision.TargetKey, out var actionId)
            ? StyleTransitionOwnerKey.ControlState(actionId, decision.TargetKey)
            : default;
    }
}
