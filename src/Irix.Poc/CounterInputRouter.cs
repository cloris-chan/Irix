using Irix.Platform;

namespace Irix.Poc;

internal static class CounterInputRouter
{
    public static bool TryMapInput(
        RawInputEvent inputEvent,
        Func<int, int, string?> tryGetActionAt,
        out CounterMessage message)
    {
        switch (inputEvent.Kind)
        {
            case RawInputEventKind.PointerReleased
                when inputEvent.Button == PointerButton.Left:
                var action = tryGetActionAt(inputEvent.X, inputEvent.Y);
                if (!string.IsNullOrWhiteSpace(action))
                {
                    message = MapAction(action);
                    return true;
                }

                break;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x26:
                message = new CounterMessage.Increment();
                return true;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x28:
                message = new CounterMessage.Decrement();
                return true;
            case RawInputEventKind.PointerWheel when inputEvent.Delta > 0:
                message = new CounterMessage.Increment();
                return true;
            case RawInputEventKind.PointerWheel when inputEvent.Delta < 0:
                message = new CounterMessage.Decrement();
                return true;
            case RawInputEventKind.CharacterInput when inputEvent.Character is 'r' or 'R':
                message = new CounterMessage.Reset(0);
                return true;
        }

        message = null!;
        return false;
    }

    internal static CounterMessage MapAction(string action)
    {
        return action switch
        {
            nameof(CounterMessage.Increment) => new CounterMessage.Increment(),
            nameof(CounterMessage.Decrement) => new CounterMessage.Decrement(),
            nameof(CounterMessage.Reset) => new CounterMessage.Reset(0),
            _ => throw new NotSupportedException($"Unsupported action: {action}")
        };
    }
}
