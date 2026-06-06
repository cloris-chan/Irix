using Irix.Rendering;

namespace Irix.Poc;

internal static class CounterStyleTransitionBridge
{
    private static readonly CompositionDuration ButtonStateDuration = CompositionDuration.FromMilliseconds(120);

    internal static bool TryCreateDecision(
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
                return false;
            }

            changed = true;
            selectedTarget = target;
            selectedPreviousState = previousState;
            selectedNextState = nextState;
        }

        if (!changed)
        {
            decision = default;
            return false;
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
        return true;
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
        return
        [
            VirtualNodeProperty.TranslateX(0),
            VirtualNodeProperty.TranslateY(translateY),
            VirtualNodeProperty.LayerOpacity(opacity)
        ];
    }

    private static CompositionAnimationInstanceId CreateInstanceId(ActionId actionId, long hoverChangeCount)
    {
        var value = actionId.Value * 1_000u + (uint)(hoverChangeCount & 0x3ff);
        return new CompositionAnimationInstanceId(value == 0 ? 1 : value);
    }
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
        CancellationToken cancellationToken = default)
        where TCompositor : IStyleTransitionCompositorAdapter
        where TSnapshotProvider : IStyleTransitionRetainedSnapshotProvider
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(appMessage);

        await runtime.DispatchAndWaitAsync(appMessage, cancellationToken);

        var adapter = new CounterStyleTransitionRuntimeAdapter(previous, next, startTimestamp);
        var coordinator = new StyleTransitionRuntimeCoordinator();
        return await coordinator.ApplyNextAsync(adapter, compositor, snapshotProvider, cancellationToken);
    }
}
