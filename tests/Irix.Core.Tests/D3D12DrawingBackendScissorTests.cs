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
    public void TryExecuteCompositionWithRenderTargetCache_builds_rect_only_suffix_layer_plan()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var layerTargets = new FrameRenderList<D3D12CompositionLayerRenderTargetRequest>();
        using var renderSegments = new FrameRenderList<D3D12CompositionFrameRenderSegment>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(64, 20, 32, 24), Color: DrawColor.Opaque(120, 140, 160))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(12, 8),
            new CompositionOpacity(0.5f)));

        var planned = D3D12DrawingBackend.TryExecuteCompositionWithRenderTargetCache(
            new D3D12CompositionLayerContentCache(),
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            layerTargets,
            renderSegments,
            out var diagnostics);

        Assert.True(planned);
        Assert.Equal(1, rects.Count);
        Assert.Equal(0, texts.Count);
        Assert.Equal(1, layerTargets.Count);
        Assert.Equal(2, renderSegments.Count);
        Assert.Equal(D3D12CompositionFrameRenderSegment.Commands(0, 1, 0, 0), renderSegments.Span[0]);
        Assert.Equal(D3D12CompositionFrameRenderSegment.LayerRenderTarget(0), renderSegments.Span[1]);
        Assert.True(layerTargets.Span[0].Content.SupportsRenderTargetCache);
        Assert.Equal(2, diagnostics.CachedLayerCommands);
        Assert.Equal(2, diagnostics.TranslatedCommands);
        Assert.Equal(2, diagnostics.OpacityAppliedCommands);
        Assert.False(diagnostics.RenderTargetBacked);
    }

    [Fact]
    public void TryExecuteCompositionWithRenderTargetCache_builds_rect_and_text_suffix_layer_plan()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var layerTargets = new FrameRenderList<D3D12CompositionLayerRenderTargetRequest>();
        using var renderSegments = new FrameRenderList<D3D12CompositionFrameRenderSegment>();
        using var resources = FrameDrawingResources.Rent();
        var style = resources.AddTextStyle(TextStyle.Default);
        var text = resources.AddText("text");
        resources.Seal();
        var textCommands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 80, 24), Color: DrawColor.Opaque(80, 120, 180)),
            new(DrawCommandKind.DrawTextRun, Rect: new DrawRect(16, 52, 120, 24), Resource: style, Text: text, Color: DrawColor.Opaque(240, 240, 240))
        };
        var textFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(7),
            CommandStart: 1,
            CommandCount: 2,
            new CompositionTransform(12, 8),
            CompositionOpacity.Opaque));

        Assert.True(D3D12DrawingBackend.TryExecuteCompositionWithRenderTargetCache(
            new D3D12CompositionLayerContentCache(),
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            textCommands,
            resources,
            textFrame,
            DisplayScale.Identity,
            rects,
            texts,
            layerTargets,
            renderSegments,
            out var diagnostics));
        Assert.Equal(1, rects.Count);
        Assert.Equal(0, texts.Count);
        Assert.Equal(1, layerTargets.Count);
        Assert.Equal(2, renderSegments.Count);
        Assert.Equal(D3D12CompositionFrameRenderSegment.Commands(0, 1, 0, 0), renderSegments.Span[0]);
        Assert.Equal(D3D12CompositionFrameRenderSegment.LayerRenderTarget(0), renderSegments.Span[1]);
        Assert.True(layerTargets.Span[0].Content.SupportsRenderTargetCache);
        Assert.Equal(1, layerTargets.Span[0].Content.Rects.Length);
        Assert.Equal(1, layerTargets.Span[0].Content.Texts.Length);
        Assert.Equal(2, diagnostics.CachedLayerCommands);
        Assert.Equal(2, diagnostics.TranslatedCommands);
    }

    [Fact]
    public void TryExecuteCompositionWithRenderTargetCache_builds_interleaved_layer_order_plan()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var layerTargets = new FrameRenderList<D3D12CompositionLayerRenderTargetRequest>();
        using var renderSegments = new FrameRenderList<D3D12CompositionFrameRenderSegment>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();

        var interleavedCommands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 240, 160), Color: DrawColor.Opaque(1, 2, 3)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, 20, 40, 24), Color: DrawColor.Opaque(100, 120, 140)),
            new(DrawCommandKind.FillRect, Rect: new DrawRect(180, 20, 40, 24), Color: DrawColor.Opaque(200, 120, 80))
        };
        var interleavedFrame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(8),
            CommandStart: 1,
            CommandCount: 1,
            new CompositionTransform(8, 0),
            CompositionOpacity.Opaque));

        Assert.True(D3D12DrawingBackend.TryExecuteCompositionWithRenderTargetCache(
            new D3D12CompositionLayerContentCache(),
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            interleavedCommands,
            resources,
            interleavedFrame,
            DisplayScale.Identity,
            rects,
            texts,
            layerTargets,
            renderSegments,
            out var diagnostics));
        Assert.Equal(2, rects.Count);
        Assert.Equal(0, texts.Count);
        Assert.Equal(1, layerTargets.Count);
        Assert.Equal(3, renderSegments.Count);
        Assert.Equal(D3D12CompositionFrameRenderSegment.Commands(0, 1, 0, 0), renderSegments.Span[0]);
        Assert.Equal(D3D12CompositionFrameRenderSegment.LayerRenderTarget(0), renderSegments.Span[1]);
        Assert.Equal(D3D12CompositionFrameRenderSegment.Commands(1, 1, 0, 0), renderSegments.Span[2]);
        Assert.Equal(1, diagnostics.CachedLayerCommands);
        Assert.Equal(1, diagnostics.TranslatedCommands);
    }

    [Fact]
    public void TryExecuteCompositionWithRenderTargetCache_rejects_fixed_clip_scroll_layers()
    {
        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRun>();
        using var layerTargets = new FrameRenderList<D3D12CompositionLayerRenderTargetRequest>();
        using var renderSegments = new FrameRenderList<D3D12CompositionFrameRenderSegment>();
        using var resources = FrameDrawingResources.Rent();
        resources.Seal();
        var commands = new DrawCommand[]
        {
            new(DrawCommandKind.FillRect, Rect: new DrawRect(16, -24, 140, 40), ClipBounds: new DrawRect(0, 0, 200, 60), Color: DrawColor.Opaque(1, 2, 3))
        };
        var frame = new CompositionFrame(new CompositionLayer(
            new CompositionLayerId(9),
            CommandStart: 0,
            CommandCount: 1,
            new CompositionTransform(0, 30),
            CompositionOpacity.Opaque,
            CompositionClipMode.Fixed,
            new DrawRect(0, 0, 200, 60)));

        var planned = D3D12DrawingBackend.TryExecuteCompositionWithRenderTargetCache(
            new D3D12CompositionLayerContentCache(),
            DrawingBackendClipMode.Scissor,
            new DrawRect(0, 0, 240, 160),
            commands,
            resources,
            frame,
            DisplayScale.Identity,
            rects,
            texts,
            layerTargets,
            renderSegments,
            out _);

        Assert.False(planned);
        Assert.Equal(0, rects.Count);
        Assert.Equal(0, texts.Count);
        Assert.Equal(0, layerTargets.Count);
        Assert.Equal(0, renderSegments.Count);
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
