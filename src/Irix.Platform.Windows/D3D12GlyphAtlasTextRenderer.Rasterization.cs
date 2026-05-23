using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
    // DirectWrite/WIC glyph source-data rasterization before atlas residency.
    private static readonly Guid WicImagingFactoryClsid = new(0xCACAF262, 0x9370, 0x4615, 0xA1, 0x3B, 0x9F, 0x55, 0x39, 0xDA, 0x4C, 0x0A);
    private static readonly Guid WicPixelFormat32bppPbgra = new(0x6FDDC324, 0x4E03, 0x4BFE, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x0F);
    private const int RpcEChangedModeHResult = unchecked((int)0x80010106);

    private byte[] _wicDecodeScratch = [];
    private bool _wicFactoryUnavailable;
    private bool _wicComInitializedForFactory;
    private int _wicComInitializationThreadId;
    private IWICImagingFactory* _wicFactory;

    private void ReleaseWicFactory()
    {
        if (_wicFactory != null)
        {
            _wicFactory->Release();
            _wicFactory = null;
        }

        if (_wicComInitializedForFactory && _wicComInitializationThreadId == Environment.CurrentManagedThreadId)
        {
            PInvoke.CoUninitialize();
        }

        _wicComInitializedForFactory = false;
        _wicComInitializationThreadId = 0;
    }

    private bool RasterizeGlyph(
        GlyphKey key,
        CachedFontFace fontFace,
        float emSize,
        GlyphAtom glyphAtom,
        long recordSerial,
        out GlyphEntry entry,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        var shapedGlyph = ShapedGlyph.Simple(glyphAtom.GlyphIndex, ComputeGlyphAdvance(fontFace, emSize, glyphAtom.GlyphIndex));
        return RasterizeGlyph(key, fontFace, emSize, shapedGlyph, recordSerial, out entry, out unsupportedReason);
    }

    private bool RasterizeGlyph(
        GlyphKey key,
        CachedFontFace fontFace,
        float emSize,
        ShapedGlyph shapedGlyph,
        long recordSerial,
        out GlyphEntry entry,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        entry = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var glyphIndex = stackalloc ushort[1];
        glyphIndex[0] = shapedGlyph.GlyphIndex;
        var advances = stackalloc float[1];
        advances[0] = shapedGlyph.Advance;

        var offsets = stackalloc DWRITE_GLYPH_OFFSET[1];
        var run = new DWRITE_GLYPH_RUN
        {
            fontFace = fontFace.Face,
            fontEmSize = emSize,
            glyphCount = 1,
            glyphIndices = glyphIndex,
            glyphAdvances = advances,
            glyphOffsets = offsets,
            isSideways = false,
            bidiLevel = 0
        };

        IDWriteGlyphRunAnalysis* analysis = null;
        try
        {
            _dwriteFactory->CreateGlyphRunAnalysis(
                &run,
                1.0f,
                null,
                DWRITE_RENDERING_MODE.DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC,
                DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL,
                0,
                0,
                &analysis);

            if (!GlyphAtlasTextCompositionHelpers.HasGlyphRunAnalysisResource(analysis != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.RasterizeGlyph found a missing DirectWrite glyph run analysis.");
            }

            analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, out var bounds);
            var width = bounds.right - bounds.left;
            var height = bounds.bottom - bounds.top;
            var atlasPage = SelectWritableAtlasPage(GlyphAtlasPageFormat.Alpha, width, height, recordSerial);
            if (atlasPage is null)
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            if (width <= 0 || height <= 0)
            {
                atlasPage.Touch(recordSerial);
                entry = new GlyphEntry(key, 0, 0, 0, 0, advances[0], 0, 0, 0, 0, atlasPage.Handle, LastUsedSerial: recordSerial);
                return true;
            }

            var clearTypeBytes = checked(width * height * 3);
            var clearType = RentClearTypeScratch(clearTypeBytes);
            analysis->CreateAlphaTexture(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, bounds, clearType);

            var grayscale = RentGrayscaleScratch(width * height);
            for (var i = 0; i < grayscale.Length; i++)
            {
                var source = i * 3;
                grayscale[i] = (byte)((clearType[source] + clearType[source + 1] + clearType[source + 2]) / 3);
            }

            if (!TryAllocateGlyph(atlasPage, width, height, out var atlasX, out var atlasY))
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            CopyGlyphToAtlas(atlasPage, grayscale, width, height, atlasX, atlasY);
            atlasPage.Touch(recordSerial);
            var u1 = atlasX / (float)AtlasWidth;
            var v1 = atlasY / (float)AtlasHeight;
            var u2 = (atlasX + width) / (float)AtlasWidth;
            var v2 = (atlasY + height) / (float)AtlasHeight;
            entry = new GlyphEntry(
                key,
                width,
                height,
                bounds.left,
                bounds.top,
                advances[0],
                u1,
                v1,
                u2,
                v2,
                atlasPage.Handle,
                LastUsedSerial: recordSerial);
            MarkAtlasDirty(atlasPage, atlasX, atlasY, width, height);
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.RasterizeGlyph",
                ex);
        }
        finally
        {
            if (analysis != null) analysis->Release();
        }
    }

    private bool RasterizeBgraColorGlyph(
        GlyphKey key,
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        float advance,
        long recordSerial,
        out GlyphEntry entry,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        entry = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        void* glyphDataContext = null;
        try
        {
            fontFace.Face4->GetGlyphImageData(
                glyphIndex,
                pixelsPerEm,
                DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8,
                out var glyphData,
                out glyphDataContext);

            if (glyphData.imageData == null || glyphData.imageDataSize == 0)
            {
                return false;
            }

            if (!TryGetBgraGlyphImageSize(glyphData, out var width, out var height))
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph | GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra;
                return false;
            }

            var rowBytes = checked(width * BgraAtlasBytesPerPixel);
            var glyphBytes = checked(rowBytes * height);
            if ((uint)glyphBytes > glyphData.imageDataSize)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph | GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra;
                return false;
            }

            var atlasPage = SelectWritableAtlasPage(GlyphAtlasPageFormat.Bgra, width, height, recordSerial);
            if (atlasPage is null)
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            if (!TryAllocateGlyph(atlasPage, width, height, out var atlasX, out var atlasY))
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            CopyGlyphToAtlas(atlasPage, new ReadOnlySpan<byte>(glyphData.imageData, glyphBytes), width, height, atlasX, atlasY);
            atlasPage.Touch(recordSerial);
            var u1 = atlasX / (float)AtlasWidth;
            var v1 = atlasY / (float)AtlasHeight;
            var u2 = (atlasX + width) / (float)AtlasWidth;
            var v2 = (atlasY + height) / (float)AtlasHeight;
            var imageScale = ComputeGlyphImageScale(fontEmSize, glyphData.pixelsPerEm);
            entry = new GlyphEntry(
                key,
                width * imageScale,
                height * imageScale,
                glyphData.horizontalLeftOrigin.X * imageScale,
                -glyphData.horizontalLeftOrigin.Y * imageScale,
                advance,
                u1,
                v1,
                u2,
                v2,
                atlasPage.Handle,
                LastUsedSerial: recordSerial);
            MarkAtlasDirty(atlasPage, atlasX, atlasY, width, height);
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.GetGlyphImageData(BGRA)",
                ex);
        }
        finally
        {
            if (glyphDataContext != null)
            {
                fontFace.Face4->ReleaseGlyphImageData(glyphDataContext);
            }
        }
    }

    private bool RasterizeEncodedBitmapColorGlyph(
        GlyphKey key,
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        DWRITE_GLYPH_IMAGE_FORMATS imageFormat,
        float advance,
        long recordSerial,
        out GlyphEntry entry,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        entry = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        if (!TryEnsureWicFactory(out unsupportedReason))
        {
            unsupportedReason = GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
            return false;
        }

        void* glyphDataContext = null;
        try
        {
            fontFace.Face4->GetGlyphImageData(glyphIndex, pixelsPerEm, imageFormat, out var glyphData, out glyphDataContext);
            if (glyphData.imageData == null || glyphData.imageDataSize == 0)
            {
                unsupportedReason = GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
                return false;
            }

            if (!TryDecodeWicGlyphImage(new ReadOnlySpan<byte>(glyphData.imageData, checked((int)glyphData.imageDataSize)), out var decodedPixels, out var width, out var height))
            {
                unsupportedReason = GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
                return false;
            }

            if (width > AtlasWidth - AtlasPadding * 2 || height > AtlasHeight - AtlasPadding * 2)
            {
                unsupportedReason = GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
                return false;
            }

            var atlasPage = SelectWritableAtlasPage(GlyphAtlasPageFormat.Bgra, width, height, recordSerial);
            if (atlasPage is null)
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            if (!TryAllocateGlyph(atlasPage, width, height, out var atlasX, out var atlasY))
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            CopyGlyphToAtlas(atlasPage, decodedPixels, width, height, atlasX, atlasY);
            atlasPage.Touch(recordSerial);
            var u1 = atlasX / (float)AtlasWidth;
            var v1 = atlasY / (float)AtlasHeight;
            var u2 = (atlasX + width) / (float)AtlasWidth;
            var v2 = (atlasY + height) / (float)AtlasHeight;
            var imageScale = ComputeGlyphImageScale(fontEmSize, glyphData.pixelsPerEm);
            entry = new GlyphEntry(
                key,
                width * imageScale,
                height * imageScale,
                glyphData.horizontalLeftOrigin.X * imageScale,
                -glyphData.horizontalLeftOrigin.Y * imageScale,
                advance,
                u1,
                v1,
                u2,
                v2,
                atlasPage.Handle,
                LastUsedSerial: recordSerial);
            MarkAtlasDirty(atlasPage, atlasX, atlasY, width, height);
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.GetGlyphImageData(encoded bitmap)",
                ex);
        }
        catch (OverflowException)
        {
            unsupportedReason = GetEncodedBitmapColorGlyphFallbackReason(imageFormat);
            return false;
        }
        finally
        {
            if (glyphDataContext != null)
            {
                fontFace.Face4->ReleaseGlyphImageData(glyphDataContext);
            }
        }
    }

    private bool TryDecodeWicGlyphImage(ReadOnlySpan<byte> encodedBytes, out ReadOnlySpan<byte> decodedPixels, out int width, out int height)
    {
        decodedPixels = default;
        width = 0;
        height = 0;
        if (_wicFactory == null || encodedBytes.IsEmpty)
        {
            return false;
        }

        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        IWICFormatConverter* converter = null;
        try
        {
            _wicFactory->CreateStream(&stream);
            stream->InitializeFromMemory(encodedBytes);
            decoder = _wicFactory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad);
            if (decoder == null)
            {
                return false;
            }

            decoder->GetFrame(0, &frame);
            if (frame == null)
            {
                return false;
            }

            _wicFactory->CreateFormatConverter(&converter);
            if (converter == null)
            {
                return false;
            }

            converter->Initialize(
                (IWICBitmapSource*)frame,
                WicPixelFormat32bppPbgra,
                WICBitmapDitherType.WICBitmapDitherTypeNone,
                null,
                0,
                WICBitmapPaletteType.WICBitmapPaletteTypeCustom);
            converter->GetSize(out var decodedWidth, out var decodedHeight);
            if (!TryGetWicGlyphImageSize(decodedWidth, decodedHeight, out width, out height))
            {
                width = 0;
                height = 0;
                return false;
            }

            var stride = checked((uint)(width * BgraAtlasBytesPerPixel));
            var decodedByteCount = checked(width * height * BgraAtlasBytesPerPixel);
            EnsureWicDecodeScratch(decodedByteCount);
            converter->CopyPixels(null, stride, _wicDecodeScratch.AsSpan(0, decodedByteCount));
            decodedPixels = _wicDecodeScratch.AsSpan(0, decodedByteCount);
            return true;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] WIC color glyph decode failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
            decodedPixels = default;
            width = 0;
            height = 0;
            return false;
        }
        catch (OverflowException)
        {
            decodedPixels = default;
            width = 0;
            height = 0;
            return false;
        }
        finally
        {
            if (converter != null) converter->Release();
            if (frame != null) frame->Release();
            if (decoder != null) decoder->Release();
            if (stream != null) stream->Release();
        }
    }

    private bool TryEnsureWicFactory(out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (_wicFactory != null)
        {
            return true;
        }

        if (_wicFactoryUnavailable)
        {
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            return false;
        }

        var coInitializeHr = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
        var ownsComInitialization = coInitializeHr.Succeeded;
        if (coInitializeHr.Failed && (int)coInitializeHr != RpcEChangedModeHResult)
        {
            _wicFactoryUnavailable = true;
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            return false;
        }

        var factoryHr = PInvoke.CoCreateInstance<IWICImagingFactory>(WicImagingFactoryClsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out var factory);
        if (factoryHr.Failed || factory == null)
        {
            if (ownsComInitialization)
            {
                PInvoke.CoUninitialize();
            }

            _wicFactoryUnavailable = true;
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            return false;
        }

        _wicFactory = factory;
        _wicComInitializedForFactory = ownsComInitialization;
        _wicComInitializationThreadId = ownsComInitialization ? Environment.CurrentManagedThreadId : 0;
        return true;
    }

    private static bool TryGetWicGlyphImageSize(uint decodedWidth, uint decodedHeight, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (decodedWidth == 0 || decodedHeight == 0 || decodedWidth > int.MaxValue || decodedHeight > int.MaxValue)
        {
            return false;
        }

        width = (int)decodedWidth;
        height = (int)decodedHeight;
        return true;
    }

    internal static float ComputeGlyphImageScale(float fontEmSize, uint pixelsPerEm)
    {
        if (!float.IsFinite(fontEmSize) || fontEmSize <= 0 || pixelsPerEm == 0)
        {
            return 1f;
        }

        var scale = fontEmSize / pixelsPerEm;
        return float.IsFinite(scale) && scale > 0 ? scale : 1f;
    }

    private void EnsureWicDecodeScratch(int byteCount)
    {
        if (_wicDecodeScratch.Length < byteCount)
        {
            _wicDecodeScratch = new byte[byteCount];
        }
    }

    private static bool TryGetBgraGlyphImageSize(DWRITE_GLYPH_IMAGE_DATA glyphData, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (glyphData.pixelSize.width == 0 || glyphData.pixelSize.height == 0 || glyphData.pixelSize.width > int.MaxValue || glyphData.pixelSize.height > int.MaxValue)
        {
            return false;
        }

        width = (int)glyphData.pixelSize.width;
        height = (int)glyphData.pixelSize.height;
        return width <= AtlasWidth - AtlasPadding * 2 && height <= AtlasHeight - AtlasPadding * 2;
    }

}

