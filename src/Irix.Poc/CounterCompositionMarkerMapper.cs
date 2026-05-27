using Irix.Rendering;

namespace Irix.Poc;

internal readonly struct CounterCompositionMarkerRuntimeEventIds
{
    public static readonly CompositionRuntimeEventId Increment = new(1);
    public static readonly CompositionRuntimeEventId Decrement = new(2);
    public static readonly CompositionRuntimeEventId Reset = new(3);
}

internal struct CounterCompositionMarkerMapper : ICompositionMarkerEventMapper<CounterMessage>
{
    public CompositionMarkerMappedMessage<CounterMessage> Map(in CompositionAnimationMarkerEvent markerEvent)
    {
        if (markerEvent.RuntimeEventId == CounterCompositionMarkerRuntimeEventIds.Increment)
        {
            return CompositionMarkerMappedMessage<CounterMessage>.FromMessage(new CounterMessage.Increment());
        }

        if (markerEvent.RuntimeEventId == CounterCompositionMarkerRuntimeEventIds.Decrement)
        {
            return CompositionMarkerMappedMessage<CounterMessage>.FromMessage(new CounterMessage.Decrement());
        }

        if (markerEvent.RuntimeEventId == CounterCompositionMarkerRuntimeEventIds.Reset)
        {
            return CompositionMarkerMappedMessage<CounterMessage>.FromMessage(new CounterMessage.Reset(0));
        }

        return CompositionMarkerMappedMessage<CounterMessage>.Unmapped;
    }
}
