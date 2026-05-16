using Irix.Drawing;
using Irix.Platform;
using Irix.Platform.Windows;
using Irix.Poc;
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
            new ActionId(100),
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
            new ActionId(100));

        var scaled = target.Scale(DisplayScale.Identity);

        Assert.Equal(target, scaled);
    }

    [Fact]
    public void Default_DisplayScale_normalizes_to_identity()
    {
        var scale = default(DisplayScale);
        var normalized = scale.Normalize();

        Assert.False(scale.IsIdentity);
        Assert.True(normalized.IsIdentity);
        Assert.Equal(0f, scale.ScaleX);
        Assert.Equal(0f, scale.ScaleY);
        Assert.Equal(1f, normalized.ScaleX);
        Assert.Equal(1f, normalized.ScaleY);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 1f)]
    public void DisplayScale_normalize_replaces_invalid_components(float input, float expected)
    {
        var scale = new DisplayScale(input, input);
        var normalized = scale.Normalize();

        Assert.Equal(expected, normalized.ScaleX);
        Assert.Equal(expected, normalized.ScaleY);
        Assert.True(normalized.IsIdentity);
    }

    [Fact]
    public void FrameContext_uses_normalized_default_scale()
    {
        var ctx = new FrameContext(1920, 1080, default);

        Assert.Equal(1920, ctx.LogicalWidth);
        Assert.Equal(1080, ctx.LogicalHeight);
    }

    [Fact]
    public void DrawCommand_scale_normalizes_default_scale()
    {
        var command = new DrawCommand(DrawCommandKind.FillRect, Rect: new DrawRect(10, 20, 30, 40));

        var scaled = command.Scale(default);

        Assert.Equal(command, scaled);
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
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void D3D12_backend_scales_text_command_to_physical_pixels_without_mutating_resources(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);

        using var resources = FrameDrawingResources.Rent();
        var style = new TextStyle("Segoe UI", 16f, TextFontWeight.Normal, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Center, TextWrapping.NoWrap);
        var handle = resources.AddTextStyle(style);
        var text = resources.AddText("Hello");
        resources.Seal();

        var logicalCmd = new DrawCommand(
            DrawCommandKind.DrawTextRun,
            Rect: new DrawRect(10, 20, 200, 30),
            Resource: handle,
            Text: text,
            ClipBounds: new DrawRect(0, 0, 240, 80));

        using var rects = new FrameRenderList<D3D12Renderer2D.RectData>();
        using var texts = new FrameRenderList<D3D12TextRenderer.TextData>();
        _ = D3D12DrawingBackend.ExecuteCore(
            DrawingBackendClipMode.Diagnostic,
            new DrawRect(0, 0, 480, 240),
            [logicalCmd],
            resources,
            scale,
            rects,
            texts);

        var textSpan = texts.Span;
        Assert.Equal(1, textSpan.Length);
        var physicalText = textSpan[0];
        Assert.Equal(10 * scaleValue, physicalText.X);
        Assert.Equal(20 * scaleValue, physicalText.Y);
        Assert.Equal(200 * scaleValue, physicalText.Width);
        Assert.Equal(30 * scaleValue, physicalText.Height);
        Assert.Equal(16f * scaleValue, physicalText.ResolvedStyle.FontSize);
        Assert.Equal(16f, resources.ResolveTextStyle(handle).FontSize);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Clip_bounds_scale_with_display_scale(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var logical = new DrawCommand(
            DrawCommandKind.PushClipRect,
            Rect: new DrawRect(50, 100, 400, 300),
            ClipBounds: new DrawRect(10, 20, 380, 260));

        var physical = logical.Scale(scale);

        Assert.Equal(50 * scaleValue, physical.Rect.X);
        Assert.Equal(100 * scaleValue, physical.Rect.Y);
        Assert.Equal(400 * scaleValue, physical.Rect.Width);
        Assert.Equal(300 * scaleValue, physical.Rect.Height);
        Assert.Equal(10 * scaleValue, physical.ClipBounds.X);
        Assert.Equal(20 * scaleValue, physical.ClipBounds.Y);
        Assert.Equal(380 * scaleValue, physical.ClipBounds.Width);
        Assert.Equal(260 * scaleValue, physical.ClipBounds.Height);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Hit_test_target_clip_bounds_scale_with_display_scale(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var logical = new HitTestTarget(
            new PixelRectangle(100, 200, 300, 400),
            new ActionId(100),
            new PixelRectangle(50, 100, 250, 350));

        var physical = logical.Scale(scale);

        Assert.Equal((int)(100 * scaleValue), physical.Bounds.X);
        Assert.Equal((int)(200 * scaleValue), physical.Bounds.Y);
        Assert.Equal((int)(300 * scaleValue), physical.Bounds.Width);
        Assert.Equal((int)(400 * scaleValue), physical.Bounds.Height);
        Assert.Equal((int)(50 * scaleValue), physical.ClipBounds.X);
        Assert.Equal((int)(100 * scaleValue), physical.ClipBounds.Y);
        Assert.Equal((int)(250 * scaleValue), physical.ClipBounds.Width);
        Assert.Equal((int)(350 * scaleValue), physical.ClipBounds.Height);
    }

    [Theory]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Multiple_text_styles_all_scaled_consistently(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);

        using var resources = FrameDrawingResources.Rent();
        var headingStyle = new TextStyle("Segoe UI", 24f, TextFontWeight.Bold, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Center, TextWrapping.NoWrap);
        var bodyStyle = new TextStyle("Segoe UI", 14f, TextFontWeight.Normal, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Leading, TextVerticalAlignment.Top, TextWrapping.Wrap);
        var buttonStyle = new TextStyle("Segoe UI", 16f, TextFontWeight.SemiBold, TextFontStyle.Normal, TextFontStretch.Normal, TextHorizontalAlignment.Center, TextVerticalAlignment.Center, TextWrapping.NoWrap);

        var headingHandle = resources.AddTextStyle(headingStyle);
        var bodyHandle = resources.AddTextStyle(bodyStyle);
        var buttonHandle = resources.AddTextStyle(buttonStyle);

        var scaledHeading = D3D12DrawingBackend.ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(headingHandle), scale);
        var scaledBody = D3D12DrawingBackend.ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(bodyHandle), scale);
        var scaledButton = D3D12DrawingBackend.ScaleTextStyleToPhysicalPixels(resources.ResolveTextStyle(buttonHandle), scale);

        Assert.Equal(24f * scaleValue, scaledHeading.FontSize);
        Assert.Equal(14f * scaleValue, scaledBody.FontSize);
        Assert.Equal(16f * scaleValue, scaledButton.FontSize);

        Assert.Equal(TextFontWeight.Bold, scaledHeading.FontWeight);
        Assert.Equal(TextWrapping.Wrap, scaledBody.Wrapping);
        Assert.Equal(TextHorizontalAlignment.Center, scaledButton.HorizontalAlignment);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2.0f)]
    public void Logical_viewport_matches_physical_divided_by_scale(float scaleValue)
    {
        var scale = new DisplayScale(scaleValue, scaleValue);
        var physical = new PixelRectangle(0, 0, 1920, 1080);
        var ctx = new FrameContext(physical.Width, physical.Height, scale);

        var expectedW = scale.IsIdentity ? 1920 : (int)(1920 / scaleValue);
        var expectedH = scale.IsIdentity ? 1080 : (int)(1080 / scaleValue);

        Assert.Equal(expectedW, ctx.LogicalWidth);
        Assert.Equal(expectedH, ctx.LogicalHeight);
    }
}
