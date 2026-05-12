namespace Irix.Platform.Windows;

public sealed class WindowsPlatformHost : IPlatformHost
{
    private readonly ObservableStream<RawInputEvent> _rawInputEvents = new();
    private readonly Lock _screensLock = new();
    private readonly Lock _windowsLock = new();
    private readonly List<WindowsPlatformWindow> _windows = [];
    private readonly WindowsPlatformThread _platformThread = new();

    private IReadOnlyList<IScreenInfo> _screens;
    private bool _isDisposed;

    public WindowsPlatformHost()
    {
        _screens = _platformThread.Invoke(WindowsScreenEnumerator.Enumerate);
    }

    public IObservable<RawInputEvent> RawInputEvents => _rawInputEvents;

    public IReadOnlyList<IScreenInfo> Screens
    {
        get
        {
            lock (_screensLock)
            {
                return _screens;
            }
        }
    }

    public event EventHandler<ScreenTopologyChangedEventArgs>? TopologyChanged;

    public INativeWindow CreateSubViewport(ScreenRegion region)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var closedTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = _platformThread.Invoke(() => new WindowsNativeWindow(
            $"Irix Screen {region.ScreenId}",
            region,
            PublishInput,
            () => closedTaskSource.TrySetResult(),
            PublishTopologyChanged,
            dpiChangedSink: null));

        WindowsPlatformWindow? platformWindow = null;
        platformWindow = new WindowsPlatformWindow(
            _platformThread,
            window,
            closedTaskSource.Task,
            () =>
            {
                if (platformWindow is not null)
                {
                    RemoveWindow(platformWindow);
                }
            });

        lock (_windowsLock)
        {
            _windows.Add(platformWindow);
        }

        return platformWindow;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        WindowsPlatformWindow[] windows;
        lock (_windowsLock)
        {
            windows = [.. _windows];
            _windows.Clear();
        }

        foreach (var window in windows)
        {
            window.Dispose();
        }

        _rawInputEvents.Complete();
        _platformThread.Dispose();
        _isDisposed = true;
    }

    public void PublishInput(RawInputEvent inputEvent)
    {
        _rawInputEvents.Publish(inputEvent);
    }

    public void PublishTopologyChanged()
    {
        var screens = _platformThread.Invoke(WindowsScreenEnumerator.Enumerate);

        lock (_screensLock)
        {
            if (AreSameScreens(_screens, screens))
            {
                return;
            }

            _screens = screens;
        }

        TopologyChanged?.Invoke(this, new ScreenTopologyChangedEventArgs(screens));
    }

    private void RemoveWindow(WindowsPlatformWindow window)
    {
        lock (_windowsLock)
        {
            _windows.Remove(window);
        }
    }

    private static bool AreSameScreens(IReadOnlyList<IScreenInfo> left, IReadOnlyList<IScreenInfo> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftScreen = left[index];
            var rightScreen = right[index];

            if (leftScreen.Id != rightScreen.Id
                || leftScreen.DpiScale != rightScreen.DpiScale
                || leftScreen.RefreshRateHz != rightScreen.RefreshRateHz
                || leftScreen.ColorSpace != rightScreen.ColorSpace
                || leftScreen.PhysicalBounds != rightScreen.PhysicalBounds)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ObservableStream<T> : IObservable<T>
    {
        private readonly Lock _observersLock = new();
        private readonly List<IObserver<T>> _observers = [];
        private bool _isCompleted;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            lock (_observersLock)
            {
                if (_isCompleted)
                {
                    observer.OnCompleted();
                    return DisposableSubscription.Instance;
                }

                _observers.Add(observer);
            }

            return new Subscription(this, observer);
        }

        public void Publish(T value)
        {
            IObserver<T>[] observers;
            lock (_observersLock)
            {
                if (_isCompleted)
                {
                    return;
                }

                observers = [.. _observers];
            }

            foreach (var observer in observers)
            {
                observer.OnNext(value);
            }
        }

        public void Complete()
        {
            IObserver<T>[] observers;
            lock (_observersLock)
            {
                if (_isCompleted)
                {
                    return;
                }

                _isCompleted = true;
                observers = [.. _observers];
                _observers.Clear();
            }

            foreach (var observer in observers)
            {
                observer.OnCompleted();
            }
        }

        private void Unsubscribe(IObserver<T> observer)
        {
            lock (_observersLock)
            {
                if (_isCompleted)
                {
                    return;
                }

                _observers.Remove(observer);
            }
        }

        private sealed class Subscription(ObservableStream<T> stream, IObserver<T> observer) : IDisposable
        {
            private bool _isDisposed;

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                stream.Unsubscribe(observer);
                _isDisposed = true;
            }
        }

        private sealed class DisposableSubscription : IDisposable
        {
            public static DisposableSubscription Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
