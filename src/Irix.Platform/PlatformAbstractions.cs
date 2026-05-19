using Irix.Drawing;

namespace Irix.Platform;

public enum RawInputEventKind
{
    PointerMoved,
    PointerPressed,
    PointerReleased,
    PointerWheel,
    KeyPressed,
    KeyReleased,
    CharacterInput,
    FocusGained,
    FocusLost
}

public enum PointerButton
{
    None,
    Left,
    Right,
    Middle
}

public enum WindowContentElementKind
{
    Text,
    Rectangle,
    Button
}

public enum ColorSpace
{
    Srgb,
    DisplayP3,
    Hdr10
}

public readonly struct WindowColor(byte A, byte R, byte G, byte B) : IEquatable<WindowColor>
{

    public byte A { get; } = A;
    public byte R { get; } = R;
    public byte G { get; } = G;
    public byte B { get; } = B;

    public static WindowColor Transparent => new(0, 0, 0, 0);

    public static WindowColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    public bool Equals(WindowColor other)
    {
        return A == other.A
            && R == other.R
            && G == other.G
            && B == other.B;
    }

    public override bool Equals(object? obj) => obj is WindowColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(A, R, G, B);

    public static bool operator ==(WindowColor left, WindowColor right) => left.Equals(right);

    public static bool operator !=(WindowColor left, WindowColor right) => !left.Equals(right);
}


public readonly struct PixelRectangle(int X, int Y, int Width, int Height) : IEquatable<PixelRectangle>
{

    public int X { get; } = X;
    public int Y { get; } = Y;
    public int Width { get; } = Width;
    public int Height { get; } = Height;

    public bool Equals(PixelRectangle other)
    {
        return X == other.X
            && Y == other.Y
            && Width == other.Width
            && Height == other.Height;
    }

    public override bool Equals(object? obj) => obj is PixelRectangle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static bool operator ==(PixelRectangle left, PixelRectangle right) => left.Equals(right);

    public static bool operator !=(PixelRectangle left, PixelRectangle right) => !left.Equals(right);
}

public readonly struct ScreenRegion(int ScreenId, PixelRectangle PhysicalBounds) : IEquatable<ScreenRegion>
{

    public int ScreenId { get; } = ScreenId;
    public PixelRectangle PhysicalBounds { get; } = PhysicalBounds;

    public bool Equals(ScreenRegion other) => ScreenId == other.ScreenId && PhysicalBounds == other.PhysicalBounds;

    public override bool Equals(object? obj) => obj is ScreenRegion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ScreenId, PhysicalBounds);

    public static bool operator ==(ScreenRegion left, ScreenRegion right) => left.Equals(right);

    public static bool operator !=(ScreenRegion left, ScreenRegion right) => !left.Equals(right);
}

public readonly struct RawInputEvent(
    RawInputEventKind Kind,
    long Timestamp,
    int X,
    int Y,
    int KeyCode = 0,
    PointerButton Button = PointerButton.None,
    int Delta = 0,
    char Character = '\0') : IEquatable<RawInputEvent>
{

    public RawInputEventKind Kind { get; } = Kind;
    public long Timestamp { get; } = Timestamp;
    public int X { get; } = X;
    public int Y { get; } = Y;
    public int KeyCode { get; } = KeyCode;
    public PointerButton Button { get; } = Button;
    public int Delta { get; } = Delta;
    public char Character { get; } = Character;

    public bool Equals(RawInputEvent other)
    {
        return Kind == other.Kind
            && Timestamp == other.Timestamp
            && X == other.X
            && Y == other.Y
            && KeyCode == other.KeyCode
            && Button == other.Button
            && Delta == other.Delta
            && Character == other.Character;
    }

    public override bool Equals(object? obj) => obj is RawInputEvent other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Timestamp, X, Y, KeyCode, Button, Delta, Character);

    public static bool operator ==(RawInputEvent left, RawInputEvent right) => left.Equals(right);

    public static bool operator !=(RawInputEvent left, RawInputEvent right) => !left.Equals(right);
}

public readonly struct WindowContentElement(
    WindowContentElementKind Kind,
    PixelRectangle Bounds,
    TextSlice Text = default,
    WindowColor ForegroundColor = default,
    WindowColor BackgroundColor = default,
    WindowColor BorderColor = default) : IEquatable<WindowContentElement>
{

    public WindowContentElementKind Kind { get; } = Kind;
    public PixelRectangle Bounds { get; } = Bounds;
    public TextSlice Text { get; } = Text;
    public WindowColor ForegroundColor { get; } = ForegroundColor;
    public WindowColor BackgroundColor { get; } = BackgroundColor;
    public WindowColor BorderColor { get; } = BorderColor;

    public bool Equals(WindowContentElement other)
    {
        return Kind == other.Kind
            && Bounds == other.Bounds
            && Text == other.Text
            && ForegroundColor == other.ForegroundColor
            && BackgroundColor == other.BackgroundColor
            && BorderColor == other.BorderColor;
    }

    public override bool Equals(object? obj) => obj is WindowContentElement other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, Bounds, Text, ForegroundColor, BackgroundColor, BorderColor);

    public static bool operator ==(WindowContentElement left, WindowContentElement right) => left.Equals(right);

    public static bool operator !=(WindowContentElement left, WindowContentElement right) => !left.Equals(right);
}

public interface INativeWindow : IDisposable
{
    string Title { get; }

    ScreenRegion Region { get; set; }

    bool ExternalRenderingEnabled { get; set; }

    nint Handle { get; }

    void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver);

    void Show();

    void RunMessageLoop();

    event Action<int, int>? SizeChanged;

    event Action<DisplayScale>? DpiChanged;
}

public interface IScreenInfo
{
    int Id { get; }

    float DpiScale { get; }

    DisplayScale Scale { get; }

    int RefreshRateHz { get; }

    ColorSpace ColorSpace { get; }

    PixelRectangle PhysicalBounds { get; }
}

public sealed class ScreenInfo : IScreenInfo
{
    public required int Id { get; init; }

    public required float DpiScale { get; init; }

    private DisplayScale _scale = DisplayScale.Identity;

    public DisplayScale Scale
    {
        get => _scale;
        init => _scale = value.Normalize();
    }

    public required int RefreshRateHz { get; init; }

    public required ColorSpace ColorSpace { get; init; }

    public required PixelRectangle PhysicalBounds { get; init; }
}

public sealed class ScreenTopologyChangedEventArgs(IReadOnlyList<IScreenInfo> screens) : EventArgs
{
    public IReadOnlyList<IScreenInfo> Screens { get; } = screens;
}

public interface IPlatformHost : IDisposable
{
    IObservable<RawInputEvent> RawInputEvents { get; }

    IReadOnlyList<IScreenInfo> Screens { get; }

    event EventHandler<ScreenTopologyChangedEventArgs>? TopologyChanged;

    INativeWindow CreateSubViewport(ScreenRegion region);
}
