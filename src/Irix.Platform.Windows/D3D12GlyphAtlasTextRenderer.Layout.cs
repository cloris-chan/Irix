using Irix.Drawing;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private void EnsureLayoutScratch(int textLength)
    {
        if (_layoutGlyphScratch.Length < textLength || _layoutLineScratch.Length < textLength + 1)
        {
            _layoutGlyphScratch = new GlyphEntry[textLength];
            _layoutAdvanceScratch = new float[textLength];
            _layoutLineScratch = new GlyphAtlasLayoutLine[textLength + 1];
        }
    }

    private bool TryBuildLineLayout(
        ReadOnlySpan<char> text,
        CachedFontFace fontFace,
        TextStyle style,
        float maxLineWidth,
        long recordSerial,
        out int lineCount,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        var glyphs = _layoutGlyphScratch.AsSpan(0, text.Length);
        var advances = _layoutAdvanceScratch.AsSpan(0, text.Length);
        var spaceAdvance = 0f;
        var hasSpaceAdvance = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (GlyphAtlasTextCompositionHelpers.IsLineBreak(text, i, out var lineBreakWidth))
            {
                glyphs[i] = default;
                advances[i] = 0;
                if (lineBreakWidth == 2)
                {
                    i++;
                    glyphs[i] = default;
                    advances[i] = 0;
                }

                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.IsTab(text[i]))
            {
                if (!hasSpaceAdvance)
                {
                    if (!TryMeasureCharacterAdvance(fontFace, style.FontSize, ' ', out spaceAdvance))
                    {
                        lineCount = 0;
                        unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
                        return false;
                    }

                    hasSpaceAdvance = true;
                }

                glyphs[i] = default;
                advances[i] = spaceAdvance * GlyphAtlasTextCompositionHelpers.TabAdvanceSpaceCount;
                continue;
            }

            if (!TryGetGlyph(fontFace, style, text[i], recordSerial, out glyphs[i], out unsupportedReason))
            {
                lineCount = 0;
                return false;
            }

            advances[i] = glyphs[i].Advance;
        }

        unsupportedReason = GlyphAtlasTextCompositionHelpers.PlanLines(
            text,
            advances,
            maxLineWidth,
            wrapping: style.Wrapping,
            lines: _layoutLineScratch.AsSpan(0, text.Length + 1),
            out lineCount);
        return unsupportedReason == GlyphAtlasFallbackReason.None;
    }

}

