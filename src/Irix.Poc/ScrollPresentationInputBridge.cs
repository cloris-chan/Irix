using Irix.Rendering;

namespace Irix.Poc;

internal static class ScrollPresentationInputBridge
{
    public static bool TryResolveWheelRetarget(
        DrawingBackendCompositor compositor,
        NodeKey scrollTargetKey,
        ScrollState state,
        double pixelDelta,
        out ScrollPresentationInputDecision decision)
    {
        ArgumentNullException.ThrowIfNull(compositor);
        if (!compositor.TryGetPresentedScrollY(scrollTargetKey, out var presentedScrollY))
        {
            decision = default;
            return false;
        }

        var interrupt = ScrollController.ResolvePresentationInterrupt(
            state,
            presentedScrollY,
            new ScrollDelta(ScrollDeltaUnit.Pixel, pixelDelta),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);
        decision = new ScrollPresentationInputDecision(scrollTargetKey, interrupt);
        return true;
    }
}

internal readonly struct ScrollPresentationInputDecision(
    NodeKey TargetKey,
    ScrollPresentationInterruptDecision Interrupt) : IEquatable<ScrollPresentationInputDecision>
{
    public NodeKey TargetKey { get; } = TargetKey;
    public ScrollPresentationInterruptDecision Interrupt { get; } = Interrupt;

    public bool Equals(ScrollPresentationInputDecision other)
    {
        return TargetKey == other.TargetKey
            && Interrupt == other.Interrupt;
    }

    public override bool Equals(object? obj) => obj is ScrollPresentationInputDecision other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(TargetKey, Interrupt);

    public static bool operator ==(ScrollPresentationInputDecision left, ScrollPresentationInputDecision right) => left.Equals(right);

    public static bool operator !=(ScrollPresentationInputDecision left, ScrollPresentationInputDecision right) => !left.Equals(right);
}
