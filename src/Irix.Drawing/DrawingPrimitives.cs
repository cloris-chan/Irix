using System.Numerics;

namespace Irix.Drawing;

public enum DrawCommandKind : byte
{
    FillRect,
    StrokeRect,
    DrawTextRun,
    PushClipRect,
    PopClip,
    PushTransform,
    PopTransform,
    DrawPath,
    DrawImage
}

public enum DrawingResourceKind : byte
{
    None,
    TextStyle,
    Brush,
    Image,
    Path
}

public enum TextFontWeight : ushort
{
    Normal = 400,
    SemiBold = 600,
    Bold = 700
}

public enum TextFontStyle : byte
{
    Normal,
    Italic,
    Oblique
}

public enum TextFontStretch : byte
{
    Normal
}

public enum TextHorizontalAlignment : byte
{
    Leading,
    Center,
    Trailing
}

public enum TextVerticalAlignment : byte
{
    Top,
    Center,
    Bottom
}

public enum TextWrapping : byte
{
    NoWrap,
    Wrap
}

public readonly record struct TextStyle(
    string FontFamily,
    float FontSize,
    TextFontWeight FontWeight,
    TextFontStyle FontStyle,
    TextFontStretch FontStretch,
    TextHorizontalAlignment HorizontalAlignment,
    TextVerticalAlignment VerticalAlignment,
    TextWrapping Wrapping)
{
    public static TextStyle Default => new(
        FontFamily: "Segoe UI",
        FontSize: 16,
        FontWeight: TextFontWeight.Normal,
        FontStyle: TextFontStyle.Normal,
        FontStretch: TextFontStretch.Normal,
        HorizontalAlignment: TextHorizontalAlignment.Leading,
        VerticalAlignment: TextVerticalAlignment.Center,
        Wrapping: TextWrapping.NoWrap);

    public TextStyle Normalize()
    {
        var defaultStyle = Default;
        return this with
        {
            FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? defaultStyle.FontFamily : FontFamily,
            FontSize = FontSize > 0 && float.IsFinite(FontSize) ? FontSize : defaultStyle.FontSize
        };
    }
}

public readonly record struct DrawRect(float X, float Y, float Width, float Height);

public readonly record struct DrawColor(byte A, byte R, byte G, byte B)
{
    public static DrawColor Transparent => new(0, 0, 0, 0);

    public static DrawColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);
}

public readonly record struct ResourceHandle(int Id, DrawingResourceKind Kind)
{
    public static ResourceHandle None => default;

    public bool IsValid => Kind != DrawingResourceKind.None && Id >= 0;
}

public readonly record struct TextSlice(int BufferId, int Start, int Length)
{
    public bool IsValid => BufferId > 0 && Start >= 0 && Length >= 0;
}

public readonly record struct FrameContext(
    int Width,
    int Height,
    float DpiScale = 1,
    long Timestamp = 0);

public readonly record struct DrawCommand(
    DrawCommandKind Kind,
    DrawRect Rect = default,
    DrawColor Color = default,
    ResourceHandle Resource = default,
    TextSlice Text = default,
    DrawRect ClipBounds = default,
    float StrokeWidth = 1,
    Matrix3x2 Transform = default,
    int ZIndex = 0);
