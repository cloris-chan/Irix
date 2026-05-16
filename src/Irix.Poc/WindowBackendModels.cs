using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal readonly struct WindowBackendRenderResult(
    IReadOnlyList<WindowContentElement> Elements,
    IReadOnlyList<HitTestTarget> HitTargets) : IEquatable<WindowBackendRenderResult>
{

    public IReadOnlyList<WindowContentElement> Elements { get; } = Elements;
    public IReadOnlyList<HitTestTarget> HitTargets { get; } = HitTargets;

    public bool Equals(WindowBackendRenderResult other)
    {
        return EqualityComparer<IReadOnlyList<WindowContentElement>>.Default.Equals(Elements, other.Elements)
            && EqualityComparer<IReadOnlyList<HitTestTarget>>.Default.Equals(HitTargets, other.HitTargets);
    }

    public override bool Equals(object? obj) => obj is WindowBackendRenderResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Elements, HitTargets);

    public static bool operator ==(WindowBackendRenderResult left, WindowBackendRenderResult right) => left.Equals(right);

    public static bool operator !=(WindowBackendRenderResult left, WindowBackendRenderResult right) => !left.Equals(right);
}
