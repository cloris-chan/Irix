namespace Irix;

public readonly struct UpdateResult<TModel, TMessage>(TModel NextModel, Command<TMessage>? Command = null) : IEquatable<UpdateResult<TModel, TMessage>>
{

    public TModel NextModel { get; } = NextModel;
    public Command<TMessage>? Command { get; } = Command;

    public bool Equals(UpdateResult<TModel, TMessage> other)
    {
        return EqualityComparer<TModel>.Default.Equals(NextModel, other.NextModel)
            && EqualityComparer<Command<TMessage>?>.Default.Equals(Command, other.Command);
    }

    public override bool Equals(object? obj) => obj is UpdateResult<TModel, TMessage> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(NextModel, Command);

    public static bool operator ==(UpdateResult<TModel, TMessage> left, UpdateResult<TModel, TMessage> right) => left.Equals(right);

    public static bool operator !=(UpdateResult<TModel, TMessage> left, UpdateResult<TModel, TMessage> right) => !left.Equals(right);
}
