using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    // DirectWrite color glyph source-data translation for atlas entries.
    private const int DWriteNoColorHResult = unchecked((int)0x8898500C);
    private const DWRITE_GLYPH_IMAGE_FORMATS SupportedLayerColorGlyphFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_CFF
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR;
    private const DWRITE_GLYPH_IMAGE_FORMATS EncodedBitmapColorGlyphFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF;
    private const DWRITE_GLYPH_IMAGE_FORMATS SupportedBitmapColorGlyphFormats =
        EncodedBitmapColorGlyphFormats
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;
    private const DWRITE_GLYPH_IMAGE_FORMATS UnsupportedNonLayerColorGlyphFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE;
    private const DWRITE_GLYPH_IMAGE_FORMATS ColorGlyphRunImageFormats =
        SupportedLayerColorGlyphFormats
        | SupportedBitmapColorGlyphFormats;
    private const DWRITE_GLYPH_IMAGE_FORMATS BitmapColorGlyphFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF;

    private bool TryAppendColorGlyphSegmentLayers(
        ShapedGlyphSegment shapedSegment,
        float baselineOriginX,
        float baselineOriginY,
        Vector4 currentBrush,
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
        unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
        if (_dwriteFactory4 == null || shapedSegment.FontFace.Face == null || shapedSegment.GlyphCount == 0)
        {
            return false;
        }

        if (TryAppendColorGlyphSegmentRuns(
            shapedSegment,
            baselineOriginX,
            baselineOriginY,
            currentBrush,
            scissor,
            viewportWidth,
            viewportHeight,
            recordSerial,
            ref vertexCount,
            ref batchCount,
            ref batchSegmentStart,
            ref batchPage,
            out var colorLayerRuns,
            out var colorBitmapRuns,
            out unsupportedReason))
        {
            acceptedColorLayerRuns += colorLayerRuns;
            acceptedColorBitmapRuns += colorBitmapRuns;
            return true;
        }

        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            return false;
        }

        if (TryAppendBgraColorGlyphSegment(
            shapedSegment,
            baselineOriginX,
            baselineOriginY,
            currentBrush,
            scissor,
            viewportWidth,
            viewportHeight,
            recordSerial,
            ref vertexCount,
            ref batchCount,
            ref batchSegmentStart,
            ref batchPage,
            out unsupportedReason))
        {
            acceptedColorBitmapRuns++;
            return true;
        }

        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            return false;
        }

        if (TryAppendEncodedBitmapColorGlyphSegment(
            shapedSegment,
            baselineOriginX,
            baselineOriginY,
            currentBrush,
            scissor,
            viewportWidth,
            viewportHeight,
            recordSerial,
            ref vertexCount,
            ref batchCount,
            ref batchSegmentStart,
            ref batchPage,
            out unsupportedReason))
        {
            acceptedColorBitmapRuns++;
            return true;
        }

        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            return false;
        }

        if (TryGetUnsupportedOnlyColorGlyphImageFormatReason(shapedSegment, out var imageFormatUnsupportedReason))
        {
            unsupportedReason = imageFormatUnsupportedReason;
            return false;
        }

        unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
        return false;
    }

    private bool TryAppendColorGlyphSegmentRuns(
        ShapedGlyphSegment shapedSegment,
        float baselineOriginX,
        float baselineOriginY,
        Vector4 currentBrush,
        IntegerScissorRect scissor,
        int viewportWidth,
        int viewportHeight,
        long recordSerial,
        ref int vertexCount,
        ref int batchCount,
        ref int batchSegmentStart,
        ref GlyphAtlasPageHandle batchPage,
        out int acceptedColorLayerRuns,
        out int acceptedColorBitmapRuns,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        acceptedColorLayerRuns = 0;
        acceptedColorBitmapRuns = 0;
        if (_dwriteFactory4 == null || shapedSegment.FontFace.Face == null || shapedSegment.GlyphCount == 0)
        {
            return false;
        }

        fixed (ushort* glyphIndicesBase = _shapeGlyphScratch)
        fixed (float* advancesBase = _shapeAdvanceScratch)
        fixed (DWRITE_GLYPH_OFFSET* offsetsBase = _shapeOffsetScratch)
        {
            var glyphRun = new DWRITE_GLYPH_RUN
            {
                fontFace = shapedSegment.FontFace.Face,
                fontEmSize = shapedSegment.FontEmSize,
                glyphCount = (uint)shapedSegment.GlyphCount,
                glyphIndices = glyphIndicesBase + shapedSegment.GlyphStart,
                glyphAdvances = advancesBase + shapedSegment.GlyphStart,
                glyphOffsets = offsetsBase + shapedSegment.GlyphStart,
                isSideways = false,
                bidiLevel = shapedSegment.BidiLevel
            };

            IDWriteColorGlyphRunEnumerator1* colorRuns = null;
            try
            {
                var baselineOrigin = new D2D_POINT_2F { x = baselineOriginX, y = baselineOriginY };
                _dwriteFactory4->TranslateColorGlyphRun(
                    baselineOrigin,
                    &glyphRun,
                    null,
                    ColorGlyphRunImageFormats,
                    DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                    null,
                    0,
                    &colorRuns);
            }
            catch (COMException ex) when (ex.ErrorCode == DWriteNoColorHResult)
            {
                return false;
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Color glyph format-aware translation failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
            catch (ArgumentException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Color glyph format-aware translation rejected: 0x{unchecked((uint)ex.HResult):X8}");
                return false;
            }

            if (colorRuns == null)
            {
                return false;
            }

            var runCount = 0;
            try
            {
                while (true)
                {
                    BOOL hasRun;
                    colorRuns->MoveNext(&hasRun);
                    if (!hasRun)
                    {
                        break;
                    }

                    DWRITE_COLOR_GLYPH_RUN1* colorGlyphRun;
                    colorRuns->GetCurrentRun(&colorGlyphRun);
                    if (colorGlyphRun == null || colorGlyphRun->Base.glyphRun.glyphCount == 0)
                    {
                        continue;
                    }

                    if (!TryAppendColorGlyphRun(
                        shapedSegment.FontFace,
                        colorGlyphRun,
                        currentBrush,
                        scissor,
                        viewportWidth,
                        viewportHeight,
                        recordSerial,
                        ref vertexCount,
                        ref batchCount,
                        ref batchSegmentStart,
                        ref batchPage,
                        out var acceptedLayerRun,
                        out var acceptedBitmapRun,
                        out unsupportedReason))
                    {
                        return false;
                    }

                    if (acceptedLayerRun)
                    {
                        acceptedColorLayerRuns++;
                    }
                    if (acceptedBitmapRun)
                    {
                        acceptedColorBitmapRuns++;
                    }
                    runCount++;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Color glyph format-aware enumeration failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
                return false;
            }
            finally
            {
                colorRuns->Release();
            }

            unsupportedReason = runCount == 0 ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph : GlyphAtlasFallbackReason.None;
            return runCount > 0;
        }
    }

    private bool TryAppendColorGlyphRun(
        CachedFontFace fontFace,
        DWRITE_COLOR_GLYPH_RUN1* colorGlyphRun,
        Vector4 currentBrush,
        IntegerScissorRect scissor,
        int viewportWidth,
        int viewportHeight,
        long recordSerial,
        ref int vertexCount,
        ref int batchCount,
        ref int batchSegmentStart,
        ref GlyphAtlasPageHandle batchPage,
        out bool acceptedLayerRun,
        out bool acceptedBitmapRun,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        acceptedLayerRun = false;
        acceptedBitmapRun = false;
        if (colorGlyphRun == null)
        {
            return true;
        }

        var imageFormat = colorGlyphRun->glyphImageFormat;
        if ((imageFormat & SupportedLayerColorGlyphFormats) != 0)
        {
            var baseRun = &colorGlyphRun->Base;
            if (TryAppendColorGlyphLayer(
                fontFace,
                &baseRun->glyphRun,
                baseRun->baselineOriginX,
                baseRun->baselineOriginY,
                ResolveColorGlyphLayerColor(baseRun->runColor, baseRun->paletteIndex, currentBrush),
                scissor,
                viewportWidth,
                viewportHeight,
                recordSerial,
                ref vertexCount,
                ref batchCount,
                ref batchSegmentStart,
                ref batchPage,
                out unsupportedReason))
            {
                acceptedLayerRun = true;
                return true;
            }

            return false;
        }

        if ((imageFormat & SupportedBitmapColorGlyphFormats) != 0)
        {
            if (TryAppendBitmapColorGlyphRun(
                fontFace,
                &colorGlyphRun->Base,
                imageFormat,
                currentBrush,
                scissor,
                viewportWidth,
                viewportHeight,
                recordSerial,
                ref vertexCount,
                ref batchCount,
                ref batchSegmentStart,
                ref batchPage,
                out unsupportedReason))
            {
                acceptedBitmapRun = true;
                return true;
            }

            return false;
        }

        unsupportedReason = GetColorGlyphRunImageFormatFallbackReason(imageFormat);
        return false;
    }

    private bool TryAppendBitmapColorGlyphRun(
        CachedFontFace fontFace,
        DWRITE_COLOR_GLYPH_RUN* colorGlyphRun,
        DWRITE_GLYPH_IMAGE_FORMATS imageFormat,
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
        if (colorGlyphRun == null || colorGlyphRun->glyphRun.glyphCount == 0)
        {
            return true;
        }

        var glyphRun = &colorGlyphRun->glyphRun;
        if (glyphRun->fontFace != fontFace.Face || glyphRun->isSideways || glyphRun->glyphIndices == null || glyphRun->glyphCount > int.MaxValue)
        {
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            return false;
        }

        var fontEmSize = glyphRun->fontEmSize;
        var pixelsPerEm = ComputeGlyphImagePixelsPerEm(fontEmSize);
        var glyphPenX = colorGlyphRun->baselineOriginX;
        var glyphCount = (int)glyphRun->glyphCount;
        var bitmapColor = ResolveColorGlyphBitmapColor(color);
        var appendedAny = false;
        for (var i = 0; i < glyphCount; i++)
        {
            var glyphIndex = glyphRun->glyphIndices[i];
            if (glyphIndex == 0)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
                return false;
            }

            var advance = glyphRun->glyphAdvances != null ? glyphRun->glyphAdvances[i] : ComputeGlyphAdvance(fontFace, fontEmSize, glyphIndex);
            var offset = glyphRun->glyphOffsets != null ? glyphRun->glyphOffsets[i] : default;
            GlyphEntry glyph;
            if (!TrySelectColorGlyphBitmapImageFormat(imageFormat, out var selectedImageFormat))
            {
                unsupportedReason = GetColorGlyphRunImageFormatFallbackReason(imageFormat);
                return false;
            }

            if (selectedImageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
            {
                if (!TryGetBgraColorGlyph(fontFace, fontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out glyph, out _, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None ? GetColorGlyphRunImageFormatFallbackReason(imageFormat) : unsupportedReason;
                    return false;
                }
            }
            else
            {
                if (!TryGetEncodedBitmapColorGlyph(fontFace, fontEmSize, pixelsPerEm, glyphIndex, selectedImageFormat, advance, recordSerial, out glyph, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None ? GetColorGlyphRunImageFormatFallbackReason(imageFormat) : unsupportedReason;
                    return false;
                }
            }

            if (!TryAppendGlyphQuad(
                glyph,
                bitmapColor,
                glyphPenX + glyph.OffsetX + offset.advanceOffset,
                colorGlyphRun->baselineOriginY + glyph.OffsetY - offset.ascenderOffset,
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

            appendedAny = true;
            glyphPenX += advance;
        }

        return appendedAny;
    }

    private bool TryAppendBgraColorGlyphSegment(
        ShapedGlyphSegment shapedSegment,
        float baselineOriginX,
        float baselineOriginY,
        Vector4 currentBrush,
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
        if (shapedSegment.FontFace.Face4 == null || shapedSegment.GlyphCount == 0)
        {
            return false;
        }

        var glyphPenX = shapedSegment.IsRightToLeft ? baselineOriginX + ComputeShapedGlyphAdvance(shapedSegment.GlyphStart, shapedSegment.GlyphCount) : baselineOriginX;
        var pixelsPerEm = ComputeGlyphImagePixelsPerEm(shapedSegment.FontEmSize);
        for (var i = 0; i < shapedSegment.GlyphCount; i++)
        {
            var glyphIndex = _shapeGlyphScratch[shapedSegment.GlyphStart + i];
            if (glyphIndex == 0)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
                return false;
            }

            try
            {
                shapedSegment.FontFace.Face4->GetGlyphImageFormats(glyphIndex, pixelsPerEm, pixelsPerEm, out var formats);
                if ((formats & SupportedLayerColorGlyphFormats) != 0)
                {
                    return false;
                }

                if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) == 0)
                {
                    return false;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] BGRA color glyph image format query failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
        }

        var bitmapColor = ResolveColorGlyphBitmapColor(currentBrush);
        var appendedAny = false;
        for (var i = 0; i < shapedSegment.GlyphCount; i++)
        {
            var glyphIndex = _shapeGlyphScratch[shapedSegment.GlyphStart + i];
            var advance = _shapeAdvanceScratch[shapedSegment.GlyphStart + i];
            var offset = _shapeOffsetScratch[shapedSegment.GlyphStart + i];
            if (shapedSegment.IsRightToLeft)
            {
                glyphPenX -= advance;
            }

            if (!TryGetBgraColorGlyph(shapedSegment.FontFace, shapedSegment.FontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out var glyph, out var glyphHadBgra, out unsupportedReason))
            {
                unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None
                    ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph | GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra
                    : unsupportedReason;
                return false;
            }

            if (!TryAppendGlyphQuad(
                glyph,
                bitmapColor,
                glyphPenX + glyph.OffsetX + (shapedSegment.IsRightToLeft ? -offset.advanceOffset : offset.advanceOffset),
                baselineOriginY + glyph.OffsetY - offset.ascenderOffset,
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

            appendedAny |= glyphHadBgra;
            if (!shapedSegment.IsRightToLeft)
            {
                glyphPenX += advance;
            }
        }

        return appendedAny;
    }

    private bool TryAppendEncodedBitmapColorGlyphSegment(
        ShapedGlyphSegment shapedSegment,
        float baselineOriginX,
        float baselineOriginY,
        Vector4 currentBrush,
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
        if (shapedSegment.FontFace.Face4 == null || shapedSegment.GlyphCount == 0)
        {
            return false;
        }

        var pixelsPerEm = ComputeGlyphImagePixelsPerEm(shapedSegment.FontEmSize);
        for (var i = 0; i < shapedSegment.GlyphCount; i++)
        {
            var glyphIndex = _shapeGlyphScratch[shapedSegment.GlyphStart + i];
            if (glyphIndex == 0)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
                return false;
            }

            try
            {
                shapedSegment.FontFace.Face4->GetGlyphImageFormats(glyphIndex, pixelsPerEm, pixelsPerEm, out var formats);
                if ((formats & SupportedLayerColorGlyphFormats) != 0)
                {
                    return false;
                }

                if ((formats & BitmapColorGlyphFormats) == 0)
                {
                    return false;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] bitmap color glyph image format query failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
        }

        var glyphPenX = shapedSegment.IsRightToLeft ? baselineOriginX + ComputeShapedGlyphAdvance(shapedSegment.GlyphStart, shapedSegment.GlyphCount) : baselineOriginX;
        var bitmapColor = ResolveColorGlyphBitmapColor(currentBrush);
        var appendedAny = false;
        for (var i = 0; i < shapedSegment.GlyphCount; i++)
        {
            var glyphIndex = _shapeGlyphScratch[shapedSegment.GlyphStart + i];
            var advance = _shapeAdvanceScratch[shapedSegment.GlyphStart + i];
            var offset = _shapeOffsetScratch[shapedSegment.GlyphStart + i];
            if (shapedSegment.IsRightToLeft)
            {
                glyphPenX -= advance;
            }

            DWRITE_GLYPH_IMAGE_FORMATS formats;
            try
            {
                shapedSegment.FontFace.Face4->GetGlyphImageFormats(glyphIndex, pixelsPerEm, pixelsPerEm, out formats);
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] bitmap color glyph image format query failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }

            if (!TrySelectColorGlyphBitmapImageFormat(formats, out var imageFormat))
            {
                return false;
            }

            if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
            {
                if (!TryGetBgraColorGlyph(shapedSegment.FontFace, shapedSegment.FontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out var glyph, out _, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None
                        ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph | GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra
                        : unsupportedReason;
                    return false;
                }

                if (!TryAppendGlyphQuad(
                    glyph,
                    bitmapColor,
                    glyphPenX + glyph.OffsetX + (shapedSegment.IsRightToLeft ? -offset.advanceOffset : offset.advanceOffset),
                    baselineOriginY + glyph.OffsetY - offset.ascenderOffset,
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
            }
            else
            {
                if (!TryGetEncodedBitmapColorGlyph(shapedSegment.FontFace, shapedSegment.FontEmSize, pixelsPerEm, glyphIndex, imageFormat, advance, recordSerial, out var glyph, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None
                        ? GetEncodedBitmapColorGlyphFallbackReason(imageFormat)
                        : unsupportedReason;
                    return false;
                }

                if (!TryAppendGlyphQuad(
                    glyph,
                    bitmapColor,
                    glyphPenX + glyph.OffsetX + (shapedSegment.IsRightToLeft ? -offset.advanceOffset : offset.advanceOffset),
                    baselineOriginY + glyph.OffsetY - offset.ascenderOffset,
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
            }

            appendedAny = true;
            if (!shapedSegment.IsRightToLeft)
            {
                glyphPenX += advance;
            }
        }

        return appendedAny;
    }

    private bool TryGetUnsupportedOnlyColorGlyphImageFormatReason(ShapedGlyphSegment shapedSegment, out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (shapedSegment.FontFace.Face4 == null)
        {
            return false;
        }

        var pixelsPerEm = ComputeGlyphImagePixelsPerEm(shapedSegment.FontEmSize);
        var glyphs = _shapeGlyphScratch.AsSpan(shapedSegment.GlyphStart, shapedSegment.GlyphCount);
        foreach (var glyphIndex in glyphs)
        {
            if (glyphIndex == 0)
            {
                continue;
            }

            try
            {
                shapedSegment.FontFace.Face4->GetGlyphImageFormats(glyphIndex, pixelsPerEm, pixelsPerEm, out var formats);
                unsupportedReason = GetUnsupportedColorGlyphImageFormatReason(formats);
                if (unsupportedReason != GlyphAtlasFallbackReason.None)
                {
                    return true;
                }
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Color glyph image format query failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
        }

        return false;
    }

    private static uint ComputeGlyphImagePixelsPerEm(float fontEmSize)
    {
        if (!float.IsFinite(fontEmSize) || fontEmSize <= 1f)
        {
            return 1;
        }

        return (uint)Math.Min(ushort.MaxValue, MathF.Ceiling(fontEmSize));
    }

    internal static GlyphAtlasFallbackReason GetUnsupportedColorGlyphImageFormatReason(DWRITE_GLYPH_IMAGE_FORMATS formats)
    {
        if ((formats & (SupportedLayerColorGlyphFormats | SupportedBitmapColorGlyphFormats)) != 0)
        {
            return GlyphAtlasFallbackReason.None;
        }

        var unsupportedFormats = formats & UnsupportedNonLayerColorGlyphFormats;
        if (unsupportedFormats == 0)
        {
            return GlyphAtlasFallbackReason.None;
        }

        var reason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
        if ((unsupportedFormats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG) != 0)
        {
            reason |= GlyphAtlasFallbackReason.ColorGlyphSvg;
        }

        if ((unsupportedFormats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE) != 0)
        {
            reason |= GlyphAtlasFallbackReason.ColorGlyphPaintTree;
        }

        return reason;
    }

    internal static bool TrySelectColorGlyphBitmapImageFormat(DWRITE_GLYPH_IMAGE_FORMATS formats, out DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) != 0)
        {
            imageFormat = DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8;
            return true;
        }

        return TrySelectEncodedBitmapColorGlyphFormat(formats, out imageFormat);
    }

    private static bool TrySelectEncodedBitmapColorGlyphFormat(DWRITE_GLYPH_IMAGE_FORMATS formats, out DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG) != 0)
        {
            imageFormat = DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG;
            return true;
        }

        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF) != 0)
        {
            imageFormat = DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF;
            return true;
        }

        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG) != 0)
        {
            imageFormat = DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG;
            return true;
        }

        imageFormat = default;
        return false;
    }

    private static GlyphAtlasFallbackReason GetEncodedBitmapColorGlyphFallbackReason(DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        var reason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
        if ((imageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG) != 0)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphPng;
        }

        if ((imageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF) != 0)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphTiff;
        }

        if ((imageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG) != 0)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphJpeg;
        }

        return reason;
    }

    internal static GlyphAtlasFallbackReason GetColorGlyphRunImageFormatFallbackReason(DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        if ((imageFormat & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) != 0)
        {
            return GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph | GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra;
        }

        if ((imageFormat & EncodedBitmapColorGlyphFormats) != 0)
        {
            return GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
        }

        var unsupportedReason = GetUnsupportedColorGlyphImageFormatReason(imageFormat);
        return unsupportedReason == GlyphAtlasFallbackReason.None
            ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph
            : unsupportedReason;
    }

    private static byte GetEncodedBitmapGlyphFormatId(DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG)
        {
            return 1;
        }

        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG)
        {
            return 2;
        }

        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF)
        {
            return 3;
        }

        return 0;
    }

    private bool TryAppendColorGlyphLayer(
        CachedFontFace fontFace,
        DWRITE_GLYPH_RUN* colorGlyphRun,
        float baselineOriginX,
        float baselineOriginY,
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
        if (colorGlyphRun == null || colorGlyphRun->glyphCount == 0)
        {
            return true;
        }

        if (colorGlyphRun->fontFace != fontFace.Face || colorGlyphRun->isSideways || colorGlyphRun->glyphIndices == null || colorGlyphRun->glyphCount > int.MaxValue)
        {
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            return false;
        }

        var glyphPenX = baselineOriginX;
        var glyphCount = (int)colorGlyphRun->glyphCount;
        for (var i = 0; i < glyphCount; i++)
        {
            var glyphIndex = colorGlyphRun->glyphIndices[i];
            if (glyphIndex == 0)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
                return false;
            }

            var advance = colorGlyphRun->glyphAdvances != null ? colorGlyphRun->glyphAdvances[i] : ComputeGlyphAdvance(fontFace, colorGlyphRun->fontEmSize, glyphIndex);
            var offset = colorGlyphRun->glyphOffsets != null ? colorGlyphRun->glyphOffsets[i] : default;
            if (!TryGetColorLayerGlyph(fontFace, colorGlyphRun->fontEmSize, glyphIndex, advance, recordSerial, out var glyph, out unsupportedReason))
            {
                return false;
            }

            if (!TryAppendGlyphQuad(
                glyph,
                color,
                glyphPenX + glyph.OffsetX + offset.advanceOffset,
                baselineOriginY + glyph.OffsetY - offset.ascenderOffset,
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

            glyphPenX += advance;
        }

        return true;
    }

    private bool TryAppendGlyphQuad(
        GlyphEntry glyph,
        Vector4 color,
        float x1,
        float y1,
        IntegerScissorRect scissor,
        int viewportWidth,
        int viewportHeight,
        ref int vertexCount,
        ref int batchCount,
        ref int batchSegmentStart,
        ref GlyphAtlasPageHandle batchPage,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (glyph.Width <= 0 || glyph.Height <= 0)
        {
            return true;
        }

        if (batchPage.IsNone)
        {
            batchPage = glyph.Page;
        }
        else if (batchPage != glyph.Page)
        {
            if (!TryAppendDrawBatch(ref batchCount, ref vertexCount, batchSegmentStart, scissor, batchPage))
            {
                unsupportedReason = GlyphAtlasFallbackReason.BatchLimit;
                return false;
            }

            batchSegmentStart = vertexCount;
            batchPage = glyph.Page;
        }

        if (vertexCount + 6 > MaxGlyphVertices)
        {
            unsupportedReason = GlyphAtlasFallbackReason.VertexLimit;
            return false;
        }

        AppendQuad(_vertices, ref vertexCount, x1, y1, x1 + glyph.Width, y1 + glyph.Height, glyph, color, viewportWidth, viewportHeight);
        return true;
    }

    private static Vector4 ResolveColorGlyphLayerColor(DWRITE_COLOR_F runColor, ushort paletteIndex, Vector4 currentBrush)
    {
        if (paletteIndex == 0xFFFF || (runColor.r == 0 && runColor.g == 0 && runColor.b == 0 && runColor.a == 0))
        {
            return currentBrush;
        }

        return new Vector4(runColor.r, runColor.g, runColor.b, runColor.a * currentBrush.W);
    }

    internal static Vector4 ResolveColorGlyphBitmapColor(Vector4 currentBrush) => new(1f, 1f, 1f, currentBrush.W);
}

