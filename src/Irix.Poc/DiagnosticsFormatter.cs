using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class DiagnosticsFormatter
{
    internal static string[] BuildBackendDeviceDiagnosticLines(BackendClipTextDiagnosticSnapshot snapshot)
    {
        return [
            $"Device removed: {snapshot.DeviceRemoved}",
            $"Device error reason: {snapshot.DeviceErrorReason}"
        ];
    }

    internal static string BuildBackendClipModeDiagnosticLine(BackendClipTextDiagnosticSnapshot snapshot)
    {
        return $"Backend clip mode: {snapshot.ClipMode}";
    }

    internal static string BuildClipScissorSmokeDiagnosticLine(DrawRect clipBounds, BackendClipTextDiagnosticSnapshot snapshot)
    {
        return $"Scissor smoke: kind=FillRect clip={FormatRect(clipBounds)} effectiveClip={FormatEffectiveScissor(snapshot.LastEffectiveScissor)} nestedClip=False textClip=False gpuScissor={snapshot.GpuScissor} clippedCommands={snapshot.ClippedCommandCount} emptyIntersectionSkipped={snapshot.EmptyIntersectionSkippedCount} scissorStateChanges={snapshot.ScissorStateChangeCount} deviceRemoved={snapshot.DeviceRemoved}";
    }

    internal static string BuildPipelineScissorSmokeDiagnosticLine(BackendClipTextDiagnosticSnapshot snapshot)
    {
        var passed = snapshot.ClippedCommandCount > 0 && snapshot.ScissorStateChangeCount > 0;
        return $"Pipeline scissor smoke: source=ScrollContainerRectangle textClip=False clippedCommands={snapshot.ClippedCommandCount} emptyIntersectionSkipped={snapshot.EmptyIntersectionSkippedCount} scissorStateChanges={snapshot.ScissorStateChangeCount} deviceRemoved={snapshot.DeviceRemoved} passed={passed}";
    }

    internal static string BuildEmptyScissorSmokeDiagnosticLine(BackendClipTextDiagnosticSnapshot snapshot)
    {
        return $"Empty scissor smoke: kind=FillRect clippedCommands={snapshot.ClippedCommandCount} emptyIntersectionSkipped={snapshot.EmptyIntersectionSkippedCount} scissorStateChanges={snapshot.ScissorStateChangeCount} deviceRemoved={snapshot.DeviceRemoved}";
    }

    internal static string BuildTextClipSmokeDiagnosticLine(BackendClipTextDiagnosticSnapshot snapshot)
    {
        return $"Text clip smoke: kind=DrawTextRun textClip=True layoutClip=True effectiveClip={FormatEffectiveScissor(snapshot.LastEffectiveTextClip)} textClipSkipped={snapshot.TextClipSkippedCount} deviceRemoved={snapshot.DeviceRemoved}";
    }

    internal static string BuildPipelineTextClipSmokeDiagnosticLine(BackendClipTextDiagnosticSnapshot snapshot)
    {
        var passed = !snapshot.LastEffectiveTextClip.IsEmpty && snapshot.ClippedCommandCount > 0 && snapshot.TextClipSkippedCount == 0;
        return $"Pipeline text clip smoke: source=ScrollContainerButton textClip=True layoutClip=True effectiveClip={FormatEffectiveScissor(snapshot.LastEffectiveTextClip)} clippedCommands={snapshot.ClippedCommandCount} textClipSkipped={snapshot.TextClipSkippedCount} deviceRemoved={snapshot.DeviceRemoved} passed={passed}";
    }

    internal static string[] BuildRenderingPipelineCompositorDiagnosticLines(RenderingPipelineDiagnosticSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"Render count: {snapshot.RenderCount}",
            $"Partial apply: {snapshot.PartialApplyCount}",
            $"Full apply: {snapshot.FullApplyCount}",
            $"Empty frames: {snapshot.EmptyFrameCount}",
            $"Partial hit rate: {snapshot.PartialHitRate:F1}%"
        };
        AppendDirtyCommandRangeDiagnosticLines(lines, "Compositor", snapshot.CompositorDirtyCommandRanges);
        AppendDirtyCommandRangeDiagnosticLines(lines, "Backend", snapshot.BackendDirtyCommandRanges);
        lines.Add($"Dirty ranges aligned: {snapshot.DirtyRangesAligned}");
        lines.Add($"Clipped commands: {snapshot.BackendClippedCommandCount}");
        return [.. lines];
    }

    internal static string[] BuildRenderingPipelineLayoutDiagnosticLines(RenderingPipelineDiagnosticSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"Layout commands: {snapshot.LayoutCommandCount}",
            $"Layout clipped commands: {snapshot.LayoutClippedCommandCount}",
            $"Layout rebuild count: {snapshot.LayoutRebuildCount}",
            $"Layout rebuild reason: {snapshot.LayoutRebuildReason}",
            $"Layout dirty classifications: {FormatLayoutDirtyClassifications(snapshot.LayoutDirtyClassifications)}",
            $"Layout hit targets: {snapshot.HitTargets.Count}"
        };
        foreach (var hitTarget in snapshot.HitTargets)
        {
            lines.Add($"  Hit target: {hitTarget.ActionId.Value} bounds=({hitTarget.Bounds.X},{hitTarget.Bounds.Y},{hitTarget.Bounds.Width},{hitTarget.Bounds.Height}) clip=({hitTarget.ClipBounds.X},{hitTarget.ClipBounds.Y},{hitTarget.ClipBounds.Width},{hitTarget.ClipBounds.Height})");
        }
        foreach (var scrollDiagnostics in snapshot.ScrollContainerDiagnostics)
        {
            lines.Add($"  ScrollContainer[{scrollDiagnostics.DfsIndex}]: visible={scrollDiagnostics.VisibleHeight} content={scrollDiagnostics.ContentHeight} scrollY={scrollDiagnostics.ScrollY} maxScrollY={scrollDiagnostics.MaxScrollY} elements={scrollDiagnostics.VisibleElementCount}/{scrollDiagnostics.VisibleElementCount + scrollDiagnostics.ClippedElementCount} visible");
        }
        return [.. lines];
    }

    internal static string[] BuildResizeViewportDiagnosticLines(ViewportDiagnosticsSnapshot snapshot)
    {
        return [
            $"windowPhysicalSize={FormatSize(snapshot.WindowPhysicalBounds)}",
            $"rendererSwapchainSize={FormatSize(snapshot.RendererSwapchainBounds)}",
            $"translatorViewportSize={FormatSize(snapshot.TranslatorViewport)}",
            $"layoutViewportSize={FormatSize(snapshot.LayoutViewport)}",
            $"lastAppliedPendingResize={FormatSize(snapshot.LastAppliedPendingResize)}",
            $"renderCount={snapshot.RenderCount}",
            $"layoutRebuildCount={snapshot.LayoutRebuildCount}",
            $"layoutRebuildReason={snapshot.LayoutRebuildReason}",
            $"viewportMatchesRenderer={snapshot.ViewportMatchesRenderer}",
            $"layoutUsesRendererSize={snapshot.LayoutUsesRendererSize}",
            $"scaleMode={snapshot.ScaleMode}",
            $"screenScale={snapshot.ScreenScale:0.###}",
            $"dpiAwareness={snapshot.DpiAwareness}",
            $"scale={snapshot.Scale.ScaleX:0.##}x{snapshot.Scale.ScaleY:0.##}",
            $"logicalViewport={FormatSize(snapshot.LogicalViewport)}",
            "coordinateSpace=PhysicalPixels logicalCoordinates=False"
        ];
    }

    internal static string[] BuildScrollDiagnosticLines(ScrollDiagnosticsSnapshot snapshot)
    {
        return [
            "=== Scroll Pump Diagnostics ===",
            $"frames={snapshot.DispatchedFrameCount}",
            $"waitMs={snapshot.RenderWaitMs:F3}",
            $"dt={snapshot.LastDt:F4}",
            $"drained={snapshot.DrainedPixels:F1}",
            $"lastFrameDrained={snapshot.LastFrameDrainedPixels:F1}",
            $"pending={snapshot.PendingPixels:F1}",
            "=== Scroll diagnostic mode complete ==="
        ];
    }

    internal static string BuildStyleOnlyPatchPlanDiagnosticLine(StyleOnlyPatchPlanDiagnosticSnapshot snapshot)
    {
        return $"styleOnlyPlan {snapshot.CaseName} eligible={snapshot.Eligible} fallback={snapshot.FallbackReason} dirtyElementRanges={FormatRanges(snapshot.DirtyElementRanges)} dirtyCommandRanges={FormatRanges(snapshot.DirtyCommandRanges)} hitTargetCount={snapshot.HitTargetCount}";
    }

    internal static string[] BuildInputDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        return [
            "=== Input Ownership Diagnostics ===",
            .. snapshot.OrderedDiagnosticLines,
            "=== Input diagnostic mode complete ==="
        ];
    }

    internal static string[] BuildStylePresetDiagnosticLines(string presetName, RenderStylePreset preset)
    {
        var layout = preset.Layout;
        var drawing = preset.Drawing;
        var visualStates = preset.VisualStates;

        return [
            "=== Style Preset Diagnostics ===",
            $"stylePreset name={presetName}",
            $"layoutMetrics horizontalPadding={layout.HorizontalPadding} verticalPadding={layout.VerticalPadding} itemSpacing={layout.ItemSpacing} textHeight={layout.TextHeight} buttonHeight={layout.ButtonHeight} rectangleHeight={layout.RectangleHeight} minimumButtonWidth={layout.MinimumButtonWidth} buttonTextWidthFactor={layout.ButtonTextWidthFactor} buttonHorizontalPadding={layout.ButtonHorizontalPadding}",
            "buttonStateColorPriority Pressed > Hovered > Focused > Normal",
            $"buttonStateColor normal={FormatColor(visualStates.ResolveButtonFillColor(drawing, default))}",
            $"buttonStateColor focused={FormatColor(visualStates.ResolveButtonFillColor(drawing, new ButtonVisualState(IsHovered: false, IsPressed: false, IsFocused: true)))}",
            $"buttonStateColor hovered={FormatColor(visualStates.ResolveButtonFillColor(drawing, new ButtonVisualState(IsHovered: true, IsPressed: false, IsFocused: true)))}",
            $"buttonStateColor pressed={FormatColor(visualStates.ResolveButtonFillColor(drawing, new ButtonVisualState(IsHovered: true, IsPressed: true, IsFocused: true)))}"
        ];
    }

    internal static string FormatLayoutDirtyClassifications(IReadOnlyList<LayoutDirtyClassification> classifications)
    {
        if (classifications.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", classifications.Select(classification => $"{classification.DfsIndex}:{classification.Reason}"));
    }

    internal static string FormatOwnership(OwnershipSnapshot snapshot)
    {
        return $"hover={FormatTarget(snapshot.HoveredTarget)} focus={FormatTarget(snapshot.FocusedTarget)} pressed={FormatTarget(snapshot.PressedTarget)} capture={FormatTarget(snapshot.CapturedTarget)} hoverChanges={snapshot.HoverChangeCount} pointerPressed={snapshot.IsPointerPressed}";
    }

    internal static string FormatMessage(CounterMessage? message)
    {
        return message?.GetType().Name ?? "-";
    }

    internal static string FormatButtonState(ButtonVisualState state)
    {
        var stylePreset = CounterStylePreset.Default;
        var color = stylePreset.VisualStates.ResolveButtonFillColor(stylePreset.Drawing, state);
        return $"hovered={state.IsHovered} pressed={state.IsPressed} focused={state.IsFocused} priority={FormatButtonStatePriority(state)} color={FormatColor(color)}";
    }

    internal static string FormatOwnershipEvent(InputOwnershipEvent diagnosticEvent)
    {
        return diagnosticEvent switch
        {
            InputOwnershipEvent.HoverChanged hover =>
                $"HoverChanged previous={FormatTarget(hover.PreviousTarget)} current={FormatTarget(hover.CurrentTarget)}",
            InputOwnershipEvent.FocusChanged focus =>
                $"FocusChanged previous={FormatTarget(focus.PreviousTarget)} current={FormatTarget(focus.CurrentTarget)}",
            InputOwnershipEvent.PressedChanged pressed =>
                $"PressedChanged previousPressed={FormatTarget(pressed.PreviousPressedTarget)} currentPressed={FormatTarget(pressed.CurrentPressedTarget)} previousCapture={FormatTarget(pressed.PreviousCapturedTarget)} currentCapture={FormatTarget(pressed.CurrentCapturedTarget)} pointerPressed={pressed.IsPointerPressed}",
            _ => diagnosticEvent.GetType().Name
        };
    }

    private static void AppendDirtyCommandRangeDiagnosticLines(List<string> lines, string label, IReadOnlyList<(int Start, int Count)> ranges)
    {
        lines.Add($"{label} dirty ranges: {ranges.Count} ranges");
        foreach (var (start, count) in ranges)
        {
            lines.Add($"  [{start}..{start + count - 1}] ({count} commands)");
        }
    }

    private static string FormatEffectiveScissor(EffectiveScissor scissor)
    {
        return scissor.IsEmpty ? "empty" : FormatRect(scissor.Bounds);
    }

    private static string FormatRect(DrawRect rect)
    {
        return $"({rect.X:0.##},{rect.Y:0.##},{rect.Width:0.##},{rect.Height:0.##})";
    }

    private static string FormatSize(PixelRectangle rectangle)
    {
        return $"{rectangle.Width}x{rectangle.Height}";
    }

    private static string FormatRanges(IReadOnlyList<(int Start, int Count)> ranges)
    {
        if (ranges.Count == 0)
        {
            return "(none)";
        }

        return string.Join(",", ranges.Select(range => $"{range.Start}:{range.Count}"));
    }

    private static string FormatTarget(ActionId target)
    {
        return target.IsNone ? "-" : ActionIdRegistry.GetName(target);
    }

    private static string FormatButtonStatePriority(ButtonVisualState state)
    {
        if (state.IsPressed)
        {
            return "Pressed";
        }

        if (state.IsHovered)
        {
            return "Hovered";
        }

        return state.IsFocused ? "Focused" : "Normal";
    }

    private static string FormatColor(DrawColor color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
