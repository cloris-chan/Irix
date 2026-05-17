namespace Irix;

internal readonly struct NodeIndexEntry(int dfsIndex, int parentDfsIndex) : IEquatable<NodeIndexEntry>
{
    public int DfsIndex { get; } = dfsIndex;
    public int ParentDfsIndex { get; } = parentDfsIndex;

    public bool Equals(NodeIndexEntry other) => DfsIndex == other.DfsIndex && ParentDfsIndex == other.ParentDfsIndex;

    public override bool Equals(object? obj) => obj is NodeIndexEntry other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(DfsIndex, ParentDfsIndex);

    public static bool operator ==(NodeIndexEntry left, NodeIndexEntry right) => left.Equals(right);

    public static bool operator !=(NodeIndexEntry left, NodeIndexEntry right) => !left.Equals(right);
}
