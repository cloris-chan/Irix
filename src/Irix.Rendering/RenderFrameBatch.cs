using System.Runtime.CompilerServices;
using Irix.Drawing;
using Irix.Platform;

namespace Irix.Rendering;

public readonly struct HitTestTarget(
    PixelRectangle Bounds,
    ActionId ActionId,
    PixelRectangle ClipBounds = default,
    int CommandStart = -1,
    int CommandCount = 0) : IEquatable<HitTestTarget>
{

    public PixelRectangle Bounds { get; } = Bounds;
    public ActionId ActionId { get; } = ActionId;
    public PixelRectangle ClipBounds { get; } = ClipBounds;
    public int CommandStart { get; } = CommandStart;
    public int CommandCount { get; } = CommandCount;

    internal bool HasCommandRange => CommandStart >= 0 && CommandCount > 0;

    public HitTestTarget Scale(DisplayScale scale)
    {
        scale = scale.Normalize();
        if (scale.IsIdentity) return this;
        return new HitTestTarget(
            new PixelRectangle(
                (int)(Bounds.X * scale.ScaleX),
                (int)(Bounds.Y * scale.ScaleY),
                (int)(Bounds.Width * scale.ScaleX),
                (int)(Bounds.Height * scale.ScaleY)),
            ActionId,
            new PixelRectangle(
                (int)(ClipBounds.X * scale.ScaleX),
                (int)(ClipBounds.Y * scale.ScaleY),
                (int)(ClipBounds.Width * scale.ScaleX),
                (int)(ClipBounds.Height * scale.ScaleY)),
            CommandStart,
            CommandCount);
    }

    public bool Equals(HitTestTarget other)
    {
        return Bounds == other.Bounds
            && ActionId.Equals(other.ActionId)
            && ClipBounds == other.ClipBounds
            && CommandStart == other.CommandStart
            && CommandCount == other.CommandCount;
    }

    public override bool Equals(object? obj) => obj is HitTestTarget other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Bounds, ActionId, ClipBounds, CommandStart, CommandCount);

    public static bool operator ==(HitTestTarget left, HitTestTarget right) => left.Equals(right);

    public static bool operator !=(HitTestTarget left, HitTestTarget right) => !left.Equals(right);
}

[CollectionBuilder(typeof(HitTargetListBuilder), nameof(HitTargetListBuilder.Create))]
public readonly struct HitTargetList : IReadOnlyList<HitTestTarget>, IEquatable<HitTargetList>
{
    internal const int InlineCapacity = 4;

    private readonly HitTestTarget[]? _items;
    private readonly HitTestTarget _target0;
    private readonly HitTestTarget _target1;
    private readonly HitTestTarget _target2;
    private readonly HitTestTarget _target3;
    private readonly int _count;

    private HitTargetList(
        HitTestTarget[]? items,
        HitTestTarget target0,
        HitTestTarget target1,
        HitTestTarget target2,
        HitTestTarget target3,
        int count)
    {
        _items = items;
        _target0 = target0;
        _target1 = target1;
        _target2 = target2;
        _target3 = target3;
        _count = count;
    }

    public int Count => _count;

    public bool IsEmpty => _count == 0;

    public HitTestTarget this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_items is not null)
            {
                return _items[index];
            }

            return index switch
            {
                0 => _target0,
                1 => _target1,
                2 => _target2,
                _ => _target3
            };
        }
    }

    public static HitTargetList Empty => default;

    internal static HitTargetList FromOwnedArray(HitTestTarget[] hitTargets)
    {
        ArgumentNullException.ThrowIfNull(hitTargets);
        return hitTargets.Length <= InlineCapacity
            ? CopyFrom(hitTargets.AsSpan())
            : new HitTargetList(hitTargets, default, default, default, default, hitTargets.Length);
    }

    public static HitTargetList CopyFrom(ReadOnlySpan<HitTestTarget> hitTargets)
    {
        return hitTargets.Length switch
        {
            0 => Empty,
            1 => new HitTargetList(null, hitTargets[0], default, default, default, 1),
            2 => new HitTargetList(null, hitTargets[0], hitTargets[1], default, default, 2),
            3 => new HitTargetList(null, hitTargets[0], hitTargets[1], hitTargets[2], default, 3),
            4 => new HitTargetList(null, hitTargets[0], hitTargets[1], hitTargets[2], hitTargets[3], 4),
            _ => FromOwnedArray(hitTargets.ToArray())
        };
    }

    public static HitTargetList CopyFrom(IReadOnlyList<HitTestTarget> hitTargets)
    {
        ArgumentNullException.ThrowIfNull(hitTargets);
        return hitTargets.Count switch
        {
            0 => Empty,
            1 => new HitTargetList(null, hitTargets[0], default, default, default, 1),
            2 => new HitTargetList(null, hitTargets[0], hitTargets[1], default, default, 2),
            3 => new HitTargetList(null, hitTargets[0], hitTargets[1], hitTargets[2], default, 3),
            4 => new HitTargetList(null, hitTargets[0], hitTargets[1], hitTargets[2], hitTargets[3], 4),
            _ => CopyListToOwnedArray(hitTargets)
        };
    }

    public HitTestTarget[] ToArray()
    {
        if (_count == 0)
        {
            return [];
        }

        var copy = new HitTestTarget[_count];
        CopyTo(copy);
        return copy;
    }

    public void CopyTo(Span<HitTestTarget> destination)
    {
        if (destination.Length < _count)
        {
            throw new ArgumentException("Destination span is too small.", nameof(destination));
        }

        if (_items is not null)
        {
            _items.AsSpan(0, _count).CopyTo(destination);
            return;
        }

        for (var i = 0; i < _count; i++)
        {
            destination[i] = this[i];
        }
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<HitTestTarget> IEnumerable<HitTestTarget>.GetEnumerator() => GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(HitTargetList other)
    {
        if (_count != other._count)
        {
            return false;
        }

        for (var i = 0; i < _count; i++)
        {
            if (this[i] != other[i])
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is HitTargetList other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        for (var i = 0; i < _count; i++)
        {
            hash.Add(this[i]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(HitTargetList left, HitTargetList right) => left.Equals(right);

    public static bool operator !=(HitTargetList left, HitTargetList right) => !left.Equals(right);

    private static HitTargetList CopyListToOwnedArray(IReadOnlyList<HitTestTarget> hitTargets)
    {
        var copy = new HitTestTarget[hitTargets.Count];
        for (var i = 0; i < copy.Length; i++)
        {
            copy[i] = hitTargets[i];
        }

        return FromOwnedArray(copy);
    }

    public struct Enumerator : IEnumerator<HitTestTarget>
    {
        private readonly HitTargetList _hitTargets;
        private int _index;

        internal Enumerator(HitTargetList hitTargets)
        {
            _hitTargets = hitTargets;
            _index = -1;
        }

        public readonly HitTestTarget Current => _hitTargets[_index];

        readonly object System.Collections.IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var next = _index + 1;
            if ((uint)next >= (uint)_hitTargets.Count)
            {
                return false;
            }

            _index = next;
            return true;
        }

        public void Reset() => _index = -1;

        public readonly void Dispose()
        {
        }
    }
}

public static class HitTargetListBuilder
{
    public static HitTargetList Create(ReadOnlySpan<HitTestTarget> hitTargets) =>
        HitTargetList.CopyFrom(hitTargets);
}

public struct RenderFrameBatch : IDisposable
{
    private readonly ulong _resourceFrameId;
    private readonly IndexRangeList _dirtyCommandRanges;
    private bool _disposed;

    public RenderFrameBatch(
        DrawCommandBatch commands,
        IReadOnlyList<HitTestTarget> hitTargets,
        IFrameResourceResolver resources,
        IReadOnlyList<(int Start, int Count)> dirtyCommandRanges)
        : this(commands, HitTargetList.CopyFrom(hitTargets), resources, IndexRangeList.CopyFrom(dirtyCommandRanges))
    {
    }

    private RenderFrameBatch(
        DrawCommandBatch commands,
        HitTargetList hitTargets,
        IFrameResourceResolver resources,
        IndexRangeList dirtyCommandRanges)
    {
        Commands = commands;
        HitTargets = hitTargets;
        Resources = resources;
        _dirtyCommandRanges = dirtyCommandRanges;
        _resourceFrameId = resources is FrameDrawingResources frameResources ? frameResources.FrameId : 0;

        if (resources is FrameDrawingResources retainedResources)
        {
            retainedResources.Retain();
        }
    }

    public RenderFrameBatch(DrawCommandBatch commands, IReadOnlyList<HitTestTarget> hitTargets)
        : this(commands, hitTargets, FrameDrawingResources.Empty, [])
    {
    }

    public RenderFrameBatch(DrawCommandBatch commands, IReadOnlyList<HitTestTarget> hitTargets, IFrameResourceResolver resources)
        : this(commands, hitTargets, resources, [])
    {
    }

    public DrawCommandBatch Commands { get; }
    public HitTargetList HitTargets { get; }
    public IFrameResourceResolver Resources { get; }
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges => _dirtyCommandRanges;
    internal readonly IndexRangeList DirtyCommandRangeList => _dirtyCommandRanges;
    internal readonly ulong ResourceFrameId => _resourceFrameId;

    internal static RenderFrameBatch WithHitTargets(
        DrawCommandBatch commands,
        HitTargetList hitTargets,
        IFrameResourceResolver resources,
        IndexRangeList dirtyCommandRanges) =>
        new(commands, hitTargets, resources, dirtyCommandRanges);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Commands.Dispose();
        if (Resources is FrameDrawingResources resources)
        {
            resources.Release(_resourceFrameId);
        }
    }
}
