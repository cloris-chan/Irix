namespace Irix.Poc;

internal sealed record CounterModel(int Count, int ScrollY);

internal abstract record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;

    public sealed record Scroll(int DeltaY) : CounterMessage;
}

internal sealed class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    public CounterModel Initialize() => new(0, 0);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message) =>
        message switch
        {
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(model with { Count = reset.Value }),
            CounterMessage.Scroll scroll => new UpdateResult<CounterModel, CounterMessage>(model with
            {
                ScrollY = Math.Max(model.ScrollY + scroll.DeltaY, 0)
            }),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };

    public VirtualNodeTree BuildView(CounterModel model)
    {
        var root = new VirtualNode(
            VirtualNodeKind.ScrollContainer,
            key: 1,
            attributes: [new VirtualNodeAttribute("ScrollY", AttributeValue.FromNumber(model.ScrollY))],
            children: [
                VirtualNodeFactory.Text($"Count: {model.Count}", 2),
                VirtualNodeFactory.Text($"ScrollY: {model.ScrollY}", 3),
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
