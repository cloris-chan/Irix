namespace Irix.Poc;

// ─── Scroll delta message ────────────────────────────────────────────

/// <summary>
/// Structured scroll delta. The router emits one of these; the controller
/// converts to pixels using <see cref="ScrollMetrics"/> and
/// <see cref="SystemScrollSettings"/>.
/// </summary>
internal enum ScrollDeltaUnit { Line, Pixel, Page, WheelRaw }

internal readonly record struct ScrollDelta(ScrollDeltaUnit Unit, double Value);

// ─── Metrics (per container, per frame) ──────────────────────────────

/// <summary>
/// Geometry and content extents for a single scroll container.
/// The controller uses these to convert logical scroll units to pixels.
/// </summary>
internal readonly record struct ScrollMetrics(
    double LineExtent,
    double PageExtent,
    double ViewportExtent,
    double ContentExtent)
{
    /// <summary>Default line height for text-like content.</summary>
    public static readonly ScrollMetrics DefaultText = new(
        LineExtent: 18,
        PageExtent: 0, // computed from viewport at call site if zero
        ViewportExtent: 0,
        ContentExtent: 0);
}

// ─── System settings ─────────────────────────────────────────────────

/// <summary>
/// Platform scroll settings. PoC uses defaults; Windows platform can
/// later read SPI_GETWHEELSCROLLLINES / SPI_GETWHEELSCROLLCHARS.
/// </summary>
internal readonly record struct SystemScrollSettings(
    int LinesPerWheelNotch,
    int WheelUnitsPerNotch)
{
    public static readonly SystemScrollSettings Default = new(
        LinesPerWheelNotch: 3,
        WheelUnitsPerNotch: 120);
}

// ─── Scroll state ────────────────────────────────────────────────────

/// <summary>
/// Immutable scroll state. All values in <b>double</b> precision.
/// </summary>
internal readonly record struct ScrollState
{
    /// <summary>Sub-pixel accumulator for raw wheel deltas.</summary>
    public double Accumulator { get; init; }

    /// <summary>Target scroll position in pixels (integer goal).</summary>
    public double TargetPosition { get; init; }

    /// <summary>Current animated scroll position in pixels.</summary>
    public double Position { get; init; }

    /// <summary>Whether the smooth animation is active.</summary>
    public bool IsAnimating { get; init; }

    /// <summary>Maximum scroll position from the last layout pass. 0 = no scroll.</summary>
    public double MaxScrollY { get; init; }

    public static ScrollState Default => default;
}

// ─── Controller ──────────────────────────────────────────────────────

/// <summary>
/// Pure-function scroll controller.
/// Every method is a pure transformation: (state, input) → newState.
/// </summary>
internal static class ScrollController
{
    private const double EaseSpeed = 12.0;
    private const double SnapThreshold = 0.5;

    // ── delta → target ───────────────────────────────────────────────

    /// <summary>
    /// Apply a structured <see cref="ScrollDelta"/> to the scroll state,
    /// converting to pixels via <paramref name="metrics"/> and
    /// <paramref name="settings"/>.
    /// </summary>
    public static ScrollState ApplyScrollDelta(
        ScrollState state,
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings)
    {
        var pixels = ConvertToPixels(delta, metrics, settings);
        return ApplyPixelDelta(state, pixels);
    }

    /// <summary>
    /// Backward-compatible overload: apply a raw wheel delta using default settings
    /// and default text metrics (LineExtent = 18px).
    /// </summary>
    public static ScrollState ApplyWheel(ScrollState state, int rawDelta)
    {
        return ApplyScrollDelta(
            state,
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, rawDelta),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
    }

    // ── pixel accumulator ────────────────────────────────────────────

    private static ScrollState ApplyPixelDelta(ScrollState state, double pixelDelta)
    {
        var newAccumulator = state.Accumulator + pixelDelta;
        var wholePixels = Math.Truncate(newAccumulator);
        var remainder = newAccumulator - wholePixels;

        if (wholePixels == 0)
        {
            return state with { Accumulator = remainder };
        }

        var newTarget = state.TargetPosition + wholePixels;
        // Clamp to [0, MaxScrollY] — MaxScrollY=0 means no scroll limit yet
        if (state.MaxScrollY > 0)
        {
            newTarget = Math.Clamp(newTarget, 0, state.MaxScrollY);
        }
        else
        {
            newTarget = Math.Max(newTarget, 0);
        }

        return state with
        {
            TargetPosition = newTarget,
            Accumulator = remainder,
            IsAnimating = true,
        };
    }

    // ── unit conversion ──────────────────────────────────────────────

    internal static double ConvertToPixels(
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings)
    {
        var lineExtent = metrics.LineExtent > 0 ? metrics.LineExtent : 18;
        var pageExtent = metrics.PageExtent > 0
            ? metrics.PageExtent
            : (metrics.ViewportExtent > 0 ? metrics.ViewportExtent * 0.9 : lineExtent * 10);

        return delta.Unit switch
        {
            ScrollDeltaUnit.Line => delta.Value * lineExtent,
            ScrollDeltaUnit.Pixel => delta.Value,
            ScrollDeltaUnit.Page => delta.Value * pageExtent,
            ScrollDeltaUnit.WheelRaw => delta.Value
                / settings.WheelUnitsPerNotch
                * settings.LinesPerWheelNotch
                * lineExtent,
            _ => 0,
        };
    }

    // ── animation tick ───────────────────────────────────────────────

    /// <summary>
    /// Advance the smooth scroll by one frame.
    /// </summary>
    public static ScrollState Tick(ScrollState state, double dt)
    {
        if (!state.IsAnimating)
        {
            return state;
        }

        var diff = state.TargetPosition - state.Position;

        if (Math.Abs(diff) < SnapThreshold)
        {
            return state with
            {
                Position = state.TargetPosition,
                IsAnimating = false,
            };
        }

        var factor = 1.0 - Math.Exp(-EaseSpeed * dt);
        return state with { Position = state.Position + diff * factor };
    }

    // ── layout helper ────────────────────────────────────────────────

    /// <summary>Integer scroll offset for layout.</summary>
    public static int GetScrollY(ScrollState state) => (int)Math.Round(state.Position);

    /// <summary>
    /// Update MaxScrollY from the layout pass. Also re-clamps TargetPosition.
    /// </summary>
    public static ScrollState WithMaxScrollY(ScrollState state, double maxScrollY)
    {
        var clampedTarget = maxScrollY > 0
            ? Math.Clamp(state.TargetPosition, 0, maxScrollY)
            : Math.Max(state.TargetPosition, 0);
        var clampedPos = maxScrollY > 0
            ? Math.Clamp(state.Position, 0, maxScrollY)
            : Math.Max(state.Position, 0);
        return state with
        {
            MaxScrollY = maxScrollY,
            TargetPosition = clampedTarget,
            Position = clampedPos,
            IsAnimating = clampedTarget != clampedPos && state.IsAnimating,
        };
    }
}
