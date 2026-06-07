using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class D3D12DrawingBackendScissorTests
{
    private static readonly DrawRect Viewport = new(0, 0, 200, 160);
    private static readonly DrawRect ClipA = new(16, 16, 80, 40);
    private static readonly DrawRect ClipB = new(32, 24, 80, 40);

    [Fact]
    public void ResolveFillRectScissor_scissor_mode_skips_empty_intersection()
    {
        var viewport = new DrawRect(0, 0, 100, 80);
        var clip = new DrawRect(120, 20, 30, 30);

        var plan = D3D12DrawingBackend.ResolveFillRectScissor(DrawingBackendClipMode.Scissor, viewport, clip);

        Assert.True(plan.Skip);
        Assert.True(plan.EffectiveScissor.IsEmpty);
        Assert.True(plan.RenderScissor.IsEmpty);
    }

    [Fact]
    public void ResolveFillRectScissor_diagnostic_mode_uses_full_viewport_scissor()
    {
        var viewport = new DrawRect(0, 0, 100, 80);
        var clip = new DrawRect(32, 16, 20, 10);

        var plan = D3D12DrawingBackend.ResolveFillRectScissor(DrawingBackendClipMode.Diagnostic, viewport, clip);

        Assert.False(plan.Skip);
        Assert.Equal(new DrawRect(32, 16, 20, 10), plan.EffectiveScissor.Bounds);
        Assert.Equal(new IntegerScissorRect(0, 0, 100, 80), plan.RenderScissor);
    }

    [Fact]
    public void ComputeFillRectScissorDiagnostics_counts_one_state_change_for_consecutive_same_scissor()
    {
        var diagnostics = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(DrawingBackendClipMode.Scissor, Viewport,
        [
            Fill(ClipA),
            Fill(ClipA)
        ]);

        Assert.Equal(2, diagnostics.ClippedCommandCount);
        Assert.Equal(0, diagnostics.EmptyIntersectionSkippedCount);
        Assert.Equal(1, diagnostics.ScissorStateChangeCount);
    }

    [Fact]
    public void ComputeFillRectScissorDiagnostics_counts_switch_for_different_scissor()
    {
        var diagnostics = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(DrawingBackendClipMode.Scissor, Viewport,
        [
            Fill(ClipA),
            Fill(ClipB)
        ]);

        Assert.Equal(2, diagnostics.ClippedCommandCount);
        Assert.Equal(0, diagnostics.EmptyIntersectionSkippedCount);
        Assert.Equal(2, diagnostics.ScissorStateChangeCount);
    }

    [Fact]
    public void ComputeFillRectScissorDiagnostics_counts_nonconsecutive_same_scissor_as_new_change()
    {
        var diagnostics = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(DrawingBackendClipMode.Scissor, Viewport,
        [
            Fill(ClipA),
            Fill(ClipB),
            Fill(ClipA)
        ]);

        Assert.Equal(3, diagnostics.ClippedCommandCount);
        Assert.Equal(0, diagnostics.EmptyIntersectionSkippedCount);
        Assert.Equal(3, diagnostics.ScissorStateChangeCount);
    }

    [Fact]
    public void ComputeFillRectScissorDiagnostics_keeps_diagnostic_and_scissor_modes_distinct()
    {
        var commands = new[] { Fill(ClipA) };

        var diagnostic = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(DrawingBackendClipMode.Diagnostic, Viewport, commands);
        var scissor = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(DrawingBackendClipMode.Scissor, Viewport, commands);

        Assert.Equal(1, diagnostic.ClippedCommandCount);
        Assert.Equal(0, diagnostic.ScissorStateChangeCount);
        Assert.Equal(1, scissor.ClippedCommandCount);
        Assert.True(scissor.ScissorStateChangeCount > 0);
    }

    [Fact]
    public void ComputeFillRectScissorDiagnostics_scales_logical_clip_bounds_on_the_fly()
    {
        var diagnostics = D3D12DrawingBackend.ComputeFillRectScissorDiagnostics(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 300, 240),
            [Fill(ClipA)],
            new DisplayScale(1.5f, 1.5f));

        Assert.Equal(1, diagnostics.ClippedCommandCount);
        Assert.Equal(1, diagnostics.ScissorStateChangeCount);
        Assert.Equal(new DrawRect(24, 24, 120, 60), diagnostics.LastEffectiveScissor.Bounds);
    }

    [Fact]
    public void ResolveTextClip_scissor_mode_skips_empty_intersection()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, Viewport, new DrawRect(240, 24, 80, 40));

        Assert.False(plan.ClipEnabled);
        Assert.True(plan.Skip);
        Assert.True(plan.EffectiveClip.IsEmpty);
    }

    [Fact]
    public void ResolveTextClip_scissor_mode_enables_clip_for_partial_intersection()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, Viewport, ClipA);

        Assert.True(plan.ClipEnabled);
        Assert.False(plan.Skip);
        Assert.Equal(ClipA, plan.EffectiveClip.Bounds);
    }

    [Fact]
    public void ResolveTextClip_scissor_mode_default_clip_uses_viewport_without_d2d_scope()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, Viewport, default);

        Assert.False(plan.ClipEnabled);
        Assert.False(plan.Skip);
        Assert.Equal(Viewport, plan.EffectiveClip.Bounds);
    }

    [Fact]
    public void ResolveTextClip_scissor_mode_full_viewport_clip_uses_original_text_path()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, Viewport, Viewport);

        Assert.False(plan.ClipEnabled);
        Assert.False(plan.Skip);
        Assert.Equal(Viewport, plan.EffectiveClip.Bounds);
    }

    [Fact]
    public void ResolveTextClip_diagnostic_mode_resolves_but_does_not_enable_clip()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Diagnostic, Viewport, ClipA);

        Assert.False(plan.ClipEnabled);
        Assert.False(plan.Skip);
        Assert.Equal(ClipA, plan.EffectiveClip.Bounds);
    }

    [Fact]
    public void ComputeTextClipDiagnostics_counts_empty_skip_and_keeps_last_effective_clip()
    {
        var diagnostics = D3D12DrawingBackend.ComputeTextClipDiagnostics(DrawingBackendClipMode.Scissor, Viewport,
        [
            Text(new DrawRect(240, 24, 80, 40)),
            Text(ClipA)
        ]);

        Assert.Equal(1, diagnostics.TextClipSkippedCount);
        Assert.Equal(ClipA, diagnostics.LastEffectiveTextClip.Bounds);
    }

    [Fact]
    public void ComputeTextClipDiagnostics_scales_logical_clip_bounds_on_the_fly()
    {
        var diagnostics = D3D12DrawingBackend.ComputeTextClipDiagnostics(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 300, 240),
            [Text(ClipA)],
            new DisplayScale(1.5f, 1.5f));

        Assert.Equal(0, diagnostics.TextClipSkippedCount);
        Assert.Equal(new DrawRect(24, 24, 120, 60), diagnostics.LastEffectiveTextClip.Bounds);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void ExecuteCore_does_not_allocate_command_array_for_scaled_commands(float scaleValue)
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), ClipBounds: ClipA, Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(100, 0, 100, 50), ClipBounds: ClipB, Color: DrawColor.Opaque(4, 5, 6))
        };
        WarmExecuteCore(commands, resources, rects, texts, scaleValue);

        rects.Reset();
        texts.Reset();
        var allocated = MeasureAllocatedBytes(() =>
        {
            _ = D3D12DrawingBackend.ExecuteCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, 400, 320),
                commands,
                resources,
                new DisplayScale(scaleValue, scaleValue),
                rects,
                texts);
        });

        Assert.Equal(0, allocated);
        Assert.Equal(commands.Length, rects.Count);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_applies_layer_translation_and_opacity_on_d3d12_path()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("composition");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 80), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), ClipBounds: new DrawRect(10, 10, 80, 60), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(20, 24, 120, 28), Resource: style, Text: text, ClipBounds: new DrawRect(10, 10, 160, 60), Color: DrawColor.Opaque(240, 240, 240))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(12, 8),
            new CompositionOpacity(0.5f)));

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.True(diagnostics.D3D12Backed);
        Assert.Equal(1, diagnostics.LayerCount);
        Assert.Equal(3, diagnostics.CommandCount);
        Assert.Equal(1, diagnostics.LayerCommandStart);
        Assert.Equal(2, diagnostics.LayerCommandCount);
        Assert.Equal(2, diagnostics.TranslatedCommands);
        Assert.Equal(2, diagnostics.OpacityAppliedCommands);
        Assert.Equal(new CompositionTransform(12, 8), diagnostics.AppliedTransform);
        Assert.Equal(0.5f, diagnostics.AppliedOpacity.Normalized);
        Assert.Equal(2, rects.Count);
        Assert.Equal(1, texts.Count);
        Assert.InRange(commands[1].CanonicalColor.A, 0.999f, 1.001f);
        Assert.Equal(DrawColor.Opaque(100, 120, 140), commands[1].ToSdrColor());

        var transformedRect = rects.Span[1];
        Assert.Equal(28, transformedRect.X);
        Assert.Equal(28, transformedRect.Y);
        Assert.Equal(128f / 255f, transformedRect.A);
        Assert.Equal(new IntegerScissorRect(22, 18, 102, 78), transformedRect.Scissor);
        Assert.Equal(new DrawRect(22, 18, 160, 60), diagnostics.ExecuteResult.TextClipDiagnostics.LastEffectiveTextClip.Bounds);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_reuses_layer_content_cache_for_stable_disjoint_layer()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("cached layer");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), ClipBounds: new DrawRect(10, 10, 160, 80), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(20, 24, 120, 28), Resource: style, Text: text, ClipBounds: new DrawRect(10, 10, 160, 80), Color: DrawColor.Opaque(240, 240, 240))
        };
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(12, 8),
            new CompositionOpacity(0.5f)));

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            firstFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        Assert.Equal(2, first.CachedLayerCommands);
        Assert.Equal(2, rects.Count);
        Assert.Equal(1, texts.Count);
        Assert.Equal(28, rects.Span[1].X);
        Assert.Equal(28, rects.Span[1].Y);
        Assert.Equal(128f / 255f, rects.Span[1].A);

        rects.Reset();
        texts.Reset();
        var secondFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(20, 10),
            new CompositionOpacity(0.25f)));

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            secondFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(1, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        Assert.Equal(2, second.CachedLayerCommands);
        Assert.Equal(36, rects.Span[1].X);
        Assert.Equal(30, rects.Span[1].Y);
        Assert.Equal(64f / 255f, rects.Span[1].A);
        Assert.Equal(new DrawRect(30, 20, 160, 80), second.ExecuteResult.TextClipDiagnostics.LastEffectiveTextClip.Bounds);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_rasterizes_internal_linear_gradient_material_through_layer_cache()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var gradient = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(100, 0));
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            DrawCommand.FromMaterial(
                DrawCommandKind.FillRect,
                Rect: new DrawRect(16, 20, 40, 24),
                ClipBounds: new DrawRect(10, 10, 160, 80),
                Material: gradient)
        };
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(9),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(12, 8),
            new CompositionOpacity(0.5f)));

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            firstFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        Assert.Equal(2, rects.Count);
        var firstGradientRect = rects.Span[1];
        Assert.Equal(28, firstGradientRect.X);
        Assert.Equal(28, firstGradientRect.Y);
        Assert.Equal(40, firstGradientRect.Width);
        Assert.Equal(24, firstGradientRect.Height);
        AssertRectGradientColors(firstGradientRect, gradient.WithOpacity(0.5f), 40, 24);
        Assert.Equal(ColorOutputKind.SdrSrgb, first.ExecuteResult.MaterialDiagnostics.OutputKind);
        Assert.Equal(DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient, first.ExecuteResult.MaterialDiagnostics.BackendCapabilities);
        Assert.Equal(DrawMaterialKind.LinearGradient, first.ExecuteResult.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.None, first.ExecuteResult.MaterialDiagnostics.FallbackReason);
        Assert.False(first.ExecuteResult.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(2, first.ExecuteResult.MaterialDiagnostics.CommandCount);
        Assert.Equal(1, first.ExecuteResult.MaterialDiagnostics.SolidColorCommandCount);
        Assert.Equal(1, first.ExecuteResult.MaterialDiagnostics.LinearGradientCommandCount);
        Assert.Equal(1, first.ExecuteResult.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(0, first.ExecuteResult.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(0, first.ExecuteResult.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(0, first.ExecuteResult.MaterialDiagnostics.FallbackCommandCount);

        rects.Reset();
        texts.Reset();
        var secondFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(9),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(20, 10),
            new CompositionOpacity(0.25f)));

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            secondFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(1, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        Assert.Equal(2, rects.Count);
        var secondGradientRect = rects.Span[1];
        Assert.Equal(36, secondGradientRect.X);
        Assert.Equal(30, secondGradientRect.Y);
        Assert.Equal(40, secondGradientRect.Width);
        Assert.Equal(24, secondGradientRect.Height);
        AssertRectGradientColors(secondGradientRect, gradient.WithOpacity(0.25f), 40, 24);
        Assert.Equal(DrawMaterialKind.LinearGradient, second.ExecuteResult.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.None, second.ExecuteResult.MaterialDiagnostics.FallbackReason);
        Assert.False(second.ExecuteResult.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(2, second.ExecuteResult.MaterialDiagnostics.CommandCount);
        Assert.Equal(1, second.ExecuteResult.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(0, second.ExecuteResult.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(0, second.ExecuteResult.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(0, second.ExecuteResult.MaterialDiagnostics.FallbackCommandCount);
    }

    [Fact]
    public void ExecuteCore_reports_material_output_diagnostics_for_solid_and_linear_gradient_rasterization()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var gradient = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(100, 0));
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(1, 2, 3)),
            DrawCommand.FromMaterial(DrawCommandKind.FillRect, Rect: new DrawRect(100, 0, 100, 50), Material: gradient)
        };

        var result = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Equal(ColorOutputKind.SdrSrgb, result.MaterialDiagnostics.OutputKind);
        Assert.Equal(DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient, result.MaterialDiagnostics.BackendCapabilities);
        Assert.Equal(DrawMaterialKind.LinearGradient, result.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.None, result.MaterialDiagnostics.FallbackReason);
        Assert.False(result.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(2, result.MaterialDiagnostics.CommandCount);
        Assert.Equal(1, result.MaterialDiagnostics.SolidColorCommandCount);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientCommandCount);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(0, result.MaterialDiagnostics.FallbackCommandCount);
        Assert.Equal(2, rects.Count);
        var gradientRect = rects.Span[1];
        Assert.Equal(100, gradientRect.X);
        Assert.Equal(0, gradientRect.Y);
        Assert.Equal(100, gradientRect.Width);
        Assert.Equal(50, gradientRect.Height);
        AssertRectGradientColors(gradientRect, gradient, 100, 50);
    }

    [Fact]
    public void ExecuteCore_keeps_linear_gradient_clamp_semantics_on_bounded_fallback_path()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var gradient = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(40, 0));
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 100, 50), Color: DrawColor.Opaque(1, 2, 3)),
            DrawCommand.FromMaterial(DrawCommandKind.FillRect, Rect: new DrawRect(100, 0, 100, 50), Material: gradient)
        };

        var result = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Equal(DrawMaterialKind.LinearGradient, result.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.None, result.MaterialDiagnostics.FallbackReason);
        Assert.False(result.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(16, result.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(0, result.MaterialDiagnostics.FallbackCommandCount);
        Assert.Equal(17, rects.Count);
        var firstGradientSegment = rects.Span[1];
        var lastGradientSegment = rects.Span[16];
        Assert.Equal(100, firstGradientSegment.X);
        Assert.Equal(0, firstGradientSegment.Y);
        Assert.Equal(6.25f, firstGradientSegment.Width);
        Assert.Equal(193.75f, lastGradientSegment.X);
        Assert.Equal(0, lastGradientSegment.Y);
        Assert.Equal(6.25f, lastGradientSegment.Width);
        AssertRectGradientSegmentColors(firstGradientSegment, gradient, 0f, 0f, 6.25f, 50f);
        AssertRectGradientSegmentColors(lastGradientSegment, gradient, 93.75f, 0f, 100f, 50f);
    }

    [Fact]
    public void ExecuteCore_rasterizes_degenerate_linear_gradient_as_start_color_single_rect()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var start = Color.FromSrgb(255, 0, 0);
        var end = Color.FromSrgb(0, 255, 0);
        var gradient = DrawMaterial.LinearGradient(
            start,
            end,
            new DrawPoint(24, 12),
            new DrawPoint(24, 12));
        var command = DrawCommand.FromMaterial(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(8, 6, 40, 24),
            Material: gradient);

        var result = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            [command],
            resources,
            DisplayScale.Identity,
            rects,
            texts);

        var startSdr = ColorOutputMapping.SdrSrgb.MapToSdr(start);
        Assert.Equal(DrawMaterialKind.LinearGradient, result.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.None, result.MaterialDiagnostics.FallbackReason);
        Assert.False(result.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientCommandCount);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(0, result.MaterialDiagnostics.FallbackCommandCount);
        Assert.Equal(startSdr, result.BackgroundColor);
        Assert.Equal(1, rects.Count);
        var rect = rects.Span[0];
        AssertRectColor(rect, startSdr);
        AssertRectGradientColors(rect, gradient, 40, 24, startSdr);
    }

    [Fact]
    public void ExecuteCore_keeps_internal_linear_gradient_text_material_on_fallback_path()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("gradient text");
        resources.Seal();
        var gradient = DrawMaterial.LinearGradient(
            Color.FromSrgb(255, 0, 0),
            Color.FromSrgb(0, 255, 0),
            new DrawPoint(0, 0),
            new DrawPoint(40, 0));
        var command = DrawCommand.FromMaterial(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(0, 0, 100, 50),
            Resource: style,
            Text: text,
            Material: gradient);
        var fallback = ColorOutputMapping.SdrSrgb.MapToSdr(gradient);

        var result = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            [command],
            resources,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Equal(0, rects.Count);
        Assert.Equal(1, texts.Count);
        Assert.Equal(fallback.R / 255f, texts.Span[0].R);
        Assert.Equal(fallback.G / 255f, texts.Span[0].G);
        Assert.Equal(fallback.B / 255f, texts.Span[0].B);
        Assert.Equal(fallback.A / 255f, texts.Span[0].A);
        Assert.Equal(DrawMaterialBackendCapabilities.SolidColor | DrawMaterialBackendCapabilities.LinearGradient, result.MaterialDiagnostics.BackendCapabilities);
        Assert.Equal(DrawMaterialKind.LinearGradient, result.MaterialDiagnostics.SelectedMaterialKind);
        Assert.Equal(DrawMaterialFallbackReason.UnsupportedNonSolidMaterial, result.MaterialDiagnostics.FallbackReason);
        Assert.True(result.MaterialDiagnostics.FallbackApplied);
        Assert.Equal(1, result.MaterialDiagnostics.LinearGradientCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSingleRectCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentedCommandCount);
        Assert.Equal(0, result.MaterialDiagnostics.LinearGradientSegmentRectCount);
        Assert.Equal(1, result.MaterialDiagnostics.FallbackCommandCount);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_layer_content_cache_hit_applies_latest_fixed_clip_scroll_state()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("cached scroll");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, -24, 140, 40), ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(16, -24, 140, 40), Resource: style, Text: text, ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(240, 240, 240))
        };
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 0,
            CommandCount: 2,
            new CompositionTransform(0, 30),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60)));

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            firstFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        Assert.Equal(2, first.CachedLayerCommands);
        Assert.Equal(16, rects.Span[0].X);
        Assert.Equal(6, rects.Span[0].Y);
        Assert.Equal(new IntegerScissorRect(0, 0, 200, 60), rects.Span[0].Scissor);
        Assert.Equal(new DrawRect(0, 0, 200, 60), first.ExecuteResult.TextClipDiagnostics.LastEffectiveTextClip.Bounds);

        rects.Reset();
        texts.Reset();
        var secondFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 0,
            CommandCount: 2,
            new CompositionTransform(0, 44),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(8, 10, 180, 44)));

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            secondFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(1, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        Assert.Equal(2, second.CachedLayerCommands);
        Assert.Equal(16, rects.Span[0].X);
        Assert.Equal(20, rects.Span[0].Y);
        Assert.Equal(new IntegerScissorRect(8, 10, 188, 54), rects.Span[0].Scissor);
        Assert.Equal(new DrawRect(8, 10, 180, 44), second.ExecuteResult.TextClipDiagnostics.LastEffectiveTextClip.Bounds);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_layer_content_cache_hit_skips_latest_empty_fixed_clip_intersection()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("cached scroll clipped out");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, -24, 140, 40), ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(16, -24, 140, 40), Resource: style, Text: text, ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(240, 240, 240))
        };
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 0,
            CommandCount: 2,
            new CompositionTransform(0, 30),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60)));

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            firstFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        Assert.Equal(2, first.CachedLayerCommands);
        Assert.Equal(1, rects.Count);
        Assert.Equal(1, texts.Count);

        rects.Reset();
        texts.Reset();
        var secondFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 0,
            CommandCount: 2,
            new CompositionTransform(0, 44),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(8, 70, 180, 44)));

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            secondFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(1, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        Assert.Equal(2, second.CachedLayerCommands);
        Assert.Equal(1, second.ExecuteResult.FillRectDiagnostics.EmptyIntersectionSkippedCount);
        Assert.Equal(1, second.ExecuteResult.TextClipDiagnostics.TextClipSkippedCount);
        Assert.Equal(0, rects.Count);
        Assert.Equal(0, texts.Count);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_reuses_layer_content_cache_for_multiple_disjoint_layers()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(72, 44, 30, 20), Color: DrawColor.Opaque(160, 180, 200))
        };
        Span<CompositionLayer> warmLayers =
        [
            new CompositionLayer(
                new CompositionLayerId(21),
                CommandStart: 1,
                CommandCount: 1,
                new CompositionTransform(12, 8),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(22),
                CommandStart: 2,
                CommandCount: 1,
                new CompositionTransform(4, 6),
                CompositionOpacity.Opaque)
        ];
        var cache = new D3D12CompositionLayerContentCache();

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            CompositionFrame.FromLayers(warmLayers),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(2, first.LayerCacheMisses);
        Assert.Equal(2, first.CachedLayerCommands);

        rects.Reset();
        texts.Reset();
        Span<CompositionLayer> hitLayers =
        [
            new CompositionLayer(
                new CompositionLayerId(21),
                CommandStart: 1,
                CommandCount: 1,
                new CompositionTransform(20, 10),
                new CompositionOpacity(0.25f)),
            new CompositionLayer(
                new CompositionLayerId(22),
                CommandStart: 2,
                CommandCount: 1,
                new CompositionTransform(8, 12),
                new CompositionOpacity(0.5f))
        ];

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            CompositionFrame.FromLayers(hitLayers),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(2, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        Assert.Equal(2, second.CachedLayerCommands);
        Assert.Equal(3, rects.Count);
        Assert.Equal(36, rects.Span[1].X);
        Assert.Equal(30, rects.Span[1].Y);
        Assert.Equal(64f / 255f, rects.Span[1].A);
        Assert.Equal(80, rects.Span[2].X);
        Assert.Equal(56, rects.Span[2].Y);
        Assert.Equal(128f / 255f, rects.Span[2].A);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_layer_content_cache_hit_preserves_source_paint_order_with_interleaved_commands()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 20, 20), Color: DrawColor.Opaque(10, 0, 0)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(10, 10, 20, 20), Color: DrawColor.Opaque(20, 0, 0)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(20, 20, 20, 20), Color: DrawColor.Opaque(30, 0, 0)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(30, 30, 20, 20), Color: DrawColor.Opaque(40, 0, 0)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(40, 40, 20, 20), Color: DrawColor.Opaque(50, 0, 0))
        };
        Span<CompositionLayer> warmLayers =
        [
            new CompositionLayer(
                new CompositionLayerId(41),
                CommandStart: 1,
                CommandCount: 1,
                new CompositionTransform(2, 3),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(42),
                CommandStart: 3,
                CommandCount: 1,
                new CompositionTransform(4, 5),
                new CompositionOpacity(0.75f))
        ];
        var cache = new D3D12CompositionLayerContentCache();

        var warm = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            CompositionFrame.FromLayers(warmLayers),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, warm.LayerCacheHits);
        Assert.Equal(2, warm.LayerCacheMisses);
        Assert.Equal(2, warm.CachedLayerCommands);

        rects.Reset();
        texts.Reset();
        Span<CompositionLayer> hitLayers =
        [
            new CompositionLayer(
                new CompositionLayerId(41),
                CommandStart: 1,
                CommandCount: 1,
                new CompositionTransform(20, 1),
                new CompositionOpacity(0.25f)),
            new CompositionLayer(
                new CompositionLayerId(42),
                CommandStart: 3,
                CommandCount: 1,
                new CompositionTransform(0, 30),
                new CompositionOpacity(0.5f))
        ];

        var hit = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            CompositionFrame.FromLayers(hitLayers),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(2, hit.LayerCacheHits);
        Assert.Equal(0, hit.LayerCacheMisses);
        Assert.Equal(2, hit.CachedLayerCommands);
        Assert.Equal(2, hit.TranslatedCommands);
        Assert.Equal(2, hit.OpacityAppliedCommands);
        Assert.Equal(5, rects.Count);
        Assert.Equal(0, rects.Span[0].X);
        Assert.Equal(0, rects.Span[0].Y);
        Assert.Equal(10f / 255f, rects.Span[0].R);
        Assert.Equal(1f, rects.Span[0].A);
        Assert.Equal(30, rects.Span[1].X);
        Assert.Equal(11, rects.Span[1].Y);
        Assert.Equal(20f / 255f, rects.Span[1].R);
        Assert.Equal(64f / 255f, rects.Span[1].A);
        Assert.Equal(20, rects.Span[2].X);
        Assert.Equal(20, rects.Span[2].Y);
        Assert.Equal(30f / 255f, rects.Span[2].R);
        Assert.Equal(1f, rects.Span[2].A);
        Assert.Equal(30, rects.Span[3].X);
        Assert.Equal(60, rects.Span[3].Y);
        Assert.Equal(40f / 255f, rects.Span[3].R);
        Assert.Equal(128f / 255f, rects.Span[3].A);
        Assert.Equal(40, rects.Span[4].X);
        Assert.Equal(40, rects.Span[4].Y);
        Assert.Equal(50f / 255f, rects.Span[4].R);
        Assert.Equal(1f, rects.Span[4].A);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_bypasses_layer_content_cache_for_overlapping_layers()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(10, 10, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(20, 20, 40, 24), Color: DrawColor.Opaque(160, 180, 200))
        };
        Span<CompositionLayer> layers =
        [
            new CompositionLayer(
                new CompositionLayerId(31),
                CommandStart: 1,
                CommandCount: 2,
                new CompositionTransform(10, 0),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(32),
                CommandStart: 2,
                CommandCount: 1,
                new CompositionTransform(0, 20),
                CompositionOpacity.Opaque)
        ];

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            CompositionFrame.FromLayers(layers),
            DisplayScale.Identity,
            rects,
            texts,
            new D3D12CompositionLayerContentCache());

        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(0, diagnostics.LayerCacheMisses);
        Assert.Equal(0, diagnostics.CachedLayerCommands);
        Assert.Equal(3, diagnostics.TranslatedCommands);
        Assert.Equal(2, diagnostics.OpacityAppliedCommands);
        Assert.Equal(3, rects.Count);
        Assert.Equal(20, rects.Span[1].X);
        Assert.Equal(10, rects.Span[1].Y);
        Assert.Equal(30, rects.Span[2].X);
        Assert.Equal(40, rects.Span[2].Y);
        Assert.Equal(128f / 255f, rects.Span[2].A);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_invalidates_layer_content_cache_when_source_commands_change()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var firstCommands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140))
        };
        var secondCommands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(18, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(9),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(10, 0),
            CompositionOpacity.Opaque));
        var cache = new D3D12CompositionLayerContentCache();

        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            firstCommands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        rects.Reset();
        texts.Reset();

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            secondCommands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(1, diagnostics.LayerCacheMisses);
        Assert.Equal(28, rects.Span[1].X);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_invalidates_layer_content_cache_when_layer_identity_or_range_changes()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(72, 44, 30, 20), Color: DrawColor.Opaque(160, 180, 200))
        };
        var cache = new D3D12CompositionLayerContentCache();
        var warmFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(14),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(10, 0),
            CompositionOpacity.Opaque));

        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            warmFrame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        rects.Reset();
        texts.Reset();

        var layerIdChanged = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            new CompositionFrame(new CompositionLayer(
                new CompositionLayerId(15),
                CommandStart: 1,
                CommandCount: 1,
                new CompositionTransform(20, 0),
                CompositionOpacity.Opaque)),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, layerIdChanged.LayerCacheHits);
        Assert.Equal(1, layerIdChanged.LayerCacheMisses);
        Assert.Equal(1, layerIdChanged.CachedLayerCommands);
        Assert.Equal(36, rects.Span[1].X);
        rects.Reset();
        texts.Reset();

        var commandRangeChanged = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            new CompositionFrame(new CompositionLayer(
                new CompositionLayerId(14),
                CommandStart: 1,
                CommandCount: 2,
                new CompositionTransform(30, 0),
                new CompositionOpacity(0.5f))),
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, commandRangeChanged.LayerCacheHits);
        Assert.Equal(1, commandRangeChanged.LayerCacheMisses);
        Assert.Equal(2, commandRangeChanged.CachedLayerCommands);
        Assert.Equal(2, commandRangeChanged.TranslatedCommands);
        Assert.Equal(2, commandRangeChanged.OpacityAppliedCommands);
        Assert.Equal(46, rects.Span[1].X);
        Assert.Equal(102, rects.Span[2].X);
        Assert.Equal(128f / 255f, rects.Span[1].A);
        Assert.Equal(128f / 255f, rects.Span[2].A);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_invalidates_layer_content_cache_when_display_scale_changes()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("scaled cached layer");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(20, 24, 120, 28), Resource: style, Text: text, Color: DrawColor.Opaque(240, 240, 240))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(12, 8),
            CompositionOpacity.Opaque));
        var cache = new D3D12CompositionLayerContentCache();

        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        Assert.Equal(TextStyle.Default.FontSize, texts.Span[0].ResolvedStyle.FontSize);
        rects.Reset();
        texts.Reset();

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 480, 320),
            commands,
            resources,
            frame,
            new DisplayScale(2f, 2f),
            rects,
            texts,
            cache);

        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(1, diagnostics.LayerCacheMisses);
        Assert.Equal(1, diagnostics.CachedLayerCommands);
        Assert.Equal(TextStyle.Default.FontSize * 2, texts.Span[0].ResolvedStyle.FontSize);
        Assert.Equal(64, texts.Span[0].X);
        Assert.Equal(64, texts.Span[0].Y);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_invalidates_layer_content_cache_when_resource_resolver_changes()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = new FrameDrawingResources();
        var commands = BuildCommands(resources, 16);
        resources.Seal();
        using var changedResources = new FrameDrawingResources();
        var changedCommands = BuildCommands(changedResources, 24);
        changedResources.Seal();
        Assert.True(commands.AsSpan().SequenceEqual(changedCommands));

        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(12, 8),
            CompositionOpacity.Opaque));
        var cache = new D3D12CompositionLayerContentCache();

        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        Assert.Equal(16, texts.Span[0].ResolvedStyle.FontSize);
        rects.Reset();
        texts.Reset();

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            changedCommands,
            changedResources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(1, diagnostics.LayerCacheMisses);
        Assert.Equal(1, diagnostics.CachedLayerCommands);
        Assert.Equal(24, texts.Span[0].ResolvedStyle.FontSize);

        static DrawCommand[] BuildCommands(FrameDrawingResources resources, float fontSize)
        {
            var style = resources.AddTextStyle(new TextStyle(
                TextFontFamily.SegoeUi,
                fontSize,
                TextFontWeight.Normal,
                TextFontStyle.Normal,
                TextFontStretch.Normal,
                TextHorizontalAlignment.Leading,
                TextVerticalAlignment.Top,
                TextWrapping.NoWrap));
            var text = resources.AddText("same text");
            return
            [
                new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
                new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(20, 24, 120, 28), Resource: style, Text: text, Color: DrawColor.Opaque(240, 240, 240))
            ];
        }
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_invalidates_layer_content_cache_when_same_resource_frame_resets()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = new FrameDrawingResources();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(12, 8),
            CompositionOpacity.Opaque));
        var cache = new D3D12CompositionLayerContentCache();
        var firstFrameId = resources.FrameId;

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        rects.Reset();
        texts.Reset();

        resources.Reset();
        resources.Seal();

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.NotEqual(firstFrameId, resources.FrameId);
        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(1, diagnostics.LayerCacheMisses);
        Assert.Equal(1, diagnostics.CachedLayerCommands);
        Assert.Equal(2, rects.Count);
        Assert.Equal(28, rects.Span[1].X);
        Assert.Equal(28, rects.Span[1].Y);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_misses_layer_content_cache_after_clear()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(12, 8),
            CompositionOpacity.Opaque));
        var cache = new D3D12CompositionLayerContentCache();

        var first = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        Assert.Equal(0, first.LayerCacheHits);
        Assert.Equal(1, first.LayerCacheMisses);
        rects.Reset();
        texts.Reset();

        var second = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        Assert.Equal(1, second.LayerCacheHits);
        Assert.Equal(0, second.LayerCacheMisses);
        rects.Reset();
        texts.Reset();

        cache.Clear();
        var cleared = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);

        Assert.Equal(0, cleared.LayerCacheHits);
        Assert.Equal(1, cleared.LayerCacheMisses);
        Assert.Equal(1, cleared.CachedLayerCommands);
        Assert.Equal(28, rects.Span[1].X);
        Assert.Equal(28, rects.Span[1].Y);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_layer_content_cache_hit_does_not_allocate()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("cached layer");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), ClipBounds: new DrawRect(10, 10, 160, 80), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(20, 24, 120, 28), Resource: style, Text: text, ClipBounds: new DrawRect(10, 10, 160, 80), Color: DrawColor.Opaque(240, 240, 240))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(12, 8),
            new CompositionOpacity(0.5f)));
        var cache = new D3D12CompositionLayerContentCache();

        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        rects.Reset();
        texts.Reset();
        _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            cache);
        rects.Reset();
        texts.Reset();

        var allocated = MeasureAllocatedBytes(() =>
        {
            _ = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
                DrawingBackendClipMode.Scissor,
                new DrawRect(0, 0, 240, 160),
                commands,
                resources,
                frame,
                DisplayScale.Identity,
                rects,
                texts,
                cache);
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_applies_multiple_layers_on_d3d12_path()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(10, 10, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(20, 20, 40, 24), Color: DrawColor.Opaque(160, 180, 200))
        };
        Span<CompositionLayer> layers =
        [
            new CompositionLayer(
                new CompositionLayerId(10),
                CommandStart: 1,
                CommandCount: 2,
                new CompositionTransform(10, 0),
                new CompositionOpacity(0.5f)),
            new CompositionLayer(
                new CompositionLayerId(11),
                CommandStart: 2,
                CommandCount: 1,
                new CompositionTransform(0, 20),
                CompositionOpacity.Opaque,
                CompositionClipMode.Fixed,
                new DrawRect(0, 0, 80, 80))
        ];
        var frame = CompositionFrame.FromLayers(layers);

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            new D3D12CompositionLayerContentCache());

        Assert.True(diagnostics.D3D12Backed);
        Assert.Equal(2, diagnostics.LayerCount);
        Assert.Equal(3, diagnostics.CommandCount);
        Assert.Equal(1, diagnostics.LayerCommandStart);
        Assert.Equal(2, diagnostics.LayerCommandCount);
        Assert.Equal(3, diagnostics.TranslatedCommands);
        Assert.Equal(2, diagnostics.OpacityAppliedCommands);
        Assert.Equal(0, diagnostics.LayerCacheHits);
        Assert.Equal(0, diagnostics.LayerCacheMisses);
        Assert.Equal(0, diagnostics.CachedLayerCommands);
        Assert.Equal(3, rects.Count);
        Assert.Equal(20, rects.Span[1].X);
        Assert.Equal(10, rects.Span[1].Y);
        Assert.Equal(128f / 255f, rects.Span[1].A);
        Assert.Equal(30, rects.Span[2].X);
        Assert.Equal(40, rects.Span[2].Y);
        Assert.Equal(128f / 255f, rects.Span[2].A);
        Assert.Equal(new IntegerScissorRect(0, 0, 80, 80), rects.Span[2].Scissor);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_keeps_transform_in_logical_space_before_backend_scale()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(10, 10, 20, 20), ClipBounds: new DrawRect(10, 10, 20, 20), Color: DrawColor.Opaque(1, 2, 3))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionTransform(10, 5),
            CompositionOpacity.Opaque));

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 200, 160),
            commands,
            resources,
            frame,
            new DisplayScale(2f, 2f),
            rects,
            texts);

        var transformedRect = rects.Span[0];
        Assert.Equal(40, transformedRect.X);
        Assert.Equal(30, transformedRect.Y);
        Assert.Equal(new DrawRect(40, 30, 40, 40), diagnostics.ExecuteResult.FillRectDiagnostics.LastEffectiveScissor.Bounds);
    }

    [Fact]
    public void ExecuteCompositionDiagnosticCore_keeps_fixed_clip_in_place_for_scroll_presentation()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("scroll");
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, -24, 140, 40), ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(16, -24, 140, 40), Resource: style, Text: text, ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(240, 240, 240))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(1),
            CommandStart: 0,
            CommandCount: 2,
            new CompositionTransform(0, 30),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60)));

        var diagnostics = D3D12DrawingBackend.ExecuteCompositionDiagnosticCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts);

        Assert.Equal(2, diagnostics.TranslatedCommands);
        Assert.Equal(0, diagnostics.OpacityAppliedCommands);
        Assert.Equal(16, rects.Span[0].X);
        Assert.Equal(6, rects.Span[0].Y);
        Assert.Equal(new IntegerScissorRect(0, 0, 200, 60), rects.Span[0].Scissor);
        Assert.Equal(new DrawRect(0, 0, 200, 60), diagnostics.ExecuteResult.TextClipDiagnostics.LastEffectiveTextClip.Bounds);
    }

    private static DrawCommand Fill(DrawRect clipBounds)
    {
        return new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 50, 50), ClipBounds: clipBounds, Color: DrawColor.Opaque(1, 2, 3));
    }

    private static DrawCommand Text(DrawRect clipBounds)
    {
        return new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 50, 50), ClipBounds: clipBounds, Color: DrawColor.Opaque(1, 2, 3));
    }

    private static DrawColor SampleLinearGradientSdr(DrawMaterial material, float x, float y)
    {
        var dx = material.EndPoint.X - material.StartPoint.X;
        var dy = material.EndPoint.Y - material.StartPoint.Y;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= float.Epsilon
            ? 0f
            : Math.Clamp(((x - material.StartPoint.X) * dx + (y - material.StartPoint.Y) * dy) / lengthSquared, 0f, 1f);

        var color = Color.FromLinearBt2020(
            Lerp(material.Color.LinearBt2020R, material.EndColor.LinearBt2020R, t),
            Lerp(material.Color.LinearBt2020G, material.EndColor.LinearBt2020G, t),
            Lerp(material.Color.LinearBt2020B, material.EndColor.LinearBt2020B, t),
            Lerp(material.Color.A, material.EndColor.A, t));
        return ColorOutputMapping.SdrSrgb.MapToSdr(color);
    }

    private static void AssertRectColor(D3D12Renderer2D.RectData rect, DrawColor expected)
    {
        Assert.Equal(expected.R / 255f, rect.R);
        Assert.Equal(expected.G / 255f, rect.G);
        Assert.Equal(expected.B / 255f, rect.B);
        Assert.Equal(expected.A / 255f, rect.A);
    }

    private static void AssertRectGradientColors(D3D12Renderer2D.RectData rect, DrawMaterial material, float width, float height)
    {
        AssertRectGradientColors(rect, material, width, height, ColorOutputMapping.SdrSrgb.MapToSdr(material));
    }

    private static void AssertRectGradientColors(D3D12Renderer2D.RectData rect, DrawMaterial material, float width, float height, DrawColor representativeColor)
    {
        AssertVectorColor(rect.TopLeftColor, SampleLinearGradientSdr(material, 0, 0));
        AssertVectorColor(rect.TopRightColor, SampleLinearGradientSdr(material, width, 0));
        AssertVectorColor(rect.BottomRightColor, SampleLinearGradientSdr(material, width, height));
        AssertVectorColor(rect.BottomLeftColor, SampleLinearGradientSdr(material, 0, height));
        AssertRectColor(rect, representativeColor);
    }

    private static void AssertRectGradientSegmentColors(
        D3D12Renderer2D.RectData rect,
        DrawMaterial material,
        float x0,
        float y0,
        float x1,
        float y1)
    {
        AssertVectorColor(rect.TopLeftColor, SampleLinearGradientSdr(material, x0, y0));
        AssertVectorColor(rect.TopRightColor, SampleLinearGradientSdr(material, x1, y0));
        AssertVectorColor(rect.BottomRightColor, SampleLinearGradientSdr(material, x1, y1));
        AssertVectorColor(rect.BottomLeftColor, SampleLinearGradientSdr(material, x0, y1));
        AssertRectColor(rect, SampleLinearGradientSdr(material, (x0 + x1) * 0.5f, (y0 + y1) * 0.5f));
    }

    private static void AssertVectorColor(System.Numerics.Vector4 actual, DrawColor expected)
    {
        Assert.Equal(expected.R / 255f, actual.X);
        Assert.Equal(expected.G / 255f, actual.Y);
        Assert.Equal(expected.B / 255f, actual.Z);
        Assert.Equal(expected.A / 255f, actual.W);
    }

    private static float Lerp(float start, float end, float t) => start + (end - start) * t;

    private static void WarmExecuteCore(
        ReadOnlySpan<DrawCommand> commands,
        IFrameResourceResolver resources,
        FrameRenderList<D3D12Renderer2D.RectData> rects,
        FrameRenderList<D3D12TextRun> texts,
        float scaleValue)
    {
        _ = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 400, 320),
            commands,
            resources,
            new DisplayScale(scaleValue, scaleValue),
            rects,
            texts);
    }

    private static long MeasureAllocatedBytes(Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
