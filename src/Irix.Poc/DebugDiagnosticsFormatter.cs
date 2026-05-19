using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class DebugDiagnosticsFormatter
{
    internal static string FormatScrollDiagnosticRow(DebugUiDiagnosticsSnapshot snapshot)
    {
        var scroll = snapshot.Scroll;
        var maxScrollText = !scroll.HasMaxScrollY
            ? "unknown"
            : scroll.MaxScrollY == 0
                ? "0(known-zero)"
                : $"{scroll.MaxScrollY:F0}";

        return $"ScrollY: applied={scroll.AppliedScrollY} target={scroll.TargetPosition:F1} pos={scroll.Position:F2} max={maxScrollText} acc={scroll.Accumulator:F3} anim={scroll.IsAnimating} pendingPx={scroll.PendingPixels:F0} drained={scroll.DrainedPixels:F0} frames={scroll.DispatchedFrameCount} waitMs={scroll.RenderWaitMs:F1} dt={scroll.LastDt:F3} frameQueued={scroll.FrameQueued} tickLoop={scroll.TickLoopRunning}";
    }

    internal static string FormatInputDiagnosticRow(DebugUiDiagnosticsSnapshot snapshot)
    {
        var ownership = snapshot.InputOwnership;
        return $"Input: hover={FormatTarget(ownership.HoveredTarget)} focus={FormatTarget(ownership.FocusedTarget)} pressed={FormatTarget(ownership.PressedTarget)} capture={FormatTarget(ownership.CapturedTarget)} hoverChanges={ownership.HoverChangeCount}";
    }

    internal static string FormatClipModeDiagnosticRow(DebugUiDiagnosticsSnapshot snapshot)
    {
        return $"ClipMode: {snapshot.BackendClipMode}";
    }

    internal static string FormatViewportDiagnosticRow(DebugUiDiagnosticsSnapshot snapshot)
    {
        var viewport = snapshot.Viewport;
        return $"Viewport: renderer={FormatSize(viewport.RendererViewport)} layout={FormatSize(viewport.LayoutViewport)} scaleMode={viewport.ScaleMode}";
    }

    internal static string FormatLayoutDirtyDiagnosticRow(DebugUiDiagnosticsSnapshot snapshot)
    {
        var layout = snapshot.Layout;
        return $"LayoutDirty: layoutRebuildCount={layout.LayoutRebuildCount} LastLayoutRebuildReason={layout.LastLayoutRebuildReason} LastDirtyClassifications={FormatLayoutDirtyClassificationSummary(layout.LastDirtyClassifications)}";
    }

    private static string FormatTarget(ActionId target)
    {
        return target.IsNone ? "-" : ActionIdRegistry.GetName(target);
    }

    private static string FormatSize(PixelRectangle rectangle)
    {
        return $"{rectangle.Width}x{rectangle.Height}";
    }

    private static string FormatLayoutDirtyClassificationSummary(IReadOnlyList<LayoutDirtyClassification> classifications)
    {
        if (classifications.Count == 0)
        {
            return "(none)";
        }

        var parts = new string[classifications.Count];
        for (var i = 0; i < classifications.Count; i++)
        {
            var classification = classifications[i];
            parts[i] = $"{classification.DfsIndex}:{classification.Reason}";
        }

        return string.Join(",", parts);
    }
}
