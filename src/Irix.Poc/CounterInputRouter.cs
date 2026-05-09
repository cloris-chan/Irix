using Irix.Platform;

namespace Irix.Poc;

internal static class CounterInputRouter
{
    public static bool TryMapInput(
        RawInputEvent inputEvent,
        Func<int, int, string?> tryGetActionIdAt,
        out CounterMessage message)
    {
        return TryMapInput(inputEvent, new InputOwnershipState(), tryGetActionIdAt, out message);
    }

    public static bool TryMapInput(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        Func<int, int, string?> tryGetActionIdAt,
        out CounterMessage message)
    {
        switch (inputEvent.Kind)
        {
            case RawInputEventKind.PointerMoved:
                ownershipState.UpdateHover(inputEvent, tryGetActionIdAt);
                break;
            case RawInputEventKind.PointerPressed
                when inputEvent.Button == PointerButton.Left:
                ownershipState.PressPointer(inputEvent, tryGetActionIdAt);
                break;
            case RawInputEventKind.PointerReleased
                when inputEvent.Button == PointerButton.Left:
                var actionId = ownershipState.ReleasePointer(inputEvent, tryGetActionIdAt);
                if (!string.IsNullOrWhiteSpace(actionId))
                {
                    message = MapActionId(actionId);
                    return true;
                }

                break;
            case RawInputEventKind.KeyPressed
                when inputEvent.KeyCode is 0x0D or 0x20:
                var focusedActionId = ownershipState.GetKeyboardTarget();
                if (!string.IsNullOrWhiteSpace(focusedActionId))
                {
                    message = MapActionId(focusedActionId);
                    return true;
                }

                break;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x26:
                message = new CounterMessage.Increment();
                return true;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x28:
                message = new CounterMessage.Decrement();
                return true;
            case RawInputEventKind.PointerWheel when inputEvent.Delta != 0:
                // Send raw delta — HandleInput accumulates for coalescing
                message = new CounterMessage.WheelRaw(inputEvent.Delta);
                return true;
            case RawInputEventKind.CharacterInput when inputEvent.Character is 'r' or 'R':
                message = new CounterMessage.Reset(0);
                return true;
            case RawInputEventKind.FocusLost:
                ownershipState.Clear();
                break;
        }

        message = null!;
        return false;
    }

    internal static CounterMessage MapActionId(string actionId)
    {
        return actionId switch
        {
            nameof(CounterMessage.Increment) => new CounterMessage.Increment(),
            nameof(CounterMessage.Decrement) => new CounterMessage.Decrement(),
            nameof(CounterMessage.Reset) => new CounterMessage.Reset(0),
            _ => throw new NotSupportedException($"Unsupported action id: {actionId}")
        };
    }
}
