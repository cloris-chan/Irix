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
            $"Device error reason: {snapshot.DeviceError}"
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
            $"Layout invalidation kind: {snapshot.LayoutInvalidationKind}",
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
            "coordinateSpace=PipelineLogicalPixels backendPhysicalPixels=True inputPhysicalMappedToLogical=True"
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
        return $"styleOnlyPlan {snapshot.Case} eligible={snapshot.Eligible} fallback={snapshot.FallbackReason} dirtyElementRanges={FormatRanges(snapshot.DirtyElementRanges)} dirtyCommandRanges={FormatRanges(snapshot.DirtyCommandRanges)} hitTargetCount={snapshot.HitTargetCount}";
    }

    internal static string[] BuildInputDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        var lines = new List<string>
        {
            "=== Input Ownership Diagnostics ===",
            "buttonPriorityOrder Pressed > Hovered > Focused > Normal"
        };

        foreach (var state in snapshot.ButtonStates)
        {
            if (state.Kind is InputDiagnosticButtonStateKind.AfterMove
                or InputDiagnosticButtonStateKind.AfterPress
                or InputDiagnosticButtonStateKind.DuringCaptureMove
                or InputDiagnosticButtonStateKind.ReleaseOutside
                or InputDiagnosticButtonStateKind.FocusLost)
            {
                continue;
            }

            lines.Add(FormatInputButtonStateLine(state));
        }

        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.AfterMove);
        AppendButtonState(lines, snapshot, InputDiagnosticButtonStateKind.AfterMove);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.AfterPress);
        AppendButtonState(lines, snapshot, InputDiagnosticButtonStateKind.AfterPress);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.DuringCaptureMove);
        AppendButtonState(lines, snapshot, InputDiagnosticButtonStateKind.DuringCaptureMove);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.ReleaseOutside);
        AppendButtonState(lines, snapshot, InputDiagnosticButtonStateKind.ReleaseOutside);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.KeyboardEnter);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.KeyboardSpace);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.PressEmpty);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.ReleaseAfterEmptyPress);
        AppendOwnershipStep(lines, snapshot, InputDiagnosticOwnershipStepKind.FocusLost);
        AppendButtonState(lines, snapshot, InputDiagnosticButtonStateKind.FocusLost);
        lines.Add("events:");
        foreach (var diagnosticEvent in snapshot.Events)
        {
            lines.Add($"  {FormatOwnershipEvent(diagnosticEvent)}");
        }

        lines.Add("dirtyReasons:");
        foreach (var dirtyReason in snapshot.DirtyReasons)
        {
            lines.Add($"dirtyReason {FormatDirtyReasonCase(dirtyReason.Case)} reason={dirtyReason.Reason} classifications={FormatLayoutDirtyClassifications(dirtyReason.Classifications)}");
        }

        lines.Add("=== Input diagnostic mode complete ===");
        return [.. lines];
    }

    internal static string[] BuildInputOwnershipDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        var lines = new string[snapshot.OwnershipSteps.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = FormatInputOwnershipStep(snapshot.OwnershipSteps[i]);
        }

        return lines;
    }

    internal static string[] BuildInputButtonStateDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        var lines = new string[snapshot.ButtonStates.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = FormatInputButtonStateLine(snapshot.ButtonStates[i]);
        }

        return lines;
    }

    internal static string[] BuildInputEventDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        var lines = new string[snapshot.Events.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            lines[i] = $"  {FormatOwnershipEvent(snapshot.Events[i])}";
        }

        return lines;
    }

    internal static string[] BuildInputDirtyReasonDiagnosticLines(InputDiagnosticsSnapshot snapshot)
    {
        var lines = new string[snapshot.DirtyReasons.Count];
        for (var i = 0; i < lines.Length; i++)
        {
            var dirtyReason = snapshot.DirtyReasons[i];
            lines[i] = $"dirtyReason {FormatDirtyReasonCase(dirtyReason.Case)} reason={dirtyReason.Reason} classifications={FormatLayoutDirtyClassifications(dirtyReason.Classifications)}";
        }

        return lines;
    }

    private static void AppendOwnershipStep(List<string> lines, InputDiagnosticsSnapshot snapshot, InputDiagnosticOwnershipStepKind kind)
    {
        foreach (var step in snapshot.OwnershipSteps)
        {
            if (step.Kind != kind)
            {
                continue;
            }

            lines.Add(FormatInputOwnershipStep(step));
            return;
        }
    }

    private static void AppendButtonState(List<string> lines, InputDiagnosticsSnapshot snapshot, InputDiagnosticButtonStateKind kind)
    {
        foreach (var state in snapshot.ButtonStates)
        {
            if (state.Kind != kind)
            {
                continue;
            }

            lines.Add(FormatInputButtonStateLine(state));
            return;
        }
    }

    private static string FormatInputButtonStateLine(InputDiagnosticButtonState state)
    {
        return $"buttonState {FormatButtonStateKind(state.Kind)} {FormatTarget(state.ActionId)} {FormatButtonState(state.State)}";
    }

    private static string FormatInputOwnershipStep(InputDiagnosticOwnershipStep step)
    {
        var prefix = FormatOwnershipStepKind(step.Kind);
        if (!step.HasMappedResult)
        {
            return $"{prefix} {FormatOwnership(step.Ownership)}";
        }

        var messageText = step.Message is null ? string.Empty : $" message={FormatMessage(step.Message)}";
        return $"{prefix} mapped={step.Mapped}{messageText} {FormatOwnership(step.Ownership)}";
    }

    private static string FormatButtonStateKind(InputDiagnosticButtonStateKind kind)
    {
        return kind switch
        {
            InputDiagnosticButtonStateKind.Normal => "normal",
            InputDiagnosticButtonStateKind.Hovered => "hovered",
            InputDiagnosticButtonStateKind.Pressed => "pressed",
            InputDiagnosticButtonStateKind.Focused => "focused",
            InputDiagnosticButtonStateKind.AfterMove => "afterMove",
            InputDiagnosticButtonStateKind.AfterPress => "afterPress",
            InputDiagnosticButtonStateKind.DuringCaptureMove => "duringCaptureMove",
            InputDiagnosticButtonStateKind.ReleaseOutside => "releaseOutside",
            InputDiagnosticButtonStateKind.FocusLost => "focusLost",
            _ => kind.ToString()
        };
    }

    private static string FormatOwnershipStepKind(InputDiagnosticOwnershipStepKind kind)
    {
        return kind switch
        {
            InputDiagnosticOwnershipStepKind.AfterMove => "afterMove",
            InputDiagnosticOwnershipStepKind.AfterPress => "afterPress",
            InputDiagnosticOwnershipStepKind.DuringCaptureMove => "duringCaptureMove",
            InputDiagnosticOwnershipStepKind.ReleaseOutside => "releaseOutside",
            InputDiagnosticOwnershipStepKind.KeyboardEnter => "keyboardEnter",
            InputDiagnosticOwnershipStepKind.KeyboardSpace => "keyboardSpace",
            InputDiagnosticOwnershipStepKind.PressEmpty => "pressEmpty",
            InputDiagnosticOwnershipStepKind.ReleaseAfterEmptyPress => "releaseAfterEmptyPress",
            InputDiagnosticOwnershipStepKind.FocusLost => "focusLost",
            _ => kind.ToString()
        };
    }

    private static string FormatDirtyReasonCase(InputDirtyReasonCase @case)
    {
        return @case switch
        {
            InputDirtyReasonCase.HoverOnly => "hoverOnly",
            InputDirtyReasonCase.Press => "press",
            InputDirtyReasonCase.Release => "release",
            _ => @case.ToString()
        };
    }

    internal static string[] BuildStylePresetDiagnosticLines(RenderStylePresetId presetId, RenderStylePreset preset)
    {
        var layout = preset.Layout;
        var drawing = preset.Drawing;
        var visualStates = preset.VisualStates;

        return [
            "=== Style Preset Diagnostics ===",
            $"stylePreset name={FormatStylePresetName(presetId)}",
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

        var parts = new string[classifications.Count];
        for (var i = 0; i < classifications.Count; i++)
        {
            var classification = classifications[i];
            parts[i] = $"{classification.DfsIndex}:{classification.Reason}/{classification.InvalidationKind}";
        }

        return string.Join(",", parts);
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
        return diagnosticEvent.Kind switch
        {
            InputOwnershipEventKind.HoverChanged =>
                $"HoverChanged previous={FormatTarget(diagnosticEvent.PreviousTarget)} current={FormatTarget(diagnosticEvent.CurrentTarget)}",
            InputOwnershipEventKind.FocusChanged =>
                $"FocusChanged previous={FormatTarget(diagnosticEvent.PreviousTarget)} current={FormatTarget(diagnosticEvent.CurrentTarget)}",
            InputOwnershipEventKind.PressedChanged =>
                $"PressedChanged previousPressed={FormatTarget(diagnosticEvent.PreviousPressedTarget)} currentPressed={FormatTarget(diagnosticEvent.CurrentPressedTarget)} previousCapture={FormatTarget(diagnosticEvent.PreviousCapturedTarget)} currentCapture={FormatTarget(diagnosticEvent.CurrentCapturedTarget)} pointerPressed={diagnosticEvent.IsPointerPressed}",
            _ => nameof(InputOwnershipEventKind.None)
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

    private static string FormatStylePresetName(RenderStylePresetId presetId)
    {
        return presetId == RenderStylePresetId.Default
            ? "RenderStylePreset.Default"
            : $"RenderStylePreset({presetId.Value})";
    }
}
