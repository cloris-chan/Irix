namespace Irix.Poc;

internal readonly struct ScrollContainerId(int DfsIndex) : IEquatable<ScrollContainerId>
{
    public int DfsIndex { get; } = DfsIndex;

    public bool Equals(ScrollContainerId other) => DfsIndex == other.DfsIndex;

    public override bool Equals(object? obj) => obj is ScrollContainerId other && Equals(other);

    public override int GetHashCode() => DfsIndex;

    public static bool operator ==(ScrollContainerId left, ScrollContainerId right) => left.Equals(right);

    public static bool operator !=(ScrollContainerId left, ScrollContainerId right) => !left.Equals(right);
}

internal readonly record struct ScrollContainerMetrics(
    ScrollContainerId ContainerId,
    double ViewportExtent,
    double ContentExtent,
    double MaxScrollY);

internal sealed record ScrollFeedback(IReadOnlyList<ScrollContainerMetrics> Containers)
{
    public static ScrollFeedback Empty { get; } = new([]);
}

internal interface IControlFeedbackSink
{
    double LastMaxScrollY { get; }

    ScrollFeedback LastScrollFeedback { get; }

    void Deliver(double maxScrollY, ScrollFeedback scrollFeedback);
}
