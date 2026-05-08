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
    private bool _retained;
    private ulong _frameId;

    /// <summary>
    /// Monotonically increasing frame identifier, incremented each time this instance
    /// is rented from the pool. Used by <see cref="Irix.Rendering.RetainedRenderFrame"/>
    /// to verify that two references to the same <see cref="FrameDrawingResources"/> instance
    /// actually belong to the same rental cycle (same frame scope).
    /// </summary>
    internal ulong FrameId => _frameId;

    public static FrameDrawingResources Rent()
    {
        lock (PoolLock)
        {
            if (Pool.Count > 0)
            {
                var instance = Pool.Dequeue();
                instance._returned = false;
                instance._frameId++;
                return instance;
            }
        }

        return new FrameDrawingResources { _frameId = 1 };
    }

    public static void Return(FrameDrawingResources resources)
    {
        if (resources._returned)
        {
            return;
        }

        // If retained by a RetainedRenderFrame, do not return to pool.
        // The retained frame will Release() when done.
        if (resources._retained)
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

    /// <summary>
    /// Mark this instance as retained by a <see cref="Irix.Rendering.RetainedRenderFrame"/>.
    /// While retained, <see cref="Return"/> is a no-op, preventing the pool from recycling
    /// the instance while the retained frame still holds TextSlice references into it.
    /// </summary>
    internal void Retain()
    {
        _retained = true;
    }

    /// <summary>
    /// Release the retained claim. If the resources have not already been returned to the pool
    /// (e.g., by a disposed batch), returns them now.
    /// </summary>
    internal void Release()
    {
        if (!_retained)
        {
            return;
        }

        _retained = false;

        if (!_returned)
        {
            Return(this);
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

    /// <summary>
    /// Reset the arena and style lists for reuse. Only valid when not retained.
    /// Typically called by <see cref="Return"/> during pool recycling.
    /// </summary>
    internal void Reset()
    {
        ObjectDisposedException.ThrowIf(_returned && _retained, this);
        _textArena.Reset();
        _textStyles.Clear();
        _textStyleHandles.Clear();
        _sealed = false;
        _returned = false;
    }

    public void Dispose()
    {
        if (_retained)
        {
            // Safety: release retained claim before disposing to avoid pool corruption.
            // This handles the case where a caller disposes resources that are still
            // retained by a RetainedRenderFrame.
            Release();
        }

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