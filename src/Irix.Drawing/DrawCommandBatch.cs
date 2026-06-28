using System.Buffers;

namespace Irix.Drawing;

public readonly struct DrawCommandBatch : IDisposable, IEquatable<DrawCommandBatch>
{
    private readonly ulong _ownerGeneration;

    public DrawCommandBatch(IMemoryOwner<DrawCommand> owner, int count)
        : this(owner, count, owner is PooledArrayMemoryOwner<DrawCommand> pooledOwner ? pooledOwner.Generation : 0)
    {
    }

    private DrawCommandBatch(IMemoryOwner<DrawCommand> owner, int count, ulong ownerGeneration)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Owner = owner;
        Count = count;
        _ownerGeneration = ownerGeneration;
    }

    public IMemoryOwner<DrawCommand> Owner { get; }
    public int Count { get; }
    public ulong OwnerGeneration => _ownerGeneration;

    public Memory<DrawCommand> Memory
    {
        get
        {
            var memory = Owner is PooledArrayMemoryOwner<DrawCommand> pooledOwner
                ? pooledOwner.GetMemory(_ownerGeneration)
                : Owner.Memory;
            return memory.Length < Count ? memory : memory[..Count];
        }
    }

    internal static DrawCommandBatch FromPooled(PooledArrayMemoryOwner<DrawCommand> owner, int count) =>
        new(owner, count, owner.Generation);

    public void Dispose()
    {
        if (Owner is PooledArrayMemoryOwner<DrawCommand> pooledOwner)
        {
            pooledOwner.Dispose(_ownerGeneration);
            return;
        }

        Owner.Dispose();
    }

    public bool Equals(DrawCommandBatch other)
    {
        return EqualityComparer<IMemoryOwner<DrawCommand>>.Default.Equals(Owner, other.Owner)
            && _ownerGeneration == other._ownerGeneration
            && Count == other.Count;
    }

    public override bool Equals(object? obj) => obj is DrawCommandBatch other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Owner, _ownerGeneration, Count);

    public static bool operator ==(DrawCommandBatch left, DrawCommandBatch right) => left.Equals(right);

    public static bool operator !=(DrawCommandBatch left, DrawCommandBatch right) => !left.Equals(right);
}
