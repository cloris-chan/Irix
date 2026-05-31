using Irix.Platform;

namespace Irix.Poc;

/// <summary>
/// Immutable read model for the PoC input ownership state.
/// </summary>
internal readonly struct OwnershipSnapshot : IEquatable<OwnershipSnapshot>
{
    public OwnershipSnapshot(
        ActionId HoveredTarget,
        ActionId FocusedTarget,
        ActionId PressedTarget,
        ActionId CapturedTarget,
        ActionId LastHoverEnteredTarget,
        ActionId LastHoverLeftTarget,
        long HoverChangeCount,
        bool IsPointerPressed)
    {
        this.HoveredTarget = HoveredTarget;
        this.FocusedTarget = FocusedTarget;
        this.PressedTarget = PressedTarget;
        this.CapturedTarget = CapturedTarget;
        this.LastHoverEnteredTarget = LastHoverEnteredTarget;
        this.LastHoverLeftTarget = LastHoverLeftTarget;
        this.HoverChangeCount = HoverChangeCount;
        this.IsPointerPressed = IsPointerPressed;
    }

    public ActionId HoveredTarget { get; }
    public ActionId FocusedTarget { get; }
    public ActionId PressedTarget { get; }
    public ActionId CapturedTarget { get; }
    public ActionId LastHoverEnteredTarget { get; }
    public ActionId LastHoverLeftTarget { get; }
    public long HoverChangeCount { get; }
    public bool IsPointerPressed { get; }

    public bool Equals(OwnershipSnapshot other)
    {
        return HoveredTarget == other.HoveredTarget
            && FocusedTarget == other.FocusedTarget
            && PressedTarget == other.PressedTarget
            && CapturedTarget == other.CapturedTarget
            && LastHoverEnteredTarget == other.LastHoverEnteredTarget
            && LastHoverLeftTarget == other.LastHoverLeftTarget
            && HoverChangeCount == other.HoverChangeCount
            && IsPointerPressed == other.IsPointerPressed;
    }

    public override bool Equals(object? obj) => obj is OwnershipSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(HoveredTarget);
        hash.Add(FocusedTarget);
        hash.Add(PressedTarget);
        hash.Add(CapturedTarget);
        hash.Add(LastHoverEnteredTarget);
        hash.Add(LastHoverLeftTarget);
        hash.Add(HoverChangeCount);
        hash.Add(IsPointerPressed);
        return hash.ToHashCode();
    }

    public static bool operator ==(OwnershipSnapshot left, OwnershipSnapshot right) => left.Equals(right);

    public static bool operator !=(OwnershipSnapshot left, OwnershipSnapshot right) => !left.Equals(right);
}

/// <summary>
/// Tracks v0 input ownership for the Counter PoC.
/// This is intentionally scoped to a single pointer, left-button press/capture,
/// and keyboard focus; it does not model multi-touch, nested focus scopes,
/// platform capture APIs, or visual states.
/// </summary>
internal sealed partial class InputOwnershipState
{
    private bool _isPointerPressed;

    /// <summary>The current hit-test target under the pointer, updated by pointer move.</summary>
    public ActionId HoveredTarget { get; private set; }

    /// <summary>The target that receives focused keyboard activation.</summary>
    public ActionId FocusedTarget { get; private set; }

    /// <summary>The target that received the current left-button press, if any.</summary>
    public ActionId PressedTarget { get; private set; }

    /// <summary>The target that owns pointer release until the current press ends.</summary>
    public ActionId CapturedTarget { get; private set; }

    /// <summary>The last target entered by hover ownership.</summary>
    public ActionId LastHoverEnteredTarget { get; private set; }

    /// <summary>The last target left by hover ownership.</summary>
    public ActionId LastHoverLeftTarget { get; private set; }

    /// <summary>The number of hover target changes observed.</summary>
    public long HoverChangeCount { get; private set; }

    /// <summary>
    /// A single consistent read of the current ownership state.
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

    /// <summary>Updates hover ownership from the latest pointer location.</summary>
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
        RecordHoverChanged(previousTarget, nextTarget);
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
            RecordFocusChanged(previousFocus, target);
        }

        if (!wasPointerPressed || previousPressed != target || previousCaptured != target)
        {
            RecordPressedChanged(
                previousPressed,
                target,
                previousCaptured,
                target,
                isPointerPressed: true);
        }
    }

    public ActionId ReleasePointer<THitTestResolver>(RawInputEvent inputEvent, ref THitTestResolver hitTestResolver)
        where THitTestResolver : struct, IActionHitTestResolver
    {
        var previousPressed = PressedTarget;
        var previousCaptured = CapturedTarget;
        var wasPointerPressed = _isPointerPressed;
        var target = _isPointerPressed ? CapturedTarget : ActionId.None;
        _isPointerPressed = false;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;

        if (wasPointerPressed || !previousPressed.IsNone || !previousCaptured.IsNone)
        {
            RecordPressedChanged(
                previousPressed,
                ActionId.None,
                previousCaptured,
                ActionId.None,
                isPointerPressed: false);
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
            RecordHoverChanged(HoveredTarget, ActionId.None);
            LastHoverLeftTarget = HoveredTarget;
            LastHoverEnteredTarget = ActionId.None;
            HoverChangeCount++;
        }

        if (!FocusedTarget.IsNone)
        {
            RecordFocusChanged(FocusedTarget, ActionId.None);
        }

        if (_isPointerPressed || !PressedTarget.IsNone || !CapturedTarget.IsNone)
        {
            RecordPressedChanged(
                PressedTarget,
                ActionId.None,
                CapturedTarget,
                ActionId.None,
                isPointerPressed: false);
        }

        _isPointerPressed = false;
        HoveredTarget = ActionId.None;
        FocusedTarget = ActionId.None;
        PressedTarget = ActionId.None;
        CapturedTarget = ActionId.None;
    }

    partial void RecordHoverChanged(ActionId previousTarget, ActionId currentTarget);

    partial void RecordFocusChanged(ActionId previousTarget, ActionId currentTarget);

    partial void RecordPressedChanged(
        ActionId previousPressedTarget,
        ActionId currentPressedTarget,
        ActionId previousCapturedTarget,
        ActionId currentCapturedTarget,
        bool isPointerPressed);
}
