namespace Irix.Poc;

internal static class ActionIdRegistry
{
    internal static readonly ActionId Increment = new(1);
    internal static readonly ActionId Decrement = new(2);
    internal static readonly ActionId Reset = new(3);

    internal static ActionId Resolve(string name) => name switch
    {
        nameof(CounterMessage.Increment) => Increment,
        nameof(CounterMessage.Decrement) => Decrement,
        nameof(CounterMessage.Reset) => Reset,
        _ => throw new NotSupportedException($"Unknown action name: {name}")
    };

    internal static string GetName(ActionId id) => id.Value switch
    {
        1 => nameof(CounterMessage.Increment),
        2 => nameof(CounterMessage.Decrement),
        3 => nameof(CounterMessage.Reset),
        _ => $"#{id.Value}"
    };
}
