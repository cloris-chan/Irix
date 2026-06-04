namespace Irix.Poc;

internal interface IAppRuntimeDispatchSink<in TMessage>
{
    void Dispatch(TMessage message);
}

internal static class AppRuntimeDispatchAdapter
{
    public static bool TryDispatchMessage<TMessage, TDispatchSink>(
        TMessage? message,
        TDispatchSink dispatchSink)
        where TMessage : class
        where TDispatchSink : struct, IAppRuntimeDispatchSink<TMessage>
    {
        if (message is null)
        {
            return false;
        }

        dispatchSink.Dispatch(message);
        return true;
    }
}

internal readonly struct CounterRuntimeDispatchSink(
    Runtime<CounterModel, CounterMessage> Runtime) : IAppRuntimeDispatchSink<CounterMessage>
{
    public void Dispatch(CounterMessage message)
    {
        Runtime.Dispatch(message);
    }
}
