namespace Irix;

public interface IMessageDispatcher<in TMessage>
{
    void Dispatch(TMessage message);
}
