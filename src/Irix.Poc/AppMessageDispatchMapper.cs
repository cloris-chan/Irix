namespace Irix.Poc;

internal enum AppDispatchIntentKind
{
    None,
    InputMessage,
    InputOwnershipChanged,
    MaxScrollFeedback,
    ScrollPresentationInterrupted
}

internal readonly struct AppDispatchIntent<TInputMessage>
{
    private AppDispatchIntent(
        AppDispatchIntentKind kind,
        TInputMessage? inputMessage,
        OwnershipSnapshot ownershipSnapshot,
        double maxScrollY,
        ScrollPresentationInterruptDecision scrollPresentationInterruptDecision)
    {
        Kind = kind;
        InputMessage = inputMessage;
        OwnershipSnapshot = ownershipSnapshot;
        MaxScrollY = maxScrollY;
        ScrollPresentationInterruptDecision = scrollPresentationInterruptDecision;
    }

    public AppDispatchIntentKind Kind { get; }
    public TInputMessage? InputMessage { get; }
    public OwnershipSnapshot OwnershipSnapshot { get; }
    public double MaxScrollY { get; }
    public ScrollPresentationInterruptDecision ScrollPresentationInterruptDecision { get; }

    public static AppDispatchIntent<TInputMessage> Input(
        TInputMessage inputMessage,
        in OwnershipSnapshot ownershipSnapshot) =>
        new(AppDispatchIntentKind.InputMessage, inputMessage, ownershipSnapshot, 0, default);

    public static AppDispatchIntent<TInputMessage> InputOwnershipChanged(
        in OwnershipSnapshot ownershipSnapshot) =>
        new(AppDispatchIntentKind.InputOwnershipChanged, default, ownershipSnapshot, 0, default);

    public static AppDispatchIntent<TInputMessage> MaxScrollFeedback(double maxScrollY) =>
        new(AppDispatchIntentKind.MaxScrollFeedback, default, default, maxScrollY, default);

    public static AppDispatchIntent<TInputMessage> ScrollPresentationInterrupted(
        in ScrollPresentationInterruptDecision decision) =>
        new(AppDispatchIntentKind.ScrollPresentationInterrupted, default, default, 0, decision);
}

internal interface IAppMessageDispatchMapper<TInputMessage, TAppMessage>
{
    bool TryMapIntent(
        in AppDispatchIntent<TInputMessage> intent,
        out TAppMessage appMessage)
    {
        switch (intent.Kind)
        {
            case AppDispatchIntentKind.InputMessage:
                return TryMapInputMessage(intent.InputMessage!, intent.OwnershipSnapshot, out appMessage);
            case AppDispatchIntentKind.InputOwnershipChanged:
                return TryMapInputOwnershipChanged(intent.OwnershipSnapshot, out appMessage);
            default:
                appMessage = default!;
                return false;
        }
    }

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
    bool TryMapControlFeedbackIntent<TInputMessage>(
        in AppDispatchIntent<TInputMessage> intent,
        out TAppMessage appMessage)
    {
        if (intent.Kind == AppDispatchIntentKind.MaxScrollFeedback)
        {
            return TryMapMaxScrollY(intent.MaxScrollY, out appMessage);
        }

        appMessage = default!;
        return false;
    }

    bool TryMapMaxScrollY(double maxScrollY, out TAppMessage appMessage);
}

internal readonly struct CounterAppMessageDispatchMapper :
    IAppMessageDispatchMapper<CounterMessage, CounterMessage>,
    IControlFeedbackDispatchMapper<CounterMessage>
{
    public bool TryMapIntent(
        in AppDispatchIntent<CounterMessage> intent,
        out CounterMessage appMessage)
    {
        switch (intent.Kind)
        {
            case AppDispatchIntentKind.InputMessage:
                appMessage = intent.InputMessage is CounterMessage.WheelRaw
                    ? intent.InputMessage
                    : new CounterMessage.RoutedInput(intent.InputMessage, intent.OwnershipSnapshot);
                return true;
            case AppDispatchIntentKind.InputOwnershipChanged:
                appMessage = new CounterMessage.InputVisualStateChanged(intent.OwnershipSnapshot);
                return true;
            case AppDispatchIntentKind.MaxScrollFeedback:
                appMessage = new CounterMessage.UpdateMaxScrollY(intent.MaxScrollY);
                return true;
            case AppDispatchIntentKind.ScrollPresentationInterrupted:
                appMessage = new CounterMessage.ScrollPresentationInterrupted(intent.ScrollPresentationInterruptDecision);
                return true;
            default:
                appMessage = null!;
                return false;
        }
    }

    public bool TryMapInputMessage(
        CounterMessage inputMessage,
        in OwnershipSnapshot ownershipSnapshot,
        out CounterMessage appMessage)
    {
        var intent = AppDispatchIntent<CounterMessage>.Input(inputMessage, in ownershipSnapshot);
        return TryMapIntent(in intent, out appMessage);
    }

    public bool TryMapInputOwnershipChanged(
        in OwnershipSnapshot ownershipSnapshot,
        out CounterMessage appMessage)
    {
        var intent = AppDispatchIntent<CounterMessage>.InputOwnershipChanged(in ownershipSnapshot);
        return TryMapIntent(in intent, out appMessage);
    }

    public bool TryMapMaxScrollY(double maxScrollY, out CounterMessage appMessage)
    {
        var intent = AppDispatchIntent<CounterMessage>.MaxScrollFeedback(maxScrollY);
        return TryMapIntent(in intent, out appMessage);
    }
}
