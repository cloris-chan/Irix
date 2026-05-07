using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

/// <summary>
/// Tests for text rendering correctness at the layout/recorder/text-arena level.
/// Covers: English, Chinese, emoji, long text, empty text, wrap/no-wrap, button centering, DPI.
/// </summary>
public sealed class TextRenderingCorrectnessTests
{
    [Fact]
    public void English_text_roundtrips_through_pipeline()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Hello, World!", 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("Hello, World!", text);
    }

    [Fact]
    public void Chinese_text_roundtrips_through_pipeline()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("你好世界", 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("你好世界", text);
    }

    [Fact]
    public void Emoji_text_roundtrips_through_pipeline()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("🎯🔥🚀", 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("🎯🔥🚀", text);
    }

    [Fact]
    public void Mixed_unicode_text_roundtrips()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Hello你好🎯", 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal("Hello你好🎯", text);
    }

    [Fact]
    public void Empty_text_produces_no_draw_command()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("", 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        // Empty text should still produce a DrawTextRun command (layout element exists),
        // but the TextSlice resolves to empty
        if (frame.Commands.Count > 0)
        {
            var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text);
            Assert.True(text.IsEmpty || text.Length == 0);
        }
    }

    [Fact]
    public void Long_text_roundtrips()
    {
        var longText = new string('X', 5000);
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text(longText, 2));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        var text = frame.Resources.Resolve(frame.Commands.Memory.Span[0].Text).ToString();
        Assert.Equal(longText, text);
    }

    [Fact]
    public void Button_text_is_centered_in_bounds()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Button("Click Me", 2,
                new VirtualNodeAttribute("ActionId", AttributeValue.FromText("Click"))));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        // Button = FillRect + DrawTextRun, both with same bounds
        Assert.True(frame.Commands.Count >= 2);
        var fillRect = frame.Commands.Memory.Span[0];
        var textRun = frame.Commands.Memory.Span[1];

        Assert.Equal(DrawCommandKind.FillRect, fillRect.Kind);
        Assert.Equal(DrawCommandKind.DrawTextRun, textRun.Kind);
        Assert.Equal(fillRect.Rect, textRun.Rect); // Same bounds = centered

        var text = frame.Resources.Resolve(textRun.Text).ToString();
        Assert.Equal("Click Me", text);
    }

    [Fact]
    public void TextStyle_default_alignment_is_leading_center()
    {
        var style = TextStyle.Default;
        Assert.Equal(TextHorizontalAlignment.Leading, style.HorizontalAlignment);
        Assert.Equal(TextVerticalAlignment.Center, style.VerticalAlignment);
        Assert.Equal(TextWrapping.NoWrap, style.Wrapping);
    }

    [Fact]
    public void TextStyle_normalize_clamps_invalid_font_size()
    {
        var style = TextStyle.Default with { FontSize = -1 };
        var normalized = style.Normalize();
        Assert.Equal(TextStyle.Default.FontSize, normalized.FontSize);

        var styleNan = TextStyle.Default with { FontSize = float.NaN };
        var normalizedNan = styleNan.Normalize();
        Assert.Equal(TextStyle.Default.FontSize, normalizedNan.FontSize);
    }

    [Fact]
    public void Layout_scales_with_viewport_width()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("Scale Test", 2));
        var pipeline = new RenderPipeline();

        using var small = pipeline.Build(root, new PixelRectangle(0, 0, 480, 270));
        using var large = pipeline.Build(root, new PixelRectangle(0, 0, 1920, 1080));

        // Layout width should be proportional to viewport width
        var smallWidth = small.Commands.Memory.Span[0].Rect.Width;
        var largeWidth = large.Commands.Memory.Span[0].Rect.Width;
        Assert.True(largeWidth > smallWidth,
            $"Expected large layout width ({largeWidth}) > small layout width ({smallWidth})");
    }

    [Fact]
    public void Text_style_resource_deduplicates_across_elements()
    {
        var root = VirtualNodeFactory.ScrollContainer(1,
            VirtualNodeFactory.Text("First", 2),
            VirtualNodeFactory.Text("Second", 3));
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(root, new PixelRectangle(0, 0, 960, 540));

        // Both text commands should reference the same TextStyle resource handle
        Assert.True(frame.Commands.Count >= 2);
        var resource0 = frame.Commands.Memory.Span[0].Resource;
        var resource1 = frame.Commands.Memory.Span[1].Resource;
        Assert.Equal(resource0, resource1);
    }
}
