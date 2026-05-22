using System.Text;
using Irix.Platform.Windows;

namespace Irix.Poc;

internal static class GlyphAtlasColorFormatDiagnosticRunner
{
    internal static void Run(TextWriter output, string familyName = "Segoe UI Emoji", uint pixelsPerEm = 64)
    {
        var snapshot = DWriteColorGlyphFormatDiagnostic.Capture(familyName, pixelsPerEm);

        output.WriteLine("=== Glyph Atlas Color Glyph Format Diagnostic ===");
        output.WriteLine($"Family: {snapshot.FamilyName}");
        output.WriteLine($"PixelsPerEm: {snapshot.PixelsPerEm}");
        output.WriteLine(FormatSummary(snapshot));
        foreach (var result in snapshot.Results)
        {
            output.WriteLine(FormatProbe(result));
        }

        output.WriteLine("=== glyph atlas color glyph format diagnostic complete ===");
    }

    internal static string FormatSummary(ColorGlyphFormatDiagnosticSnapshot snapshot)
    {
        return string.IsNullOrEmpty(snapshot.Failure)
            ? $"Color glyph formats: factory4={snapshot.Factory4Available}, face4={snapshot.Face4Available}, probes={snapshot.ProbeCount}, glyphs={snapshot.Glyphs}, colorRunCandidates={snapshot.ColorRunCandidates}, layerCandidates={snapshot.LayerCandidates}, bgraCandidates={snapshot.BgraCandidates}, encodedBitmapCandidates={snapshot.EncodedBitmapCandidates}, unsupportedColorCandidates={snapshot.UnsupportedColorCandidates}, bitmapRenderableCandidates={snapshot.BitmapRenderableCandidates}"
            : $"Color glyph formats: failure={snapshot.Failure}, factory4={snapshot.Factory4Available}, face4={snapshot.Face4Available}, probes={snapshot.ProbeCount}, glyphs={snapshot.Glyphs}, colorRunCandidates=0, layerCandidates=0, bgraCandidates=0, encodedBitmapCandidates=0, unsupportedColorCandidates=0, bitmapRenderableCandidates=0";
    }

    internal static string FormatProbe(ColorGlyphFormatProbeResult result)
    {
        var builder = new StringBuilder(160);
        builder.Append("Probe: U+");
        builder.Append(result.CodePoint.ToString("X"));
        builder.Append(' ');
        builder.Append(result.Label);
        builder.Append(" glyph=");
        builder.Append(result.GlyphIndex);
        builder.Append(" status=");
        builder.Append(result.Status);
        builder.Append(" formats=");
        builder.Append(FormatFlags(result.Formats));
        builder.Append(" route=");
        builder.Append(result.BitmapRoute);
        builder.Append(" colorRuns=");
        builder.Append(result.ColorRunCount);
        builder.Append(" runFormats=");
        builder.Append(FormatFlags(result.ColorRunFormats));
        builder.Append(" runRoute=");
        builder.Append(result.ColorRunBitmapRoute);
        if (!string.IsNullOrEmpty(result.Error))
        {
            builder.Append(" error=");
            builder.Append(result.Error);
        }
        if (!string.IsNullOrEmpty(result.ColorRunError))
        {
            builder.Append(" runError=");
            builder.Append(result.ColorRunError);
        }

        return builder.ToString();
    }

    internal static string FormatFlags(ColorGlyphImageFormatFlags flags)
    {
        if (flags == ColorGlyphImageFormatFlags.None)
        {
            return "None";
        }

        var builder = new StringBuilder(96);
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.TrueType, "TRUETYPE");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Cff, "CFF");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Colr, "COLR");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Svg, "SVG");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Png, "PNG");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Jpeg, "JPEG");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.Tiff, "TIFF");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.PremultipliedBgra, "BGRA");
        AppendFlag(builder, flags, ColorGlyphImageFormatFlags.ColrPaintTree, "COLR_PAINT_TREE");
        return builder.ToString();
    }

    private static void AppendFlag(StringBuilder builder, ColorGlyphImageFormatFlags flags, ColorGlyphImageFormatFlags flag, string label)
    {
        if ((flags & flag) == 0)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append('|');
        }

        builder.Append(label);
    }
}
