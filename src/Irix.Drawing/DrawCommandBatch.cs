using System.Buffers;

namespace Irix.Drawing;

public readonly struct DrawCommandBatch(IMemoryOwner<DrawCommand> Owner, int Count) : IDisposable, IEquatable<DrawCommandBatch>
{

    public IMemoryOwner<DrawCommand> Owner { get; } = Owner;
    public int Count { get; } = Count;

    public Memory<DrawCommand> Memory
    {
        get
        {
            var memory = Owner.Memory;
            return memory.Length < Count ? memory : memory[..Count];
        }
    }

    public void Dispose()
    {
        Owner.Dispose();
    }

    public bool Equals(DrawCommandBatch other)
    {
        return EqualityComparer<IMemoryOwner<DrawCommand>>.Default.Equals(Owner, other.Owner)
            && Count == other.Count;
    }

    public override bool Equals(object? obj) => obj is DrawCommandBatch other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Owner, Count);

    public static bool operator ==(DrawCommandBatch left, DrawCommandBatch right) => left.Equals(right);

    public static bool operator !=(DrawCommandBatch left, DrawCommandBatch right) => !left.Equals(right);
}
