using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public class DisplayScaleTests
{
    [Fact]
    public void Identity_scale_has_no_effect_on_logical_dimensions()
    {
        var scale = DisplayScale.Identity;
        var physical = new PixelRectangle(0, 0, 1920, 1080);
        var ctx = new FrameContext(physical.Width, physical.Height, scale);

        Assert.True(scale.IsIdentity);
        Assert.Equal(1920, ctx.LogicalWidth);
        Assert.Equal(1080, ctx.LogicalHeight);
    }

    [Theory]
    [InlineData(1.25f, 1536, 864)]
    [InlineData(1.5f, 1280, 720)]
    [InlineData(2.0f, 960, 540)]
    public void Logical_dimensions_match_physical_divided_by_scale(float scaleValue, int expectedLogicalW, int expectedLogicalH)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var ctx = new FrameContext(1920, 1080, scale);

        Assert.False(scale.IsIdentity);
        Assert.Equal(expectedLogicalW, ctx.LogicalWidth);
        Assert.Equal(expectedLogicalH, ctx.LogicalHeight);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void DrawCommand_scale_converts_logical_to_physical(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var logical = new DrawCommand(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(10, 20, 100, 50),
            ClipBounds: new DrawRect(0, 0, 800, 600));

        var physical = logical.Scale(scale);

        Assert.Equal(10 * scaleValue, physical.Rect.X);
        Assert.Equal(20 * scaleValue, physical.Rect.Y);
        Assert.Equal(100 * scaleValue, physical.Rect.Width);
        Assert.Equal(50 * scaleValue, physical.Rect.Height);
        Assert.Equal(0, physical.ClipBounds.X);
        Assert.Equal(0, physical.ClipBounds.Y);
        Assert.Equal(800 * scaleValue, physical.ClipBounds.Width);
        Assert.Equal(600 * scaleValue, physical.ClipBounds.Height);
    }

    [Fact]
    public void DrawCommand_scale_identity_returns_same_instance()
    {
        var cmd = new DrawCommand(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(10, 20, 100, 50));

        var scaled = cmd.Scale(DisplayScale.Identity);

        Assert.Equal(cmd, scaled);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void HitTestTarget_scale_converts_logical_to_physical(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var logical = new HitTestTarget(
            new PixelRectangle(100, 200, 300, 400),
            "action1",
            new PixelRectangle(0, 0, 800, 600));

        var physical = logical.Scale(scale);

        Assert.Equal((int)(100 * scaleValue), physical.Bounds.X);
        Assert.Equal((int)(200 * scaleValue), physical.Bounds.Y);
        Assert.Equal((int)(300 * scaleValue), physical.Bounds.Width);
        Assert.Equal((int)(400 * scaleValue), physical.Bounds.Height);
        Assert.Equal(0, physical.ClipBounds.X);
        Assert.Equal(0, physical.ClipBounds.Y);
        Assert.Equal((int)(800 * scaleValue), physical.ClipBounds.Width);
        Assert.Equal((int)(600 * scaleValue), physical.ClipBounds.Height);
    }

    [Fact]
    public void HitTestTarget_scale_identity_returns_same_instance()
    {
        var target = new HitTestTarget(
            new PixelRectangle(100, 200, 300, 400),
            "action1");

        var scaled = target.Scale(DisplayScale.Identity);

        Assert.Equal(target, scaled);
    }

    [Fact]
    public void Default_DisplayScale_is_identity()
    {
        var scale = default(DisplayScale);

        Assert.True(scale.IsIdentity);
        Assert.Equal(0f, scale.ScaleX);
        Assert.Equal(0f, scale.ScaleY);
    }

    [Fact]
    public void Asymmetric_scale_scales_x_and_y_independently()
    {
        var scale = new DisplayScale(1.5f, 2.0f);
        var ctx = new FrameContext(1920, 1080, scale);

        Assert.Equal(1280, ctx.LogicalWidth);
        Assert.Equal(540, ctx.LogicalHeight);
    }

    [Fact]
    public void Logical_to_physical_roundtrip_preserves_approximate_dimensions()
    {
        var scale = new DisplayScale(1.5f, 1.5f);
        var physicalW = 1920;
        var physicalH = 1080;
        var ctx = new FrameContext(physicalW, physicalH, scale);

        var logicalW = ctx.LogicalWidth;
        var logicalH = ctx.LogicalHeight;

        var cmd = new DrawCommand(
            DrawCommandKind.FillRect,
            Rect: new DrawRect(0, 0, logicalW, logicalH));

        var scaled = cmd.Scale(scale);

        Assert.Equal(physicalW, (int)scaled.Rect.Width);
        Assert.Equal(physicalH, (int)scaled.Rect.Height);
    }

    [Theory]
    [InlineData(1.25f, 20f)]
    [InlineData(1.5f, 24f)]
    [InlineData(2.0f, 32f)]
    public void FrameDrawingResources_ScaleTextStyles_scales_font_size(float scaleValue, float expectedFontSize)
    {
        using var resources = FrameDrawingResources.Rent();
        var style = new TextStyle("Segoe UI", 16f, TextFontWeight.Normal, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Center, TextWrapping.NoWrap);
        var handle = resources.AddTextStyle(style);

        resources.ScaleTextStyles(new DisplayScale(scaleValue, scaleValue));
        var resolved = resources.ResolveTextStyle(handle);

        Assert.Equal(expectedFontSize, resolved.FontSize);
    }

    [Fact]
    public void FrameDrawingResources_ScaleTextStyles_identity_is_noop()
    {
        using var resources = FrameDrawingResources.Rent();
        var style = new TextStyle("Segoe UI", 16f, TextFontWeight.Normal, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Center, TextWrapping.NoWrap);
        var handle = resources.AddTextStyle(style);

        resources.ScaleTextStyles(DisplayScale.Identity);
        var resolved = resources.ResolveTextStyle(handle);

        Assert.Equal(16f, resolved.FontSize);
    }

    [Fact]
    public void FrameDrawingResources_ScaleTextStyles_preserves_non_font_fields()
    {
        using var resources = FrameDrawingResources.Rent();
        var style = new TextStyle("Consolas", 14f, TextFontWeight.Bold, TextFontStyle.Italic, TextFontStretch.Normal, TextHorizontalAlignment.Center, TextVerticalAlignment.Top, TextWrapping.Wrap);
        var handle = resources.AddTextStyle(style);

        resources.ScaleTextStyles(new DisplayScale(1.5f, 1.5f));
        var resolved = resources.ResolveTextStyle(handle);

        Assert.Equal(21f, resolved.FontSize);
        Assert.Equal("Consolas", resolved.FontFamily);
        Assert.Equal(TextFontWeight.Bold, resolved.FontWeight);
        Assert.Equal(TextFontStyle.Italic, resolved.FontStyle);
        Assert.Equal(TextHorizontalAlignment.Center, resolved.HorizontalAlignment);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void DrawCommand_and_text_style_scale_consistently(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);

        using var resources = FrameDrawingResources.Rent();
        var style = new TextStyle("Segoe UI", 16f, TextFontWeight.Normal, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Center, TextWrapping.NoWrap);
        var handle = resources.AddTextStyle(style);

        var logicalCmd = new DrawCommand(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(10, 20, 200, 30),
            Resource: handle);

        var physicalCmd = logicalCmd.Scale(scale);
        resources.ScaleTextStyles(scale);
        var physicalStyle = resources.ResolveTextStyle(handle);

        Assert.Equal(10 * scaleValue, physicalCmd.Rect.X);
        Assert.Equal(20 * scaleValue, physicalCmd.Rect.Y);
        Assert.Equal(200 * scaleValue, physicalCmd.Rect.Width);
        Assert.Equal(30 * scaleValue, physicalCmd.Rect.Height);
        Assert.Equal(16f * scaleValue, physicalStyle.FontSize);
    }
}
