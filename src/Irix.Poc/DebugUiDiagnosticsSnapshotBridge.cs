using Irix.Drawing;
using Irix.Rendering;

namespace Irix.Poc;

internal interface IDebugDiagnosticsSnapshotBridge
{
    DebugUiDiagnosticsSnapshot Capture();
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
    ScrollState scroll) : IDebugDiagnosticsSnapshotBridge
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
            scroll.HasMaxScrollY);

        return new DebugUiDiagnosticsSnapshot(
            viewport,
            layout,
            scrollSnapshot,
            Program.DiagInputOwnership,
            Program.DiagBackendClipMode);
    }
}