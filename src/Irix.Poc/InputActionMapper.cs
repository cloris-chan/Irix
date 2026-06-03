using Irix.Platform;

namespace Irix.Poc;

internal interface IInputActionMapper<TMessage>
{
    bool TryMapAction(ActionId actionId, in RawInputEvent inputEvent, out TMessage message);
}

internal readonly struct CounterInputActionMapper : IInputActionMapper<CounterMessage>
{
    public bool TryMapAction(ActionId actionId, in RawInputEvent inputEvent, out CounterMessage message)
    {
        message = actionId.Value switch
        {
            1 => new CounterMessage.Increment(),
            2 => new CounterMessage.Decrement(),
            3 => new CounterMessage.Reset(0),
            _ => null!
        };

        return message is not null;
    }
}
