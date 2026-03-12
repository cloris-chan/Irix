namespace Irix;

public abstract record Command<TMessage>
{
    private Command()
    {
    }

    public sealed record None : Command<TMessage>;

    public sealed record Async(Func<CancellationToken, ValueTask<TMessage>> Callback) : Command<TMessage>;

    public sealed record Batch(IReadOnlyList<Command<TMessage>> Commands) : Command<TMessage>;
}
