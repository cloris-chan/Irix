using Irix.Drawing;

namespace Irix.Poc;

internal interface IDebugDiagnosticsSnapshotBridge : IDiagnosticsProvider<DebugUiDiagnosticsSnapshot>
{
}

internal readonly record struct DebugUiDiagnosticsSnapshot(
    CounterViewportDiagnostics Viewport,
    CounterLayoutDiagnostics Layout,
    ScrollDiagnosticsSnapshot Scroll,
    OwnershipSnapshot InputOwnership,
    DrawingBackendClipMode BackendClipMode);

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