using System.Text;
using Irix.Platform.Windows;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Poc;

internal static class GlyphAtlasGlyphOracleDiagnosticRunner
{
    internal static void Run(TextWriter output)
    {
        var snapshot = DWriteGlyphOracleDiagnostic.Capture();

        output.WriteLine("=== Glyph Atlas Glyph Oracle Diagnostic ===");
        output.WriteLine(FormatExpectedSnapshot());
        output.WriteLine(FormatSummary(snapshot));
        output.WriteLine(FormatActualSnapshot(snapshot));
        foreach (var result in snapshot.Results)
        {
            output.WriteLine(FormatProbe(result));
        }

        output.WriteLine("=== glyph atlas glyph oracle diagnostic complete ===");
    }

    internal static string FormatSummary(GlyphOracleDiagnosticSnapshot snapshot)
    {
        return string.IsNullOrEmpty(snapshot.Failure)
            ? $"Glyph oracle: factory={snapshot.FactoryAvailable}, analyzer={snapshot.AnalyzerAvailable}, fontFallback={snapshot.FontFallbackAvailable}, probes={snapshot.ProbeCount}, failedProbes={snapshot.FailedProbes}, fallbackFontProbes={snapshot.FallbackFontProbes}, mixedBidiProbes={snapshot.MixedBidiProbes}, lineBreakProbes={snapshot.LineBreakProbes}, totalGlyphs={snapshot.TotalGlyphs}"
            : $"Glyph oracle: failure={snapshot.Failure}, factory={snapshot.FactoryAvailable}, analyzer={snapshot.AnalyzerAvailable}, fontFallback={snapshot.FontFallbackAvailable}, probes={snapshot.ProbeCount}, failedProbes=0, fallbackFontProbes=0, mixedBidiProbes=0, lineBreakProbes=0, totalGlyphs=0";
    }

    internal static string FormatExpectedSnapshot() =>
        "glyph-oracle.expected probes=5 labels=ascii|cjk-fallback|arabic-rtl|mixed-bidi|tab-crlf fields=glyphCount|glyphIndices|advances|offsets|bidiLevels|lineBreaks|segments layoutOracle=False pixelOracle=False overlayFallback=False";

    internal static string FormatActualSnapshot(GlyphOracleDiagnosticSnapshot snapshot)
    {
        var labels = new StringBuilder(112);
        for (var i = 0; i < snapshot.Results.Count; i++)
        {
            if (i > 0)
            {
                labels.Append('|');
            }

            labels.Append(snapshot.Results[i].Label);
        }

        return string.IsNullOrEmpty(snapshot.Failure)
            ? $"glyph-oracle.actual probes={snapshot.ProbeCount} labels={labels} failedProbes={snapshot.FailedProbes} fallbackFontProbes={snapshot.FallbackFontProbes} mixedBidiProbes={snapshot.MixedBidiProbes} lineBreakProbes={snapshot.LineBreakProbes} totalGlyphs={snapshot.TotalGlyphs} layoutOracle=False pixelOracle=False overlayFallback=False"
            : $"glyph-oracle.actual probes={snapshot.ProbeCount} labels={labels} failure={snapshot.Failure} failedProbes=0 fallbackFontProbes=0 mixedBidiProbes=0 lineBreakProbes=0 totalGlyphs=0 layoutOracle=False pixelOracle=False overlayFallback=False";
    }

    internal static string FormatProbe(GlyphOracleProbeResult result)
    {
        var builder = new StringBuilder(384);
        builder.Append("Probe: ");
        builder.Append(result.Label);
        builder.Append(" base=");
        builder.Append(FormatDirection(result.BaseDirection));
        builder.Append(" textLength=");
        builder.Append(result.TextLength);
        if (!string.IsNullOrEmpty(result.Failure))
        {
            builder.Append(" failure=");
            builder.Append(result.Failure);
            return builder.ToString();
        }

        builder.Append(" glyphCount=");
        builder.Append(result.GlyphCount);
        builder.Append(" bidiLevels=");
        AppendLevels(builder, result.BidiLevels);
        builder.Append(" lineBreaks=");
        AppendLineBreaks(builder, result.LineBreaks);
        builder.Append(" segments=");
        AppendSegments(builder, result.Segments);
        builder.Append(" glyphs=");
        AppendGlyphs(builder, result.Glyphs);
        return builder.ToString();
    }

    private static string FormatDirection(DWRITE_READING_DIRECTION direction) =>
        direction == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? "RTL" : "LTR";

    private static void AppendLevels(StringBuilder builder, IReadOnlyList<byte> levels)
    {
        for (var i = 0; i < levels.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(levels[i]);
        }
    }

    private static void AppendLineBreaks(StringBuilder builder, IReadOnlyList<GlyphOracleLineBreak> lineBreaks)
    {
        builder.Append('[');
        for (var i = 0; i < lineBreaks.Count; i++)
        {
            var lineBreak = lineBreaks[i];
            if (!lineBreak.CanBreak && !lineBreak.MustBreak && !lineBreak.Whitespace && !lineBreak.SoftHyphen)
            {
                continue;
            }

            if (builder[builder.Length - 1] != '[')
            {
                builder.Append('|');
            }

            builder.Append(i);
            builder.Append(':');
            builder.Append(lineBreak.Before);
            builder.Append('/');
            builder.Append(lineBreak.After);
            if (lineBreak.Whitespace)
            {
                builder.Append('w');
            }

            if (lineBreak.SoftHyphen)
            {
                builder.Append('s');
            }
        }

        builder.Append(']');
    }

    private static void AppendSegments(StringBuilder builder, IReadOnlyList<GlyphOracleSegment> segments)
    {
        builder.Append('[');
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            var segment = segments[i];
            builder.Append(segment.TextStart);
            builder.Append("..");
            builder.Append(segment.TextEnd);
            builder.Append("=>");
            builder.Append(segment.GlyphStart);
            builder.Append("..");
            builder.Append(segment.GlyphEnd);
            builder.Append("@script");
            builder.Append(segment.Script);
            builder.Append("/bidi");
            builder.Append(segment.BidiLevel);
            if (segment.FallbackFont)
            {
                builder.Append("/fallback");
            }
        }

        builder.Append(']');
    }

    private static void AppendGlyphs(StringBuilder builder, IReadOnlyList<GlyphOracleGlyph> glyphs)
    {
        builder.Append('[');
        for (var i = 0; i < glyphs.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            var glyph = glyphs[i];
            builder.Append(glyph.GlyphIndex);
            builder.Append('@');
            builder.Append(glyph.Advance.ToString("0.###"));
            if (glyph.AdvanceOffset != 0 || glyph.AscenderOffset != 0)
            {
                builder.Append('+');
                builder.Append(glyph.AdvanceOffset.ToString("0.###"));
                builder.Append(',');
                builder.Append(glyph.AscenderOffset.ToString("0.###"));
            }

            if (glyph.ClusterStart)
            {
                builder.Append('c');
            }

            if (glyph.Diacritic)
            {
                builder.Append('d');
            }

            if (glyph.ZeroWidthSpace)
            {
                builder.Append('z');
            }
        }

        builder.Append(']');
    }
}
