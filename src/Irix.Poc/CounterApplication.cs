namespace Irix.Poc;

internal sealed record CounterModel(int Count);

internal abstract record CounterMessage
{
    public sealed record Increment : CounterMessage;

    public sealed record Decrement : CounterMessage;

    public sealed record Reset(int Value) : CounterMessage;
}

internal sealed class CounterApplication : IApplication<CounterModel, CounterMessage>
{
    public CounterModel Initialize() => new(0);

    public UpdateResult<CounterModel, CounterMessage> Update(CounterModel model, CounterMessage message) =>
        message switch
        {
            CounterMessage.Increment => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count + 1 }),
            CounterMessage.Decrement => new UpdateResult<CounterModel, CounterMessage>(model with { Count = model.Count - 1 }),
            CounterMessage.Reset reset => new UpdateResult<CounterModel, CounterMessage>(model with { Count = reset.Value }),
            _ => throw new NotSupportedException($"Unsupported message type: {message.GetType().Name}")
        };

    public VirtualNodeTree BuildView(CounterModel model)
    {
        var root = VirtualNodeFactory.ScrollContainer(
            1,
            VirtualNodeFactory.Text($"Count: {model.Count}", 2),
            VirtualNodeFactory.Text("Click a button or use Up/Down, mouse wheel, and R.", 3),
            VirtualNodeFactory.Rectangle(220, 48, 4),
            VirtualNodeFactory.Button(
                "Increment",
                5,
                new VirtualNodeAttribute("Action", AttributeValue.FromText(nameof(CounterMessage.Increment)))),
            VirtualNodeFactory.Button(
                "Decrement",
                6,
                new VirtualNodeAttribute("Action", AttributeValue.FromText(nameof(CounterMessage.Decrement)))),
            VirtualNodeFactory.Button(
                "Reset",
                7,
                new VirtualNodeAttribute("Action", AttributeValue.FromText(nameof(CounterMessage.Reset)))));

        return new VirtualNodeTree(root);
    }
}
