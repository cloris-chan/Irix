using Irix.Drawing;

namespace Irix.Platform.Windows;

internal static class GlyphAtlasTextCompositionHelpers
{
    internal static D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason GetUnsupportedReason(
        ReadOnlySpan<char> text,
        TextStyle style)
    {
        if (style.Wrapping != TextWrapping.NoWrap)
        {
            return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Wrapping;
        }

        foreach (var character in text)
        {
            if (character is < ' ' or > '~')
            {
                return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii;
            }
        }

        return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
    }

    internal static float ComputeAlignedPenX(
        float runX,
        float runWidth,
        TextHorizontalAlignment horizontalAlignment,
        float lineWidth)
    {
        return horizontalAlignment switch
        {
            TextHorizontalAlignment.Center => runX + MathF.Max(0, (runWidth - lineWidth) * 0.5f),
            TextHorizontalAlignment.Trailing => runX + MathF.Max(0, runWidth - lineWidth),
            _ => runX
        };
    }

    internal static GlyphAtlasDirtyRect MergeDirtyRect(
        bool hasDirtyRect,
        int currentLeft,
        int currentTop,
        int currentRight,
        int currentBottom,
        int x,
        int y,
        int width,
        int height)
    {
        if (width <= 0 || height <= 0)
        {
            return new GlyphAtlasDirtyRect(hasDirtyRect, currentLeft, currentTop, currentRight, currentBottom);
        }

        if (!hasDirtyRect)
        {
            return new GlyphAtlasDirtyRect(true, x, y, x + width, y + height);
        }

        return new GlyphAtlasDirtyRect(
            true,
            Math.Min(currentLeft, x),
            Math.Min(currentTop, y),
            Math.Max(currentRight, x + width),
            Math.Max(currentBottom, y + height));
    }

    internal static D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationException WrapInitializationException(
        D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase phase,
        Exception exception)
    {
        return exception is D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationException initializationException
            ? initializationException
            : new D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationException(phase, exception);
    }
}

internal readonly struct GlyphAtlasDirtyRect(
    bool HasDirtyRect,
    int Left,
    int Top,
    int Right,
    int Bottom) : IEquatable<GlyphAtlasDirtyRect>
{
    public bool HasDirtyRect { get; } = HasDirtyRect;
    public int Left { get; } = Left;
    public int Top { get; } = Top;
    public int Right { get; } = Right;
    public int Bottom { get; } = Bottom;

    public bool Equals(GlyphAtlasDirtyRect other)
    {
        return HasDirtyRect == other.HasDirtyRect
            && Left == other.Left
            && Top == other.Top
            && Right == other.Right
            && Bottom == other.Bottom;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasDirtyRect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(HasDirtyRect, Left, Top, Right, Bottom);

    public static bool operator ==(GlyphAtlasDirtyRect left, GlyphAtlasDirtyRect right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasDirtyRect left, GlyphAtlasDirtyRect right) => !left.Equals(right);
}
