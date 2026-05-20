using Irix.Drawing;

namespace Irix.Platform.Windows;

internal static class GlyphAtlasTextCompositionHelpers
{
    internal const int TabAdvanceSpaceCount = 4;

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
            if (character is > '~' || (character < ' ' && character is not '\r' and not '\n' and not '\t'))
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

        var index = 0;
        while (index < text.Length)
        {
            if (IsLineBreak(text, index, out var lineBreakWidth))
            {
                AppendLine(lines, ref lineCount, index, index, 0);
                index += lineBreakWidth;
                continue;
            }

            if (wrapping == TextWrapping.NoWrap)
            {
                var noWrapLineStart = index;
                var noWrapLineWidth = 0f;
                while (index < text.Length && !IsLineBreak(text, index, out lineBreakWidth))
                {
                    noWrapLineWidth += advances[index];
                    if (noWrapLineWidth > maxLineWidth)
                    {
                        return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.Clip;
                    }

                    index++;
                }

                AppendLine(lines, ref lineCount, noWrapLineStart, index, noWrapLineWidth);
                if (index < text.Length && IsLineBreak(text, index, out lineBreakWidth))
                {
                    index += lineBreakWidth;
                }

                continue;
            }

            while (index < text.Length && IsWrapWhitespace(text[index]))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            if (IsLineBreak(text, index, out lineBreakWidth))
            {
                AppendLine(lines, ref lineCount, index, index, 0);
                index += lineBreakWidth;
                continue;
            }

            var lineStart = index;
            var lineWidth = 0f;
            var breakIndex = -1;
            var breakWidth = 0f;

            for (var i = lineStart; i < text.Length; i++)
            {
                if (IsLineBreak(text, i, out var explicitBreakWidth))
                {
                    var explicitLineEnd = i;
                    var resolvedLineWidth = lineWidth;
                    while (explicitLineEnd > lineStart && IsWrapWhitespace(text[explicitLineEnd - 1]))
                    {
                        explicitLineEnd--;
                        resolvedLineWidth -= advances[explicitLineEnd];
                    }

                    AppendLine(lines, ref lineCount, lineStart, explicitLineEnd, resolvedLineWidth);
                    index = i + explicitBreakWidth;
                    goto NextLine;
                }

                if (IsWrapWhitespace(text[i]) && i > lineStart && !IsWrapWhitespace(text[i - 1]))
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
            while (lineEnd > lineStart && IsWrapWhitespace(text[lineEnd - 1]))
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
        else if (EndsWithLineBreak(text))
        {
            AppendLine(lines, ref lineCount, text.Length, text.Length, 0);
        }

        return D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.None;
    }

    private static bool EndsWithLineBreak(ReadOnlySpan<char> text)
    {
        return !text.IsEmpty && text[^1] is '\r' or '\n';
    }

    internal static bool IsTab(char character) => character == '\t';

    internal static bool IsWrapWhitespace(char character) => character is ' ' or '\t';

    internal static bool CanApplyAtlasPageReuseRequest(bool hasPendingRequest, long requestedRecordSerial, long currentRecordSerial)
    {
        return hasPendingRequest && currentRecordSerial > requestedRecordSerial;
    }

    internal static bool ShouldClearGlyphForReusedPage(bool isLiveGlyph, int glyphPageIndex, int glyphPageGeneration, int reusedPageIndex, int reusedPageGeneration)
    {
        return isLiveGlyph && glyphPageIndex == reusedPageIndex && glyphPageGeneration == reusedPageGeneration;
    }

    internal static GlyphAtlasPageReuseResetState CreatePageReuseResetState(int atlasWidth, int atlasHeight, int atlasPadding)
    {
        return new GlyphAtlasPageReuseResetState(
            NextX: atlasPadding,
            NextY: atlasPadding,
            RowHeight: 0,
            IsDirty: true,
            DirtyLeft: 0,
            DirtyTop: 0,
            DirtyRight: atlasWidth,
            DirtyBottom: atlasHeight,
            UsedPixels: 0,
            AllocatedPixels: 0,
            LastUsedSerial: 0);
    }

    internal static bool IsLineBreak(ReadOnlySpan<char> text, int index, out int width)
    {
        if ((uint)index >= (uint)text.Length)
        {
            width = 0;
            return false;
        }

        if (text[index] == '\r')
        {
            width = index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
            return true;
        }

        if (text[index] == '\n')
        {
            width = 1;
            return true;
        }

        width = 0;
        return false;
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

internal readonly struct GlyphAtlasPageReuseResetState(
    int NextX,
    int NextY,
    int RowHeight,
    bool IsDirty,
    int DirtyLeft,
    int DirtyTop,
    int DirtyRight,
    int DirtyBottom,
    int UsedPixels,
    int AllocatedPixels,
    long LastUsedSerial) : IEquatable<GlyphAtlasPageReuseResetState>
{
    public int NextX { get; } = NextX;
    public int NextY { get; } = NextY;
    public int RowHeight { get; } = RowHeight;
    public bool IsDirty { get; } = IsDirty;
    public int DirtyLeft { get; } = DirtyLeft;
    public int DirtyTop { get; } = DirtyTop;
    public int DirtyRight { get; } = DirtyRight;
    public int DirtyBottom { get; } = DirtyBottom;
    public int UsedPixels { get; } = UsedPixels;
    public int AllocatedPixels { get; } = AllocatedPixels;
    public long LastUsedSerial { get; } = LastUsedSerial;

    public bool Equals(GlyphAtlasPageReuseResetState other)
    {
        return NextX == other.NextX
            && NextY == other.NextY
            && RowHeight == other.RowHeight
            && IsDirty == other.IsDirty
            && DirtyLeft == other.DirtyLeft
            && DirtyTop == other.DirtyTop
            && DirtyRight == other.DirtyRight
            && DirtyBottom == other.DirtyBottom
            && UsedPixels == other.UsedPixels
            && AllocatedPixels == other.AllocatedPixels
            && LastUsedSerial == other.LastUsedSerial;
    }

    public override bool Equals(object? obj) => obj is GlyphAtlasPageReuseResetState other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(NextX);
        hash.Add(NextY);
        hash.Add(RowHeight);
        hash.Add(IsDirty);
        hash.Add(DirtyLeft);
        hash.Add(DirtyTop);
        hash.Add(DirtyRight);
        hash.Add(DirtyBottom);
        hash.Add(UsedPixels);
        hash.Add(AllocatedPixels);
        hash.Add(LastUsedSerial);
        return hash.ToHashCode();
    }

    public static bool operator ==(GlyphAtlasPageReuseResetState left, GlyphAtlasPageReuseResetState right) => left.Equals(right);

    public static bool operator !=(GlyphAtlasPageReuseResetState left, GlyphAtlasPageReuseResetState right) => !left.Equals(right);
}
