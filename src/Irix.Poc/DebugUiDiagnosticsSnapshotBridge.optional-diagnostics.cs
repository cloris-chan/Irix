#if IRIX_DIAGNOSTICS
using Irix.Drawing;

namespace Irix.Poc;

internal interface IDebugDiagnosticsSnapshotBridge : IDiagnosticsProvider<DebugUiDiagnosticsSnapshot>
{
}

internal readonly struct DebugUiDiagnosticsSnapshot(
    CounterViewportDiagnostics Viewport,
    CounterLayoutDiagnostics Layout,
    ScrollDiagnosticsSnapshot Scroll,
    OwnershipSnapshot InputOwnership,
    DrawingBackendClipMode BackendClipMode) : IEquatable<DebugUiDiagnosticsSnapshot>
{
    public CounterViewportDiagnostics Viewport { get; } = Viewport;
    public CounterLayoutDiagnostics Layout { get; } = Layout;
    public ScrollDiagnosticsSnapshot Scroll { get; } = Scroll;
    public OwnershipSnapshot InputOwnership { get; } = InputOwnership;
    public DrawingBackendClipMode BackendClipMode { get; } = BackendClipMode;

    public bool Equals(DebugUiDiagnosticsSnapshot other)
    {
        return Viewport == other.Viewport
            && Layout == other.Layout
            && Scroll == other.Scroll
            && InputOwnership == other.InputOwnership
            && BackendClipMode == other.BackendClipMode;
    }

    public override bool Equals(object? obj) => obj is DebugUiDiagnosticsSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Viewport, Layout, Scroll, InputOwnership, BackendClipMode);

    public static bool operator ==(DebugUiDiagnosticsSnapshot left, DebugUiDiagnosticsSnapshot right) => left.Equals(right);

    public static bool operator !=(DebugUiDiagnosticsSnapshot left, DebugUiDiagnosticsSnapshot right) => !left.Equals(right);
}

internal sealed class DefaultDebugDiagnosticsSnapshotBridge(
    CounterViewportDiagnostics viewport,
    CounterLayoutDiagnostics layout,
    ScrollState scroll,
    OwnershipSnapshot inputOwnership) : IDebugDiagnosticsSnapshotBridge
{
    public DebugUiDiagnosticsSnapshot Capture()
    {
        var scrollSnapshot = new ScrollDiagnosticsSnapshot(
            Program.DiagScrollDispatchedFrameCount,
            Program.DiagScrollRenderWaitMs,
            Program.DiagScrollLastDt,
            Program.DiagScrollDrainedPixels,
            Program.DiagScrollDrainedPixels,
            Program.DiagPendingPx,
            Program.DiagScrollFrameQueued,
            Program.DiagTickLoopRunning,
            ScrollController.GetScrollY(scroll),
            scroll.TargetPosition,
            scroll.MaxScrollY,
            scroll.HasMaxScrollY,
            scroll.Position,
            scroll.Accumulator,
            scroll.IsAnimating);

        return new DebugUiDiagnosticsSnapshot(
            viewport,
            layout,
            scrollSnapshot,
            inputOwnership,
            Program.DiagBackendClipMode);
    }
}
#endif
