namespace Irix.Poc;

internal sealed record CounterModel(int Count, ScrollState Scroll);

internal abstract record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;

    /// <summary>
    /// Apply a coalesced scroll delta to the target position.
    /// Dispatched immediately by the tick loop on every drain.
    /// </summary>
    public sealed record ScrollDeltaMsg(ScrollDelta Delta) : CounterMessage;

    /// <summary>
    /// Advance the smooth scroll animation by one tick.
    /// Dispatched only when there is no backpressure.
    /// </summary>
    public sealed record ScrollTick(double DeltaTime) : CounterMessage;

    /// <summary>Update MaxScrollY from the layout pass.</summary>
    public sealed record UpdateMaxScrollY(double MaxScrollY) : CounterMessage;

    /// <summary>Raw wheel delta from input. Never reaches Update — coalesced by HandleInput.</summary>
    public sealed record WheelRaw(int RawDelta) : CounterMessage;
}

internal sealed class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    // Track the display target separately from the model target.
    // The model's TargetPosition only updates when the Runtime processes a ScrollDeltaMsg,
    // which can lag behind actual input when the Runtime is backed up with rendering.
    // The display target adds the raw pending accumulator to show the true intended target.
    private double _displayTarget;
    private double _prevModelTarget = double.NaN;

    public CounterModel Initialize() => new(0, ScrollState.Default);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message) =>
        message switch
        {
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(model with { Count = reset.Value }),
            CounterMessage.ScrollDeltaMsg delta => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.ApplyScrollDelta(
                    model.Scroll,
                    delta.Delta,
                    ScrollMetrics.DefaultText,
                    SystemScrollSettings.Default),
            }),
            CounterMessage.ScrollTick tick => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.Tick(model.Scroll, tick.DeltaTime),
            }),
            CounterMessage.UpdateMaxScrollY update => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.WithMaxScrollY(model.Scroll, update.MaxScrollY),
            }),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };

    public VirtualNodeTree BuildView(CounterModel model)
    {
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        var s = model.Scroll;

        // Compute display target: the true intended scroll position.
        // Read raw pending bits directly — this includes deltas that the tick loop
        // has drained but the Runtime hasn't processed yet, as well as deltas that
        // arrived after the last tick drain.
        var pendingPx = Program.DiagPendingPx;
        if (_prevModelTarget != s.TargetPosition)
        {
            // Model target changed (Runtime processed a ScrollDeltaMsg).
            // Adopt model target as base. If new pending arrived since the drain,
            // add it — it hasn't been dispatched yet so it's truly new.
            _displayTarget = s.TargetPosition + pendingPx;
        }
        else
        {
            // Model target unchanged — add newly accumulated pending.
            // The tick loop drains once per tick, so pending here contains only
            // deltas that arrived since the last drain.
            _displayTarget += pendingPx;
        }
        _prevModelTarget = s.TargetPosition;

        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
            children: [
                VirtualNodeFactory.Text($"Count: {model.Count}", 2),
                VirtualNodeFactory.Text($"ScrollY: applied={scrollY} target={_displayTarget:F1} pos={s.Position:F2} max={s.MaxScrollY:F0} acc={s.Accumulator:F3} anim={s.IsAnimating} maxKnown={s.HasMaxScrollY} pendingPx={pendingPx:F0} deltaQueued={Program.DiagDeltaQueued} tickLoop={Program.DiagTickLoopRunning}", 3),
                VirtualNodeFactory.Text("Click a button or use Up/Down, mouse wheel, and R.", 4),
                VirtualNodeFactory.Rectangle(220, 48, 5),
                VirtualNodeFactory.Button(
                    "Increment",
                    6,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText(nameof(CounterMessage.Increment)))),
                VirtualNodeFactory.Button(
                    "Decrement",
                    7,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText(nameof(CounterMessage.Decrement)))),
                VirtualNodeFactory.Button(
                    "Reset",
                    8,
                    new VirtualNodeAttribute("ActionId", AttributeValue.FromText(nameof(CounterMessage.Reset))))
            ]);

        return new VirtualNodeTree(root);
    }
}
