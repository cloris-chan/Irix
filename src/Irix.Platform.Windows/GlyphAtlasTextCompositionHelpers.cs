using Irix.Drawing;

namespace Irix.Platform.Windows;

internal static class GlyphAtlasTextCompositionHelpers
{
    internal static int CountRenderableRuns(
        ReadOnlySpan<D3D12TextRun> textRuns,
        IFrameResourceResolver resources)
    {
        var count = 0;
        foreach (var textRun in textRuns)
        {
            if (ShouldRenderTextRun(textRun, resources))
            {
                count++;
            }
        }

        return count;
    }

    internal static D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason GetUnsupportedReason(
        ReadOnlySpan<char> text,
        TextStyle style)
    {
        foreach (var character in text)
        {
            if (character is < ' ' or > '~')
            {
                return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.NonAscii;
            }
        }

        return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
    }

    internal static D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason PlanLines(
        ReadOnlySpan<char> text,
        ReadOnlySpan<float> advances,
        float maxLineWidth,
        TextWrapping wrapping,
        Span<GlyphAtlasLayoutLine> lines,
        out int lineCount)
    {
        lineCount = 0;
        if (text.IsEmpty)
        {
            return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
        }

        if (advances.Length < text.Length || lines.Length == 0)
        {
            throw new ArgumentException("Glyph atlas line planner scratch is too small.");
        }

        if (wrapping == TextWrapping.NoWrap)
        {
            var lineWidth = 0f;
            for (var i = 0; i < text.Length; i++)
            {
                lineWidth += advances[i];
            }

            if (lineWidth > maxLineWidth)
            {
                return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Clip;
            }

            lines[0] = new GlyphAtlasLayoutLine(0, text.Length, lineWidth);
            lineCount = 1;
            return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
        }

        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && text[index] == ' ')
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var lineStart = index;
            var lineWidth = 0f;
            var breakIndex = -1;
            var breakWidth = 0f;

            for (var i = lineStart; i < text.Length; i++)
            {
                if (text[i] == ' ' && i > lineStart && text[i - 1] != ' ')
                {
                    breakIndex = i;
                    breakWidth = lineWidth;
                }

                var nextWidth = lineWidth + advances[i];
                if (nextWidth > maxLineWidth)
                {
                    if (breakIndex >= lineStart)
                    {
                        AppendLine(lines, ref lineCount, lineStart, breakIndex, breakWidth);
                        index = breakIndex + 1;
                        goto NextLine;
                    }

                    return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Wrapping;
                }

                lineWidth = nextWidth;
            }

            var lineEnd = text.Length;
            while (lineEnd > lineStart && text[lineEnd - 1] == ' ')
            {
                lineEnd--;
                lineWidth -= advances[lineEnd];
            }

            AppendLine(lines, ref lineCount, lineStart, lineEnd, lineWidth);
            index = text.Length;

        NextLine:
            continue;
        }

        if (lineCount == 0)
        {
            lines[0] = new GlyphAtlasLayoutLine(0, 0, 0);
            lineCount = 1;
        }

        return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
    }

    private static void AppendLine(
        Span<GlyphAtlasLayoutLine> lines,
        ref int lineCount,
        int start,
        int end,
        float width)
    {
        if ((uint)lineCount >= (uint)lines.Length)
        {
            throw new ArgumentException("Glyph atlas line planner scratch is too small.");
        }

        lines[lineCount++] = new GlyphAtlasLayoutLine(start, end, width);
    }

    internal static bool ShouldRenderTextRun(D3D12TextRun textRun, IFrameResourceResolver resources)
    {
        if (textRun.Width <= 0 || textRun.Height <= 0)
        {
            return false;
        }

        var runResolver = textRun.Resolver ?? resources;
        return !runResolver.Resolve(textRun.Text).IsEmpty;
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

internal readonly struct GlyphAtlasLayoutLine(int Start, int End, float Width) : IEquatable<GlyphAtlasLayoutLine>
{
    public int Start { get; } = Start;
    public int End { get; } = End;
    public float Width { get; } = Width;

    public bool Equals(GlyphAtlasLayoutLine other)
    {
        return Start == other.Start
            && End == other.End
            && Width.Equals(other.Width);
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasLayoutLine other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Start, End, Width);

    public static bool operator ==(GlyphAtlasLayoutLine left, GlyphAtlasLayoutLine right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasLayoutLine left, GlyphAtlasLayoutLine right) => !left.Equals(right);
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
