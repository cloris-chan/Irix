namespace Irix.Poc;

internal sealed record CounterModel(int Count, ScrollState Scroll);

internal abstract record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;

    /// <summary>
    /// Coalesced animation frame: combines drained scroll delta with tick.
    /// Sent once per frame by the animation loop. Replaces both Scroll and Tick.
    /// </summary>
    public sealed record ScrollFrame(ScrollDelta Delta, double DeltaTime) : CounterMessage;

    /// <summary>Update MaxScrollY from the layout pass.</summary>
    public sealed record UpdateMaxScrollY(double MaxScrollY) : CounterMessage;

    /// <summary>Raw wheel delta from input. Never reaches Update — coalesced by HandleInput.</summary>
    public sealed record WheelRaw(int RawDelta) : CounterMessage;
}

internal sealed class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    public CounterModel Initialize() => new(0, ScrollState.Default);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message) =>
        message switch
        {
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(model with { Count = reset.Value }),
            CounterMessage.ScrollFrame frame => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                Scroll = ScrollController.Tick(
                    ScrollController.ApplyScrollDelta(
                        model.Scroll,
                        frame.Delta,
                        ScrollMetrics.DefaultText,
                        SystemScrollSettings.Default),
                    frame.DeltaTime),
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
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(scrollY))],
            children: [
                VirtualNodeFactory.Text($"Count: {model.Count}", 2),
                VirtualNodeFactory.Text($"ScrollY: applied={scrollY} target={s.TargetPosition:F1} pos={s.Position:F2} max={s.MaxScrollY:F0} acc={s.Accumulator:F3} anim={s.IsAnimating}", 3),
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
