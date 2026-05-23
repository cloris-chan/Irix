using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    private bool TryBuildShapedAtlasRun(
        ReadOnlySpan<char> text,
        D3D12TextRun textRun,
        TextStyle style,
        ShapedGlyphRun shapedRun,
        int viewportWidth,
        int viewportHeight,
        long recordSerial,
        ref int vertexCount,
        ref int batchCount,
        ref int acceptedColorLayerRuns,
        ref int acceptedColorBitmapRuns,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (shapedRun.LineCount == 0 || shapedRun.HasMissingGlyph())
        {
            unsupportedReason = shapedRun.RequiresColorGlyph ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph : GlyphAtlasFallbackReason.NonAscii;
            return false;
        }

        var totalAdvance = shapedRun.ComputeAdvance();
        if (totalAdvance > textRun.Width)
        {
            unsupportedReason = style.Wrapping == TextWrapping.NoWrap ? GlyphAtlasFallbackReason.Clip : GlyphAtlasFallbackReason.Wrapping;
            return false;
        }

        var lineHeight = shapedRun.ComputeLineHeight();
        var scissor = ResolveRunScissor(textRun, viewportWidth, viewportHeight);
        if (scissor.IsEmpty)
        {
            unsupportedReason = GlyphAtlasFallbackReason.Clip;
            return false;
        }

        var firstBaselineY = ComputeFirstBaselineY(textRun, style, shapedRun.ComputeAscent(), lineHeight, shapedRun.LineCount);
        var color = new Vector4(textRun.R, textRun.G, textRun.B, textRun.A);
        var batchStart = vertexCount;
        var batchSegmentStart = vertexCount;
        var batchPage = default(GlyphAtlasPageHandle);
        var colorLayerRuns = 0;
        var colorBitmapRuns = 0;

        for (var lineIndex = 0; lineIndex < shapedRun.LineCount; lineIndex++)
        {
            var line = shapedRun.Lines[lineIndex];
            var lineX = GlyphAtlasTextCompositionHelpers.ComputeAlignedPenX(textRun.X, textRun.Width, style.HorizontalAlignment, line.Width);
            var baselineY = firstBaselineY + lineIndex * lineHeight;
            var lineSegments = shapedRun.Segments.Slice(line.SegmentStart, line.SegmentCount);

            if (line.IsRightToLeft)
            {
                var penX = lineX + line.Width;
                foreach (ref readonly var shapedSegment in lineSegments)
                {
                    var segmentAdvance = ComputeShapedSegmentAdvance(shapedSegment);
                    penX -= segmentAdvance;
                    if (shapedSegment.GlyphCount == 0)
                    {
                        continue;
                    }

                    if (!TryAppendShapedSegment(
                        text,
                        shapedRun,
                        shapedSegment,
                        penX,
                        baselineY,
                        color,
                        scissor,
                        viewportWidth,
                        viewportHeight,
                        recordSerial,
                        ref vertexCount,
                        ref batchCount,
                        ref batchSegmentStart,
                        ref batchPage,
                        ref colorLayerRuns,
                        ref colorBitmapRuns,
                        out unsupportedReason))
                    {
                        break;
                    }
                }
            }
            else
            {
                var penX = lineX;
                foreach (ref readonly var shapedSegment in lineSegments)
                {
                    if (shapedSegment.GlyphCount == 0)
                    {
                        penX += shapedSegment.ControlAdvance;
                        continue;
                    }

                    if (!TryAppendShapedSegment(
                        text,
                        shapedRun,
                        shapedSegment,
                        penX,
                        baselineY,
                        color,
                        scissor,
                        viewportWidth,
                        viewportHeight,
                        recordSerial,
                        ref vertexCount,
                        ref batchCount,
                        ref batchSegmentStart,
                        ref batchPage,
                        ref colorLayerRuns,
                        ref colorBitmapRuns,
                        out unsupportedReason))
                    {
                        break;
                    }

                    penX += ComputeShapedGlyphAdvance(shapedSegment.GlyphStart, shapedSegment.GlyphCount);
                }
            }

            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                break;
            }
        }

        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            vertexCount = batchStart;
            while (batchCount > 0 && _batches[batchCount - 1].StartVertex >= batchStart)
            {
                batchCount--;
            }

            return false;
        }

        if (vertexCount > batchSegmentStart && !TryAppendDrawBatch(ref batchCount, ref vertexCount, batchSegmentStart, scissor, batchPage))
        {
            vertexCount = batchStart;
            while (batchCount > 0 && _batches[batchCount - 1].StartVertex >= batchStart)
            {
                batchCount--;
            }

            unsupportedReason = GlyphAtlasFallbackReason.BatchLimit;
            return false;
        }

        acceptedColorLayerRuns += colorLayerRuns;
        acceptedColorBitmapRuns += colorBitmapRuns;
        return true;
    }

    private bool TryAppendShapedSegment(
        ReadOnlySpan<char> text,
        ShapedGlyphRun shapedRun,
        ShapedGlyphSegment shapedSegment,
        float penX,
        float baselineY,
        Vector4 color,
        IntegerScissorRect scissor,
        int viewportWidth,
        int viewportHeight,
        long recordSerial,
        ref int vertexCount,
        ref int batchCount,
        ref int batchSegmentStart,
        ref GlyphAtlasPageHandle batchPage,
        ref int acceptedColorLayerRuns,
        ref int acceptedColorBitmapRuns,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        var segmentRequiresColor = shapedRun.RequiresColorGlyph
            && GlyphAtlasTextCompositionHelpers.ContainsColorGlyphCandidate(text.Slice(shapedSegment.TextStart, shapedSegment.TextLength));
        return segmentRequiresColor
            ? TryAppendColorGlyphSegmentLayers(
                shapedSegment,
                penX,
                baselineY,
                color,
                scissor,
                viewportWidth,
                viewportHeight,
                recordSerial,
                ref vertexCount,
                ref batchCount,
                ref batchSegmentStart,
                ref batchPage,
                ref acceptedColorLayerRuns,
                ref acceptedColorBitmapRuns,
                out unsupportedReason)
            : TryAppendShapedGlyphSegment(
                shapedRun.Glyphs,
                shapedSegment,
                penX,
                baselineY,
                color,
                scissor,
                viewportWidth,
                viewportHeight,
                recordSerial,
                ref vertexCount,
                ref batchCount,
                ref batchSegmentStart,
                ref batchPage,
                out unsupportedReason);
    }

    private bool TryAppendShapedGlyphSegment(
        ReadOnlySpan<ShapedGlyph> shapedGlyphs,
        ShapedGlyphSegment shapedSegment,
        float penX,
        float baselineY,
        Vector4 color,
        IntegerScissorRect scissor,
        int viewportWidth,
        int viewportHeight,
        long recordSerial,
        ref int vertexCount,
        ref int batchCount,
        ref int batchSegmentStart,
        ref GlyphAtlasPageHandle batchPage,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var segmentGlyphs = shapedGlyphs.Slice(shapedSegment.GlyphStart, shapedSegment.GlyphCount);
        var glyphPenX = shapedSegment.IsRightToLeft ? penX + ComputeShapedGlyphAdvance(shapedSegment.GlyphStart, shapedSegment.GlyphCount) : penX;
        foreach (ref readonly var shapedGlyph in segmentGlyphs)
        {
            if (shapedSegment.IsRightToLeft)
            {
                glyphPenX -= shapedGlyph.Advance;
            }

            if (!TryGetShapedGlyph(shapedSegment.FontFace, shapedSegment.FontEmSize, shapedGlyph, recordSerial, out var glyph, out unsupportedReason))
            {
                return false;
            }

            if (!TryAppendGlyphQuad(
                glyph,
                color,
                glyphPenX + glyph.OffsetX + (shapedSegment.IsRightToLeft ? -shapedGlyph.AdvanceOffset : shapedGlyph.AdvanceOffset),
                baselineY + glyph.OffsetY - shapedGlyph.AscenderOffset,
                scissor,
                viewportWidth,
                viewportHeight,
                ref vertexCount,
                ref batchCount,
                ref batchSegmentStart,
                ref batchPage,
                out unsupportedReason))
            {
                return false;
            }

            if (!shapedSegment.IsRightToLeft)
            {
                glyphPenX += shapedGlyph.Advance;
            }
        }

        return true;
    }

}

