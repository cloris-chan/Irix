using Irix.Drawing;
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
    public void ResolveTextClip_scissor_mode_skips_empty_intersection()
    {
        var plan = D3D12DrawingBackend.ResolveTextClip(DrawingBackendClipMode.Scissor, Viewport, new DrawRect(240, 24, 80, 40));

        Assert.True(plan.ClipEnabled);
        Assert.True(plan.Skip);
        Assert.True(plan.EffectiveClip.IsEmpty);
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

    private static DrawCommand Fill(DrawRect clipBounds)
    {
        return new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(0, 0, 50, 50), ClipBounds: clipBounds, Color: DrawColor.Opaque(1, 2, 3));
    }

    private static DrawCommand Text(DrawRect clipBounds)
    {
        return new DrawCommand(DrawCommandKind.DrawTextRun, Rect: new DrawRect(0, 0, 50, 50), ClipBounds: clipBounds, Color: DrawColor.Opaque(1, 2, 3));
    }
}