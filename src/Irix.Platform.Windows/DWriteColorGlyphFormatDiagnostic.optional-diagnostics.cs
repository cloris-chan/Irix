#if IRIX_DIAGNOSTICS
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal static unsafe class DWriteColorGlyphFormatDiagnostic
{
    private static readonly Guid WicImagingFactoryClsid = new(0xCACAF262, 0x9370, 0x4615, 0xA1, 0x3B, 0x9F, 0x55, 0x39, 0xDA, 0x4C, 0x0A);
    private static readonly Guid WicPixelFormat32bppPbgra = new(0x6FDDC324, 0x4E03, 0x4BFE, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x0F);
    private const uint DefaultPixelsPerEm = 64;
    private const int DWriteNoColorHResult = unchecked((int)0x8898500C);
    private const int RpcEChangedModeHResult = unchecked((int)0x80010106);
    private const int BgraBytesPerPixel = 4;
    private const DWRITE_GLYPH_IMAGE_FORMATS ColorGlyphRunImageFormats =
        DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_CFF
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
        | DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF;

    private static readonly ColorGlyphProbeDefinition[] DefaultProbes =
    [
        new("heart", 0x2764),
        new("grinning", 0x1F600),
        new("smiling", 0x1F603),
        new("rocket", 0x1F680),
        new("target", 0x1F3AF),
        new("fire", 0x1F525),
        new("woman", 0x1F469),
        new("flag-us", 0x1F1FA)
    ];

    internal static ColorGlyphFormatDiagnosticSnapshot Capture(string familyName = "Segoe UI Emoji", uint pixelsPerEm = DefaultPixelsPerEm)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            familyName = "Segoe UI Emoji";
        }

        pixelsPerEm = Math.Clamp(pixelsPerEm, 1, ushort.MaxValue);

        IDWriteFactory* factory = null;
        IDWriteFactory4* factory4 = null;
        IDWriteFontCollection* fontCollection = null;
        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;

        try
        {
            PInvoke.DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                typeof(IDWriteFactory).GUID,
                out var factoryObject).ThrowOnFailure();
            factory = (IDWriteFactory*)factoryObject;
            factory->QueryInterface<IDWriteFactory4>(out factory4).ThrowOnFailure();
            if (factory4 == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "Factory4Unavailable");
            }

            factory->GetSystemFontCollection(&fontCollection, false);
            if (fontCollection == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontCollectionUnavailable");
            }

            fontCollection->FindFamilyName(familyName, out var familyIndex, out var exists);
            if (!exists)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFamilyMissing");
            }

            fontCollection->GetFontFamily(familyIndex, &family);
            if (family == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFamilyUnavailable");
            }

            family->GetFirstMatchingFont(
                DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
                DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
                DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
                &font);
            if (font == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontUnavailable");
            }

            font->CreateFontFace(&face);
            if (face == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFaceUnavailable");
            }

            face4 = TryQueryFontFace4(face);
            var results = new ColorGlyphFormatProbeResult[DefaultProbes.Length];
            for (var i = 0; i < DefaultProbes.Length; i++)
            {
                results[i] = Probe(factory4, face, face4, DefaultProbes[i], pixelsPerEm);
            }

            return ColorGlyphFormatDiagnosticSnapshot.Create(familyName, pixelsPerEm, factory4Available: true, face4 != null, results);
        }
        catch (COMException ex)
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, $"DirectWrite=0x{unchecked((uint)ex.ErrorCode):X8}");
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (factory4 != null) factory4->Release();
            if (face != null) face->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
            if (fontCollection != null) fontCollection->Release();
            if (factory != null) factory->Release();
        }
    }

    internal static ColorGlyphFormatDiagnosticSnapshot CaptureFromFontFile(string fontFilePath, uint pixelsPerEm = DefaultPixelsPerEm)
    {
        if (string.IsNullOrWhiteSpace(fontFilePath))
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed("FontFile", pixelsPerEm, "FontFilePathMissing");
        }

        pixelsPerEm = Math.Clamp(pixelsPerEm, 1, ushort.MaxValue);
        var fullPath = Path.GetFullPath(fontFilePath);
        var familyName = $"FontFile:{Path.GetFileName(fullPath)}";
        if (!File.Exists(fullPath))
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFileMissing");
        }

        IDWriteFactory* factory = null;
        IDWriteFactory4* factory4 = null;
        IDWriteFontFile* fontFile = null;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;

        try
        {
            PInvoke.DWriteCreateFactory(
                DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                typeof(IDWriteFactory).GUID,
                out var factoryObject).ThrowOnFailure();
            factory = (IDWriteFactory*)factoryObject;
            factory->QueryInterface<IDWriteFactory4>(out factory4).ThrowOnFailure();
            if (factory4 == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "Factory4Unavailable");
            }

            factory4->CreateFontFileReference(fullPath, null, &fontFile);
            if (fontFile == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFileReferenceUnavailable");
            }

            fontFile->Analyze(out var isSupportedFontType, out _, out var fontFaceType, out var faceCount);
            if (!isSupportedFontType || faceCount == 0)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "UnsupportedFontFile");
            }

            var fontFiles = stackalloc IDWriteFontFile*[1];
            fontFiles[0] = fontFile;
            factory4->CreateFontFace(
                fontFaceType,
                numberOfFiles: 1,
                fontFiles,
                faceIndex: 0,
                DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE,
                &face);
            if (face == null)
            {
                return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, "FontFaceUnavailable");
            }

            face4 = TryQueryFontFace4(face);
            var results = new ColorGlyphFormatProbeResult[DefaultProbes.Length];
            for (var i = 0; i < DefaultProbes.Length; i++)
            {
                results[i] = Probe(factory4, face, face4, DefaultProbes[i], pixelsPerEm);
            }

            return ColorGlyphFormatDiagnosticSnapshot.Create(familyName, pixelsPerEm, factory4Available: true, face4 != null, results);
        }
        catch (COMException ex)
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, $"DirectWrite=0x{unchecked((uint)ex.ErrorCode):X8}");
        }
        catch (IOException ex)
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, $"IO=0x{unchecked((uint)ex.HResult):X8}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return ColorGlyphFormatDiagnosticSnapshot.Failed(familyName, pixelsPerEm, $"Access=0x{unchecked((uint)ex.HResult):X8}");
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (face != null) face->Release();
            if (fontFile != null) fontFile->Release();
            if (factory4 != null) factory4->Release();
            if (factory != null) factory->Release();
        }
    }

    private static IDWriteFontFace4* TryQueryFontFace4(IDWriteFontFace* face)
    {
        try
        {
            face->QueryInterface<IDWriteFontFace4>(out var face4).ThrowOnFailure();
            return face4;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static ColorGlyphFormatProbeResult Probe(IDWriteFactory4* factory4, IDWriteFontFace* face, IDWriteFontFace4* face4, ColorGlyphProbeDefinition probe, uint pixelsPerEm)
    {
        var codePoint = probe.CodePoint;
        var glyphIndex = stackalloc ushort[1];
        try
        {
            face->GetGlyphIndices(new ReadOnlySpan<uint>(&codePoint, 1), new Span<ushort>(glyphIndex, 1));
        }
        catch (COMException ex)
        {
            return new ColorGlyphFormatProbeResult(probe.Label, probe.CodePoint, GlyphIndex: 0, GlyphFound: false, Factory4Available: true, Face4Available: face4 != null, Formats: ColorGlyphImageFormatFlags.None, BitmapRoute: ColorGlyphBitmapRoute.None, Status: ColorGlyphFormatProbeStatus.GlyphIndexFailed, Error: $"0x{unchecked((uint)ex.ErrorCode):X8}");
        }

        if (glyphIndex[0] == 0)
        {
            return new ColorGlyphFormatProbeResult(probe.Label, probe.CodePoint, GlyphIndex: 0, GlyphFound: false, Factory4Available: true, Face4Available: face4 != null, Formats: ColorGlyphImageFormatFlags.None, BitmapRoute: ColorGlyphBitmapRoute.None, Status: ColorGlyphFormatProbeStatus.GlyphMissing, Error: "");
        }

        DWRITE_GLYPH_IMAGE_FORMATS faceNativeFormats = default;
        var faceFormats = ColorGlyphImageFormatFlags.None;
        var faceRoute = ColorGlyphBitmapRoute.None;
        var status = ColorGlyphFormatProbeStatus.Ok;
        var error = "";
        if (face4 == null)
        {
            status = ColorGlyphFormatProbeStatus.Face4Missing;
        }
        else
        {
            try
            {
                face4->GetGlyphImageFormats(glyphIndex[0], pixelsPerEm, pixelsPerEm, out var formats);
                faceNativeFormats = formats;
                faceFormats = ToDiagnosticFlags(formats);
                faceRoute = SelectBitmapRoute(formats);
            }
            catch (COMException ex)
            {
                status = ColorGlyphFormatProbeStatus.FormatQueryFailed;
                error = $"0x{unchecked((uint)ex.ErrorCode):X8}";
            }
        }

        var colorRunCount = 0;
        DWRITE_GLYPH_IMAGE_FORMATS colorRunNativeFormats = default;
        var colorRunFormats = ColorGlyphImageFormatFlags.None;
        var colorRunRoute = ColorGlyphBitmapRoute.None;
        var colorRunError = "";
        ProbeColorGlyphRuns(factory4, face, glyphIndex[0], pixelsPerEm, out colorRunCount, out colorRunNativeFormats, out colorRunFormats, out colorRunRoute, out colorRunError);

        var imageDataFormats = faceRoute != ColorGlyphBitmapRoute.None ? faceNativeFormats : colorRunNativeFormats;
        ProbeGlyphImageData(face4, glyphIndex[0], pixelsPerEm, imageDataFormats, out var imageDataRoute, out var imageDataBytes, out var imageDataPixelsPerEm, out var imageDataWidth, out var imageDataHeight, out var imageDataError, out var imageDecodeBytes, out var imageDecodeWidth, out var imageDecodeHeight, out var imageDecodeError);

        return new ColorGlyphFormatProbeResult(probe.Label, probe.CodePoint, glyphIndex[0], GlyphFound: true, Factory4Available: true, Face4Available: face4 != null, Formats: faceFormats, BitmapRoute: faceRoute, Status: status, Error: error, ColorRunCount: colorRunCount, ColorRunFormats: colorRunFormats, ColorRunBitmapRoute: colorRunRoute, ColorRunError: colorRunError, ImageDataRoute: imageDataRoute, ImageDataBytes: imageDataBytes, ImageDataPixelsPerEm: imageDataPixelsPerEm, ImageDataWidth: imageDataWidth, ImageDataHeight: imageDataHeight, ImageDataError: imageDataError, ImageDecodeBytes: imageDecodeBytes, ImageDecodeWidth: imageDecodeWidth, ImageDecodeHeight: imageDecodeHeight, ImageDecodeError: imageDecodeError);
    }

    private static void ProbeColorGlyphRuns(
        IDWriteFactory4* factory4,
        IDWriteFontFace* face,
        ushort glyphIndex,
        uint pixelsPerEm,
        out int colorRunCount,
        out DWRITE_GLYPH_IMAGE_FORMATS colorRunNativeFormats,
        out ColorGlyphImageFormatFlags colorRunFormats,
        out ColorGlyphBitmapRoute colorRunRoute,
        out string error)
    {
        colorRunCount = 0;
        colorRunNativeFormats = default;
        colorRunFormats = ColorGlyphImageFormatFlags.None;
        colorRunRoute = ColorGlyphBitmapRoute.None;
        error = "";

        var glyphIndices = stackalloc ushort[1];
        glyphIndices[0] = glyphIndex;
        var advances = stackalloc float[1];
        var offsets = stackalloc DWRITE_GLYPH_OFFSET[1];
        var glyphRun = new DWRITE_GLYPH_RUN
        {
            fontFace = face,
            fontEmSize = pixelsPerEm,
            glyphCount = 1,
            glyphIndices = glyphIndices,
            glyphAdvances = advances,
            glyphOffsets = offsets,
            isSideways = false,
            bidiLevel = 0
        };

        IDWriteColorGlyphRunEnumerator1* colorRuns = null;
        try
        {
            var baselineOrigin = new D2D_POINT_2F();
            factory4->TranslateColorGlyphRun(
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
            return;
        }
        catch (COMException ex)
        {
            error = $"0x{unchecked((uint)ex.ErrorCode):X8}";
            return;
        }
        catch (ArgumentException ex)
        {
            error = $"0x{unchecked((uint)ex.HResult):X8}";
            return;
        }

        if (colorRuns == null)
        {
            return;
        }

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

                DWRITE_COLOR_GLYPH_RUN1* currentRun;
                colorRuns->GetCurrentRun(&currentRun);
                if (currentRun == null)
                {
                    continue;
                }

                colorRunCount++;
                var imageFormat = currentRun->glyphImageFormat;
                colorRunNativeFormats |= imageFormat;
                colorRunFormats |= ToDiagnosticFlags(imageFormat);
                if (colorRunRoute == ColorGlyphBitmapRoute.None)
                {
                    colorRunRoute = SelectBitmapRoute(imageFormat);
                }
            }
        }
        catch (COMException ex)
        {
            error = $"0x{unchecked((uint)ex.ErrorCode):X8}";
        }
        finally
        {
            colorRuns->Release();
        }
    }

    private static void ProbeGlyphImageData(
        IDWriteFontFace4* face4,
        ushort glyphIndex,
        uint pixelsPerEm,
        DWRITE_GLYPH_IMAGE_FORMATS formats,
        out ColorGlyphBitmapRoute imageDataRoute,
        out uint imageDataBytes,
        out uint imageDataPixelsPerEm,
        out int imageDataWidth,
        out int imageDataHeight,
        out string imageDataError,
        out uint imageDecodeBytes,
        out int imageDecodeWidth,
        out int imageDecodeHeight,
        out string imageDecodeError)
    {
        imageDataRoute = ColorGlyphBitmapRoute.None;
        imageDataBytes = 0;
        imageDataPixelsPerEm = 0;
        imageDataWidth = 0;
        imageDataHeight = 0;
        imageDataError = "";
        imageDecodeBytes = 0;
        imageDecodeWidth = 0;
        imageDecodeHeight = 0;
        imageDecodeError = "";
        if (face4 == null || !D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(formats, out var imageFormat))
        {
            return;
        }

        void* glyphDataContext = null;
        try
        {
            face4->GetGlyphImageData(glyphIndex, pixelsPerEm, imageFormat, out var glyphData, out glyphDataContext);
            if (glyphData.imageData == null || glyphData.imageDataSize == 0)
            {
                imageDataError = "EmptyImageData";
                return;
            }

            imageDataRoute = SelectBitmapRoute(imageFormat);
            imageDataBytes = glyphData.imageDataSize;
            imageDataPixelsPerEm = glyphData.pixelsPerEm;
            if (glyphData.pixelSize.width <= int.MaxValue && glyphData.pixelSize.height <= int.MaxValue)
            {
                imageDataWidth = (int)glyphData.pixelSize.width;
                imageDataHeight = (int)glyphData.pixelSize.height;
            }

            if (imageDataRoute == ColorGlyphBitmapRoute.Bgra)
            {
                imageDecodeBytes = imageDataBytes;
                imageDecodeWidth = imageDataWidth;
                imageDecodeHeight = imageDataHeight;
            }
            else
            {
                TryDecodeWicImage(new ReadOnlySpan<byte>(glyphData.imageData, checked((int)glyphData.imageDataSize)), out imageDecodeBytes, out imageDecodeWidth, out imageDecodeHeight, out imageDecodeError);
            }
        }
        catch (COMException ex)
        {
            imageDataError = $"0x{unchecked((uint)ex.ErrorCode):X8}";
        }
        catch (OverflowException)
        {
            imageDataError = "Overflow";
        }
        finally
        {
            if (glyphDataContext != null)
            {
                face4->ReleaseGlyphImageData(glyphDataContext);
            }
        }
    }

    private static bool TryDecodeWicImage(ReadOnlySpan<byte> encodedBytes, out uint decodedBytes, out int width, out int height, out string error)
    {
        decodedBytes = 0;
        width = 0;
        height = 0;
        error = "";
        if (encodedBytes.IsEmpty)
        {
            error = "EmptyEncodedData";
            return false;
        }

        var coInitializeHr = PInvoke.CoInitializeEx(COINIT.COINIT_MULTITHREADED);
        var ownsComInitialization = coInitializeHr.Succeeded;
        if (coInitializeHr.Failed && (int)coInitializeHr != RpcEChangedModeHResult)
        {
            error = $"CoInitialize=0x{unchecked((uint)coInitializeHr.Value):X8}";
            return false;
        }

        IWICImagingFactory* factory = null;
        IWICStream* stream = null;
        IWICBitmapDecoder* decoder = null;
        IWICBitmapFrameDecode* frame = null;
        IWICFormatConverter* converter = null;
        try
        {
            var factoryHr = PInvoke.CoCreateInstance<IWICImagingFactory>(WicImagingFactoryClsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out factory);
            if (factoryHr.Failed || factory == null)
            {
                error = $"CoCreateWIC=0x{unchecked((uint)factoryHr.Value):X8}";
                return false;
            }

            factory->CreateStream(&stream);
            if (stream == null)
            {
                error = "WicStreamUnavailable";
                return false;
            }

            stream->InitializeFromMemory(encodedBytes);
            decoder = factory->CreateDecoderFromStream((IStream*)stream, null, WICDecodeOptions.WICDecodeMetadataCacheOnLoad);
            if (decoder == null)
            {
                error = "WicDecoderUnavailable";
                return false;
            }

            decoder->GetFrame(0, &frame);
            if (frame == null)
            {
                error = "WicFrameUnavailable";
                return false;
            }

            factory->CreateFormatConverter(&converter);
            if (converter == null)
            {
                error = "WicConverterUnavailable";
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
            if (decodedWidth == 0 || decodedHeight == 0 || decodedWidth > int.MaxValue || decodedHeight > int.MaxValue)
            {
                error = "InvalidDecodedSize";
                return false;
            }

            width = (int)decodedWidth;
            height = (int)decodedHeight;
            var stride = checked((uint)(width * BgraBytesPerPixel));
            var decodedByteCount = checked(width * height * BgraBytesPerPixel);
            var decodedPixels = new byte[decodedByteCount];
            converter->CopyPixels(null, stride, decodedPixels);
            decodedBytes = (uint)decodedByteCount;
            return true;
        }
        catch (COMException ex)
        {
            error = $"WIC=0x{unchecked((uint)ex.ErrorCode):X8}";
            decodedBytes = 0;
            width = 0;
            height = 0;
            return false;
        }
        catch (OverflowException)
        {
            error = "Overflow";
            decodedBytes = 0;
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
            if (factory != null) factory->Release();
            if (ownsComInitialization)
            {
                PInvoke.CoUninitialize();
            }
        }
    }

    private static ColorGlyphBitmapRoute SelectBitmapRoute(DWRITE_GLYPH_IMAGE_FORMATS formats)
    {
        if (!D3D12GlyphAtlasTextRenderer.TrySelectColorGlyphBitmapImageFormat(formats, out var selectedFormat))
        {
            return ColorGlyphBitmapRoute.None;
        }

        if (selectedFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
        {
            return ColorGlyphBitmapRoute.Bgra;
        }

        if (selectedFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG)
        {
            return ColorGlyphBitmapRoute.Png;
        }

        if (selectedFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF)
        {
            return ColorGlyphBitmapRoute.Tiff;
        }

        return selectedFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG
            ? ColorGlyphBitmapRoute.Jpeg
            : ColorGlyphBitmapRoute.None;
    }

    private static ColorGlyphImageFormatFlags ToDiagnosticFlags(DWRITE_GLYPH_IMAGE_FORMATS formats)
    {
        var flags = ColorGlyphImageFormatFlags.None;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TRUETYPE) != 0) flags |= ColorGlyphImageFormatFlags.TrueType;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_CFF) != 0) flags |= ColorGlyphImageFormatFlags.Cff;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR) != 0) flags |= ColorGlyphImageFormatFlags.Colr;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_SVG) != 0) flags |= ColorGlyphImageFormatFlags.Svg;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG) != 0) flags |= ColorGlyphImageFormatFlags.Png;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG) != 0) flags |= ColorGlyphImageFormatFlags.Jpeg;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF) != 0) flags |= ColorGlyphImageFormatFlags.Tiff;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) != 0) flags |= ColorGlyphImageFormatFlags.PremultipliedBgra;
        if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_COLR_PAINT_TREE) != 0) flags |= ColorGlyphImageFormatFlags.ColrPaintTree;
        return flags;
    }
}

internal readonly struct ColorGlyphProbeDefinition(string Label, uint CodePoint)
{
    public string Label { get; } = Label;
    public uint CodePoint { get; } = CodePoint;
}

[Flags]
internal enum ColorGlyphImageFormatFlags : ushort
{
    None = 0,
    TrueType = 1 << 0,
    Cff = 1 << 1,
    Colr = 1 << 2,
    Svg = 1 << 3,
    Png = 1 << 4,
    Jpeg = 1 << 5,
    Tiff = 1 << 6,
    PremultipliedBgra = 1 << 7,
    ColrPaintTree = 1 << 8
}

internal enum ColorGlyphBitmapRoute : byte
{
    None,
    Bgra,
    Png,
    Tiff,
    Jpeg
}

internal enum ColorGlyphFormatProbeStatus : byte
{
    Ok,
    GlyphMissing,
    Face4Missing,
    GlyphIndexFailed,
    FormatQueryFailed
}

internal readonly struct ColorGlyphFormatProbeResult(
    string Label,
    uint CodePoint,
    ushort GlyphIndex,
    bool GlyphFound,
    bool Factory4Available,
    bool Face4Available,
    ColorGlyphImageFormatFlags Formats,
    ColorGlyphBitmapRoute BitmapRoute,
    ColorGlyphFormatProbeStatus Status,
    string Error,
    int ColorRunCount = 0,
    ColorGlyphImageFormatFlags ColorRunFormats = ColorGlyphImageFormatFlags.None,
    ColorGlyphBitmapRoute ColorRunBitmapRoute = ColorGlyphBitmapRoute.None,
    string ColorRunError = "",
    ColorGlyphBitmapRoute ImageDataRoute = ColorGlyphBitmapRoute.None,
    uint ImageDataBytes = 0,
    uint ImageDataPixelsPerEm = 0,
    int ImageDataWidth = 0,
    int ImageDataHeight = 0,
    string ImageDataError = "",
    uint ImageDecodeBytes = 0,
    int ImageDecodeWidth = 0,
    int ImageDecodeHeight = 0,
    string ImageDecodeError = "") : IEquatable<ColorGlyphFormatProbeResult>
{
    public string Label { get; } = Label;
    public uint CodePoint { get; } = CodePoint;
    public ushort GlyphIndex { get; } = GlyphIndex;
    public bool GlyphFound { get; } = GlyphFound;
    public bool Factory4Available { get; } = Factory4Available;
    public bool Face4Available { get; } = Face4Available;
    public ColorGlyphImageFormatFlags Formats { get; } = Formats;
    public ColorGlyphBitmapRoute BitmapRoute { get; } = BitmapRoute;
    public ColorGlyphFormatProbeStatus Status { get; } = Status;
    public string Error { get; } = Error;
    public int ColorRunCount { get; } = ColorRunCount;
    public ColorGlyphImageFormatFlags ColorRunFormats { get; } = ColorRunFormats;
    public ColorGlyphBitmapRoute ColorRunBitmapRoute { get; } = ColorRunBitmapRoute;
    public string ColorRunError { get; } = ColorRunError;
    public ColorGlyphBitmapRoute ImageDataRoute { get; } = ImageDataRoute;
    public uint ImageDataBytes { get; } = ImageDataBytes;
    public uint ImageDataPixelsPerEm { get; } = ImageDataPixelsPerEm;
    public int ImageDataWidth { get; } = ImageDataWidth;
    public int ImageDataHeight { get; } = ImageDataHeight;
    public string ImageDataError { get; } = ImageDataError;
    public uint ImageDecodeBytes { get; } = ImageDecodeBytes;
    public int ImageDecodeWidth { get; } = ImageDecodeWidth;
    public int ImageDecodeHeight { get; } = ImageDecodeHeight;
    public string ImageDecodeError { get; } = ImageDecodeError;
    public bool HasLayerFormat => HasLayerFormatFlags(Formats | ColorRunFormats);
    public bool HasEncodedBitmapFormat => HasEncodedBitmapFormatFlags(Formats | ColorRunFormats);
    public bool HasBgraFormat => ((Formats | ColorRunFormats) & ColorGlyphImageFormatFlags.PremultipliedBgra) != 0;
    public bool HasUnsupportedColorFormat => ((Formats | ColorRunFormats) & (ColorGlyphImageFormatFlags.Svg | ColorGlyphImageFormatFlags.ColrPaintTree)) != 0;
    public bool HasColorRuns => ColorRunCount > 0;
    public bool HasImageData => ImageDataRoute != ColorGlyphBitmapRoute.None && ImageDataBytes > 0;
    public bool HasDecodedImage => ImageDecodeBytes > 0;
    public bool HasBitmapRenderableFormat => BitmapRoute != ColorGlyphBitmapRoute.None || ColorRunBitmapRoute != ColorGlyphBitmapRoute.None || HasImageData;

    private static bool HasLayerFormatFlags(ColorGlyphImageFormatFlags flags) => (flags & (ColorGlyphImageFormatFlags.TrueType | ColorGlyphImageFormatFlags.Cff | ColorGlyphImageFormatFlags.Colr)) != 0;

    private static bool HasEncodedBitmapFormatFlags(ColorGlyphImageFormatFlags flags) => (flags & (ColorGlyphImageFormatFlags.Png | ColorGlyphImageFormatFlags.Jpeg | ColorGlyphImageFormatFlags.Tiff)) != 0;

    public bool Equals(ColorGlyphFormatProbeResult other)
    {
        return Label == other.Label
            && CodePoint == other.CodePoint
            && GlyphIndex == other.GlyphIndex
            && GlyphFound == other.GlyphFound
            && Factory4Available == other.Factory4Available
            && Face4Available == other.Face4Available
            && Formats == other.Formats
            && BitmapRoute == other.BitmapRoute
            && Status == other.Status
            && Error == other.Error
            && ColorRunCount == other.ColorRunCount
            && ColorRunFormats == other.ColorRunFormats
            && ColorRunBitmapRoute == other.ColorRunBitmapRoute
            && ColorRunError == other.ColorRunError
            && ImageDataRoute == other.ImageDataRoute
            && ImageDataBytes == other.ImageDataBytes
            && ImageDataPixelsPerEm == other.ImageDataPixelsPerEm
            && ImageDataWidth == other.ImageDataWidth
            && ImageDataHeight == other.ImageDataHeight
            && ImageDataError == other.ImageDataError
            && ImageDecodeBytes == other.ImageDecodeBytes
            && ImageDecodeWidth == other.ImageDecodeWidth
            && ImageDecodeHeight == other.ImageDecodeHeight
            && ImageDecodeError == other.ImageDecodeError;
    }

    public override bool Equals(object? obj) => obj is ColorGlyphFormatProbeResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Label);
        hash.Add(CodePoint);
        hash.Add(GlyphIndex);
        hash.Add(GlyphFound);
        hash.Add(Factory4Available);
        hash.Add(Face4Available);
        hash.Add(Formats);
        hash.Add(BitmapRoute);
        hash.Add(Status);
        hash.Add(Error);
        hash.Add(ColorRunCount);
        hash.Add(ColorRunFormats);
        hash.Add(ColorRunBitmapRoute);
        hash.Add(ColorRunError);
        hash.Add(ImageDataRoute);
        hash.Add(ImageDataBytes);
        hash.Add(ImageDataPixelsPerEm);
        hash.Add(ImageDataWidth);
        hash.Add(ImageDataHeight);
        hash.Add(ImageDataError);
        hash.Add(ImageDecodeBytes);
        hash.Add(ImageDecodeWidth);
        hash.Add(ImageDecodeHeight);
        hash.Add(ImageDecodeError);
        return hash.ToHashCode();
    }

    public static bool operator ==(ColorGlyphFormatProbeResult left, ColorGlyphFormatProbeResult right) => left.Equals(right);

    public static bool operator !=(ColorGlyphFormatProbeResult left, ColorGlyphFormatProbeResult right) => !left.Equals(right);
}

internal readonly struct ColorGlyphFormatDiagnosticSnapshot(
    string FamilyName,
    uint PixelsPerEm,
    bool Factory4Available,
    bool Face4Available,
    string Failure,
    ColorGlyphFormatProbeResult[] Results,
    int Glyphs,
    int ColorRunCandidates,
    int LayerCandidates,
    int BgraCandidates,
    int EncodedBitmapCandidates,
    int UnsupportedColorCandidates,
    int BitmapRenderableCandidates,
    int ImageDataCandidates,
    int DecodedBitmapCandidates) : IEquatable<ColorGlyphFormatDiagnosticSnapshot>
{
    public string FamilyName { get; } = FamilyName;
    public uint PixelsPerEm { get; } = PixelsPerEm;
    public bool Factory4Available { get; } = Factory4Available;
    public bool Face4Available { get; } = Face4Available;
    public string Failure { get; } = Failure;
    public IReadOnlyList<ColorGlyphFormatProbeResult> Results { get; } = Results;
    public int ProbeCount => Results.Count;
    public int Glyphs { get; } = Glyphs;
    public int ColorRunCandidates { get; } = ColorRunCandidates;
    public int LayerCandidates { get; } = LayerCandidates;
    public int BgraCandidates { get; } = BgraCandidates;
    public int EncodedBitmapCandidates { get; } = EncodedBitmapCandidates;
    public int UnsupportedColorCandidates { get; } = UnsupportedColorCandidates;
    public int BitmapRenderableCandidates { get; } = BitmapRenderableCandidates;
    public int ImageDataCandidates { get; } = ImageDataCandidates;
    public int DecodedBitmapCandidates { get; } = DecodedBitmapCandidates;

    public static ColorGlyphFormatDiagnosticSnapshot Failed(string familyName, uint pixelsPerEm, string failure) =>
        new(familyName, pixelsPerEm, Factory4Available: false, Face4Available: false, failure, [], Glyphs: 0, ColorRunCandidates: 0, LayerCandidates: 0, BgraCandidates: 0, EncodedBitmapCandidates: 0, UnsupportedColorCandidates: 0, BitmapRenderableCandidates: 0, ImageDataCandidates: 0, DecodedBitmapCandidates: 0);

    public static ColorGlyphFormatDiagnosticSnapshot Create(string familyName, uint pixelsPerEm, bool factory4Available, bool face4Available, ColorGlyphFormatProbeResult[] results)
    {
        var glyphs = 0;
        var colorRunCandidates = 0;
        var layerCandidates = 0;
        var bgraCandidates = 0;
        var encodedBitmapCandidates = 0;
        var unsupportedColorCandidates = 0;
        var bitmapRenderableCandidates = 0;
        var imageDataCandidates = 0;
        var decodedBitmapCandidates = 0;
        foreach (ref readonly var result in results.AsSpan())
        {
            if (result.GlyphFound) glyphs++;
            if (result.HasColorRuns) colorRunCandidates++;
            if (result.HasLayerFormat) layerCandidates++;
            if (result.HasBgraFormat) bgraCandidates++;
            if (result.HasEncodedBitmapFormat) encodedBitmapCandidates++;
            if (result.HasUnsupportedColorFormat) unsupportedColorCandidates++;
            if (result.HasBitmapRenderableFormat) bitmapRenderableCandidates++;
            if (result.HasImageData) imageDataCandidates++;
            if (result.HasDecodedImage) decodedBitmapCandidates++;
        }

        return new ColorGlyphFormatDiagnosticSnapshot(familyName, pixelsPerEm, factory4Available, face4Available, "", results, glyphs, colorRunCandidates, layerCandidates, bgraCandidates, encodedBitmapCandidates, unsupportedColorCandidates, bitmapRenderableCandidates, imageDataCandidates, decodedBitmapCandidates);
    }

    public bool Equals(ColorGlyphFormatDiagnosticSnapshot other)
    {
        return FamilyName == other.FamilyName
            && PixelsPerEm == other.PixelsPerEm
            && Factory4Available == other.Factory4Available
            && Face4Available == other.Face4Available
            && Failure == other.Failure
            && ReferenceEquals(Results, other.Results)
            && Glyphs == other.Glyphs
            && ColorRunCandidates == other.ColorRunCandidates
            && LayerCandidates == other.LayerCandidates
            && BgraCandidates == other.BgraCandidates
            && EncodedBitmapCandidates == other.EncodedBitmapCandidates
            && UnsupportedColorCandidates == other.UnsupportedColorCandidates
            && BitmapRenderableCandidates == other.BitmapRenderableCandidates
            && ImageDataCandidates == other.ImageDataCandidates
            && DecodedBitmapCandidates == other.DecodedBitmapCandidates;
    }

    public override bool Equals(object? obj) => obj is ColorGlyphFormatDiagnosticSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(FamilyName);
        hash.Add(PixelsPerEm);
        hash.Add(Factory4Available);
        hash.Add(Face4Available);
        hash.Add(Failure);
        hash.Add(Results.Count);
        hash.Add(Glyphs);
        hash.Add(ColorRunCandidates);
        hash.Add(LayerCandidates);
        hash.Add(BgraCandidates);
        hash.Add(EncodedBitmapCandidates);
        hash.Add(UnsupportedColorCandidates);
        hash.Add(BitmapRenderableCandidates);
        hash.Add(ImageDataCandidates);
        hash.Add(DecodedBitmapCandidates);
        return hash.ToHashCode();
    }

    public static bool operator ==(ColorGlyphFormatDiagnosticSnapshot left, ColorGlyphFormatDiagnosticSnapshot right) => left.Equals(right);

    public static bool operator !=(ColorGlyphFormatDiagnosticSnapshot left, ColorGlyphFormatDiagnosticSnapshot right) => !left.Equals(right);
}
#endif
