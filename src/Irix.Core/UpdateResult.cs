namespace Irix;

public readonly record struct UpdateResult<TModel, TMessage>(
    TModel NextModel,
    Command<TMessage>? Command = null);
