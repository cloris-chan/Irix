namespace Irix;

internal interface IApplication<TModel, TMessage>
    where TModel : notnull
    where TMessage : notnull
{
    TModel Initialize();

    UpdateResult<TModel, TMessage> Update(TModel model, TMessage message);

    VirtualNodeTree BuildView(TModel model);
}
