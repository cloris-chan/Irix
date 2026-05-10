using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ProgramDiagnosticsTests
{
    [Fact]
    public async Task Diagnose_scroll_outputs_scroll_pump_counters()
    {
        var writer = new StringWriter();

        await Program.RunScrollDiagnosticModeAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Scroll Pump Diagnostics ===", output);
        Assert.Contains("frames=2", output);
        Assert.Contains("waitMs=", output);
        Assert.Contains("dt=", output);
        Assert.Contains("drained=54.0", output);
        Assert.Contains("pending=0.0", output);
    }

    [Fact]
    public async Task Diagnose_input_outputs_ownership_state_transitions()
    {
        var writer = new StringWriter();

        await Program.RunInputDiagnosticModeAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Input Ownership Diagnostics ===", output);
        Assert.Contains("buttonPriorityOrder Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("buttonState normal Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", output);
        Assert.Contains("buttonState hovered Increment hovered=True pressed=False focused=True priority=Hovered color=#FF4888FF", output);
        Assert.Contains("buttonState pressed Increment hovered=True pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("buttonState focused Increment hovered=False pressed=False focused=True priority=Focused color=#FF54A0FF", output);
        Assert.Contains("afterMove hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False", output);
        Assert.Contains("buttonState afterMove Increment hovered=True pressed=False focused=False priority=Hovered color=#FF4888FF", output);
        Assert.Contains("afterPress hover=Increment focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("buttonState afterPress Increment hovered=True pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("duringCaptureMove hover=Decrement focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("buttonState duringCaptureMove Increment hovered=False pressed=True focused=True priority=Pressed color=#FF245CD2", output);
        Assert.Contains("releaseOutside mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("buttonState releaseOutside Increment hovered=False pressed=False focused=True priority=Focused color=#FF54A0FF", output);
        Assert.Contains("keyboardEnter mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("keyboardSpace mapped=True message=Increment hover=Decrement focus=Increment pressed=- capture=-", output);
        Assert.Contains("pressEmpty mapped=False hover=Decrement focus=- pressed=- capture=-", output);
        Assert.Contains("releaseAfterEmptyPress mapped=False", output);
        Assert.Contains("focusLost hover=- focus=- pressed=- capture=-", output);
        Assert.Contains("buttonState focusLost Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", output);
        Assert.Contains("HoverChanged previous=- current=Increment", output);
        Assert.Contains("FocusChanged previous=- current=Increment", output);
        Assert.Contains("PressedChanged previousPressed=- currentPressed=Increment", output);
        Assert.Contains("PressedChanged previousPressed=Increment currentPressed=-", output);
        Assert.Contains("FocusChanged previous=Increment current=-", output);
        Assert.Contains("dirtyReasons:", output);
        Assert.Contains("dirtyReason hoverOnly reason=StyleOnly classifications=4:StyleOnly", output);
        Assert.Contains("dirtyReason press reason=StyleOnly classifications=4:StyleOnly", output);
        Assert.Contains("dirtyReason release reason=TextSizeAffecting classifications=1:TextSizeAffecting,4:StyleOnly", output);
    }

    [Fact]
    public void Diagnose_style_preset_outputs_metrics_and_button_colors()
    {
        var output = string.Join(Environment.NewLine, Program.BuildStylePresetDiagnosticLines(RenderStylePreset.DefaultName, RenderStylePreset.Default));

        Assert.Contains("=== Style Preset Diagnostics ===", output);
        Assert.Contains("stylePreset name=RenderStylePreset.Default", output);
        Assert.Contains("layoutMetrics horizontalPadding=16 verticalPadding=16 itemSpacing=12 textHeight=32 buttonHeight=40 rectangleHeight=48 minimumButtonWidth=140 buttonTextWidthFactor=12 buttonHorizontalPadding=32", output);
        Assert.Contains("buttonStateColorPriority Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("buttonStateColor normal=#FF3478F6", output);
        Assert.Contains("buttonStateColor focused=#FF54A0FF", output);
        Assert.Contains("buttonStateColor hovered=#FF4888FF", output);
        Assert.Contains("buttonStateColor pressed=#FF245CD2", output);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_snapshot_captures_formatter_fields()
    {
        var plan = StyleOnlyPatchPlan.CreateEligible(
            [(0, 1)],
            [(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment", new PixelRectangle(0, 0, 960, 540))]);

        var snapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("hoverOnly", plan);

        Assert.Equal("hoverOnly", snapshot.CaseName);
        Assert.True(snapshot.Eligible);
        Assert.Equal(StyleOnlyPatchFallbackReason.None, snapshot.FallbackReason);
        Assert.Equal([(0, 1)], snapshot.DirtyElementRanges);
        Assert.Equal([(0, 2)], snapshot.DirtyCommandRanges);
        Assert.Equal(1, snapshot.HitTargetCount);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_formatter_outputs_stable_fields()
    {
        var plan = StyleOnlyPatchPlan.CreateEligible(
            [(0, 1)],
            [(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), "Increment", new PixelRectangle(0, 0, 960, 540))]);
        var snapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("hoverOnly", plan);

        var line = Program.BuildStyleOnlyPatchPlanDiagnosticLine(snapshot);

        Assert.Equal("styleOnlyPlan hoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", line);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_smoke_outputs_eligible_and_fallback()
    {
        var output = string.Join(Environment.NewLine, Program.BuildStyleOnlyPatchPlanSmokeDiagnosticLines());

        Assert.Contains("=== StyleOnly Patch Plan Diagnostics ===", output);
        Assert.Contains("styleOnlyPlan hoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", output);
        Assert.Contains("styleOnlyPlan layoutAffecting eligible=False fallback=NotStyleOnly dirtyElementRanges=0:1 dirtyCommandRanges=(none) hitTargetCount=0", output);
    }

    [Fact]
    public void Diagnose_clip_scissor_smoke_outputs_stable_fields()
    {
        var line = Program.BuildClipScissorSmokeDiagnosticLine(new DrawRect(32, 32, 80, 40), new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), true, 1, 0, 1, false);

        Assert.Equal("Scissor smoke: kind=FillRect clip=(32,32,80,40) effectiveClip=(32,32,80,40) nestedClip=False textClip=False gpuScissor=True clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_scissor_smoke_outputs_real_counter_fields()
    {
        var line = Program.BuildPipelineScissorSmokeDiagnosticLine(clippedCommandCount: 1, emptyIntersectionSkippedCount: 0, scissorStateChangeCount: 1, deviceRemoved: false);

        Assert.Equal("Pipeline scissor smoke: source=ScrollContainerRectangle textClip=False clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False passed=True", line);
    }

    [Fact]
    public void Diagnose_empty_scissor_smoke_outputs_skip_counter()
    {
        var line = Program.BuildEmptyScissorSmokeDiagnosticLine(clippedCommandCount: 1, emptyIntersectionSkippedCount: 1, scissorStateChangeCount: 0, deviceRemoved: false);

        Assert.Equal("Empty scissor smoke: kind=FillRect clippedCommands=1 emptyIntersectionSkipped=1 scissorStateChanges=0 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_text_clip_smoke_outputs_effective_clip_and_skip_counter()
    {
        var line = Program.BuildTextClipSmokeDiagnosticLine(new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), textClipSkippedCount: 1, deviceRemoved: false);

        Assert.Equal("Text clip smoke: kind=DrawTextRun textClip=True layoutClip=True effectiveClip=(32,32,80,40) textClipSkipped=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_text_clip_smoke_outputs_pipeline_fields()
    {
        var line = Program.BuildPipelineTextClipSmokeDiagnosticLine(new EffectiveScissor(new DrawRect(0, 0, 960, 20), false), clippedCommandCount: 2, textClipSkippedCount: 0, deviceRemoved: false);

        Assert.Equal("Pipeline text clip smoke: source=ScrollContainerButton textClip=True layoutClip=True effectiveClip=(0,0,960,20) clippedCommands=2 textClipSkipped=0 deviceRemoved=False passed=True", line);
    }

    [Fact]
    public void Diagnose_resize_viewport_snapshot_captures_source_of_truth_fields()
    {
        var snapshot = new Program.ViewportDiagnosticsSnapshot(
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            RenderCount: 80,
            LayoutRebuildCount: 80,
            LayoutRebuildReason: "ViewportChanged",
            ScreenScale: 1.25f,
            DpiAwareness: "ProcessDefault",
            ScaleMode: "PhysicalPixelsV0");

        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.WindowPhysicalBounds);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.RendererSwapchainBounds);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.TranslatorViewport);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.LayoutViewport);
        Assert.Equal(new PixelRectangle(10, 20, 929, 454), snapshot.LastAppliedPendingResize);
        Assert.Equal(80, snapshot.RenderCount);
        Assert.Equal(80, snapshot.LayoutRebuildCount);
        Assert.Equal("ViewportChanged", snapshot.LayoutRebuildReason);
        Assert.True(snapshot.ViewportMatchesRenderer);
        Assert.True(snapshot.LayoutUsesRendererSize);
        Assert.Equal(1.25f, snapshot.ScreenScale);
        Assert.Equal("ProcessDefault", snapshot.DpiAwareness);
        Assert.Equal("PhysicalPixelsV0", snapshot.ScaleMode);
    }

    [Fact]
    public void Diagnose_resize_viewport_outputs_source_of_truth_fields()
    {
        var snapshot = new Program.ViewportDiagnosticsSnapshot(
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            new PixelRectangle(10, 20, 929, 454),
            RenderCount: 80,
            LayoutRebuildCount: 80,
            LayoutRebuildReason: "ViewportChanged",
            ScreenScale: 1.25f,
            DpiAwareness: "ProcessDefault",
            ScaleMode: "PhysicalPixelsV0");

        var output = string.Join(Environment.NewLine, Program.BuildResizeViewportDiagnosticLines(snapshot));

        Assert.Contains("windowPhysicalSize=929x454", output);
        Assert.Contains("rendererSwapchainSize=929x454", output);
        Assert.Contains("translatorViewportSize=929x454", output);
        Assert.Contains("layoutViewportSize=929x454", output);
        Assert.Contains("lastAppliedPendingResize=929x454", output);
        Assert.Contains("renderCount=80", output);
        Assert.Contains("layoutRebuildCount=80", output);
        Assert.Contains("layoutRebuildReason=ViewportChanged", output);
        Assert.Contains("viewportMatchesRenderer=True", output);
        Assert.Contains("layoutUsesRendererSize=True", output);
        Assert.Contains("scaleMode=PhysicalPixelsV0", output);
        Assert.Contains("screenScale=1.25", output);
        Assert.Contains("dpiAwareness=ProcessDefault", output);
        Assert.Contains("coordinateSpace=PhysicalPixels logicalCoordinates=False", output);
    }

    [Fact]
    public void Debug_ui_outputs_viewport_diagnostic_row()
    {
        var app = new CounterApplication(
            showDiagnostics: true,
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                "PhysicalPixelsV0"),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, "0:LayoutAffecting,3:StyleOnly"));

        var tree = app.BuildView(app.Initialize());

        Assert.Contains(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && node.Content.Text == "Viewport: renderer=929x454 layout=929x454 scaleMode=PhysicalPixelsV0");
        Assert.Contains(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && node.Content.Text == "LayoutDirty: layoutRebuildCount=12 LastLayoutRebuildReason=LayoutAffecting LastDirtyClassifications=0:LayoutAffecting,3:StyleOnly");
    }
}