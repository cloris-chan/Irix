using Irix.Platform;

namespace Irix.Poc;

/// <summary>
/// Immutable read model for the PoC input ownership state.
/// </summary>
internal readonly record struct OwnershipSnapshot(
    ActionId HoveredTarget,
    ActionId FocusedTarget,
    ActionId PressedTarget,
    ActionId CapturedTarget,
    ActionId LastHoverEnteredTarget,
    ActionId LastHoverLeftTarget,
    long HoverChangeCount,
    bool IsPointerPressed);

/// <summary>
/// Diagnostic-only ownership event stream for v0 input ownership changes.
/// These events are not visual state and do not imply multi-pointer, nested focus,
/// or platform capture support.
/// </summary>
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

/// <summary>
/// Tracks v0 input ownership for the Counter PoC.
/// This is intentionally scoped to a single pointer, left-button press/capture,
/// and diagnostic ownership state; it does not model multi-touch, nested focus
/// scopes, platform capture APIs, or visual states.
/// </summary>
internal sealed class InputOwnershipState
{
    private const int MaxDiagnosticEventCount = 128;

    private readonly List<InputOwnershipEvent> _diagnosticEvents = new(MaxDiagnosticEventCount);
    private bool _isPointerPressed;

    /// <summary>The current hit-test target under the pointer, updated by pointer move.</summary>
    public ActionId HoveredTarget { get; private set; }

    /// <summary>The target that receives focused keyboard activation.</summary>
    public ActionId FocusedTarget { get; private set; }

    /// <summary>The target that received the current left-button press, if any.</summary>
    public ActionId PressedTarget { get; private set; }

    /// <summary>The target that owns pointer release until the current press ends.</summary>
    public ActionId CapturedTarget { get; private set; }

    /// <summary>The last target entered by hover diagnostics.</summary>
    public ActionId LastHoverEnteredTarget { get; private set; }

    /// <summary>The last target left by hover diagnostics.</summary>
    public ActionId LastHoverLeftTarget { get; private set; }

    /// <summary>The number of hover target changes observed.</summary>
    public long HoverChangeCount { get; private set; }

    /// <summary>Diagnostic ownership events emitted by v0 state transitions.</summary>
    public IReadOnlyList<InputOwnershipEvent> DiagnosticEvents => _diagnosticEvents;

    /// <summary>
    /// A single consistent read of the current ownership state for diagnostics.
    /// </summary>
    public OwnershipSnapshot Snapshot => new(
        HoveredTarget,
        FocusedTarget,
        PressedTarget,
        CapturedTarget,
        LastHoverEnteredTarget,
        LastHoverLeftTarget,
        HoverChangeCount,
        _isPointerPressed);

    /// <summary>Updates hover diagnostics from the latest pointer location.</summary>
    public void UpdateHover(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAtPhysicalPixel)
    {
        var resolver = new DelegateActionHitTestResolver(tryGetActionIdAtPhysicalPixel);
        UpdateHover(inputEvent, ref resolver);
    }

    public void UpdateHover<THitTestResolver>(RawInputEvent inputEvent, ref THitTestResolver hitTestResolver)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var previousTarget = HoveredTarget;
        var nextTarget = hitTestResolver.Resolve(inputEvent.X, inputEvent.Y);
        if (previousTarget == nextTarget)
        {
            return;
        }

        HoveredTarget = nextTarget;
        LastHoverLeftTarget = previousTarget;
        LastHoverEnteredTarget = nextTarget;
        HoverChangeCount++;
        AddDiagnosticEvent(InputOwnershipEvent.HoverChanged(previousTarget, nextTarget));
    }

    /// <summary>
    /// Starts left-button ownership. A hit target becomes pressed, captured, and focused;
    /// an empty press clears focus and prevents the matching release from activating a target.
    /// </summary>
    public void PressPointer(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAtPhysicalPixel)
    {
        var resolver = new DelegateActionHitTestResolver(tryGetActionIdAtPhysicalPixel);
        PressPointer(inputEvent, ref resolver);
    }

    public void PressPointer<THitTestResolver>(RawInputEvent inputEvent, ref THitTestResolver hitTestResolver)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var target = hitTestResolver.Resolve(inputEvent.X, inputEvent.Y);
        var previousFocus = FocusedTarget;
        var previousPressed = PressedTarget;
        var previousCaptured = CapturedTarget;
        var wasPointerPressed = _isPointerPressed;
        _isPointerPressed = true;
        PressedTarget = target;
        CapturedTarget = target;
        FocusedTarget = target;

        if (previousFocus != target)
        {
            AddDiagnosticEvent(InputOwnershipEvent.FocusChanged(previousFocus, target));
        }

        if (!wasPointerPressed || previousPressed != target || previousCaptured != target)
        {
            AddDiagnosticEvent(InputOwnershipEvent.PressedChanged(
                previousPressed,
                target,
                previousCaptured,
                target,
                isPointerPressed: true));
        }
    }

    /// <summary>
    /// Ends left-button ownership and returns the action target, if any. Captured target wins;
    /// a release without a prior stateful press falls back to release-point hit testing for
    /// compatibility with the legacy stateless router overload.
    /// </summary>
    public ActionId ReleasePointer(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAtPhysicalPixel)
    {
        var resolver = new DelegateActionHitTestResolver(tryGetActionIdAtPhysicalPixel);
        return ReleasePointer(inputEvent, ref resolver);
    }

    public ActionId ReleasePointer<THitTestResolver>(RawInputEvent inputEvent, ref THitTestResolver hitTestResolver)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var previousPressed = PressedTarget;
        var previousCaptured = CapturedTarget;
        var wasPointerPressed = _isPointerPressed;
        var target = _isPointerPressed
            ? CapturedTarget
            : hitTestResolver.Resolve(inputEvent.X, inputEvent.Y);
        _isPointerPressed = false;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;

        if (wasPointerPressed || !previousPressed.IsNone || !previousCaptured.IsNone)
        {
            AddDiagnosticEvent(InputOwnershipEvent.PressedChanged(
                previousPressed,
                ActionId.None,
                previousCaptured,
                ActionId.None,
                isPointerPressed: false));
        }

        return target;
    }

    /// <summary>Returns the target that should receive focused keyboard activation.</summary>
    public ActionId GetKeyboardTarget() => FocusedTarget;

    /// <summary>Clears all ownership state, used when the native window loses focus.</summary>
    public void Clear()
    {
        if (!HoveredTarget.IsNone)
        {
            AddDiagnosticEvent(InputOwnershipEvent.HoverChanged(HoveredTarget, ActionId.None));
            LastHoverLeftTarget = HoveredTarget;
            LastHoverEnteredTarget = ActionId.None;
            HoverChangeCount++;
        }

        if (!FocusedTarget.IsNone)
        {
            AddDiagnosticEvent(InputOwnershipEvent.FocusChanged(FocusedTarget, ActionId.None));
        }

        if (_isPointerPressed || !PressedTarget.IsNone || !CapturedTarget.IsNone)
        {
            AddDiagnosticEvent(InputOwnershipEvent.PressedChanged(
                PressedTarget,
                ActionId.None,
                CapturedTarget,
                ActionId.None,
                isPointerPressed: false));
        }

        _isPointerPressed = false;
        HoveredTarget = ActionId.None;
        FocusedTarget = ActionId.None;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;
    }

    private void AddDiagnosticEvent(InputOwnershipEvent diagnosticEvent)
    {
        if (_diagnosticEvents.Count == MaxDiagnosticEventCount)
        {
            _diagnosticEvents.RemoveAt(0);
        }

        _diagnosticEvents.Add(diagnosticEvent);
    }
}
