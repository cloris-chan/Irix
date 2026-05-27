namespace Irix.Rendering;

internal interface ICompositionMarkerEventMapper<TMessage>
    where TMessage : notnull
{
    CompositionMarkerMappedMessage<TMessage> Map(in CompositionAnimationMarkerEvent markerEvent);
}

internal readonly struct CompositionMarkerMappedMessage<TMessage> : IEquatable<CompositionMarkerMappedMessage<TMessage>>
    where TMessage : notnull
{
    private readonly TMessage _message;

    private CompositionMarkerMappedMessage(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _message = message;
        HasMessage = true;
    }

    public bool HasMessage { get; }

    public TMessage Message
    {
        get
        {
            if (!HasMessage)
            {
                throw new InvalidOperationException("Composition marker event did not map to a runtime message.");
            }

            return _message;
        }
    }

    public static CompositionMarkerMappedMessage<TMessage> Unmapped => default;

    public static CompositionMarkerMappedMessage<TMessage> FromMessage(TMessage message) => new(message);

    public bool Equals(CompositionMarkerMappedMessage<TMessage> other)
    {
        return HasMessage == other.HasMessage
            && EqualityComparer<TMessage>.Default.Equals(_message, other._message);
    }

    public override bool Equals(object? obj) => obj is CompositionMarkerMappedMessage<TMessage> other && Equals(other);

    public override int GetHashCode() => HasMessage ? HashCode.Combine(HasMessage, _message) : 0;

    public static bool operator ==(CompositionMarkerMappedMessage<TMessage> left, CompositionMarkerMappedMessage<TMessage> right) => left.Equals(right);

    public static bool operator !=(CompositionMarkerMappedMessage<TMessage> left, CompositionMarkerMappedMessage<TMessage> right) => !left.Equals(right);
}

internal readonly struct CompositionMarkerDispatchResult(
    int DrainedEvents,
    int DispatchedMessages,
    int UnmappedEvents) : IEquatable<CompositionMarkerDispatchResult>
{
    public int DrainedEvents { get; } = DrainedEvents;
    public int DispatchedMessages { get; } = DispatchedMessages;
    public int UnmappedEvents { get; } = UnmappedEvents;

    public bool Equals(CompositionMarkerDispatchResult other)
    {
        return DrainedEvents == other.DrainedEvents
            && DispatchedMessages == other.DispatchedMessages
            && UnmappedEvents == other.UnmappedEvents;
    }

    public override bool Equals(object? obj) => obj is CompositionMarkerDispatchResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DrainedEvents, DispatchedMessages, UnmappedEvents);

    public static bool operator ==(CompositionMarkerDispatchResult left, CompositionMarkerDispatchResult right) => left.Equals(right);

    public static bool operator !=(CompositionMarkerDispatchResult left, CompositionMarkerDispatchResult right) => !left.Equals(right);
}

internal static class CompositionMarkerEventPump
{
    private const int StackEventCapacity = 16;

    public static CompositionMarkerDispatchResult DrainAndDispatch<TMapper, TMessage>(
        DrawingBackendCompositor compositor,
        ref TMapper mapper,
        IMessageDispatcher<TMessage> dispatcher)
        where TMapper : struct, ICompositionMarkerEventMapper<TMessage>
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(compositor);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Span<CompositionAnimationMarkerEvent> events = stackalloc CompositionAnimationMarkerEvent[StackEventCapacity];
        var drained = 0;
        var dispatched = 0;
        var unmapped = 0;
        while (true)
        {
            var count = compositor.DrainCompositionMarkerEvents(events);
            if (count == 0)
            {
                break;
            }

            drained += count;
            for (var i = 0; i < count; i++)
            {
                var mapped = mapper.Map(events[i]);
                if (mapped.HasMessage)
                {
                    dispatcher.Dispatch(mapped.Message);
                    dispatched++;
                }
                else
                {
                    unmapped++;
                }
            }
        }

        return new CompositionMarkerDispatchResult(drained, dispatched, unmapped);
    }
}
