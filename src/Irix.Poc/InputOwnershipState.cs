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
internal abstract record InputOwnershipEvent
{
    public sealed record HoverChanged(ActionId PreviousTarget, ActionId CurrentTarget) : InputOwnershipEvent;

    public sealed record FocusChanged(ActionId PreviousTarget, ActionId CurrentTarget) : InputOwnershipEvent;

    public sealed record PressedChanged(
        ActionId PreviousPressedTarget,
        ActionId CurrentPressedTarget,
        ActionId PreviousCapturedTarget,
        ActionId CurrentCapturedTarget,
        bool IsPointerPressed) : InputOwnershipEvent;
}

/// <summary>
/// Tracks v0 input ownership for the Counter PoC.
/// This is intentionally scoped to a single pointer, left-button press/capture,
/// and diagnostic ownership state; it does not model multi-touch, nested focus
/// scopes, platform capture APIs, or visual states.
/// </summary>
internal sealed class InputOwnershipState
{
    private readonly List<InputOwnershipEvent> _diagnosticEvents = [];
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
    public void UpdateHover(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAt)
    {
        var previousTarget = HoveredTarget;
        var nextTarget = tryGetActionIdAt(inputEvent.X, inputEvent.Y);
        if (previousTarget == nextTarget)
        {
            return;
        }

        HoveredTarget = nextTarget;
        LastHoverLeftTarget = previousTarget;
        LastHoverEnteredTarget = nextTarget;
        HoverChangeCount++;
        _diagnosticEvents.Add(new InputOwnershipEvent.HoverChanged(previousTarget, nextTarget));
    }

    /// <summary>
    /// Starts left-button ownership. A hit target becomes pressed, captured, and focused;
    /// an empty press clears focus and prevents the matching release from activating a target.
    /// </summary>
    public void PressPointer(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAt)
    {
        var target = tryGetActionIdAt(inputEvent.X, inputEvent.Y);
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
            _diagnosticEvents.Add(new InputOwnershipEvent.FocusChanged(previousFocus, target));
        }

        if (!wasPointerPressed || previousPressed != target || previousCaptured != target)
        {
            _diagnosticEvents.Add(new InputOwnershipEvent.PressedChanged(
                previousPressed,
                target,
                previousCaptured,
                target,
                IsPointerPressed: true));
        }
    }

    /// <summary>
    /// Ends left-button ownership and returns the action target, if any. Captured target wins;
    /// a release without a prior stateful press falls back to release-point hit testing for
    /// compatibility with the legacy stateless router overload.
    /// </summary>
    public ActionId ReleasePointer(RawInputEvent inputEvent, Func<int, int, ActionId> tryGetActionIdAt)
    {
        var previousPressed = PressedTarget;
        var previousCaptured = CapturedTarget;
        var wasPointerPressed = _isPointerPressed;
        var target = _isPointerPressed
            ? CapturedTarget
            : tryGetActionIdAt(inputEvent.X, inputEvent.Y);
        _isPointerPressed = false;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;

        if (wasPointerPressed || !previousPressed.IsNone || !previousCaptured.IsNone)
        {
            _diagnosticEvents.Add(new InputOwnershipEvent.PressedChanged(
                previousPressed,
                ActionId.None,
                previousCaptured,
                ActionId.None,
                IsPointerPressed: false));
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
            _diagnosticEvents.Add(new InputOwnershipEvent.HoverChanged(HoveredTarget, ActionId.None));
            LastHoverLeftTarget = HoveredTarget;
            LastHoverEnteredTarget = ActionId.None;
            HoverChangeCount++;
        }

        if (!FocusedTarget.IsNone)
        {
            _diagnosticEvents.Add(new InputOwnershipEvent.FocusChanged(FocusedTarget, ActionId.None));
        }

        if (_isPointerPressed || !PressedTarget.IsNone || !CapturedTarget.IsNone)
        {
            _diagnosticEvents.Add(new InputOwnershipEvent.PressedChanged(
                PressedTarget,
                ActionId.None,
                CapturedTarget,
                ActionId.None,
                IsPointerPressed: false));
        }

        _isPointerPressed = false;
        HoveredTarget = ActionId.None;
        FocusedTarget = ActionId.None;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;
    }
}