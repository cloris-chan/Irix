namespace Irix.Platform.Windows;

internal sealed class WindowsPlatformWindow : INativeWindow
{
    private readonly Action _disposeCallback;
    private readonly Task _closedTask;
    private readonly WindowsNativeWindow _window;
    private readonly WindowsPlatformThread _platformThread;

    private bool _isDisposed;

    public WindowsPlatformWindow(
        WindowsPlatformThread platformThread,
        WindowsNativeWindow window,
        Task closedTask,
        Action disposeCallback)
    {
        _platformThread = platformThread;
        _window = window;
        _closedTask = closedTask;
        _disposeCallback = disposeCallback;
    }

    public string Title => _window.Title;

    public ScreenRegion Region => _window.Region;

    public nint Handle => _window.Handle;

    public void SetContentText(string text)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _platformThread.Invoke(() => _window.SetContentText(text));
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
