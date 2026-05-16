namespace Irix.Drawing;

public interface IFrameResourceResolver : ITextResolver
{
    TextStyle ResolveTextStyle(ResourceHandle handle);
}

public sealed class FrameDrawingResources : IFrameResourceResolver, IDisposable
{
    private const int MaxPoolSize = 4;

    public static IFrameResourceResolver Empty { get; } = new EmptyFrameResourceResolver();

    private static readonly Lock PoolLock = new();
    private static readonly Queue<FrameDrawingResources> Pool = new();
    private static long _rentCount;
    private static long _createdCount;
    private static long _reusedCount;
    private static long _returnCallCount;
    private static long _returnedToPoolCount;
    private static long _retainedReturnSkipCount;
    private static long _duplicateReturnSkipCount;
    private static long _staleReturnSkipCount;
    private static long _disposedOverflowCount;

    private readonly FrameTextArena _textArena = new();
    private readonly List<TextStyle> _textStyles = [];
    private readonly Dictionary<TextStyle, ResourceHandle> _textStyleHandles = [];
    private bool _sealed;
    private bool _returned;
    private int _retainCount;
    private ulong _frameId;

    /// <summary>
    /// Monotonically increasing frame identifier, incremented each time this instance
    /// is rented from the pool. Used by <see cref="Irix.Rendering.RetainedRenderFrame"/>
    /// to verify that two references to the same <see cref="FrameDrawingResources"/> instance
    /// actually belong to the same rental cycle (same frame scope).
    /// </summary>
    internal ulong FrameId => _frameId;

    internal static FrameDrawingResourcesPoolDiagnostics GetPoolDiagnostics()
    {
        lock (PoolLock)
        {
            return new FrameDrawingResourcesPoolDiagnostics(
                System.Threading.Interlocked.Read(ref _rentCount),
                System.Threading.Interlocked.Read(ref _createdCount),
                System.Threading.Interlocked.Read(ref _reusedCount),
                System.Threading.Interlocked.Read(ref _returnCallCount),
                System.Threading.Interlocked.Read(ref _returnedToPoolCount),
                System.Threading.Interlocked.Read(ref _retainedReturnSkipCount),
                System.Threading.Interlocked.Read(ref _duplicateReturnSkipCount),
                System.Threading.Interlocked.Read(ref _staleReturnSkipCount),
                System.Threading.Interlocked.Read(ref _disposedOverflowCount),
                Pool.Count);
        }
    }

    public static FrameDrawingResources Rent()
    {
        System.Threading.Interlocked.Increment(ref _rentCount);
        lock (PoolLock)
        {
            if (Pool.Count > 0)
            {
                System.Threading.Interlocked.Increment(ref _reusedCount);
                var instance = Pool.Dequeue();
                instance._returned = false;
                instance._sealed = false;
                instance._retainCount = 0; // defensive: clear stale retain state
                instance._textArena.Reset(); // defensive: ensure arena is unsealed
                instance._textStyles.Clear();
                instance._textStyleHandles.Clear();
                instance._frameId++;
                return instance;
            }
        }

        System.Threading.Interlocked.Increment(ref _createdCount);
        return new FrameDrawingResources { _frameId = 1 };
    }

    public static void Return(FrameDrawingResources resources)
    {
        System.Threading.Interlocked.Increment(ref _returnCallCount);
        if (resources._returned)
        {
            System.Threading.Interlocked.Increment(ref _duplicateReturnSkipCount);
            return;
        }

        // If retained by a RetainedRenderFrame, do not return to pool.
        // The retained frame will Release() when done.
        if (resources._retainCount > 0)
        {
            System.Threading.Interlocked.Increment(ref _retainedReturnSkipCount);
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
                System.Threading.Interlocked.Increment(ref _returnedToPoolCount);
            }
            else
            {
                resources._textArena.Dispose();
                System.Threading.Interlocked.Increment(ref _disposedOverflowCount);
            }
        }
    }

    internal static void Return(FrameDrawingResources resources, ulong expectedFrameId)
    {
        if (expectedFrameId != 0 && resources._frameId != expectedFrameId)
        {
            System.Threading.Interlocked.Increment(ref _staleReturnSkipCount);
            return;
        }

        Return(resources);
    }

    /// <summary>
    /// Mark this instance as retained by an owner such as <see cref="Irix.Rendering.RetainedRenderFrame"/>.
    /// While retained, <see cref="Return"/> is a no-op, preventing the pool from recycling
    /// the instance while any retained frame still holds TextSlice references into it.
    /// </summary>
    internal void Retain()
    {
        if (_returned)
        {
            throw new InvalidOperationException("Cannot retain FrameDrawingResources after it has been returned to the pool.");
        }

        checked
        {
            _retainCount++;
        }
    }

    /// <summary>
    /// Release one retained claim. The final release returns the resources to the pool
    /// if they have not already been returned.
    /// </summary>
    internal void Release()
    {
        if (_retainCount == 0)
        {
            return;
        }

        _retainCount--;

        if (_retainCount == 0 && !_returned)
        {
            Return(this);
        }
    }

    internal void Release(ulong expectedFrameId)
    {
        if (expectedFrameId != 0 && _frameId != expectedFrameId)
        {
            System.Threading.Interlocked.Increment(ref _staleReturnSkipCount);
            return;
        }

        Release();
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
        if (_retainCount > 0)
        {
            throw new InvalidOperationException(
                "Cannot reset FrameDrawingResources while it is retained by a RetainedRenderFrame. " +
                "Call Release() first.");
        }

        _textArena.Reset();
        _textStyles.Clear();
        _textStyleHandles.Clear();
        _sealed = false;
        _returned = false;
    }

    public void Dispose()
    {
        if (_retainCount > 0)
        {
            // Safety: release retained claim before disposing to avoid pool corruption.
            // This handles the case where a caller disposes resources that are still
            // retained by a RetainedRenderFrame.
            _retainCount = 0;
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

    internal readonly struct FrameDrawingResourcesPoolDiagnostics(
        long RentCount,
        long CreatedCount,
        long ReusedCount,
        long ReturnCallCount,
        long ReturnedToPoolCount,
        long RetainedReturnSkipCount,
        long DuplicateReturnSkipCount,
        long StaleReturnSkipCount,
        long DisposedOverflowCount,
        int PoolCount) : IEquatable<FrameDrawingResourcesPoolDiagnostics>
    {
        public long RentCount { get; } = RentCount;
        public long CreatedCount { get; } = CreatedCount;
        public long ReusedCount { get; } = ReusedCount;
        public long ReturnCallCount { get; } = ReturnCallCount;
        public long ReturnedToPoolCount { get; } = ReturnedToPoolCount;
        public long RetainedReturnSkipCount { get; } = RetainedReturnSkipCount;
        public long DuplicateReturnSkipCount { get; } = DuplicateReturnSkipCount;
        public long StaleReturnSkipCount { get; } = StaleReturnSkipCount;
        public long DisposedOverflowCount { get; } = DisposedOverflowCount;
        public int PoolCount { get; } = PoolCount;

        public FrameDrawingResourcesPoolDiagnostics Delta(FrameDrawingResourcesPoolDiagnostics before) => new(
            RentCount - before.RentCount,
            CreatedCount - before.CreatedCount,
            ReusedCount - before.ReusedCount,
            ReturnCallCount - before.ReturnCallCount,
            ReturnedToPoolCount - before.ReturnedToPoolCount,
            RetainedReturnSkipCount - before.RetainedReturnSkipCount,
            DuplicateReturnSkipCount - before.DuplicateReturnSkipCount,
            StaleReturnSkipCount - before.StaleReturnSkipCount,
            DisposedOverflowCount - before.DisposedOverflowCount,
            PoolCount);

        public bool Equals(FrameDrawingResourcesPoolDiagnostics other)
        {
            return RentCount == other.RentCount
                && CreatedCount == other.CreatedCount
                && ReusedCount == other.ReusedCount
                && ReturnCallCount == other.ReturnCallCount
                && ReturnedToPoolCount == other.ReturnedToPoolCount
                && RetainedReturnSkipCount == other.RetainedReturnSkipCount
                && DuplicateReturnSkipCount == other.DuplicateReturnSkipCount
                && StaleReturnSkipCount == other.StaleReturnSkipCount
                && DisposedOverflowCount == other.DisposedOverflowCount
                && PoolCount == other.PoolCount;
        }

        public override bool Equals(object? obj) => obj is FrameDrawingResourcesPoolDiagnostics other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(RentCount);
            hash.Add(CreatedCount);
            hash.Add(ReusedCount);
            hash.Add(ReturnCallCount);
            hash.Add(ReturnedToPoolCount);
            hash.Add(RetainedReturnSkipCount);
            hash.Add(DuplicateReturnSkipCount);
            hash.Add(StaleReturnSkipCount);
            hash.Add(DisposedOverflowCount);
            hash.Add(PoolCount);
            return hash.ToHashCode();
        }

        public static bool operator ==(FrameDrawingResourcesPoolDiagnostics left, FrameDrawingResourcesPoolDiagnostics right) => left.Equals(right);

        public static bool operator !=(FrameDrawingResourcesPoolDiagnostics left, FrameDrawingResourcesPoolDiagnostics right) => !left.Equals(right);
    }
}
