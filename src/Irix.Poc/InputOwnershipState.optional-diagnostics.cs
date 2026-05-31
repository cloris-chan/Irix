#if IRIX_DIAGNOSTICS
namespace Irix.Poc;

internal enum InputOwnershipEventKind : byte
{
    None,
    HoverChanged,
    FocusChanged,
    PressedChanged
}

internal readonly struct InputOwnershipEvent : IEquatable<InputOwnershipEvent>
{
    private InputOwnershipEvent(
        InputOwnershipEventKind kind,
        ActionId previousTarget,
        ActionId currentTarget,
        ActionId previousCapturedTarget,
        ActionId currentCapturedTarget,
        bool isPointerPressed)
    {
        Kind = kind;
        PreviousTarget = previousTarget;
        CurrentTarget = currentTarget;
        PreviousCapturedTarget = previousCapturedTarget;
        CurrentCapturedTarget = currentCapturedTarget;
        IsPointerPressed = isPointerPressed;
    }

    public InputOwnershipEventKind Kind { get; }
    public ActionId PreviousTarget { get; }
    public ActionId CurrentTarget { get; }
    public ActionId PreviousCapturedTarget { get; }
    public ActionId CurrentCapturedTarget { get; }
    public bool IsPointerPressed { get; }

    public ActionId PreviousPressedTarget => PreviousTarget;
    public ActionId CurrentPressedTarget => CurrentTarget;

    public static InputOwnershipEvent HoverChanged(ActionId previousTarget, ActionId currentTarget) =>
        new(InputOwnershipEventKind.HoverChanged, previousTarget, currentTarget, ActionId.None, ActionId.None, false);

    public static InputOwnershipEvent FocusChanged(ActionId previousTarget, ActionId currentTarget) =>
        new(InputOwnershipEventKind.FocusChanged, previousTarget, currentTarget, ActionId.None, ActionId.None, false);

    public static InputOwnershipEvent PressedChanged(
        ActionId previousPressedTarget,
        ActionId currentPressedTarget,
        ActionId previousCapturedTarget,
        ActionId currentCapturedTarget,
        bool isPointerPressed) =>
        new(
            InputOwnershipEventKind.PressedChanged,
            previousPressedTarget,
            currentPressedTarget,
            previousCapturedTarget,
            currentCapturedTarget,
            isPointerPressed);

    public bool Equals(InputOwnershipEvent other)
    {
        return Kind == other.Kind
            && PreviousTarget == other.PreviousTarget
            && CurrentTarget == other.CurrentTarget
            && PreviousCapturedTarget == other.PreviousCapturedTarget
            && CurrentCapturedTarget == other.CurrentCapturedTarget
            && IsPointerPressed == other.IsPointerPressed;
    }

    public override bool Equals(object? obj) => obj is InputOwnershipEvent other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Kind,
            PreviousTarget,
            CurrentTarget,
            PreviousCapturedTarget,
            CurrentCapturedTarget,
            IsPointerPressed);
    }

    public static bool operator ==(InputOwnershipEvent left, InputOwnershipEvent right) => left.Equals(right);

    public static bool operator !=(InputOwnershipEvent left, InputOwnershipEvent right) => !left.Equals(right);
}

internal sealed partial class InputOwnershipState
{
    private const int MaxDiagnosticEventCount = 128;

    private readonly InputOwnershipEvent[] _diagnosticEvents = new InputOwnershipEvent[MaxDiagnosticEventCount];
    private readonly InputOwnershipEventList _diagnosticEventView;
    private int _diagnosticEventStart;
    private int _diagnosticEventCount;

    public InputOwnershipState()
    {
        _diagnosticEventView = new InputOwnershipEventList(this);
    }

    public IReadOnlyList<InputOwnershipEvent> DiagnosticEvents => _diagnosticEventView;

    partial void RecordHoverChanged(ActionId previousTarget, ActionId currentTarget)
    {
        AddDiagnosticEvent(InputOwnershipEvent.HoverChanged(previousTarget, currentTarget));
    }

    partial void RecordFocusChanged(ActionId previousTarget, ActionId currentTarget)
    {
        AddDiagnosticEvent(InputOwnershipEvent.FocusChanged(previousTarget, currentTarget));
    }

    partial void RecordPressedChanged(
        ActionId previousPressedTarget,
        ActionId currentPressedTarget,
        ActionId previousCapturedTarget,
        ActionId currentCapturedTarget,
        bool isPointerPressed)
    {
        AddDiagnosticEvent(InputOwnershipEvent.PressedChanged(
            previousPressedTarget,
            currentPressedTarget,
            previousCapturedTarget,
            currentCapturedTarget,
            isPointerPressed));
    }

    private void AddDiagnosticEvent(InputOwnershipEvent diagnosticEvent)
    {
        var writeIndex = (_diagnosticEventStart + _diagnosticEventCount) % MaxDiagnosticEventCount;
        if (_diagnosticEventCount == MaxDiagnosticEventCount)
        {
            _diagnosticEvents[_diagnosticEventStart] = diagnosticEvent;
            _diagnosticEventStart = (_diagnosticEventStart + 1) % MaxDiagnosticEventCount;
            return;
        }

        _diagnosticEvents[writeIndex] = diagnosticEvent;
        _diagnosticEventCount++;
    }

    private InputOwnershipEvent GetDiagnosticEvent(int index)
    {
        if ((uint)index >= (uint)_diagnosticEventCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _diagnosticEvents[(_diagnosticEventStart + index) % MaxDiagnosticEventCount];
    }

    private sealed class InputOwnershipEventList(InputOwnershipState owner) : IReadOnlyList<InputOwnershipEvent>
    {
        public int Count => owner._diagnosticEventCount;

        public InputOwnershipEvent this[int index] => owner.GetDiagnosticEvent(index);

        public IEnumerator<InputOwnershipEvent> GetEnumerator()
        {
            for (var i = 0; i < owner._diagnosticEventCount; i++)
            {
                yield return owner.GetDiagnosticEvent(i);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
#endif
