using Irix.Platform;

namespace Irix.Poc;

internal sealed class InputOwnershipState
{
    public string? HoveredTarget { get; private set; }

    public string? FocusedTarget { get; private set; }

    public string? PressedTarget { get; private set; }

    public string? CapturedTarget { get; private set; }

    public string? LastHoverEnteredTarget { get; private set; }

    public string? LastHoverLeftTarget { get; private set; }

    public long HoverChangeCount { get; private set; }

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

    public void PressPointer(RawInputEvent inputEvent, Func<int, int, string?> tryGetActionIdAt)
    {
        var target = NormalizeTarget(tryGetActionIdAt(inputEvent.X, inputEvent.Y));
        PressedTarget = target;
        CapturedTarget = target;
        FocusedTarget = target;
    }

    public string? ReleasePointer(RawInputEvent inputEvent, Func<int, int, string?> tryGetActionIdAt)
    {
        var target = CapturedTarget ?? NormalizeTarget(tryGetActionIdAt(inputEvent.X, inputEvent.Y));
        PressedTarget = null;
        CapturedTarget = null;
        return target;
    }

    public string? GetKeyboardTarget() => FocusedTarget;

    public void Clear()
    {
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