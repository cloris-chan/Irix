using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class ProgramDiagnosticsTests
{
    #region Scroll Snapshot

    [Fact]
    public async Task Diagnose_scroll_outputs_scroll_pump_counters()
    {
        var writer = new StringWriter();

        await ScrollDiagnosticRunner.RunAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

        var output = writer.ToString();
        Assert.Contains("=== Scroll Pump Diagnostics ===", output);
        Assert.Contains("frames=2", output);
        Assert.Contains("waitMs=", output);
        Assert.Contains("dt=", output);
        Assert.Contains("drained=54.0", output);
        Assert.Contains("pending=0.0", output);
    }

    [Fact]
    public void Diagnose_scroll_snapshot_captures_formatter_fields()
    {
        var snapshot = new ScrollDiagnosticsSnapshot(
            DispatchedFrameCount: 2,
            RenderWaitMs: 30.125,
            LastDt: 0.0376,
            DrainedPixels: 54,
            LastFrameDrainedPixels: 0,
            PendingPixels: 0,
            FrameQueued: false,
            TickLoopRunning: false,
            AppliedScrollY: 54,
            TargetPosition: 54,
            MaxScrollY: 240,
            HasMaxScrollY: true);

        Assert.Equal(2, snapshot.DispatchedFrameCount);
        Assert.Equal(30.125, snapshot.RenderWaitMs);
        Assert.Equal(0.0376, snapshot.LastDt);
        Assert.Equal(54, snapshot.DrainedPixels);
        Assert.Equal(0, snapshot.LastFrameDrainedPixels);
        Assert.Equal(0, snapshot.PendingPixels);
        Assert.False(snapshot.FrameQueued);
        Assert.False(snapshot.TickLoopRunning);
        Assert.Equal(54, snapshot.AppliedScrollY);
        Assert.Equal(54, snapshot.TargetPosition);
        Assert.Equal(240, snapshot.MaxScrollY);
        Assert.True(snapshot.HasMaxScrollY);
    }

    [Fact]
    public void Diagnose_scroll_formatter_outputs_stable_fields()
    {
        var snapshot = new ScrollDiagnosticsSnapshot(
            DispatchedFrameCount: 2,
            RenderWaitMs: 30.125,
            LastDt: 0.0376,
            DrainedPixels: 54,
            LastFrameDrainedPixels: 0,
            PendingPixels: 0,
            FrameQueued: false,
            TickLoopRunning: false,
            AppliedScrollY: 54,
            TargetPosition: 54,
            MaxScrollY: 240,
            HasMaxScrollY: true);

        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildScrollDiagnosticLines(snapshot));

        Assert.Equal(string.Join(Environment.NewLine, [
            "=== Scroll Pump Diagnostics ===",
            "frames=2",
            "waitMs=30.125",
            "dt=0.0376",
            "drained=54.0",
            "lastFrameDrained=0.0",
            "pending=0.0",
            "=== Scroll diagnostic mode complete ==="
        ]), output);
    }

    #endregion

    #region Input Snapshot

    [Fact]
    public async Task Diagnose_input_outputs_ownership_state_transitions()
    {
        var writer = new StringWriter();

        await InputDiagnosticRunner.RunAsync(writer, cancellationToken: TestContext.Current.CancellationToken);

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
        Assert.Contains("dirtyReason hoverOnly reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("dirtyReason press reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("dirtyReason release reason=TextSizeAffecting classifications=1:TextSizeAffecting/TextMeasure,4:StyleOnly/VisualOnly", output);
    }

    [Fact]
    public void Diagnose_input_snapshot_captures_formatter_fields()
    {
        var snapshot = InputDiagnosticRunner.BuildInputDiagnosticsSnapshot();

        Assert.True(snapshot.Ownership.HoveredTarget.IsNone);
        Assert.True(snapshot.Ownership.FocusedTarget.IsNone);
        Assert.True(snapshot.Ownership.PressedTarget.IsNone);
        Assert.True(snapshot.Ownership.CapturedTarget.IsNone);
        Assert.Equal(3, snapshot.Ownership.HoverChangeCount);
        Assert.False(snapshot.Ownership.IsPointerPressed);
        Assert.Contains("afterMove hover=Increment focus=- pressed=- capture=- hoverChanges=1 pointerPressed=False", snapshot.OwnershipLines);
        Assert.Contains(snapshot.OwnershipLines, line => line.StartsWith("keyboardEnter mapped=True message=Increment hover=Decrement focus=Increment", StringComparison.Ordinal));
        Assert.Contains("buttonState normal Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", snapshot.ButtonVisualStateLines);
        Assert.Contains("buttonState focusLost Increment hovered=False pressed=False focused=False priority=Normal color=#FF3478F6", snapshot.ButtonVisualStateLines);
        Assert.Contains("  HoverChanged previous=- current=Increment", snapshot.EventLines);
        Assert.Contains("  FocusChanged previous=Increment current=-", snapshot.EventLines);
        Assert.Contains("dirtyReason hoverOnly reason=StyleOnly classifications=4:StyleOnly/VisualOnly", snapshot.DirtyReasonLines);
        Assert.Contains("dirtyReason release reason=TextSizeAffecting classifications=1:TextSizeAffecting/TextMeasure,4:StyleOnly/VisualOnly", snapshot.DirtyReasonLines);
    }

    [Fact]
    public void Diagnose_input_formatter_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildInputDiagnosticLines(InputDiagnosticRunner.BuildInputDiagnosticsSnapshot()));

        Assert.Contains("=== Input Ownership Diagnostics ===", output);
        Assert.Contains("buttonPriorityOrder Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("afterPress hover=Increment focus=Increment pressed=Increment capture=Increment", output);
        Assert.Contains("events:", output);
        Assert.Contains("dirtyReasons:", output);
        Assert.Contains("dirtyReason press reason=StyleOnly classifications=4:StyleOnly/VisualOnly", output);
        Assert.Contains("=== Input diagnostic mode complete ===", output);
    }

    #endregion

    #region Style Preset Diagnostics

    [Fact]
    public void Diagnose_style_preset_outputs_metrics_and_button_colors()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildStylePresetDiagnosticLines(RenderStylePreset.DefaultName, RenderStylePreset.Default));

        Assert.Contains("=== Style Preset Diagnostics ===", output);
        Assert.Contains("stylePreset name=RenderStylePreset.Default", output);
        Assert.Contains("layoutMetrics horizontalPadding=16 verticalPadding=16 itemSpacing=12 textHeight=32 buttonHeight=40 rectangleHeight=48 minimumButtonWidth=140 buttonTextWidthFactor=12 buttonHorizontalPadding=32", output);
        Assert.Contains("buttonStateColorPriority Pressed > Hovered > Focused > Normal", output);
        Assert.Contains("buttonStateColor normal=#FF3478F6", output);
        Assert.Contains("buttonStateColor focused=#FF54A0FF", output);
        Assert.Contains("buttonStateColor hovered=#FF4888FF", output);
        Assert.Contains("buttonStateColor pressed=#FF245CD2", output);
    }

    #endregion

    #region StyleOnly Snapshot

    [Fact]
    public void Diagnose_style_only_patch_plan_snapshot_captures_formatter_fields()
    {
        var plan = StyleOnlyPatchPlan.CreateEligible(
            [(0, 1)],
            [(0, 2)],
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(1), new PixelRectangle(0, 0, 960, 540))]);

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
            [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(1), new PixelRectangle(0, 0, 960, 540))]);
        var snapshot = StyleOnlyPatchPlanDiagnosticSnapshot.FromPlan("hoverOnly", plan);

        var line = DiagnosticsFormatter.BuildStyleOnlyPatchPlanDiagnosticLine(snapshot);

        Assert.Equal("styleOnlyPlan hoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", line);
    }

    [Fact]
    public void Diagnose_style_only_patch_plan_smoke_outputs_eligible_and_fallback()
    {
        var output = string.Join(Environment.NewLine, StyleOnlyPatchPlanSmokeDiagnostics.BuildDiagnosticLines());

        Assert.Contains("=== StyleOnly Patch Plan Diagnostics ===", output);
        Assert.Contains("styleOnlyPlan hoverOnly eligible=True fallback=None dirtyElementRanges=0:1 dirtyCommandRanges=0:2 hitTargetCount=1", output);
        Assert.Contains("styleOnlyPlan layoutAffecting eligible=False fallback=NotStyleOnly dirtyElementRanges=0:1 dirtyCommandRanges=(none) hitTargetCount=0", output);
    }

    #endregion

    #region Backend Clip/Text Snapshot

    [Fact]
    public void Diagnose_backend_clip_text_snapshot_captures_formatter_fields()
    {
        var lastEffectiveScissor = new EffectiveScissor(new DrawRect(32, 32, 80, 40), false);
        var lastEffectiveTextClip = new EffectiveScissor(new DrawRect(0, 0, 960, 20), false);
        var snapshot = CreateBackendClipTextSnapshot(3, 1, 2, lastEffectiveScissor, lastEffectiveTextClip, textClipSkippedCount: 4, deviceRemoved: true, deviceErrorReason: "DeviceLost");

        Assert.Equal(DrawingBackendClipMode.Scissor, snapshot.ClipMode);
        Assert.Equal(3, snapshot.ClippedCommandCount);
        Assert.Equal(1, snapshot.EmptyIntersectionSkippedCount);
        Assert.Equal(2, snapshot.ScissorStateChangeCount);
        Assert.Equal(lastEffectiveScissor, snapshot.LastEffectiveScissor);
        Assert.Equal(lastEffectiveTextClip, snapshot.LastEffectiveTextClip);
        Assert.Equal(4, snapshot.TextClipSkippedCount);
        Assert.True(snapshot.DeviceRemoved);
        Assert.Equal("DeviceLost", snapshot.DeviceErrorReason);
        Assert.True(snapshot.GpuScissor);
    }

    [Fact]
    public void Diagnose_clip_scissor_smoke_outputs_stable_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 0, 1, new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildClipScissorSmokeDiagnosticLine(new DrawRect(32, 32, 80, 40), snapshot);

        Assert.Equal("Scissor smoke: kind=FillRect clip=(32,32,80,40) effectiveClip=(32,32,80,40) nestedClip=False textClip=False gpuScissor=True clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_scissor_smoke_outputs_real_counter_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 0, 1, EffectiveScissor.Empty, EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildPipelineScissorSmokeDiagnosticLine(snapshot);

        Assert.Equal("Pipeline scissor smoke: source=ScrollContainerRectangle textClip=False clippedCommands=1 emptyIntersectionSkipped=0 scissorStateChanges=1 deviceRemoved=False passed=True", line);
    }

    [Fact]
    public void Diagnose_empty_scissor_smoke_outputs_skip_counter()
    {
        var snapshot = CreateBackendClipTextSnapshot(1, 1, 0, EffectiveScissor.Empty, EffectiveScissor.Empty);

        var line = DiagnosticsFormatter.BuildEmptyScissorSmokeDiagnosticLine(snapshot);

        Assert.Equal("Empty scissor smoke: kind=FillRect clippedCommands=1 emptyIntersectionSkipped=1 scissorStateChanges=0 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_text_clip_smoke_outputs_effective_clip_and_skip_counter()
    {
        var snapshot = CreateBackendClipTextSnapshot(0, 0, 0, EffectiveScissor.Empty, new EffectiveScissor(new DrawRect(32, 32, 80, 40), false), textClipSkippedCount: 1);

        var line = DiagnosticsFormatter.BuildTextClipSmokeDiagnosticLine(snapshot);

        Assert.Equal("Text clip smoke: kind=DrawTextRun textClip=True layoutClip=True effectiveClip=(32,32,80,40) textClipSkipped=1 deviceRemoved=False", line);
    }

    [Fact]
    public void Diagnose_pipeline_text_clip_smoke_outputs_pipeline_fields()
    {
        var snapshot = CreateBackendClipTextSnapshot(2, 0, 1, EffectiveScissor.Empty, new EffectiveScissor(new DrawRect(0, 0, 960, 20), false));

        var line = DiagnosticsFormatter.BuildPipelineTextClipSmokeDiagnosticLine(snapshot);

        Assert.Equal("Pipeline text clip smoke: source=ScrollContainerButton textClip=True layoutClip=True effectiveClip=(0,0,960,20) clippedCommands=2 textClipSkipped=0 deviceRemoved=False passed=True", line);
    }

    #endregion

    #region Rendering Pipeline Snapshot

    [Fact]
    public void Diagnose_rendering_pipeline_snapshot_captures_minimal_fields()
    {
        var snapshot = CreateRenderingPipelineSnapshot();

        Assert.Equal([(0, 4)], snapshot.CompositorDirtyCommandRanges);
        Assert.Equal([(0, 4)], snapshot.BackendDirtyCommandRanges);
        Assert.True(snapshot.DirtyRangesAligned);
        Assert.Equal(0, snapshot.BackendClippedCommandCount);
        Assert.Equal(3, snapshot.LayoutCommandCount);
        Assert.Equal(3, snapshot.LayoutClippedCommandCount);
        Assert.Equal(1, snapshot.LayoutRebuildCount);
        Assert.Equal(LayoutRebuildReason.TreeStructure, snapshot.LayoutRebuildReason);
        Assert.Equal(InvalidationKind.TreeStructure, snapshot.LayoutInvalidationKind);
        Assert.Equal([new LayoutDirtyClassification(4, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)], snapshot.LayoutDirtyClassifications);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_snapshot_captures_additional_fields()
    {
        var snapshot = CreateRenderingPipelineSnapshot();

        Assert.Equal(3, snapshot.RenderCount);
        Assert.Equal(2, snapshot.PartialApplyCount);
        Assert.Equal(1, snapshot.FullApplyCount);
        Assert.Equal(0, snapshot.EmptyFrameCount);
        Assert.Equal(66.7, Math.Round(snapshot.PartialHitRate, 1));
        Assert.Single(snapshot.HitTargets);
        Assert.Equal(new ActionId(100), snapshot.HitTargets[0].ActionId);
        Assert.Single(snapshot.ScrollContainerDiagnostics);
        Assert.Equal(540, snapshot.ScrollContainerDiagnostics[0].VisibleHeight);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_compositor_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildRenderingPipelineCompositorDiagnosticLines(CreateRenderingPipelineSnapshot()));

        Assert.Equal(string.Join(Environment.NewLine, [
            "Render count: 3",
            "Partial apply: 2",
            "Full apply: 1",
            "Empty frames: 0",
            "Partial hit rate: 66.7%",
            "Compositor dirty ranges: 1 ranges",
            "  [0..3] (4 commands)",
            "Backend dirty ranges: 1 ranges",
            "  [0..3] (4 commands)",
            "Dirty ranges aligned: True",
            "Clipped commands: 0"
        ]), output);
    }

    [Fact]
    public void Diagnose_rendering_pipeline_layout_outputs_stable_fields()
    {
        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildRenderingPipelineLayoutDiagnosticLines(CreateRenderingPipelineSnapshot()));

        Assert.Equal(string.Join(Environment.NewLine, [
            "Layout commands: 3",
            "Layout clipped commands: 3",
            "Layout rebuild count: 1",
            "Layout rebuild reason: TreeStructure",
            "Layout invalidation kind: TreeStructure",
            "Layout dirty classifications: 4:StyleOnly/VisualOnly",
            "Layout hit targets: 1",
            "  Hit target: 100 bounds=(16,60,140,40) clip=(0,0,960,540)",
            "  ScrollContainer[0]: visible=540 content=96 scrollY=0 maxScrollY=0 elements=2/2 visible"
        ]), output);
    }

    #endregion

    #region Viewport Snapshot

    [Fact]
    public void Diagnose_resize_viewport_snapshot_captures_source_of_truth_fields()
    {
        var snapshot = new ViewportDiagnosticsSnapshot(
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
        var snapshot = new ViewportDiagnosticsSnapshot(
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

        var output = string.Join(Environment.NewLine, DiagnosticsFormatter.BuildResizeViewportDiagnosticLines(snapshot));

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
    public void Diagnose_resize_runner_report_outputs_stable_fields()
    {
        var snapshot = new ViewportDiagnosticsSnapshot(
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
        var writer = new StringWriter();

        ResizeDiagnosticRunner.WriteReport(writer, deviceRemoved: false, deviceErrorReason: null, swapchainWidth: 929, swapchainHeight: 454, snapshot);

        Assert.Equal(string.Join(Environment.NewLine, [
            "=== D3D12 Resize Diagnostics ===",
            "Device removed: False",
            "Device error reason: (none)",
            "Swapchain size: 929x454",
            "windowPhysicalSize=929x454",
            "rendererSwapchainSize=929x454",
            "translatorViewportSize=929x454",
            "layoutViewportSize=929x454",
            "lastAppliedPendingResize=929x454",
            "renderCount=80",
            "layoutRebuildCount=80",
            "layoutRebuildReason=ViewportChanged",
            "viewportMatchesRenderer=True",
            "layoutUsesRendererSize=True",
            "scaleMode=PhysicalPixelsV0",
            "screenScale=1.25",
            "dpiAwareness=ProcessDefault",
            "scale=0x0",
            "logicalViewport=0x0",
            "coordinateSpace=PhysicalPixels logicalCoordinates=False",
            "=== Resize diagnostic mode complete ===",
            string.Empty
        ]), writer.ToString());
    }

    #endregion

    #region Debug UI Bridge Baseline

    [Fact]
    public void Default_debug_bridge_captures_existing_debug_state()
    {
        var viewport = new CounterViewportDiagnostics(
            new PixelRectangle(0, 0, 929, 454),
            new PixelRectangle(0, 0, 929, 454),
            "PhysicalPixelsV0");
        var layout = new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, "0:LayoutAffecting,3:StyleOnly");
        var input = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 5,
            IsPointerPressed: false);
        var scroll = ScrollState.Default with
        {
            Accumulator = 0.375,
            Position = 42.4,
            TargetPosition = 48,
            IsAnimating = true,
            MaxScrollY = 240,
            HasMaxScrollY = true
        };

        var snapshot = new DefaultDebugDiagnosticsSnapshotBridge(viewport, layout, scroll, input).Capture();

        Assert.Equal(viewport, snapshot.Viewport);
        Assert.Equal(layout, snapshot.Layout);
        Assert.Equal(42, snapshot.Scroll.AppliedScrollY);
        Assert.Equal(42.4, snapshot.Scroll.Position);
        Assert.Equal(48, snapshot.Scroll.TargetPosition);
        Assert.Equal(0.375, snapshot.Scroll.Accumulator);
        Assert.True(snapshot.Scroll.IsAnimating);
        Assert.Equal(240, snapshot.Scroll.MaxScrollY);
        Assert.True(snapshot.Scroll.HasMaxScrollY);
        Assert.Equal(Program.DiagScrollDispatchedFrameCount, snapshot.Scroll.DispatchedFrameCount);
        Assert.Equal(Program.DiagScrollRenderWaitMs, snapshot.Scroll.RenderWaitMs);
        Assert.Equal(Program.DiagScrollLastDt, snapshot.Scroll.LastDt);
        Assert.Equal(Program.DiagScrollDrainedPixels, snapshot.Scroll.DrainedPixels);
        Assert.Equal(Program.DiagPendingPx, snapshot.Scroll.PendingPixels);
        Assert.Equal(Program.DiagScrollFrameQueued, snapshot.Scroll.FrameQueued);
        Assert.Equal(Program.DiagTickLoopRunning, snapshot.Scroll.TickLoopRunning);
        Assert.Equal(input, snapshot.InputOwnership);
        Assert.Equal(Program.DiagBackendClipMode, snapshot.BackendClipMode);
    }

    [Fact]
    public void Debug_diagnostics_formatter_outputs_stable_bridge_rows()
    {
        var snapshot = new DebugUiDiagnosticsSnapshot(
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                "PhysicalPixelsV0"),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, "0:LayoutAffecting,3:StyleOnly"),
            new ScrollDiagnosticsSnapshot(
                DispatchedFrameCount: 2,
                RenderWaitMs: 12.25,
                LastDt: 0.0167,
                DrainedPixels: 54,
                LastFrameDrainedPixels: 0,
                PendingPixels: 3,
                FrameQueued: true,
                TickLoopRunning: true,
                AppliedScrollY: 42,
                TargetPosition: 48,
                MaxScrollY: 240,
                HasMaxScrollY: true,
                Position: 42.4,
                Accumulator: 0.375,
                IsAnimating: true),
            new OwnershipSnapshot(
                HoveredTarget: new ActionId(1),
                FocusedTarget: new ActionId(1),
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: new ActionId(1),
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 5,
                IsPointerPressed: false),
            DrawingBackendClipMode.Diagnostic);

        Assert.Equal("Viewport: renderer=929x454 layout=929x454 scaleMode=PhysicalPixelsV0", DebugDiagnosticsFormatter.FormatViewportDiagnosticRow(snapshot));
        Assert.Equal("ScrollY: applied=42 target=48.0 pos=42.40 max=240 acc=0.375 anim=True pendingPx=3 drained=54 frames=2 waitMs=12.2 dt=0.017 frameQueued=True tickLoop=True", DebugDiagnosticsFormatter.FormatScrollDiagnosticRow(snapshot));
        Assert.Equal("ClipMode: Diagnostic", DebugDiagnosticsFormatter.FormatClipModeDiagnosticRow(snapshot));
        Assert.Equal("LayoutDirty: layoutRebuildCount=12 LastLayoutRebuildReason=LayoutAffecting LastDirtyClassifications=0:LayoutAffecting,3:StyleOnly", DebugDiagnosticsFormatter.FormatLayoutDirtyDiagnosticRow(snapshot));
        Assert.Equal("Input: hover=Increment focus=Increment pressed=- capture=- hoverChanges=5", DebugDiagnosticsFormatter.FormatInputDiagnosticRow(snapshot));
    }

    [Fact]
    public void Default_debug_bridge_exposes_provider_contract()
    {
        IDiagnosticsProvider<DebugUiDiagnosticsSnapshot> provider = new DefaultDebugDiagnosticsSnapshotBridge(
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                "PhysicalPixelsV0"),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, "0:LayoutAffecting,3:StyleOnly"),
            ScrollState.Default,
            default);

        var snapshot = provider.Capture();

        Assert.Equal("PhysicalPixelsV0", snapshot.Viewport.ScaleMode);
    }

    [Fact]
    public void Debug_ui_outputs_bridge_backed_diagnostic_rows()
    {
        var app = new CounterApplication(
            showDiagnostics: true,
            new CounterViewportDiagnostics(
                new PixelRectangle(0, 0, 929, 454),
                new PixelRectangle(0, 0, 929, 454),
                "PhysicalPixelsV0"),
            new CounterLayoutDiagnostics(12, LayoutRebuildReason.LayoutAffecting, "0:LayoutAffecting,3:StyleOnly"));
        var input = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 5,
            IsPointerPressed: false);
        var model = app.Initialize() with { InputOwnership = input };

        var tree = app.BuildView(model);

        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "Viewport: renderer=929x454 layout=929x454 scaleMode=PhysicalPixelsV0"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "ScrollY: applied=0 target=0.0 pos=0.00 max=unknown acc=0.000 anim=False pendingPx=0 drained=0 frames=0 waitMs=0.0 dt=0.000 frameQueued=False tickLoop=False"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "ClipMode: Diagnostic"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "LayoutDirty: layoutRebuildCount=12 LastLayoutRebuildReason=LayoutAffecting LastDirtyClassifications=0:LayoutAffecting,3:StyleOnly"));
        Assert.True(ContainsNode(tree.Root.Children, node =>
            node.Kind == VirtualNodeKind.Text
            && ResolveNodeText(app._arena, node.Content) == "Input: hover=Increment focus=Increment pressed=- capture=- hoverChanges=5"));
    }

    #endregion

    #region Test Helpers

    private static BackendClipTextDiagnosticSnapshot CreateBackendClipTextSnapshot(
        int clippedCommandCount,
        int emptyIntersectionSkippedCount,
        int scissorStateChangeCount,
        EffectiveScissor lastEffectiveScissor,
        EffectiveScissor lastEffectiveTextClip,
        int textClipSkippedCount = 0,
        bool deviceRemoved = false,
        string deviceErrorReason = "(none)")
    {
        return new BackendClipTextDiagnosticSnapshot(
            DrawingBackendClipMode.Scissor,
            clippedCommandCount,
            emptyIntersectionSkippedCount,
            scissorStateChangeCount,
            lastEffectiveScissor,
            lastEffectiveTextClip,
            textClipSkippedCount,
            deviceRemoved,
            deviceErrorReason);
    }

    private static RenderingPipelineDiagnosticSnapshot CreateRenderingPipelineSnapshot()
    {
        return new RenderingPipelineDiagnosticSnapshot(
            RenderCount: 3,
            PartialApplyCount: 2,
            FullApplyCount: 1,
            EmptyFrameCount: 0,
            CompositorDirtyCommandRanges: [(0, 4)],
            BackendDirtyCommandRanges: [(0, 4)],
            BackendClippedCommandCount: 0,
            LayoutCommandCount: 3,
            LayoutClippedCommandCount: 3,
            LayoutRebuildCount: 1,
            LayoutRebuildReason: LayoutRebuildReason.TreeStructure,
            LayoutInvalidationKind: InvalidationKind.TreeStructure,
            LayoutDirtyClassifications: [new LayoutDirtyClassification(4, LayoutRebuildReason.StyleOnly, InvalidationKind.VisualOnly)],
            HitTargets: [new HitTestTarget(new PixelRectangle(16, 60, 140, 40), new ActionId(100), new PixelRectangle(0, 0, 960, 540))],
            ScrollContainerDiagnostics: [new ScrollContainerDiag(0, 540, 96, 0, 0, 2, 0)]);
    }

    #endregion

    private static string ResolveNodeText(VirtualTextArena arena, NodeContent content) =>
        content.TryGetText(out var tc) ? arena.ResolveRequired(tc).ToString() : "";

    private static bool ContainsNode(ReadOnlySpan<VirtualNode> nodes, Func<VirtualNode, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
            {
                return true;
            }
        }

        return false;
    }
}
