namespace Irix.Poc;

internal interface IAppMessageDispatchMapper<TInputMessage, TAppMessage>
{
    bool TryMapInputMessage(
        TInputMessage inputMessage,
        in OwnershipSnapshot ownershipSnapshot,
        out TAppMessage appMessage);

    bool TryMapInputOwnershipChanged(
        in OwnershipSnapshot ownershipSnapshot,
        out TAppMessage appMessage);
}

internal interface IControlFeedbackDispatchMapper<TAppMessage>
{
    bool TryMapMaxScrollY(double maxScrollY, out TAppMessage appMessage);
}

internal readonly struct CounterAppMessageDispatchMapper :
    IAppMessageDispatchMapper<CounterMessage, CounterMessage>,
    IControlFeedbackDispatchMapper<CounterMessage>
{
    public bool TryMapInputMessage(
        CounterMessage inputMessage,
        in OwnershipSnapshot ownershipSnapshot,
        out CounterMessage appMessage)
    {
        appMessage = inputMessage is CounterMessage.WheelRaw
            ? inputMessage
            : new CounterMessage.RoutedInput(inputMessage, ownershipSnapshot);
        return true;
    }

    public bool TryMapInputOwnershipChanged(
        in OwnershipSnapshot ownershipSnapshot,
        out CounterMessage appMessage)
    {
        appMessage = new CounterMessage.InputVisualStateChanged(ownershipSnapshot);
        return true;
    }

    public bool TryMapMaxScrollY(double maxScrollY, out CounterMessage appMessage)
    {
        appMessage = new CounterMessage.UpdateMaxScrollY(maxScrollY);
        return true;
    }
}
