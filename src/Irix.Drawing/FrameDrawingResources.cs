using System.Buffers;

namespace Irix.Drawing;

public interface IFrameResourceResolver : ITextResolver
{
    TextStyle ResolveTextStyle(ResourceHandle handle);
}

public sealed class FrameDrawingResources : IFrameResourceResolver, IDisposable
{
    private const int MaxPoolSize = 4;

    public static IFrameResourceResolver Empty { get; } = new EmptyFrameResourceResolver();

    private static readonly object PoolLock = new();
    private static readonly Queue<FrameDrawingResources> Pool = new();

    private readonly FrameTextArena _textArena = new();
    private readonly List<TextStyle> _textStyles = [];
    private readonly Dictionary<TextStyle, ResourceHandle> _textStyleHandles = [];
    private bool _sealed;
    private bool _returned;

    public static FrameDrawingResources Rent()
    {
        lock (PoolLock)
        {
            if (Pool.Count > 0)
            {
                var instance = Pool.Dequeue();
                instance._returned = false;
                return instance;
            }
        }

        return new FrameDrawingResources();
    }

    public static void Return(FrameDrawingResources resources)
    {
        if (resources._returned)
        {
            return;
        }

        resources._returned = true;
        resources._textArena.Reset();
        resources._textStyles.Clear();
        resources._textStyleHandles.Clear();
        resources._sealed = false;

        lock (PoolLock)
        {
            if (Pool.Count < MaxPoolSize)
            {
                Pool.Enqueue(resources);
            }
            else
            {
                resources._textArena.Dispose();
            }
        }
    }

    public TextSlice AddText(string? text)
    {
        EnsureCanAdd();
        return _textArena.Add(text);
    }

    public TextSlice AddText(ReadOnlySpan<char> text)
    {
        EnsureCanAdd();
        return _textArena.Add(text);
    }

    public ResourceHandle AddTextStyle(TextStyle style)
    {
        EnsureCanAdd();

        style = style.Normalize();
        if (_textStyleHandles.TryGetValue(style, out var existingHandle))
        {
            return existingHandle;
        }

        var handle = new ResourceHandle(_textStyles.Count, DrawingResourceKind.TextStyle);
        _textStyles.Add(style);
        _textStyleHandles.Add(style, handle);
        return handle;
    }

    public void Seal()
    {
        if (_sealed)
        {
            return;
        }

        _textArena.Seal();
        _sealed = true;
    }

    public ReadOnlySpan<char> Resolve(TextSlice slice) => _textArena.Resolve(slice);

    public TextStyle ResolveTextStyle(ResourceHandle handle)
    {
        if (handle.Kind != DrawingResourceKind.TextStyle
            || (uint)handle.Id >= (uint)_textStyles.Count)
        {
            return TextStyle.Default;
        }

        return _textStyles[handle.Id];
    }

    public void Reset()
    {
        _textArena.Reset();
        _textStyles.Clear();
        _textStyleHandles.Clear();
        _sealed = false;
        _returned = false;
    }

    public void Dispose()
    {
        _textArena.Dispose();
        _textStyles.Clear();
        _textStyleHandles.Clear();
        _sealed = false;
        _returned = false;
    }

    private void EnsureCanAdd()
    {
        if (_sealed)
        {
            throw new InvalidOperationException("Cannot add resources after the frame resource set has been sealed.");
        }
    }

    private sealed class EmptyFrameResourceResolver : IFrameResourceResolver
    {
        public ReadOnlySpan<char> Resolve(TextSlice slice) => default;

        public TextStyle ResolveTextStyle(ResourceHandle handle) => TextStyle.Default;
    }
}