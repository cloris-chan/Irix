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

public readonly record struct WindowColor(byte A, byte R, byte G, byte B)
{
    public static WindowColor Transparent => new(0, 0, 0, 0);

    public static WindowColor Opaque(byte r, byte g, byte b) => new(255, r, g, b);
}


public readonly record struct PixelRectangle(int X, int Y, int Width, int Height);

public readonly record struct ScreenRegion(int ScreenId, PixelRectangle PhysicalBounds);

public readonly record struct RawInputEvent(
    RawInputEventKind Kind,
    long Timestamp,
    int X,
    int Y,
    int KeyCode = 0,
    PointerButton Button = PointerButton.None,
    int Delta = 0,
    char Character = '\0');

public readonly record struct WindowContentElement(
    WindowContentElementKind Kind,
    PixelRectangle Bounds,
    string? Text = null,
    WindowColor ForegroundColor = default,
    WindowColor BackgroundColor = default,
    WindowColor BorderColor = default);

public interface INativeWindow : IDisposable
{
    string Title { get; }

    ScreenRegion Region { get; set; }

    bool ExternalRenderingEnabled { get; set; }

    nint Handle { get; }

    void SetContentElements(IReadOnlyList<WindowContentElement> elements);

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
