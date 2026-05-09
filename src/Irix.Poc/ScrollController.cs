namespace Irix.Poc;

/// <summary>
/// Pure-function scroll controller that accumulates raw wheel deltas,
/// converts to target scroll position, and smoothly animates toward it.
/// All state is immutable — each method returns a new ScrollState.
/// </summary>
internal static class ScrollController
{
    /// <summary>
    /// Raw wheel units per whole notch (Windows WHEEL_DELTA).
    /// High-precision touchpads may send fractional notches (e.g., 30 = ¼ notch).
    /// </summary>
    private const float WheelUnitsPerNotch = 120f;

    /// <summary>
    /// Pixels of scroll per whole notch.
    /// </summary>
    private const float PixelsPerNotch = 40f;

    /// <summary>
    /// Easing factor per second. Higher = faster convergence.
    /// At 60fps, dt ≈ 16.7ms, factor ≈ 1 - e^(-12 * 0.0167) ≈ 0.18 per frame.
    /// </summary>
    private const float EaseSpeed = 12f;

    /// <summary>
    /// Threshold in pixels below which animation snaps to target and stops.
    /// </summary>
    private const float SnapThreshold = 0.5f;

    /// <summary>
    /// Apply a raw wheel delta to the scroll state.
    /// Accumulates small deltas until a whole pixel is reached.
    /// Returns a new state with updated accumulator and target position.
    /// </summary>
    public static ScrollState ApplyWheel(ScrollState state, int rawDelta)
    {
        // Convert raw delta to subpixel scroll amount
        // Convention: positive rawDelta → positive scroll position (scroll down)
        var subpixelDelta = rawDelta / WheelUnitsPerNotch * PixelsPerNotch;
        var newAccumulator = state.Accumulator + subpixelDelta;

        // Extract whole pixels from accumulator
        var wholePixels = (int)newAccumulator;
        var remainingAccumulator = newAccumulator - wholePixels;

        if (wholePixels == 0)
        {
            // Not enough accumulated yet — just save the accumulator
            return state with { Accumulator = remainingAccumulator };
        }

        var newTarget = Math.Max(state.TargetPosition + wholePixels, 0);
        return state with
        {
            TargetPosition = newTarget,
            Accumulator = remainingAccumulator,
            IsAnimating = true,
        };
    }

    /// <summary>
    /// Advance the scroll animation by one frame.
    /// Moves Position toward TargetPosition using exponential easing.
    /// Returns a new state; IsAnimating becomes false when snap threshold is reached.
    /// </summary>
    public static ScrollState Tick(ScrollState state, float dt)
    {
        if (!state.IsAnimating)
        {
            return state;
        }

        var diff = state.TargetPosition - state.Position;

        if (Math.Abs(diff) < SnapThreshold)
        {
            // Snap to target and stop
            return state with
            {
                Position = state.TargetPosition,
                IsAnimating = false,
            };
        }

        // Exponential ease: position += diff * (1 - e^(-speed * dt))
        var factor = 1f - MathF.Exp(-EaseSpeed * dt);
        var newPosition = state.Position + diff * factor;

        return state with { Position = newPosition };
    }

    /// <summary>
    /// Get the integer scroll offset for layout (rounded Position).
    /// </summary>
    public static int GetScrollY(ScrollState state) => (int)Math.Round(state.Position);
}

/// <summary>
/// Immutable scroll state for a single scroll container.
/// </summary>
internal readonly record struct ScrollState
{
    /// <summary>Subpixel accumulator for raw wheel deltas. Drains when a whole pixel is reached.</summary>
    public float Accumulator { get; init; }

    /// <summary>The target scroll position in pixels (integer goal).</summary>
    public int TargetPosition { get; init; }

    /// <summary>The current animated scroll position in pixels (subpixel, smooth).</summary>
    public float Position { get; init; }

    /// <summary>Whether the scroll animation is active (Position converging toward TargetPosition).</summary>
    public bool IsAnimating { get; init; }

    /// <summary>Default state: no scroll, no animation.</summary>
    public static ScrollState Default => default;
}
