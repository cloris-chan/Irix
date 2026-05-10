using Irix.Drawing;
using Xunit;

namespace Irix.Core.Tests;

public sealed class DrawingScissorTests
{
    [Fact]
    public void ResolveEffectiveScissor_uses_viewport_for_default_clip()
    {
        var viewport = new DrawRect(0, 0, 960, 540);

        var scissor = DrawingScissor.ResolveEffectiveScissor(viewport, default);

        Assert.False(scissor.IsEmpty);
        Assert.Equal(viewport, scissor.Bounds);
    }

    [Fact]
    public void ResolveEffectiveScissor_keeps_clip_inside_viewport()
    {
        var viewport = new DrawRect(0, 0, 960, 540);
        var clip = new DrawRect(32, 32, 80, 40);

        var scissor = DrawingScissor.ResolveEffectiveScissor(viewport, clip);

        Assert.False(scissor.IsEmpty);
        Assert.Equal(clip, scissor.Bounds);
    }

    [Fact]
    public void ResolveEffectiveScissor_intersects_clip_outside_viewport()
    {
        var viewport = new DrawRect(0, 0, 100, 80);
        var clip = new DrawRect(60, 30, 80, 70);

        var scissor = DrawingScissor.ResolveEffectiveScissor(viewport, clip);

        Assert.False(scissor.IsEmpty);
        Assert.Equal(new DrawRect(60, 30, 40, 50), scissor.Bounds);
    }

    [Fact]
    public void ResolveEffectiveScissor_reports_empty_intersection()
    {
        var viewport = new DrawRect(0, 0, 100, 80);
        var clip = new DrawRect(120, 20, 30, 30);

        var scissor = DrawingScissor.ResolveEffectiveScissor(viewport, clip);

        Assert.True(scissor.IsEmpty);
        Assert.Equal(default, scissor.Bounds);
    }

    [Fact]
    public void ToIntegerScissorRect_floors_origin_and_ceils_extent()
    {
        var scissor = new EffectiveScissor(new DrawRect(10.75f, 20.25f, 30.5f, 40.5f), false);

        var integerScissor = DrawingScissor.ToIntegerScissorRect(scissor, 200, 160);

        Assert.Equal(new IntegerScissorRect(10, 20, 42, 61), integerScissor);
    }

    [Fact]
    public void ToIntegerScissorRect_clamps_to_viewport()
    {
        var scissor = new EffectiveScissor(new DrawRect(-5.5f, -3.25f, 120.75f, 90.5f), false);

        var integerScissor = DrawingScissor.ToIntegerScissorRect(scissor, 100, 80);

        Assert.Equal(new IntegerScissorRect(0, 0, 100, 80), integerScissor);
    }
}