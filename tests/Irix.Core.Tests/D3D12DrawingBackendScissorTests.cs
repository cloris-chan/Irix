using Irix.Drawing;
using Irix.Platform.Windows;
using Irix.Poc;
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
