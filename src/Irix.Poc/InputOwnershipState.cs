using Irix.Platform;

namespace Irix.Poc;

/// <summary>
/// Immutable read model for the PoC input ownership state.
/// </summary>
internal readonly record struct OwnershipSnapshot(
    string? HoveredTarget,
    string? FocusedTarget,
    string? PressedTarget,
    string? CapturedTarget,
    string? LastHoverEnteredTarget,
    string? LastHoverLeftTarget,
    long HoverChangeCount,
    bool IsPointerPressed);

/// <summary>
/// Tracks v0 input ownership for the Counter PoC.
/// This is intentionally scoped to a single pointer, left-button press/capture,
/// and diagnostic ownership state; it does not model multi-touch, nested focus
/// scopes, platform capture APIs, or visual states.
/// </summary>
internal sealed class InputOwnershipState
{
    private bool _isPointerPressed;

    /// <summary>The current hit-test target under the pointer, updated by pointer move.</summary>
    public string? HoveredTarget { get; private set; }

    /// <summary>The target that receives focused keyboard activation.</summary>
    public string? FocusedTarget { get; private set; }

    /// <summary>The target that received the current left-button press, if any.</summary>
    public string? PressedTarget { get; private set; }

    /// <summary>The target that owns pointer release until the current press ends.</summary>
    public string? CapturedTarget { get; private set; }

    /// <summary>The last target entered by hover diagnostics.</summary>
    public string? LastHoverEnteredTarget { get; private set; }

    /// <summary>The last target left by hover diagnostics.</summary>
    public string? LastHoverLeftTarget { get; private set; }

    /// <summary>The number of hover target changes observed.</summary>
    public long HoverChangeCount { get; private set; }

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
    public void UpdateHover(RawInputEvent inputEvent, Func<int, int, string?> tryGetActionIdAt)
    {
        var previousTarget = HoveredTarget;
        var nextTarget = NormalizeTarget(tryGetActionIdAt(inputEvent.X, inputEvent.Y));
        if (previousTarget == nextTarget)
        {
            return;
        }

        HoveredTarget = nextTarget;
        LastHoverLeftTarget = previousTarget;
        LastHoverEnteredTarget = nextTarget;
        HoverChangeCount++;
    }

    /// <summary>
    /// Starts left-button ownership. A hit target becomes pressed, captured, and focused;
    /// an empty press clears focus and prevents the matching release from activating a target.
    /// </summary>
    public void PressPointer(RawInputEvent inputEvent, Func<int, int, string?> tryGetActionIdAt)
    {
        var target = NormalizeTarget(tryGetActionIdAt(inputEvent.X, inputEvent.Y));
        _isPointerPressed = true;
        PressedTarget = target;
        CapturedTarget = target;
        FocusedTarget = target;
    }

    /// <summary>
    /// Ends left-button ownership and returns the action target, if any. Captured target wins;
    /// a release without a prior stateful press falls back to release-point hit testing for
    /// compatibility with the legacy stateless router overload.
    /// </summary>
    public string? ReleasePointer(RawInputEvent inputEvent, Func<int, int, string?> tryGetActionIdAt)
    {
        var target = _isPointerPressed
            ? CapturedTarget
            : NormalizeTarget(tryGetActionIdAt(inputEvent.X, inputEvent.Y));
        _isPointerPressed = false;
        PressedTarget = null;
        CapturedTarget = null;
        return target;
    }

    /// <summary>Returns the target that should receive focused keyboard activation.</summary>
    public string? GetKeyboardTarget() => FocusedTarget;

    /// <summary>Clears all ownership state, used when the native window loses focus.</summary>
    public void Clear()
    {
        _isPointerPressed = false;
        HoveredTarget = null;
        FocusedTarget = null;
        PressedTarget = null;
        CapturedTarget = null;
        LastHoverEnteredTarget = null;
        LastHoverLeftTarget = null;
    }

    private static string? NormalizeTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target) ? null : target;
    }
}