using Irix.Platform;
using Irix.Rendering;
using Irix.Drawing;

namespace Irix.Poc;

internal readonly struct WindowBackendRenderResult(
    IReadOnlyList<WindowContentElement> Elements,
    IReadOnlyList<HitTestTarget> HitTargets,
    ITextResolver TextResolver) : IEquatable<WindowBackendRenderResult>
{

    public IReadOnlyList<WindowContentElement> Elements { get; } = Elements;
    public IReadOnlyList<HitTestTarget> HitTargets { get; } = HitTargets;
    public ITextResolver TextResolver { get; } = TextResolver;

    public bool Equals(WindowBackendRenderResult other)
    {
        return EqualityComparer<IReadOnlyList<WindowContentElement>>.Default.Equals(Elements, other.Elements)
            && EqualityComparer<IReadOnlyList<HitTestTarget>>.Default.Equals(HitTargets, other.HitTargets)
            && EqualityComparer<ITextResolver>.Default.Equals(TextResolver, other.TextResolver);
    }

    public override bool Equals(object? obj) => obj is WindowBackendRenderResult other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Elements, HitTargets, TextResolver);

    public static bool operator ==(WindowBackendRenderResult left, WindowBackendRenderResult right) => left.Equals(right);

    public static bool operator !=(WindowBackendRenderResult left, WindowBackendRenderResult right) => !left.Equals(right);
}
