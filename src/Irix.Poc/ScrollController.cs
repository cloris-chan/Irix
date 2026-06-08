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

    /// <summary>Maximum scroll position from the last layout pass.</summary>
    public double MaxScrollY { get; init; }

    /// <summary>
    /// Whether <see cref="MaxScrollY"/> has been reported by the layout pass.
    /// When false, the target position is not clamped — the layout max is unknown.
    /// </summary>
    public bool HasMaxScrollY { get; init; }

    public static ScrollState Default => default;
}

internal enum ScrollPresentationInterruptPolicy : byte
{
    CommitPresented,
    CancelToLogicalTarget,
    RetargetFromPresentedToLogicalTarget
}

internal readonly record struct ScrollPresentationInterruptDecision(
    ScrollPresentationInterruptPolicy Policy,
    ScrollState NextState,
    double PresentedScrollY,
    double LogicalTargetScrollY,
    double AppliedDeltaPixels,
    bool DispatchesLayoutFrame);

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
        state = NormalizeState(state);
        var pixels = ConvertToPixels(delta, metrics, settings);
        return ApplyPixelDelta(state, pixels);
    }

    // ── pixel accumulator ────────────────────────────────────────────

    private static ScrollState ApplyPixelDelta(ScrollState state, double pixelDelta)
    {
        pixelDelta = NormalizeDelta(pixelDelta);
        if (pixelDelta == 0)
        {
            return state with { Accumulator = ClampBoundaryAccumulator(state, state.Accumulator) };
        }

        var newAccumulator = state.Accumulator + pixelDelta;
        if (!double.IsFinite(newAccumulator))
        {
            return state with { Accumulator = 0 };
        }

        var wholePixels = Math.Truncate(newAccumulator);
        var remainder = newAccumulator - wholePixels;

        if (wholePixels == 0)
        {
            return state with { Accumulator = ClampBoundaryAccumulator(state, newAccumulator) };
        }

        var unclampedTarget = state.TargetPosition + wholePixels;
        var newTarget = ClampToKnownRange(unclampedTarget, state);
        var accumulator = newTarget == state.TargetPosition && IsBoundaryOverscroll(unclampedTarget, state) ? 0 : remainder;

        return state with
        {
            TargetPosition = newTarget,
            Accumulator = accumulator,
            IsAnimating = newTarget != state.Position,
        };
    }

    // ── unit conversion ──────────────────────────────────────────────

    internal static double ConvertToPixels(
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings)
    {
        var value = NormalizeDelta(delta.Value);
        var lineExtent = NormalizePositive(metrics.LineExtent, 18);
        var viewportExtent = NormalizePositive(metrics.ViewportExtent, 0);
        var pageExtent = IsPositiveFinite(metrics.PageExtent)
            ? metrics.PageExtent
            : (viewportExtent > 0 ? viewportExtent * 0.9 : lineExtent * 10);
        var wheelUnitsPerNotch = settings.WheelUnitsPerNotch > 0
            ? settings.WheelUnitsPerNotch
            : SystemScrollSettings.Default.WheelUnitsPerNotch;
        var linesPerWheelNotch = settings.LinesPerWheelNotch > 0
            ? settings.LinesPerWheelNotch
            : SystemScrollSettings.Default.LinesPerWheelNotch;

        var pixels = delta.Unit switch
        {
            ScrollDeltaUnit.Line => value * lineExtent,
            ScrollDeltaUnit.Pixel => value,
            ScrollDeltaUnit.Page => value * pageExtent,
            ScrollDeltaUnit.WheelRaw => -value
                / wheelUnitsPerNotch
                * linesPerWheelNotch
                * lineExtent,
            _ => 0,
        };
        return NormalizeDelta(pixels);
    }

    // ── animation tick ───────────────────────────────────────────────

    /// <summary>
    /// Advance the smooth scroll by one frame.
    /// </summary>
    public static ScrollState Tick(ScrollState state, double dt)
    {
        state = NormalizeState(state);
        if (!state.IsAnimating)
        {
            return state;
        }

        dt = NormalizeDeltaTime(dt);
        if (dt == 0)
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
        var nextPosition = state.Position + diff * factor;
        if (!double.IsFinite(nextPosition))
        {
            nextPosition = state.TargetPosition;
        }

        nextPosition = ClampToKnownRange(nextPosition, state);
        return state with { Position = nextPosition, IsAnimating = nextPosition != state.TargetPosition };
    }

    // ── layout helper ────────────────────────────────────────────────

    /// <summary>Integer scroll offset for layout.</summary>
    public static int GetScrollY(ScrollState state)
    {
        state = NormalizeState(state);
        var rounded = Math.Round(state.Position);
        return rounded >= int.MaxValue ? int.MaxValue : (int)rounded;
    }

    /// <summary>
    /// Update MaxScrollY from the layout pass. Also re-clamps TargetPosition.
    /// </summary>
    public static ScrollState WithMaxScrollY(ScrollState state, double maxScrollY)
    {
        state = NormalizeState(state, clampToKnownRange: false);
        maxScrollY = NormalizeMaxScrollY(maxScrollY);

        // HasMaxScrollY=true means maxScrollY is a known value from layout.
        // When maxScrollY=0, content fits in viewport — clamp to 0.
        var clampedTarget = Math.Clamp(state.TargetPosition, 0, maxScrollY);
        var clampedPos = Math.Clamp(state.Position, 0, maxScrollY);
        return state with
        {
            MaxScrollY = maxScrollY,
            HasMaxScrollY = true,
            TargetPosition = clampedTarget,
            Position = clampedPos,
            IsAnimating = clampedTarget != clampedPos && state.IsAnimating,
        };
    }

    public static ScrollPresentationInterruptDecision ResolvePresentationInterrupt(
        ScrollState state,
        double presentedScrollY,
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings,
        ScrollPresentationInterruptPolicy policy)
    {
        state = NormalizeState(state);
        var normalizedPresented = ClampToKnownRange(presentedScrollY, state);
        return policy switch
        {
            ScrollPresentationInterruptPolicy.CommitPresented => ResolveCommitPresented(state, normalizedPresented),
            ScrollPresentationInterruptPolicy.CancelToLogicalTarget => ResolveCancelToLogicalTarget(state),
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget => ResolveRetargetFromPresentedToLogicalTarget(state, normalizedPresented, delta, metrics, settings),
            _ => throw new ArgumentOutOfRangeException(nameof(policy))
        };
    }

    public static ScrollState CommitPresented(ScrollState state, double presentedScrollY)
    {
        state = NormalizeState(state);
        var committed = ClampToKnownRange(presentedScrollY, state);
        return state with
        {
            Accumulator = 0,
            TargetPosition = committed,
            Position = committed,
            IsAnimating = false,
        };
    }

    public static ScrollState CancelPresentation(ScrollState state)
    {
        state = NormalizeState(state);
        var target = ClampToKnownRange(state.TargetPosition, state);
        return state with
        {
            Accumulator = 0,
            TargetPosition = target,
            Position = target,
            IsAnimating = false,
        };
    }

    public static ScrollState RetargetFromPresentedToLogicalTarget(
        ScrollState state,
        double presentedScrollY,
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings)
    {
        state = NormalizeState(state);
        var normalizedPresented = ClampToKnownRange(presentedScrollY, state);
        var targetState = ApplyScrollDelta(state, delta, metrics, settings);
        return targetState with
        {
            Position = normalizedPresented,
            IsAnimating = targetState.TargetPosition != normalizedPresented,
        };
    }

    private static ScrollPresentationInterruptDecision ResolveCommitPresented(
        ScrollState state,
        double presentedScrollY)
    {
        var next = CommitPresented(state, presentedScrollY);
        return new ScrollPresentationInterruptDecision(
            ScrollPresentationInterruptPolicy.CommitPresented,
            next,
            presentedScrollY,
            next.TargetPosition,
            0,
            DispatchesLayoutFrame: true);
    }

    private static ScrollPresentationInterruptDecision ResolveCancelToLogicalTarget(ScrollState state)
    {
        var next = CancelPresentation(state);
        return new ScrollPresentationInterruptDecision(
            ScrollPresentationInterruptPolicy.CancelToLogicalTarget,
            next,
            next.Position,
            next.TargetPosition,
            0,
            DispatchesLayoutFrame: true);
    }

    private static ScrollPresentationInterruptDecision ResolveRetargetFromPresentedToLogicalTarget(
        ScrollState state,
        double presentedScrollY,
        ScrollDelta delta,
        in ScrollMetrics metrics,
        in SystemScrollSettings settings)
    {
        var pixels = ConvertToPixels(delta, metrics, settings);
        var next = RetargetFromPresentedToLogicalTarget(state, presentedScrollY, delta, metrics, settings);
        return new ScrollPresentationInterruptDecision(
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget,
            next,
            presentedScrollY,
            next.TargetPosition,
            pixels,
            DispatchesLayoutFrame: true);
    }

    private static double ClampToKnownRange(double value, ScrollState state)
    {
        var finite = NormalizeScrollCoordinate(value);
        var maxScrollY = NormalizeMaxScrollY(state.MaxScrollY);
        return state.HasMaxScrollY ? Math.Clamp(finite, 0, maxScrollY) : finite;
    }

    private static double NormalizeMaxScrollY(double maxScrollY)
    {
        return maxScrollY >= 0 && double.IsFinite(maxScrollY) ? maxScrollY : 0;
    }

    private static ScrollState NormalizeState(ScrollState state, bool clampToKnownRange = true)
    {
        var maxScrollY = NormalizeMaxScrollY(state.MaxScrollY);
        var target = NormalizeScrollCoordinate(state.TargetPosition);
        var position = NormalizeScrollCoordinate(state.Position);
        if (clampToKnownRange && state.HasMaxScrollY)
        {
            target = Math.Clamp(target, 0, maxScrollY);
            position = Math.Clamp(position, 0, maxScrollY);
        }

        return state with
        {
            Accumulator = double.IsFinite(state.Accumulator) ? state.Accumulator : 0,
            TargetPosition = target,
            Position = position,
            MaxScrollY = maxScrollY,
            IsAnimating = state.IsAnimating && target != position,
        };
    }

    private static double NormalizeScrollCoordinate(double value)
    {
        return value >= 0 && double.IsFinite(value) ? value : 0;
    }

    private static double NormalizeDelta(double value)
    {
        return double.IsFinite(value) ? value : 0;
    }

    private static double NormalizeDeltaTime(double value)
    {
        return value > 0 && double.IsFinite(value) ? value : 0;
    }

    private static double NormalizePositive(double value, double fallback)
    {
        return IsPositiveFinite(value) ? value : fallback;
    }

    private static bool IsPositiveFinite(double value)
    {
        return value > 0 && double.IsFinite(value);
    }

    private static double ClampBoundaryAccumulator(ScrollState state, double accumulator)
    {
        if ((state.TargetPosition <= 0 && accumulator < 0)
            || (state.HasMaxScrollY && state.TargetPosition >= state.MaxScrollY && accumulator > 0))
        {
            return 0;
        }

        return accumulator;
    }

    private static bool IsBoundaryOverscroll(double unclampedTarget, ScrollState state)
    {
        return unclampedTarget < 0
            || (state.HasMaxScrollY && unclampedTarget > state.MaxScrollY);
    }
}
