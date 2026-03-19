using System.Runtime.InteropServices;

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

public enum ColorSpace
{
    Srgb,
    DisplayP3,
    Hdr10
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

public interface INativeWindow : IDisposable
{
    string Title { get; }

    ScreenRegion Region { get; }

    nint Handle { get; }

    void SetContentText(string text);

    void Show();

    void RunMessageLoop();
}

public interface IScreenInfo
{
    int Id { get; }

    float DpiScale { get; }

    int RefreshRateHz { get; }

    ColorSpace ColorSpace { get; }

    PixelRectangle PhysicalBounds { get; }
}

public sealed class ScreenInfo : IScreenInfo
{
    public required int Id { get; init; }

    public required float DpiScale { get; init; }

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
