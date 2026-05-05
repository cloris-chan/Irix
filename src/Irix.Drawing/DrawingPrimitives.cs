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

public readonly record struct DrawRect(float X, float Y, float Width, float Height);

public readonly record struct DrawColor(byte A, byte R, byte G, byte B)
{
    public static DrawColor Transparent => new(0, 0, 0, 0);

    public static DrawColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);
}

public readonly record struct ResourceHandle(int Id, DrawingResourceKind Kind)
{
    public static ResourceHandle None => default;
}

/// <summary>
/// Parallel text content entry referenced by <see cref="DrawCommand.Resource"/> on DrawTextRun commands.
/// Kept outside DrawCommand to maintain blittable/serializable command structure.
/// </summary>
public readonly record struct TextRunEntry(int Id, string Text);

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
    float StrokeWidth = 1,
    Matrix3x2 Transform = default,
    int ZIndex = 0);
