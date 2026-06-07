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

internal readonly struct DrawPoint(float X, float Y) : IEquatable<DrawPoint>
{

    public float X { get; } = X;
    public float Y { get; } = Y;

    public bool Equals(DrawPoint other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) => obj is DrawPoint other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(DrawPoint left, DrawPoint right) => left.Equals(right);

    public static bool operator !=(DrawPoint left, DrawPoint right) => !left.Equals(right);
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

internal enum ColorOutputKind : byte
{
    SdrSrgb
}

[Flags]
internal enum DrawMaterialBackendCapabilities : byte
{
    None = 0,
    SolidColor = 1,
    LinearGradient = 2
}

internal enum DrawMaterialFallbackReason : byte
{
    None,
    UnsupportedNonSolidMaterial,
    UnsupportedMaterialKind
}

internal readonly struct DrawMaterialOutputMappingResult(
    DrawColor Color,
    DrawMaterialKind MaterialKind,
    DrawMaterialBackendCapabilities BackendCapabilities,
    DrawMaterialFallbackReason FallbackReason) : IEquatable<DrawMaterialOutputMappingResult>
{
    public DrawColor Color { get; } = Color;
    public DrawMaterialKind MaterialKind { get; } = MaterialKind;
    public DrawMaterialBackendCapabilities BackendCapabilities { get; } = BackendCapabilities;
    public DrawMaterialFallbackReason FallbackReason { get; } = FallbackReason;
    public bool FallbackApplied => FallbackReason != DrawMaterialFallbackReason.None;

    public bool Equals(DrawMaterialOutputMappingResult other)
    {
        return Color == other.Color
            && MaterialKind == other.MaterialKind
            && BackendCapabilities == other.BackendCapabilities
            && FallbackReason == other.FallbackReason;
    }

    public override bool Equals(object? obj) => obj is DrawMaterialOutputMappingResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Color, MaterialKind, BackendCapabilities, FallbackReason);

    public static bool operator ==(DrawMaterialOutputMappingResult left, DrawMaterialOutputMappingResult right) => left.Equals(right);

    public static bool operator !=(DrawMaterialOutputMappingResult left, DrawMaterialOutputMappingResult right) => !left.Equals(right);
}

internal readonly struct DrawMaterialOutputDiagnostics(
    ColorOutputKind OutputKind,
    DrawMaterialBackendCapabilities BackendCapabilities,
    DrawMaterialKind SelectedMaterialKind,
    DrawMaterialFallbackReason FallbackReason,
    int CommandCount,
    int SolidColorCommandCount,
    int LinearGradientCommandCount,
    int FallbackCommandCount) : IEquatable<DrawMaterialOutputDiagnostics>
{
    public ColorOutputKind OutputKind { get; } = OutputKind;
    public DrawMaterialBackendCapabilities BackendCapabilities { get; } = BackendCapabilities;
    public DrawMaterialKind SelectedMaterialKind { get; } = SelectedMaterialKind;
    public DrawMaterialFallbackReason FallbackReason { get; } = FallbackReason;
    public int CommandCount { get; } = CommandCount;
    public int SolidColorCommandCount { get; } = SolidColorCommandCount;
    public int LinearGradientCommandCount { get; } = LinearGradientCommandCount;
    public int FallbackCommandCount { get; } = FallbackCommandCount;
    public bool FallbackApplied => FallbackCommandCount > 0;

    public bool Equals(DrawMaterialOutputDiagnostics other)
    {
        return OutputKind == other.OutputKind
            && BackendCapabilities == other.BackendCapabilities
            && SelectedMaterialKind == other.SelectedMaterialKind
            && FallbackReason == other.FallbackReason
            && CommandCount == other.CommandCount
            && SolidColorCommandCount == other.SolidColorCommandCount
            && LinearGradientCommandCount == other.LinearGradientCommandCount
            && FallbackCommandCount == other.FallbackCommandCount;
    }

    public override bool Equals(object? obj) => obj is DrawMaterialOutputDiagnostics other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(
            OutputKind,
            BackendCapabilities,
            SelectedMaterialKind,
            FallbackReason,
            CommandCount,
            SolidColorCommandCount,
            LinearGradientCommandCount,
            FallbackCommandCount);
    }

    public static bool operator ==(DrawMaterialOutputDiagnostics left, DrawMaterialOutputDiagnostics right) => left.Equals(right);

    public static bool operator !=(DrawMaterialOutputDiagnostics left, DrawMaterialOutputDiagnostics right) => !left.Equals(right);
}

internal readonly struct ColorOutputMapping(ColorOutputKind Kind) : IEquatable<ColorOutputMapping>
{

    public ColorOutputKind Kind { get; } = Kind;

    public static ColorOutputMapping SdrSrgb => new(ColorOutputKind.SdrSrgb);

    public DrawColor MapToSdr(Color color)
    {
        var srgb = color.ToSrgb();
        return new DrawColor(srgb.A, srgb.R, srgb.G, srgb.B);
    }

    public DrawColor MapToSdr(DrawMaterial material) => MapToSdr(material.FallbackColor);

    internal DrawMaterialOutputMappingResult MapToSdr(
        DrawMaterial material,
        DrawMaterialBackendCapabilities backendCapabilities)
    {
        return new DrawMaterialOutputMappingResult(
            MapToSdr(material),
            material.Kind,
            backendCapabilities,
            ResolveFallbackReason(material.Kind, backendCapabilities));
    }

    public DrawColor MapToSdr(in DrawCommand command) => MapToSdr(command.Material);

    public bool Equals(ColorOutputMapping other) => Kind == other.Kind;

    public override bool Equals(object? obj) => obj is ColorOutputMapping other && Equals(other);

    public override int GetHashCode() => Kind.GetHashCode();

    public static bool operator ==(ColorOutputMapping left, ColorOutputMapping right) => left.Equals(right);

    public static bool operator !=(ColorOutputMapping left, ColorOutputMapping right) => !left.Equals(right);

    private static DrawMaterialFallbackReason ResolveFallbackReason(
        DrawMaterialKind materialKind,
        DrawMaterialBackendCapabilities backendCapabilities)
    {
        return materialKind switch
        {
            DrawMaterialKind.None => DrawMaterialFallbackReason.None,
            DrawMaterialKind.SolidColor => (backendCapabilities & DrawMaterialBackendCapabilities.SolidColor) != DrawMaterialBackendCapabilities.None
                ? DrawMaterialFallbackReason.None
                : DrawMaterialFallbackReason.UnsupportedMaterialKind,
            DrawMaterialKind.LinearGradient => (backendCapabilities & DrawMaterialBackendCapabilities.LinearGradient) != DrawMaterialBackendCapabilities.None
                ? DrawMaterialFallbackReason.None
                : DrawMaterialFallbackReason.UnsupportedNonSolidMaterial,
            _ => DrawMaterialFallbackReason.UnsupportedMaterialKind
        };
    }
}

internal enum DrawMaterialKind : byte
{
    None,
    SolidColor,
    LinearGradient
}

internal readonly struct DrawMaterial(
    DrawMaterialKind Kind,
    Color Color,
    Color EndColor = default,
    DrawPoint StartPoint = default,
    DrawPoint EndPoint = default) : IEquatable<DrawMaterial>
{

    public DrawMaterialKind Kind { get; } = Kind;
    public Color Color { get; } = Color;
    public Color EndColor { get; } = EndColor;
    public DrawPoint StartPoint { get; } = StartPoint;
    public DrawPoint EndPoint { get; } = EndPoint;

    public static DrawMaterial None => default;

    public static DrawMaterial SolidColor(Color color) => new(DrawMaterialKind.SolidColor, color);

    public static DrawMaterial LinearGradient(Color startColor, Color endColor, DrawPoint startPoint, DrawPoint endPoint) =>
        new(DrawMaterialKind.LinearGradient, startColor, endColor, startPoint, endPoint);

    public DrawMaterial WithOpacity(float opacity) =>
        Kind switch
        {
            DrawMaterialKind.SolidColor => SolidColor(Color.WithOpacity(opacity)),
            DrawMaterialKind.LinearGradient => LinearGradient(
                Color.WithOpacity(opacity),
                EndColor.WithOpacity(opacity),
                StartPoint,
                EndPoint),
            _ => this
        };

    internal DrawMaterial Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        if (scale.IsIdentity || Kind != DrawMaterialKind.LinearGradient)
        {
            return this;
        }

        return LinearGradient(
            Color,
            EndColor,
            new DrawPoint(StartPoint.X * scale.ScaleX, StartPoint.Y * scale.ScaleY),
            new DrawPoint(EndPoint.X * scale.ScaleX, EndPoint.Y * scale.ScaleY));
    }

    public Color FallbackColor => Kind switch
    {
        DrawMaterialKind.SolidColor => Color,
        DrawMaterialKind.LinearGradient => Average(Color, EndColor),
        _ => Irix.Color.Transparent
    };

    public bool Equals(DrawMaterial other)
    {
        return Kind == other.Kind
            && Color == other.Color
            && EndColor == other.EndColor
            && StartPoint == other.StartPoint
            && EndPoint == other.EndPoint;
    }

    public override bool Equals(object? obj) => obj is DrawMaterial other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Color, EndColor, StartPoint, EndPoint);

    public static bool operator ==(DrawMaterial left, DrawMaterial right) => left.Equals(right);

    public static bool operator !=(DrawMaterial left, DrawMaterial right) => !left.Equals(right);

    private static Color Average(Color left, Color right)
    {
        return Color.FromLinearBt2020(
            (left.LinearBt2020R + right.LinearBt2020R) * 0.5f,
            (left.LinearBt2020G + right.LinearBt2020G) * 0.5f,
            (left.LinearBt2020B + right.LinearBt2020B) * 0.5f,
            (left.A + right.A) * 0.5f);
    }
}

internal readonly struct DrawPayloadColor(Color Value) : IEquatable<DrawPayloadColor>
{

    public Color Value { get; } = Value;

    public static DrawPayloadColor Transparent => default;

    public static DrawPayloadColor FromSdr(DrawColor color) => new(Color.FromSrgb(color.A, color.R, color.G, color.B));

    public DrawColor ToSdrColor() => ColorOutputMapping.SdrSrgb.MapToSdr(Value);

    public bool Equals(DrawPayloadColor other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is DrawPayloadColor other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(DrawPayloadColor left, DrawPayloadColor right) => left.Equals(right);

    public static bool operator !=(DrawPayloadColor left, DrawPayloadColor right) => !left.Equals(right);
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

public readonly struct DrawCommand : IEquatable<DrawCommand>
{
    private readonly DrawPayloadColor _color;
    private readonly DrawMaterial _material;

    public DrawCommand(
        DrawCommandKind Kind,
        DrawRect Rect = default,
        DrawColor Color = default,
        ResourceHandle Resource = default,
        TextSlice Text = default,
        DrawRect ClipBounds = default,
        float StrokeWidth = 1,
        Matrix3x2 Transform = default,
        int ZIndex = 0)
        : this(
            Kind,
            Rect,
            DrawMaterial.SolidColor(DrawPayloadColor.FromSdr(Color).Value),
            Resource,
            Text,
            ClipBounds,
            StrokeWidth,
            Transform,
            ZIndex)
    {
    }

    private DrawCommand(
        DrawCommandKind kind,
        DrawRect rect,
        DrawMaterial material,
        ResourceHandle resource,
        TextSlice text,
        DrawRect clipBounds,
        float strokeWidth,
        Matrix3x2 transform,
        int zIndex)
    {
        Kind = kind;
        Rect = rect;
        _material = material;
        _color = new DrawPayloadColor(material.FallbackColor);
        Resource = resource;
        Text = text;
        ClipBounds = clipBounds;
        StrokeWidth = strokeWidth;
        Transform = transform;
        ZIndex = zIndex;
    }

    public DrawCommandKind Kind { get; }
    public DrawRect Rect { get; }
    public DrawColor Color => _color.ToSdrColor();
    public ResourceHandle Resource { get; }
    public TextSlice Text { get; }
    public DrawRect ClipBounds { get; }
    public float StrokeWidth { get; }
    public Matrix3x2 Transform { get; }
    public int ZIndex { get; }

    internal Color CanonicalColor => _color.Value;

    internal DrawMaterial Material => _material;

    internal static DrawCommand FromCanonicalColor(
        DrawCommandKind Kind,
        DrawRect Rect = default,
        Color Color = default,
        ResourceHandle Resource = default,
        TextSlice Text = default,
        DrawRect ClipBounds = default,
        float StrokeWidth = 1,
        Matrix3x2 Transform = default,
        int ZIndex = 0) =>
        new(Kind, Rect, DrawMaterial.SolidColor(Color), Resource, Text, ClipBounds, StrokeWidth, Transform, ZIndex);

    internal static DrawCommand FromMaterial(
        DrawCommandKind Kind,
        DrawRect Rect = default,
        DrawMaterial Material = default,
        ResourceHandle Resource = default,
        TextSlice Text = default,
        DrawRect ClipBounds = default,
        float StrokeWidth = 1,
        Matrix3x2 Transform = default,
        int ZIndex = 0) =>
        new(Kind, Rect, Material, Resource, Text, ClipBounds, StrokeWidth, Transform, ZIndex);

    internal DrawColor ToSdrColor() => _color.ToSdrColor();

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
            _material.Scale(scale),
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
            && _color == other._color
            && _material == other._material
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
        hash.Add(_color);
        hash.Add(_material);
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
