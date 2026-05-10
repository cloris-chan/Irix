namespace Irix.Poc;

internal readonly record struct ScrollContainerMetrics(
    string ContainerId,
    double ViewportExtent,
    double ContentExtent,
    double MaxScrollY);

internal sealed record ScrollFeedback(IReadOnlyList<ScrollContainerMetrics> Containers)
{
    public static ScrollFeedback Empty { get; } = new([]);
}