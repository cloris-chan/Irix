using Irix.Platform;

namespace Irix.Poc;

internal static class CounterInputRouter
{
    /// <summary>
    /// Maps one input event using Counter PoC ownership v0: single pointer, left-button
    /// pressed/captured target, hover diagnostics, and focused keyboard activation.
    /// </summary>
    public static bool TryMapInput(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        Func<int, int, ActionId> tryGetActionIdAtPhysicalPixel,
        out CounterMessage message)
    {
        var resolver = new DelegateActionHitTestResolver(tryGetActionIdAtPhysicalPixel);
        return TryMapInput(inputEvent, ownershipState, resolver, out message);
    }

    /// <summary>
    /// Maps one input event using a value-type resolver. Runtime input paths use this overload
    /// to avoid allocating a delegate/closure per native input event.
    /// </summary>
    public static bool TryMapInput<THitTestResolver>(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        THitTestResolver hitTestResolver,
        out CounterMessage message)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var actionMapper = new CounterInputActionMapper();
        return TryMapInput(inputEvent, ownershipState, hitTestResolver, actionMapper, out message);
    }

    public static bool TryMapInput<THitTestResolver, TActionMapper>(
        RawInputEvent inputEvent,
        InputOwnershipState ownershipState,
        THitTestResolver hitTestResolver,
        TActionMapper actionMapper,
        out CounterMessage message)
        where THitTestResolver : struct, IActionHitTestResolver
        where TActionMapper : struct, IInputActionMapper<CounterMessage>
    {
        switch (inputEvent.Kind)
        {
            case RawInputEventKind.PointerMoved:
                ownershipState.UpdateHover(inputEvent, ref hitTestResolver);
                break;
            case RawInputEventKind.PointerPressed
                when inputEvent.Button == PointerButton.Left:
                ownershipState.PressPointer(inputEvent, ref hitTestResolver);
                break;
            case RawInputEventKind.PointerReleased
                when inputEvent.Button == PointerButton.Left:
                var actionId = ownershipState.ReleasePointer(inputEvent, ref hitTestResolver);
                if (!actionId.IsNone)
                {
                    return actionMapper.TryMapAction(actionId, in inputEvent, out message);
                }

                break;
            case RawInputEventKind.KeyPressed
                when inputEvent.KeyCode is 0x0D or 0x20:
                var focusedActionId = ownershipState.GetKeyboardTarget();
                if (!focusedActionId.IsNone)
                {
                    return actionMapper.TryMapAction(focusedActionId, in inputEvent, out message);
                }

                break;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x26:
                message = new CounterMessage.Increment();
                return true;
            case RawInputEventKind.KeyPressed when inputEvent.KeyCode == 0x28:
                message = new CounterMessage.Decrement();
                return true;
            case RawInputEventKind.PointerWheel when inputEvent.Delta != 0:
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

    internal static CounterMessage MapActionId(ActionId actionId)
    {
        var actionMapper = new CounterInputActionMapper();
        if (actionMapper.TryMapAction(actionId, default, out var message))
        {
            return message;
        }

        throw new NotSupportedException($"Unsupported action id: {actionId.Value}");
    }
}
