using Irix.Drawing;
using Irix.Platform;

namespace Irix.Platform.Windows;

internal readonly struct D3D12TextRun(
    float X,
    float Y,
    float Width,
    float Height,
    float R,
    float G,
    float B,
    float A,
    TextSlice Text,
    ResourceHandle Style,
    EffectiveScissor EffectiveClip,
    bool ClipEnabled,
    TextStyle ResolvedStyle = default,
    IFrameResourceResolver? Resolver = null) : IEquatable<D3D12TextRun>
{
    public float X { get; } = X;
    public float Y { get; } = Y;
    public float Width { get; } = Width;
    public float Height { get; } = Height;
    public float R { get; } = R;
    public float G { get; } = G;
    public float B { get; } = B;
    public float A { get; } = A;
    public TextSlice Text { get; } = Text;
    public ResourceHandle Style { get; } = Style;
    public EffectiveScissor EffectiveClip { get; } = EffectiveClip;
    public bool ClipEnabled { get; } = ClipEnabled;
    public TextStyle ResolvedStyle { get; } = ResolvedStyle;
    public IFrameResourceResolver? Resolver { get; } = Resolver;

    public bool Equals(D3D12TextRun other)
    {
        return X.Equals(other.X)
            && Y.Equals(other.Y)
            && Width.Equals(other.Width)
            && Height.Equals(other.Height)
            && R.Equals(other.R)
            && G.Equals(other.G)
            && B.Equals(other.B)
            && A.Equals(other.A)
            && Text == other.Text
            && Style == other.Style
            && EffectiveClip == other.EffectiveClip
            && ClipEnabled == other.ClipEnabled
            && ResolvedStyle == other.ResolvedStyle
            && EqualityComparer<IFrameResourceResolver?>.Default.Equals(Resolver, other.Resolver);
    }

    public override bool Equals(object? obj) => obj is D3D12TextRun other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(X);
        hash.Add(Y);
        hash.Add(Width);
        hash.Add(Height);
        hash.Add(R);
        hash.Add(G);
        hash.Add(B);
        hash.Add(A);
        hash.Add(Text);
        hash.Add(Style);
        hash.Add(EffectiveClip);
        hash.Add(ClipEnabled);
        hash.Add(ResolvedStyle);
        hash.Add(Resolver);
        return hash.ToHashCode();
    }

    public static bool operator ==(D3D12TextRun left, D3D12TextRun right) => left.Equals(right);

    public static bool operator !=(D3D12TextRun left, D3D12TextRun right) => !left.Equals(right);
}
