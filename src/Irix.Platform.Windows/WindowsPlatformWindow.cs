using Irix.Drawing;

namespace Irix.Platform.Windows;

internal sealed class WindowsPlatformWindow(
    WindowsPlatformThread platformThread,
    WindowsNativeWindow window,
    Task closedTask,
    Action disposeCallback) : INativeWindow
{
    private readonly Action _disposeCallback = disposeCallback;
    private readonly Task _closedTask = closedTask;
    private readonly WindowsNativeWindow _window = window;
    private readonly WindowsPlatformThread _platformThread = platformThread;

    private bool _isDisposed;

    public string Title => _window.Title;

    public ScreenRegion Region
    {
        get => _window.Region;
        set => _window.Region = value;
    }

    public bool ExternalRenderingEnabled
    {
        get => _window.ExternalRenderingEnabled;
        set => _window.ExternalRenderingEnabled = value;
    }

    public nint Handle => _window.Handle;

    public void SetContentElements(IReadOnlyList<WindowContentElement> elements)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _platformThread.Invoke(() => _window.SetContentElements(elements));
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _platformThread.Invoke(_window.Show);
    }

    public void RunMessageLoop()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _closedTask.GetAwaiter().GetResult();
    }

    public event Action<int, int>? SizeChanged
    {
        add => _window.SizeChanged += value;
        remove => _window.SizeChanged -= value;
    }

    public event Action<DisplayScale>? DpiChanged
    {
        add => _window.DpiChanged += value;
        remove => _window.DpiChanged -= value;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _platformThread.Invoke(_window.Dispose);
        _disposeCallback();
        _isDisposed = true;
    }
}
