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

public enum TextFontFamily : byte
{
    Default,
    SegoeUi
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

public readonly struct TextStyle(
    TextFontFamily FontFamily,
    float FontSize,
    TextFontWeight FontWeight,
    TextFontStyle FontStyle,
    TextFontStretch FontStretch,
    TextHorizontalAlignment HorizontalAlignment,
    TextVerticalAlignment VerticalAlignment,
    TextWrapping Wrapping) : IEquatable<TextStyle>
{

    public TextFontFamily FontFamily { get; } = FontFamily;
    public float FontSize { get; } = FontSize;
    public TextFontWeight FontWeight { get; } = FontWeight;
    public TextFontStyle FontStyle { get; } = FontStyle;
    public TextFontStretch FontStretch { get; } = FontStretch;
    public TextHorizontalAlignment HorizontalAlignment { get; } = HorizontalAlignment;
    public TextVerticalAlignment VerticalAlignment { get; } = VerticalAlignment;
    public TextWrapping Wrapping { get; } = Wrapping;

    public static TextStyle Default => new(
        FontFamily: TextFontFamily.SegoeUi,
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
        return new TextStyle(
            FontFamily == TextFontFamily.Default ? defaultStyle.FontFamily : FontFamily,
            FontSize > 0 && float.IsFinite(FontSize) ? FontSize : defaultStyle.FontSize,
            FontWeight,
            FontStyle,
            FontStretch,
            HorizontalAlignment,
            VerticalAlignment,
            Wrapping);
    }

    public bool Equals(TextStyle other)
    {
        return FontFamily == other.FontFamily
            && FontSize.Equals(other.FontSize)
            && FontWeight == other.FontWeight
            && FontStyle == other.FontStyle
            && FontStretch == other.FontStretch
            && HorizontalAlignment == other.HorizontalAlignment
            && VerticalAlignment == other.VerticalAlignment
            && Wrapping == other.Wrapping;
    }

    public override bool Equals(object? obj) => obj is TextStyle other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            FontFamily,
            FontSize,
            FontWeight,
            FontStyle,
            FontStretch,
            HorizontalAlignment,
            VerticalAlignment,
            Wrapping);
    }

    public static bool operator ==(TextStyle left, TextStyle right) => left.Equals(right);

    public static bool operator !=(TextStyle left, TextStyle right) => !left.Equals(right);
}

public readonly struct DrawRect(float X, float Y, float Width, float Height) : IEquatable<DrawRect>
{

    public float X { get; } = X;
    public float Y { get; } = Y;
    public float Width { get; } = Width;
    public float Height { get; } = Height;

    public bool Equals(DrawRect other)
    {
        return X.Equals(other.X)
            && Y.Equals(other.Y)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj) => obj is DrawRect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(DrawRect left, DrawRect right) => left.Equals(right);

    public static bool operator !=(DrawRect left, DrawRect right) => !left.Equals(right);
}

public readonly struct DrawColor(byte A, byte R, byte G, byte B) : IEquatable<DrawColor>
{

    public byte A { get; } = A;
    public byte R { get; } = R;
    public byte G { get; } = G;
    public byte B { get; } = B;

    public static DrawColor Transparent => new(0, 0, 0, 0);

    public static DrawColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    public bool Equals(DrawColor other)
    {
        return A == other.A
            && R == other.R
            && G == other.G
            && B == other.B;
    }

    public override bool Equals(object? obj) => obj is DrawColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, R, G, B);

    public static bool operator ==(DrawColor left, DrawColor right) => left.Equals(right);

    public static bool operator !=(DrawColor left, DrawColor right) => !left.Equals(right);
}

public readonly struct ResourceHandle(int Id, DrawingResourceKind Kind) : IEquatable<ResourceHandle>
{

    public int Id { get; } = Id;
    public DrawingResourceKind Kind { get; } = Kind;

    public static ResourceHandle None => default;

    public bool IsValid => Kind != DrawingResourceKind.None && Id >= 0;

    public bool Equals(ResourceHandle other) => Id == other.Id && Kind == other.Kind;

    public override bool Equals(object? obj) => obj is ResourceHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Kind);

    public static bool operator ==(ResourceHandle left, ResourceHandle right) => left.Equals(right);

    public static bool operator !=(ResourceHandle left, ResourceHandle right) => !left.Equals(right);
}

public readonly struct TextSlice(int BufferId, int Start, int Length) : IEquatable<TextSlice>
{

    public int BufferId { get; } = BufferId;
    public int Start { get; } = Start;
    public int Length { get; } = Length;

    public bool IsValid => BufferId > 0 && Start >= 0 && Length >= 0;

    public bool Equals(TextSlice other)
    {
        return BufferId == other.BufferId
            && Start == other.Start
            && Length == other.Length;
    }

    public override bool Equals(object? obj) => obj is TextSlice other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(BufferId, Start, Length);

    public static bool operator ==(TextSlice left, TextSlice right) => left.Equals(right);

    public static bool operator !=(TextSlice left, TextSlice right) => !left.Equals(right);
}

public readonly struct DisplayScale(float ScaleX, float ScaleY) : IEquatable<DisplayScale>
{

    public float ScaleX { get; } = ScaleX;
    public float ScaleY { get; } = ScaleY;

    public static DisplayScale Identity => new(1f, 1f);
    public bool IsIdentity => ScaleX == 1f && ScaleY == 1f;
    public float TextScale => Normalize().ScaleY;

    public DisplayScale Normalize()
    {
        var scaleX = ScaleX > 0f && float.IsFinite(ScaleX) ? ScaleX : 1f;
        var scaleY = ScaleY > 0f && float.IsFinite(ScaleY) ? ScaleY : 1f;
        return new DisplayScale(scaleX, scaleY);
    }

    public bool Equals(DisplayScale other) => ScaleX.Equals(other.ScaleX) && ScaleY.Equals(other.ScaleY);

    public override bool Equals(object? obj) => obj is DisplayScale other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ScaleX, ScaleY);

    public static bool operator ==(DisplayScale left, DisplayScale right) => left.Equals(right);

    public static bool operator !=(DisplayScale left, DisplayScale right) => !left.Equals(right);
}

public readonly struct FrameContext(int Width, int Height, DisplayScale Scale = default, long Timestamp = 0) : IEquatable<FrameContext>
{
    public int Width { get; } = Width;
    public int Height { get; } = Height;
    public DisplayScale Scale { get; } = Scale;
    public long Timestamp { get; } = Timestamp;

    public int LogicalWidth
    {
        get
        {
            var scale = Scale.Normalize();
            return scale.IsIdentity ? Width : (int)(Width / scale.ScaleX);
        }
    }

    public int LogicalHeight
    {
        get
        {
            var scale = Scale.Normalize();
            return scale.IsIdentity ? Height : (int)(Height / scale.ScaleY);
        }
    }

    public bool Equals(FrameContext other)
    {
        return Width == other.Width
            && Height == other.Height
            && Scale == other.Scale
            && Timestamp == other.Timestamp;
    }

    public override bool Equals(object? obj) => obj is FrameContext other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Width, Height, Scale, Timestamp);

    public static bool operator ==(FrameContext left, FrameContext right) => left.Equals(right);

    public static bool operator !=(FrameContext left, FrameContext right) => !left.Equals(right);
}

public readonly struct DrawCommand(
    DrawCommandKind Kind,
    DrawRect Rect = default,
    DrawColor Color = default,
    ResourceHandle Resource = default,
    TextSlice Text = default,
    DrawRect ClipBounds = default,
    float StrokeWidth = 1,
    Matrix3x2 Transform = default,
    int ZIndex = 0) : IEquatable<DrawCommand>
{

    public DrawCommandKind Kind { get; } = Kind;
    public DrawRect Rect { get; } = Rect;
    public DrawColor Color { get; } = Color;
    public ResourceHandle Resource { get; } = Resource;
    public TextSlice Text { get; } = Text;
    public DrawRect ClipBounds { get; } = ClipBounds;
    public float StrokeWidth { get; } = StrokeWidth;
    public Matrix3x2 Transform { get; } = Transform;
    public int ZIndex { get; } = ZIndex;

    public DrawCommand Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        if (scale.IsIdentity) return this;
        return new DrawCommand(
            Kind,
            new DrawRect(
                Rect.X * scale.ScaleX,
                Rect.Y * scale.ScaleY,
                Rect.Width * scale.ScaleX,
                Rect.Height * scale.ScaleY),
            Color,
            Resource,
            Text,
            new DrawRect(
                ClipBounds.X * scale.ScaleX,
                ClipBounds.Y * scale.ScaleY,
                ClipBounds.Width * scale.ScaleX,
                ClipBounds.Height * scale.ScaleY),
            StrokeWidth,
            Transform,
            ZIndex);
    }

    public bool Equals(DrawCommand other)
    {
        return Kind == other.Kind
            && Rect == other.Rect
            && Color == other.Color
            && Resource == other.Resource
            && Text == other.Text
            && ClipBounds == other.ClipBounds
            && StrokeWidth.Equals(other.StrokeWidth)
            && Transform.Equals(other.Transform)
            && ZIndex == other.ZIndex;
    }

    public override bool Equals(object? obj) => obj is DrawCommand other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Rect);
        hash.Add(Color);
        hash.Add(Resource);
        hash.Add(Text);
        hash.Add(ClipBounds);
        hash.Add(StrokeWidth);
        hash.Add(Transform);
        hash.Add(ZIndex);
        return hash.ToHashCode();
    }

    public static bool operator ==(DrawCommand left, DrawCommand right) => left.Equals(right);

    public static bool operator !=(DrawCommand left, DrawCommand right) => !left.Equals(right);
}
