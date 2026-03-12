namespace Irix.Platform.Windows;

public sealed class WindowsPlatformHost : IPlatformHost
{
    private readonly ObservableStream<RawInputEvent> _rawInputEvents = new();

    public WindowsPlatformHost()
    {
        Screens =
        [
            new ScreenInfo
            {
                Id = 0,
                DpiScale = 1.0f,
                RefreshRateHz = 60,
                ColorSpace = ColorSpace.Srgb,
                PhysicalBounds = new PixelRectangle(0, 0, 1920, 1080)
            }
        ];
    }

    public IObservable<RawInputEvent> RawInputEvents => _rawInputEvents;

    public IReadOnlyList<IScreenInfo> Screens { get; }

    public event EventHandler<ScreenTopologyChangedEventArgs>? TopologyChanged;

    public INativeWindow CreateSubViewport(ScreenRegion region)
    {
        return new WindowsNativeWindow($"Irix Screen {region.ScreenId}", region, PublishInput);
    }

    public void Dispose()
    {
    }

    public void PublishInput(RawInputEvent inputEvent)
    {
        _rawInputEvents.Publish(inputEvent);
    }

    public void PublishTopologyChanged()
    {
        TopologyChanged?.Invoke(this, new ScreenTopologyChangedEventArgs(Screens));
    }

    private sealed class ObservableStream<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _observers.Add(observer);
            return new Subscription(_observers, observer);
        }

        public void Publish(T value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        private sealed class Subscription(List<IObserver<T>> observers, IObserver<T> observer) : IDisposable
        {
            public void Dispose()
            {
                observers.Remove(observer);
            }
        }
    }
}
