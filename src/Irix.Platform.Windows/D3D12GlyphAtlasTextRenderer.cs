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
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe class D3D12GlyphAtlasTextRenderer : IDisposable
{
    private static readonly Guid IUnknownGuid = new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
    private static readonly Guid WicImagingFactoryClsid = new(0xCACAF262, 0x9370, 0x4615, 0xA1, 0x3B, 0x9F, 0x55, 0x39, 0xDA, 0x4C, 0x0A);
    private static readonly Guid WicPixelFormat32bppPbgra = new(0x6FDDC324, 0x4E03, 0x4BFE, 0xB1, 0x85, 0x3D, 0x77, 0x76, 0x8D, 0xC9, 0x0F);
    private const int UploadFrameCount = 2;
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;
    private const int AtlasPadding = 1;
    private const int MaxAtlasPages = 48;
    private const int AtlasPagePixels = AtlasWidth * AtlasHeight;
    private const int AtlasBudgetPixels = MaxAtlasPages * AtlasPagePixels;
    private const int MaxGlyphQuads = 4096;
    private const int MaxGlyphVertices = MaxGlyphQuads * 6;
    private const int MaxGlyphDrawBatches = 1024;
    private const int MaxShapedRunSegments = 64;
    private const int DWriteNoColorHResult = unchecked((int)0x8898500C);
    private const int RpcEChangedModeHResult = unchecked((int)0x80010106);
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
    private const int AlphaAtlasBytesPerPixel = 1;
    private const int BgraAtlasBytesPerPixel = 4;
    private const int AtlasRowPitch = AtlasWidth * AlphaAtlasBytesPerPixel;
    private const int TextureDataPitchAlignment = 256;
    private const uint Shader4ComponentMapping = 0u | (1u << 3) | (2u << 6) | (3u << 9) | (1u << 12);
    private const string VertexShaderBytecodeBase64 =
        "RFhCQ0hOwF2N9gtTu/HrKRNRsV8BAAAA4AIAAAUAAAA0AAAAoAAAABABAACEAQAARAIAAFJERUZkAAAAAAAAAAAAAAAAAAAAPAAAAAAF/v8AgQAAPAAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAElTR05oAAAAAwAAAAgAAABQAAAAAAAAAAAAAAADAAAAAAAAAAMDAABZAAAAAAAAAAAAAAADAAAAAQAAAAMDAABiAAAAAAAAAAAAAAADAAAAAgAAAA8PAABQT1NJVElPTgBURVhDT09SRABDT0xPUgBPU0dObAAAAAMAAAAIAAAAUAAAAAAAAAABAAAAAwAAAAAAAAAPAAAAXAAAAAAAAAAAAAAAAwAAAAEAAAADDAAAZQAAAAAAAAAAAAAAAwAAAAIAAAAPAAAAU1ZfUE9TSVRJT04AVEVYQ09PUkQAQ09MT1IAq1NIRVi4AAAAUAABAC4AAABqCAABXwAAAzIQEAAAAAAAXwAAAzIQEAABAAAAXwAAA/IQEAACAAAAZwAABPIgEAAAAAAAAQAAAGUAAAMyIBAAAQAAAGUAAAPyIBAAAgAAADYAAAUyIBAAAAAAAEYQEAAAAAAANgAACMIgEAAAAAAAAkAAAAAAAAAAAAAAAAAAAAAAgD82AAAFMiAQAAEAAABGEBAAAQAAADYAAAXyIBAAAgAAAEYeEAACAAAAPgAAAVNUQVSUAAAABQAAAAAAAAAAAAAABgAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
    private const string PixelShaderBytecodeBase64 =
        "RFhCQw3tOgpD5F95IlkFLIOjg2ABAAAA9AIAAAUAAAA0AAAA9AAAAGgBAACcAQAAWAIAAFJERUa4AAAAAAAAAAAAAAACAAAAPAAAAAAF//8AgQAAjwAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAAfAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAEAAACJAAAAAgAAAAUAAAAEAAAA/////wAAAAABAAAAAQAAAEF0bGFzU2FtcGxlcgBBdGxhcwBNaWNyb3NvZnQgKFIpIEhMU0wgU2hhZGVyIENvbXBpbGVyIDEwLjEAq0lTR05sAAAAAwAAAAgAAABQAAAAAAAAAAEAAAADAAAAAAAAAA8AAABcAAAAAAAAAAAAAAADAAAAAQAAAAMDAABlAAAAAAAAAAAAAAADAAAAAgAAAA8PAABTVl9QT1NJVElPTgBURVhDT09SRABDT0xPUgCrT1NHTiwAAAABAAAACAAAACAAAAAAAAAAAAAAAAMAAAAAAAAADwAAAFNWX1RBUkdFVACrq1NIRVi0AAAAUAAAAC0AAABqCAABWgAAAwBgEAAAAAAAWBgABABwEAAAAAAAVVUAAGIQAAMyEBAAAQAAAGIQAAPyEBAAAgAAAGUAAAPyIBAAAAAAAGgAAAIBAAAARQAAi8IAAIBDVRUAEgAQAAAAAABGEBAAAQAAAEZ+EAAAAAAAAGAQAAAAAAA4AAAHgiAQAAAAAAAKABAAAAAAADoQEAACAAAANgAABXIgEAAAAAAARhIQAAIAAAA+AAABU1RBVJQAAAAEAAAAAQAAAAAAAAADAAAAAQAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string BgraPixelShaderBytecodeBase64 =
        "RFhCQ6RFWgCEelPeWMrtfrsGtrUBAAAAPAMAAAUAAAA0AAAA9AAAAGgBAACcAQAAoAIAAFJERUa4AAAAAAAAAAAAAAACAAAAPAAAAAAF//8AAQAAjwAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAAfAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAEAAACJAAAAAgAAAAUAAAAEAAAA/////wAAAAABAAAADQAAAEF0bGFzU2FtcGxlcgBBdGxhcwBNaWNyb3NvZnQgKFIpIEhMU0wgU2hhZGVyIENvbXBpbGVyIDEwLjEAq0lTR05sAAAAAwAAAAgAAABQAAAAAAAAAAEAAAADAAAAAAAAAA8AAABcAAAAAAAAAAAAAAADAAAAAQAAAAMDAABlAAAAAAAAAAAAAAADAAAAAgAAAA8IAABTVl9QT1NJVElPTgBURVhDT09SRABDT0xPUgCrT1NHTiwAAAABAAAACAAAACAAAAAAAAAAAAAAAAMAAAAAAAAADwAAAFNWX1RBUkdFVACrq1NIRVj8AAAAUAAAAD8AAABqCAABWgAAAwBgEAAAAAAAWBgABABwEAAAAAAAVVUAAGIQAAMyEBAAAQAAAGIQAAOCEBAAAgAAAGUAAAPyIBAAAAAAAGgAAAICAAAARQAAi8IAAIBDVRUA8gAQAAAAAABGEBAAAQAAAEZ+EAAAAAAAAGAQAAAAAAAxAAAHEgAQAAEAAAABQAAAAAAAADoAEAAAAAAADgAAB+IAEAABAAAABgkQAAAAAAD2DxAAAAAAADcAAAlyIBAAAAAAAAYAEAABAAAAlgcQAAEAAABGAhAAAAAAADgAAAeCIBAAAAAAADoAEAAAAAAAOhAQAAIAAAA+AAABU1RBVJQAAAAGAAAAAgAAAAAAAAADAAAAAwAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly ID3D12Device* _device;
    private IDWriteFactory* _dwriteFactory;
    private IDWriteFactory4* _dwriteFactory4;
    private IDWriteFontCollection* _fontCollection;
    private IDWriteTextAnalyzer* _textAnalyzer;
    private IDWriteFontFallback* _fontFallback;
    private ID3D12RootSignature* _rootSig;
    private ID3D12PipelineState* _pso;
    private ID3D12PipelineState* _bgraPso;
    private readonly ID3D12Resource*[] _vbufs = new ID3D12Resource*[UploadFrameCount];
    private readonly D3D12_VERTEX_BUFFER_VIEW[] _vbvs = new D3D12_VERTEX_BUFFER_VIEW[UploadFrameCount];
    private byte[] _vertexShaderBytecode = [];
    private byte[] _pixelShaderBytecode = [];
    private byte[] _bgraPixelShaderBytecode = [];
    private byte[] _clearTypeScratch = [];
    private byte[] _grayscaleScratch = [];
    private int _rasterScratchResizeCount;
    private ushort[] _shapeClusterScratch = [];
    private DWRITE_SHAPING_TEXT_PROPERTIES[] _shapeTextPropsScratch = [];
    private ushort[] _shapeGlyphScratch = [];
    private DWRITE_SHAPING_GLYPH_PROPERTIES[] _shapeGlyphPropsScratch = [];
    private float[] _shapeAdvanceScratch = [];
    private DWRITE_GLYPH_OFFSET[] _shapeOffsetScratch = [];
    private byte[] _wicDecodeScratch = [];
    private DWRITE_SCRIPT_ANALYSIS[] _shapeScriptScratch = [];
    private byte[] _shapeBidiLevelScratch = [];
    private ShapedGlyph[] _shapedGlyphScratch = [];
    private ShapedGlyphSegment[] _shapedSegmentScratch = [];
    private ShapedGlyphLine[] _shapedLineScratch = [];
    private GlyphAtlasLayoutLine[] _shapedLayoutLineScratch = [];
    private float[] _shapedTextAdvanceScratch = [];
    private int _shapeScratchResizeCount;
    private readonly Vertex[] _vertices = new Vertex[MaxGlyphVertices];
    private readonly GlyphDrawBatch[] _batches = new GlyphDrawBatch[MaxGlyphDrawBatches];
    private GlyphEntry[] _layoutGlyphScratch = [];
    private float[] _layoutAdvanceScratch = [];
    private GlyphAtlasLayoutLine[] _layoutLineScratch = [];
    private readonly Dictionary<FontFaceKey, CachedFontFace> _fontFaces = [];
    private readonly Dictionary<nint, CachedFontFace> _fallbackFontFaces = [];
    private readonly Dictionary<GlyphKey, GlyphAtlasEntryHandle> _glyphs = [];
    private readonly List<GlyphEntry> _glyphEntries = new(512);
    private readonly List<int> _freeGlyphEntryIndices = new(128);
    private readonly List<int> _runGlyphEntryIndices = new(128);
    private readonly List<GlyphEntryMutationState> _runGlyphEntryStates = new(128);
    private readonly List<GlyphAtlasPage> _atlasPages = new(MaxAtlasPages);
    private readonly GlyphAtlasPageMutationState[] _runPageStates = new GlyphAtlasPageMutationState[MaxAtlasPages];
    private GlyphAtlasPageHandle _activeAtlasPage;
    private GlyphAtlasPageHandle _runActiveAtlasPage;
    private GlyphAtlasPageReuseRequest _pendingAlphaAtlasPageReuse;
    private GlyphAtlasPageReuseRequest _pendingBgraAtlasPageReuse;
    private GlyphAtlasPageReuseRequest _runPendingAlphaAtlasPageReuse;
    private GlyphAtlasPageReuseRequest _runPendingBgraAtlasPageReuse;
    private int _cachedGlyphCount;
    private int _runCachedGlyphCount;
    private int _runAtlasPageCount;
    private int _nextFontFaceIdentity = 1;
    private long _glyphRecordSerial;
    private bool _runAtlasMutationActive;
    private bool _runAtlasMutationUsedPageReuse;
    private bool _disposed;
    private bool _disabled;
    private bool _wicFactoryUnavailable;
    private bool _wicComInitializedForFactory;
    private int _wicComInitializationThreadId;
    private IWICImagingFactory* _wicFactory;
    private DeviceErrorDiagnostic _deviceError = DeviceErrorDiagnostic.None;
    private GlyphAtlasTextRendererDiagnostics _diagnostics;
    private GlyphAtlasTextRendererDiagnostics _runDiagnostics;

    public D3D12GlyphAtlasTextRenderer(ID3D12Device* device)
    {
        _device = device;

        try
        {
            RunInitializationPhase(GlyphAtlasInitializationPhase.DirectWriteFactory, () =>
            {
                PInvoke.DWriteCreateFactory(
                    DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
                    typeof(IDWriteFactory).GUID,
                    out var dwriteFactoryObject).ThrowOnFailure();
                _dwriteFactory = (IDWriteFactory*)dwriteFactoryObject;
            });
            RunInitializationPhase(GlyphAtlasInitializationPhase.FontCollection, () =>
            {
                IDWriteFontCollection* fontCollection;
                _dwriteFactory->GetSystemFontCollection(&fontCollection, false);
                _fontCollection = fontCollection;
            });
            RunInitializationPhase(GlyphAtlasInitializationPhase.TextAnalyzer, () =>
            {
                IDWriteTextAnalyzer* textAnalyzer;
                _dwriteFactory->CreateTextAnalyzer(&textAnalyzer);
                RequirePointer(textAnalyzer, "D3D12GlyphAtlasTextRenderer.CreateTextAnalyzer returned a null text analyzer.");
                _textAnalyzer = textAnalyzer;
            });
            RunInitializationPhase(GlyphAtlasInitializationPhase.FontFallback, () =>
            {
                _dwriteFactory->QueryInterface<IDWriteFactory4>(out var factory4).ThrowOnFailure();
                RequirePointer(factory4, "D3D12GlyphAtlasTextRenderer.QueryInterface(IDWriteFactory4) returned a null factory.");
                _dwriteFactory4 = factory4;
                IDWriteFontFallback* fontFallback;
                _dwriteFactory4->GetSystemFontFallback(&fontFallback);
                RequirePointer(fontFallback, "D3D12GlyphAtlasTextRenderer.GetSystemFontFallback returned a null font fallback.");
                _fontFallback = fontFallback;
            });

            RunInitializationPhase(GlyphAtlasInitializationPhase.RootSignature, CreateRootSignature);
            RunInitializationPhase(GlyphAtlasInitializationPhase.ShaderCompile, LoadEmbeddedShaderBytecode);
            RunInitializationPhase(GlyphAtlasInitializationPhase.PSO, CreatePSO);
            CreateAtlasResources();
            RunInitializationPhase(GlyphAtlasInitializationPhase.VertexBuffer, CreateVertexBuffers);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsDisabled => _disabled;
    public DeviceErrorDiagnostic DeviceError => _deviceError;

    public GlyphAtlasTextRendererDiagnostics GetDiagnostics()
    {
        var pageUsage = GetAtlasPageUsage();
        return _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPages(_atlasPages.Count)
            .WithAtlasPendingPageReuse(CountPendingAtlasPageReuseRequests())
            .WithAtlasPageUsage(pageUsage.UsedPixels, pageUsage.FragmentedPixels)
            .WithAtlasTouchMetrics(_glyphRecordSerial, pageUsage.OldestPageAge, pageUsage.NewestPageAge)
            .WithRasterScratch(
                _clearTypeScratch.Length + _grayscaleScratch.Length + GetShapeScratchByteCount(),
                _rasterScratchResizeCount + _shapeScratchResizeCount);
    }

    public void ResetDiagnostics()
    {
        var pageUsage = GetAtlasPageUsage();
        _diagnostics = new GlyphAtlasTextRendererDiagnostics(
            _cachedGlyphCount,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default,
            InitializationFailurePhase: GlyphAtlasInitializationPhase.None,
            RecordFailurePhase: GlyphAtlasRecordFailurePhase.None,
            RasterScratchBytes: _clearTypeScratch.Length + _grayscaleScratch.Length,
            RasterScratchResizes: 0,
            AtlasPages: _atlasPages.Count,
            AtlasUsedPixels: pageUsage.UsedPixels,
            AtlasFragmentedPixels: pageUsage.FragmentedPixels,
            AtlasRecordSerial: _glyphRecordSerial,
            AtlasOldestPageAge: pageUsage.OldestPageAge,
            AtlasNewestPageAge: pageUsage.NewestPageAge,
            AtlasPendingPageReuses: CountPendingAtlasPageReuseRequests());
        _rasterScratchResizeCount = 0;
    }

    public GlyphAtlasRecordResult TryRecord(
        ID3D12GraphicsCommandList* list,
        ReadOnlySpan<D3D12TextRun> textRuns,
        IFrameResourceResolver resources,
        int viewportWidth,
        int viewportHeight,
        int frameResourceIndex)
    {
        if (textRuns.Length == 0)
        {
            return GlyphAtlasRecordResult.Empty;
        }

        if (_disabled)
        {
            var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(textRuns, resources);
            if (degradedRunCount > 0)
            {
                RecordDegradation(degradedRunCount, GlyphAtlasFallbackReason.RecordFailed);
            }

            return GlyphAtlasRecordResult.DegradedOnly(degradedRunCount);
        }

        try
        {
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphRecordCommandList(list != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.CommandList,
                    "D3D12GlyphAtlasTextRenderer.TryRecord found a missing command list.");
            }

            if (!GlyphAtlasTextCompositionHelpers.HasGlyphDirectWriteResources(_dwriteFactory != null, _dwriteFactory4 != null, _fontCollection != null, _textAnalyzer != null, _fontFallback != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryRecord found missing DirectWrite resources.");
            }

            var recordSerial = ++_glyphRecordSerial;
            ApplyPendingAtlasPageEviction(recordSerial, oldestRetainedRecordSerial: recordSerial);
            var frame = BuildFrame(textRuns, resources, viewportWidth, viewportHeight, recordSerial);

            if (frame.VertexCount == 0)
            {
                _diagnostics = _diagnostics.WithAtlasRuns(frame.AtlasRunCount);
                RecordDegradation(frame.DegradedRunCount, frame.DegradationReasons);
                return new GlyphAtlasRecordResult(true, frame.AtlasRunCount, frame.DegradedRunCount);
            }

            UploadVertices(_vertices.AsSpan(0, frame.VertexCount), frameResourceIndex);

            for (var i = 0; i < _atlasPages.Count; i++)
            {
                var page = _atlasPages[i];
                if (page.IsDirty)
                {
                    UploadAtlas(list, page, frameResourceIndex);
                }
            }

            DrawGlyphs(list, frame, viewportWidth, viewportHeight, frameResourceIndex);
            _diagnostics = _diagnostics
                .WithDrawnGlyphs(frame.VertexCount / 6)
                .WithAtlasRuns(frame.AtlasRunCount);
            RecordDegradation(frame.DegradedRunCount, frame.DegradationReasons);
            return new GlyphAtlasRecordResult(true, frame.AtlasRunCount, frame.DegradedRunCount);
        }
        catch (GlyphAtlasRecordException ex)
        {
            var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(textRuns, resources);
            DisableGlyphAtlasDegradation(
                GlyphAtlasFallbackReason.RecordFailed,
                ex.Phase,
                DeviceErrorDiagnostic.FromException(DeviceErrorSite.GlyphAtlasRecord, ex.InnerException),
                degradedRunCount);
            return GlyphAtlasRecordResult.DegradedOnly(degradedRunCount);
        }
        catch (COMException ex)
        {
            var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(textRuns, resources);
            DisableGlyphAtlasDegradation(
                GlyphAtlasFallbackReason.RecordFailed,
                GlyphAtlasRecordFailurePhase.Record,
                DeviceErrorDiagnostic.FromComException(DeviceErrorSite.GlyphAtlasRecord, ex.ErrorCode),
                degradedRunCount);
            return GlyphAtlasRecordResult.DegradedOnly(degradedRunCount);
        }
        catch (InvalidOperationException)
        {
            var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(textRuns, resources);
            DisableGlyphAtlasDegradation(
                GlyphAtlasFallbackReason.RecordFailed,
                GlyphAtlasRecordFailurePhase.Record,
                DeviceErrorDiagnostic.FromInvalidOperation(DeviceErrorSite.GlyphAtlasRecord),
                degradedRunCount);
            return GlyphAtlasRecordResult.DegradedOnly(degradedRunCount);
        }
    }

    private GlyphFrame BuildFrame(
        ReadOnlySpan<D3D12TextRun> textRuns,
        IFrameResourceResolver resources,
        int viewportWidth,
        int viewportHeight,
        long recordSerial)
    {
        var vertexCount = 0;
        var batchCount = 0;
        var atlasRunCount = 0;
        var degradedRunCount = 0;
        var degradationReasons = default(GlyphAtlasFallbackReasonCounts);

        foreach (var textRun in textRuns)
        {
            var runResolver = textRun.Resolver ?? resources;
            var text = runResolver.Resolve(textRun.Text);
            if (text.IsEmpty || textRun.Width <= 0 || textRun.Height <= 0)
            {
                continue;
            }

            var style = (textRun.ResolvedStyle != default ? textRun.ResolvedStyle : runResolver.ResolveTextStyle(textRun.Style)).Normalize();
            BeginAtlasRunMutation();
            var unsupportedReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(text, style);
            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                var shapedUnsupportedReason = unsupportedReason;
                if (unsupportedReason == GlyphAtlasFallbackReason.NonAscii
                    && TryProbeShapedRun(text, style, textRun.Width, out var shapedRun, out shapedUnsupportedReason))
                {
                    _diagnostics = _diagnostics.WithShapedGlyphProbe(shapedRun.GlyphCount);
                    if (TryBuildShapedAtlasRun(text, textRun, style, shapedRun, viewportWidth, viewportHeight, recordSerial, ref vertexCount, ref batchCount, out unsupportedReason))
                    {
                        CommitAtlasRunMutation();
                        atlasRunCount++;
                        continue;
                    }
                }
                else if (unsupportedReason == GlyphAtlasFallbackReason.NonAscii)
                {
                    unsupportedReason = shapedUnsupportedReason;
                }

                RollbackAtlasRunMutation(recordSerial, unsupportedReason);
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            if (!TryGetFontFace(style, out var fontFace))
            {
                RollbackAtlasRunMutation(recordSerial, GlyphAtlasFallbackReason.FontMissing);
                AddDegradedRun(GlyphAtlasFallbackReason.FontMissing, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            EnsureLayoutScratch(text.Length);
            if (!TryBuildLineLayout(text, fontFace, style, textRun.Width, recordSerial, out var lineCount, out unsupportedReason))
            {
                RollbackAtlasRunMutation(recordSerial, unsupportedReason);
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var lineHeight = ComputeLineHeight(fontFace.Metrics, style.FontSize);
            var firstBaselineY = ComputeFirstBaselineY(textRun, style, fontFace.Metrics, style.FontSize, lineHeight, lineCount);
            var scissor = ResolveRunScissor(textRun, viewportWidth, viewportHeight);
            if (scissor.IsEmpty)
            {
                RollbackAtlasRunMutation(recordSerial, GlyphAtlasFallbackReason.Clip);
                AddDegradedRun(GlyphAtlasFallbackReason.Clip, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var color = new Vector4(textRun.R, textRun.G, textRun.B, textRun.A);
            var batchStart = vertexCount;
            var batchSegmentStart = vertexCount;
            var batchPage = default(GlyphAtlasPageHandle);
            var maxX = textRun.X + textRun.Width;

            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var line = _layoutLineScratch[lineIndex];
                var penX = GlyphAtlasTextCompositionHelpers.ComputeAlignedPenX(textRun.X, textRun.Width, style.HorizontalAlignment, line.Width);
                var baselineY = firstBaselineY + lineIndex * lineHeight;

                for (var glyphIndex = line.Start; glyphIndex < line.End; glyphIndex++)
                {
                    if (GlyphAtlasTextCompositionHelpers.IsLineBreak(text, glyphIndex, out _) || GlyphAtlasTextCompositionHelpers.IsTab(text[glyphIndex]))
                    {
                        penX += _layoutAdvanceScratch[glyphIndex];
                        continue;
                    }

                    var glyph = _layoutGlyphScratch[glyphIndex];
                    if (style.Wrapping == TextWrapping.NoWrap && penX + glyph.Advance > maxX)
                    {
                        unsupportedReason = GlyphAtlasFallbackReason.Clip;
                        break;
                    }

                    if (glyph.Width > 0 && glyph.Height > 0)
                    {
                        if (batchPage.IsNone)
                        {
                            batchPage = glyph.Page;
                        }
                        else if (batchPage != glyph.Page)
                        {
                            if (!TryAppendDrawBatch(ref batchCount, ref vertexCount, batchSegmentStart, scissor, batchPage))
                            {
                                unsupportedReason = GlyphAtlasFallbackReason.BatchLimit;
                                break;
                            }

                            batchSegmentStart = vertexCount;
                            batchPage = glyph.Page;
                        }

                        if (vertexCount + 6 > MaxGlyphVertices)
                        {
                            unsupportedReason = GlyphAtlasFallbackReason.VertexLimit;
                            break;
                        }

                        var x1 = penX + glyph.OffsetX;
                        var y1 = baselineY + glyph.OffsetY;
                        var x2 = x1 + glyph.Width;
                        var y2 = y1 + glyph.Height;
                        AppendQuad(_vertices, ref vertexCount, x1, y1, x2, y2, glyph, color, viewportWidth, viewportHeight);
                    }

                    penX += glyph.Advance;
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

                RollbackAtlasRunMutation(recordSerial, unsupportedReason);
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            if (vertexCount > batchSegmentStart && !TryAppendDrawBatch(ref batchCount, ref vertexCount, batchSegmentStart, scissor, batchPage))
            {
                vertexCount = batchStart;
                while (batchCount > 0 && _batches[batchCount - 1].StartVertex >= batchStart)
                {
                    batchCount--;
                }

                RollbackAtlasRunMutation(recordSerial, GlyphAtlasFallbackReason.BatchLimit);
                AddDegradedRun(GlyphAtlasFallbackReason.BatchLimit, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            CommitAtlasRunMutation();
            atlasRunCount++;
        }

        return new GlyphFrame(vertexCount, batchCount, atlasRunCount, degradedRunCount, degradationReasons);
    }

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
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        var segmentRequiresColor = shapedRun.RequiresColorGlyph
            && GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector(text.Slice(shapedSegment.TextStart, shapedSegment.TextLength));
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
            out unsupportedReason))
        {
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
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
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
                        out unsupportedReason))
                    {
                        return false;
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
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (colorGlyphRun == null)
        {
            return true;
        }

        var imageFormat = colorGlyphRun->glyphImageFormat;
        if ((imageFormat & SupportedLayerColorGlyphFormats) != 0)
        {
            var baseRun = &colorGlyphRun->Base;
            return TryAppendColorGlyphLayer(
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
                out unsupportedReason);
        }

        if ((imageFormat & SupportedBitmapColorGlyphFormats) != 0)
        {
            return TryAppendBitmapColorGlyphRun(
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
                out unsupportedReason);
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
            if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
            {
                if (!TryGetBgraColorGlyph(fontFace, fontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out glyph, out _, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None ? GetColorGlyphRunImageFormatFallbackReason(imageFormat) : unsupportedReason;
                    return false;
                }
            }
            else if (TrySelectEncodedBitmapColorGlyphFormat(imageFormat, out var encodedImageFormat))
            {
                if (!TryGetEncodedBitmapColorGlyph(fontFace, fontEmSize, pixelsPerEm, glyphIndex, encodedImageFormat, advance, recordSerial, out glyph, out unsupportedReason))
                {
                    unsupportedReason = unsupportedReason == GlyphAtlasFallbackReason.None ? GetColorGlyphRunImageFormatFallbackReason(imageFormat) : unsupportedReason;
                    return false;
                }
            }
            else
            {
                unsupportedReason = GetColorGlyphRunImageFormatFallbackReason(imageFormat);
                return false;
            }

            if (!TryAppendGlyphQuad(
                glyph,
                color,
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
                currentBrush,
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

            if ((formats & DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8) != 0)
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
                    currentBrush,
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
            else if (TrySelectEncodedBitmapColorGlyphFormat(formats, out var imageFormat))
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
                    currentBrush,
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
                return false;
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
        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PNG)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphPng;
        }

        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_JPEG)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphJpeg;
        }

        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_TIFF)
        {
            return reason | GlyphAtlasFallbackReason.ColorGlyphTiff;
        }

        return reason;
    }

    private static GlyphAtlasFallbackReason GetColorGlyphRunImageFormatFallbackReason(DWRITE_GLYPH_IMAGE_FORMATS imageFormat)
    {
        if (imageFormat == DWRITE_GLYPH_IMAGE_FORMATS.DWRITE_GLYPH_IMAGE_FORMATS_PREMULTIPLIED_B8G8R8A8)
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

    private static bool TryMeasureCharacterAdvance(
        CachedFontFace fontFace,
        float emSize,
        char character,
        out float advance)
    {
        advance = 0;
        if (!TryMapCharacterToSimpleGlyph(fontFace, character, out var glyphAtom))
        {
            return false;
        }

        advance = ComputeGlyphAdvance(fontFace, emSize, glyphAtom.GlyphIndex);
        return true;
    }

    private static void AddDegradedRun(
        GlyphAtlasFallbackReason reason,
        ref int degradedRunCount,
        ref GlyphAtlasFallbackReasonCounts degradationReasons)
    {
        degradedRunCount++;
        degradationReasons = degradationReasons.With(reason);
    }

    private bool TryAppendDrawBatch(
        ref int batchCount,
        ref int vertexCount,
        int batchStart,
        IntegerScissorRect scissor,
        GlyphAtlasPageHandle page)
    {
        if (vertexCount == batchStart)
        {
            return true;
        }

        if (batchCount >= MaxGlyphDrawBatches || page.IsNone)
        {
            return false;
        }

        _batches[batchCount++] = new GlyphDrawBatch(batchStart, vertexCount - batchStart, scissor, page);
        return true;
    }

    private static IntegerScissorRect ResolveRunScissor(D3D12TextRun textRun, int viewportWidth, int viewportHeight)
    {
        if (textRun.ClipEnabled)
        {
            return DrawingScissor.ToIntegerScissorRect(textRun.EffectiveClip, viewportWidth, viewportHeight);
        }

        return new IntegerScissorRect(0, 0, viewportWidth, viewportHeight);
    }

    private bool TryGetFontFace(TextStyle style, out CachedFontFace fontFace)
    {
        var key = new FontFaceKey(style.FontFamily, style.FontWeight, style.FontStyle, style.FontStretch, style.FontSize);
        if (_fontFaces.TryGetValue(key, out fontFace!))
        {
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(fontFace.Face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing cached font face.");
            }

            return true;
        }

        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;

        try
        {
            _fontCollection->FindFamilyName(ToDirectWriteFontFamily(style.FontFamily), out var familyIndex, out var exists);
            if (!exists)
            {
                _fontCollection->FindFamilyName(ToDirectWriteFontFamily(TextStyle.Default.FontFamily), out familyIndex, out exists);
                if (!exists)
                {
                    return false;
                }
            }

            _fontCollection->GetFontFamily(familyIndex, &family);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFamilyResource(family != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font family.");
            }

            family->GetFirstMatchingFont(
                ToDirectWriteFontWeight(style.FontWeight),
                ToDirectWriteFontStretch(style.FontStretch),
                ToDirectWriteFontStyle(style.FontStyle),
                &font);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontResource(font != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font.");
            }

            font->CreateFontFace(&face);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetFontFace found a missing DirectWrite font face.");
            }

            face->GetMetrics(out var metrics);
            face4 = TryQueryFontFace4(face);
            fontFace = new CachedFontFace(new FontFaceIdentity(_nextFontFaceIdentity++), face, metrics, face4);
            _fontFaces.Add(key, fontFace);
            face = null;
            face4 = null;
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.TryGetFontFace",
                ex);
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (face != null) face->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
        }
    }

    private bool TryGetGlyph(
        CachedFontFace fontFace,
        TextStyle style,
        char character,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (!TryMapCharacterToSimpleGlyph(fontFace, character, out var glyphAtom))
        {
            glyph = default;
            unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
            return false;
        }

        var key = new GlyphKey(fontFace.Identity, style.FontSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, style.FontSize, glyphAtom, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetShapedGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        in ShapedGlyph shapedGlyph,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var glyphAtom = GlyphAtom.ShapedPlacement(
            shapedGlyph.GlyphIndex,
            shapedGlyph.IsDiacritic,
            shapedGlyph.IsZeroWidthSpace);
        var key = new GlyphKey(fontFace.Identity, fontEmSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, fontEmSize, shapedGlyph, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetColorLayerGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        ushort glyphIndex,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var glyphAtom = GlyphAtom.ColorLayer(glyphIndex);
        var key = new GlyphKey(fontFace.Identity, fontEmSize, glyphAtom);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(key, fontFace, fontEmSize, ShapedGlyph.Simple(glyphIndex, advance), recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetBgraColorGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out bool glyphHadBgra,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        glyph = default;
        glyphHadBgra = false;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        glyphHadBgra = true;
        var key = new GlyphKey(fontFace.Identity, fontEmSize, GlyphAtom.BgraGlyph(glyphIndex, pixelsPerEm));
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeBgraColorGlyph(key, fontFace, fontEmSize, pixelsPerEm, glyphIndex, advance, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private bool TryGetEncodedBitmapColorGlyph(
        CachedFontFace fontFace,
        float fontEmSize,
        uint pixelsPerEm,
        ushort glyphIndex,
        DWRITE_GLYPH_IMAGE_FORMATS imageFormat,
        float advance,
        long recordSerial,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        glyph = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        if (fontFace.Face4 == null)
        {
            return false;
        }

        var key = new GlyphKey(fontFace.Identity, fontEmSize, GlyphAtom.EncodedBitmapGlyph(glyphIndex, pixelsPerEm, GetEncodedBitmapGlyphFormatId(imageFormat)));
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, recordSerial, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeEncodedBitmapColorGlyph(key, fontFace, fontEmSize, pixelsPerEm, glyphIndex, imageFormat, advance, recordSerial, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _cachedGlyphCount++;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithUploadedGlyph();
        return true;
    }

    private GlyphAtlasEntryHandle AddGlyphEntry(in GlyphEntry entry)
    {
        if (_freeGlyphEntryIndices.Count > 0)
        {
            var freeIndex = _freeGlyphEntryIndices.Count - 1;
            var entryIndex = _freeGlyphEntryIndices[freeIndex];
            _freeGlyphEntryIndices.RemoveAt(freeIndex);
            var generation = checked(_glyphEntries[entryIndex].Generation + 1);
            var reusedHandle = new GlyphAtlasEntryHandle(entryIndex, generation);
            _glyphEntries[entryIndex] = entry.WithGeneration(generation);
            TrackRunGlyphEntry(entryIndex);
            return reusedHandle;
        }

        var handle = new GlyphAtlasEntryHandle(_glyphEntries.Count, 1);
        _glyphEntries.Add(entry.WithGeneration(handle.Generation));
        TrackRunGlyphEntry(handle.Index);
        return handle;
    }

    private void TrackRunGlyphEntry(int entryIndex)
    {
        if (_runAtlasMutationActive)
        {
            _runGlyphEntryIndices.Add(entryIndex);
        }
    }

    private void BeginAtlasRunMutation()
    {
        _runAtlasMutationActive = true;
        _runAtlasMutationUsedPageReuse = false;
        _runGlyphEntryIndices.Clear();
        _runGlyphEntryStates.Clear();
        _runActiveAtlasPage = _activeAtlasPage;
        _runPendingAlphaAtlasPageReuse = _pendingAlphaAtlasPageReuse;
        _runPendingBgraAtlasPageReuse = _pendingBgraAtlasPageReuse;
        _runCachedGlyphCount = _cachedGlyphCount;
        _runAtlasPageCount = _atlasPages.Count;
        _runDiagnostics = _diagnostics;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _runPageStates[i] = _atlasPages[i].CaptureMutationState();
        }
    }

    private void CommitAtlasRunMutation()
    {
        _runAtlasMutationActive = false;
        _runAtlasMutationUsedPageReuse = false;
        _runGlyphEntryIndices.Clear();
        _runGlyphEntryStates.Clear();
        _runActiveAtlasPage = default;
        _runPendingAlphaAtlasPageReuse = default;
        _runPendingBgraAtlasPageReuse = default;
        _runAtlasPageCount = 0;
    }

    private void RollbackAtlasRunMutation(long recordSerial, GlyphAtlasFallbackReason reason)
    {
        if (!_runAtlasMutationActive)
        {
            return;
        }

        if (_runAtlasMutationUsedPageReuse)
        {
            RemoveRunGlyphEntries(clearPixels: true);
            RestoreRunGlyphEntryTouches();
            ReleaseAtlasPagesCreatedDuringRun();
            RestoreRunPageStates();
            var resetPageCount = ResetPagesReusedDuringRun();
            _pendingAlphaAtlasPageReuse = _runPendingAlphaAtlasPageReuse;
            _pendingBgraAtlasPageReuse = _runPendingBgraAtlasPageReuse;
            _cachedGlyphCount = CountLiveGlyphEntries();
            _diagnostics = _runDiagnostics
                .WithCachedGlyphs(_cachedGlyphCount)
                .WithAtlasPages(_atlasPages.Count);
            for (var i = 0; i < resetPageCount; i++)
            {
                _diagnostics = _diagnostics.WithAtlasEviction();
            }
        }
        else
        {
            RemoveRunGlyphEntries(clearPixels: true);
            RestoreRunGlyphEntryTouches();
            ReleaseAtlasPagesCreatedDuringRun();
            RestoreRunPageStates();
            _activeAtlasPage = _runActiveAtlasPage;
            _pendingAlphaAtlasPageReuse = _runPendingAlphaAtlasPageReuse;
            _pendingBgraAtlasPageReuse = _runPendingBgraAtlasPageReuse;
            _cachedGlyphCount = _runCachedGlyphCount;
            _diagnostics = _runDiagnostics;
        }

        CommitAtlasRunMutation();
    }

    private void RemoveRunGlyphEntries(bool clearPixels)
    {
        for (var i = 0; i < _runGlyphEntryIndices.Count; i++)
        {
            var entryIndex = _runGlyphEntryIndices[i];
            if ((uint)entryIndex >= (uint)_glyphEntries.Count)
            {
                continue;
            }

            var entry = _glyphEntries[entryIndex];
            if (!entry.IsLive)
            {
                continue;
            }

            if (clearPixels)
            {
                ClearGlyphEntryPixels(entry);
            }

            _glyphs.Remove(entry.Key);
            _glyphEntries[entryIndex] = entry.Clear();
            _freeGlyphEntryIndices.Add(entryIndex);
        }
    }

    private void RestoreRunGlyphEntryTouches()
    {
        for (var i = 0; i < _runGlyphEntryStates.Count; i++)
        {
            var state = _runGlyphEntryStates[i];
            if ((uint)state.Index >= (uint)_glyphEntries.Count)
            {
                continue;
            }

            var entry = _glyphEntries[state.Index];
            if (entry.IsLive && entry.Generation == state.Generation)
            {
                _glyphEntries[state.Index] = entry.WithLastUsedSerial(state.LastUsedSerial);
            }
        }
    }

    private void ClearGlyphEntryPixels(GlyphEntry entry)
    {
        if (!TryResolveAtlasPage(entry.Page, out var page))
        {
            return;
        }

        var x = Math.Clamp((int)MathF.Round(entry.U1 * AtlasWidth), 0, AtlasWidth);
        var y = Math.Clamp((int)MathF.Round(entry.V1 * AtlasHeight), 0, AtlasHeight);
        var width = Math.Clamp((int)MathF.Round(entry.U2 * AtlasWidth), x, AtlasWidth) - x;
        var height = Math.Clamp((int)MathF.Round(entry.V2 * AtlasHeight), y, AtlasHeight) - y;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var rowPitch = page.RowPitch;
        var bytesPerPixel = page.BytesPerPixel;
        var byteX = x * bytesPerPixel;
        var byteWidth = width * bytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            page.Pixels.AsSpan((y + row) * rowPitch + byteX, byteWidth).Clear();
        }
    }

    private int ResetPagesReusedDuringRun()
    {
        var resetPageCount = 0;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Handle == _runPageStates[i].Handle)
            {
                continue;
            }

            _activeAtlasPage = page.ResetForReuse();
            resetPageCount++;
        }

        return resetPageCount;
    }

    private void RestoreRunPageStates()
    {
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _atlasPages[i].RestoreMutationState(_runPageStates[i]);
        }
    }

    private void ReleaseAtlasPagesCreatedDuringRun()
    {
        while (_atlasPages.Count > _runAtlasPageCount)
        {
            var pageIndex = _atlasPages.Count - 1;
            _atlasPages[pageIndex].Release();
            _atlasPages.RemoveAt(pageIndex);
        }
    }

    private int CountLiveGlyphEntries()
    {
        var count = 0;
        for (var i = 0; i < _glyphEntries.Count; i++)
        {
            if (_glyphEntries[i].IsLive)
            {
                count++;
            }
        }

        return count;
    }

    private bool TryResolveGlyph(GlyphAtlasEntryHandle handle, long recordSerial, out GlyphEntry entry)
    {
        if (handle.IsNone || (uint)handle.Index >= (uint)_glyphEntries.Count)
        {
            entry = default;
            return false;
        }

        entry = _glyphEntries[handle.Index];
        if (!entry.IsLive || entry.Generation != handle.Generation || !TryResolveAtlasPage(entry.Page, out var page))
        {
            return false;
        }

        if (_runAtlasMutationActive && entry.LastUsedSerial != recordSerial)
        {
            _runGlyphEntryStates.Add(new GlyphEntryMutationState(handle.Index, entry.Generation, entry.LastUsedSerial));
        }

        page.Touch(recordSerial);
        entry = entry.WithLastUsedSerial(recordSerial);
        _glyphEntries[handle.Index] = entry;
        return true;
    }

    private GlyphAtlasPageHandle AddAtlasPage(GlyphAtlasPageFormat format, ID3D12Resource* texture, ID3D12Resource*[] uploads, ID3D12DescriptorHeap* srvHeap, D3D12_RESOURCE_STATES textureState)
    {
        if (_atlasPages.Count >= MaxAtlasPages)
        {
            throw new InvalidOperationException("Glyph atlas page pool exceeded its fixed capacity.");
        }

        var handle = new GlyphAtlasPageHandle(_atlasPages.Count, 1);
        _atlasPages.Add(new GlyphAtlasPage(handle, format, texture, uploads, srvHeap, textureState, new byte[GetAtlasPixelBytes(format)]));
        return handle;
    }

    private bool TryResolveAtlasPage(GlyphAtlasPageHandle handle, [NotNullWhen(true)] out GlyphAtlasPage? page)
    {
        if (handle.IsNone || (uint)handle.Index >= (uint)_atlasPages.Count)
        {
            page = null;
            return false;
        }

        page = _atlasPages[handle.Index];
        return page.Generation == handle.Generation;
    }

    private GlyphAtlasPage RequireActiveAtlasPage()
    {
        if (!TryResolveAtlasPage(_activeAtlasPage, out var page))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.RequireActiveAtlasPage found a stale active glyph atlas page handle.");
        }

        return page;
    }

    private GlyphAtlasPage? SelectWritableAtlasPage(GlyphAtlasPageFormat format, int width, int height, long recordSerial)
    {
        if (width <= 0 || height <= 0)
        {
            var active = RequireActiveAtlasPage();
            if (active.Format == format)
            {
                return active;
            }

            return FindAtlasPageByFormat(format) ?? TryCreateAdditionalAtlasPage(format);
        }

        var activePage = RequireActiveAtlasPage();
        if (activePage.Format == format && CanAllocateGlyph(activePage, width, height))
        {
            return activePage;
        }

        var selectedIndex = -1;
        var selectedAvailablePixels = -1;
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Handle == activePage.Handle || page.Format != format || !CanAllocateGlyph(page, width, height))
            {
                continue;
            }

            var availablePixels = page.ComputeAvailablePixels();
            if (GlyphAtlasTextCompositionHelpers.ShouldSelectWritableAtlasPage(selectedAvailablePixels, selectedLastUsedSerial, availablePixels, page.LastUsedSerial))
            {
                selectedIndex = i;
                selectedAvailablePixels = availablePixels;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        if (selectedIndex >= 0)
        {
            var page = _atlasPages[selectedIndex];
            _activeAtlasPage = page.Handle;
            return page;
        }

        var newPage = TryCreateAdditionalAtlasPage(format);
        if (newPage is not null)
        {
            _activeAtlasPage = newPage.Handle;
            return newPage;
        }

        var reusedPage = TryReuseColdAtlasPageForCurrentRecord(format, width, height, recordSerial);
        if (reusedPage is not null)
        {
            return reusedPage;
        }

        ScheduleAtlasPageReuse(recordSerial, format);
        return null;
    }

    private GlyphAtlasPage? FindAtlasPageByFormat(GlyphAtlasPageFormat format)
    {
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Format == format)
            {
                return page;
            }
        }

        return null;
    }

    private GlyphAtlasPage? TryReuseColdAtlasPageForCurrentRecord(GlyphAtlasPageFormat format, int width, int height, long recordSerial)
    {
        if (width + AtlasPadding * 2 > AtlasWidth || height + AtlasPadding * 2 > AtlasHeight)
        {
            return null;
        }

        var selectedIndex = -1;
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (page.Format != format || !GlyphAtlasTextCompositionHelpers.CanReuseAtlasPageInCurrentRecord(page.LastUsedSerial, recordSerial))
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial, page.LastUsedSerial))
            {
                selectedIndex = i;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        if (selectedIndex < 0)
        {
            return null;
        }

        var selected = _atlasPages[selectedIndex];
        var reusedHandle = selected.Handle;
        if (_runAtlasMutationActive)
        {
            _runAtlasMutationUsedPageReuse = true;
        }

        _activeAtlasPage = selected.ResetForReuse();
        RemoveGlyphsForReusedPage(reusedHandle);
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPages(_atlasPages.Count)
            .WithAtlasEviction();
        return selected;
    }

    private void ScheduleAtlasPageReuse(long recordSerial, GlyphAtlasPageFormat? format = null)
    {
        var page = SelectOldestAtlasPageHandle(format);
        if (page.IsNone)
        {
            _diagnostics = _diagnostics.WithAtlasFullWithoutPageReuse();
            return;
        }

        if (!TryResolveAtlasPage(page, out var atlasPage))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.ScheduleAtlasPageReuse found a stale glyph atlas page handle.");
        }

        ref var pending = ref GetPendingAtlasPageReuse(atlasPage.Format);
        if (!pending.IsNone)
        {
            return;
        }

        pending = new GlyphAtlasPageReuseRequest(page, recordSerial);
        _diagnostics = _diagnostics.WithAtlasPageReuseRequest();
    }

    private ref GlyphAtlasPageReuseRequest GetPendingAtlasPageReuse(GlyphAtlasPageFormat format)
    {
        if (format == GlyphAtlasPageFormat.Bgra)
        {
            return ref _pendingBgraAtlasPageReuse;
        }

        return ref _pendingAlphaAtlasPageReuse;
    }

    private int CountPendingAtlasPageReuseRequests()
    {
        var count = 0;
        if (!_pendingAlphaAtlasPageReuse.IsNone)
        {
            count++;
        }

        if (!_pendingBgraAtlasPageReuse.IsNone)
        {
            count++;
        }

        return count;
    }

    private GlyphAtlasPageHandle SelectOldestAtlasPageHandle(GlyphAtlasPageFormat? format)
    {
        var selected = default(GlyphAtlasPageHandle);
        var selectedLastUsedSerial = long.MaxValue;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            if (format.HasValue && page.Format != format.GetValueOrDefault())
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldSelectOlderAtlasPage(selectedLastUsedSerial, page.LastUsedSerial))
            {
                selected = page.Handle;
                selectedLastUsedSerial = page.LastUsedSerial;
            }
        }

        return selected;
    }

    private GlyphAtlasPageUsage GetAtlasPageUsage()
    {
        var usedPixels = 0;
        var fragmentedPixels = 0;
        var oldestPageAge = 0L;
        var newestPageAge = 0L;
        var hasUsedPage = false;
        for (var i = 0; i < _atlasPages.Count; i++)
        {
            var page = _atlasPages[i];
            usedPixels = checked(usedPixels + page.UsedPixels);
            fragmentedPixels = checked(fragmentedPixels + Math.Max(0, page.AllocatedPixels - page.UsedPixels));
            if (page.LastUsedSerial > 0)
            {
                var age = _glyphRecordSerial - page.LastUsedSerial;
                oldestPageAge = Math.Max(oldestPageAge, age);
                newestPageAge = hasUsedPage ? Math.Min(newestPageAge, age) : age;
                hasUsedPage = true;
            }
        }

        return new GlyphAtlasPageUsage(usedPixels, fragmentedPixels, oldestPageAge, newestPageAge);
    }

    private void ApplyPendingAtlasPageEviction(long recordSerial, long oldestRetainedRecordSerial)
    {
        ApplyPendingAtlasPageEviction(ref _pendingAlphaAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);
        ApplyPendingAtlasPageEviction(ref _pendingBgraAtlasPageReuse, recordSerial, oldestRetainedRecordSerial);
    }

    private void ApplyPendingAtlasPageEviction(ref GlyphAtlasPageReuseRequest pendingPageReuse, long recordSerial, long oldestRetainedRecordSerial)
    {
        if (!pendingPageReuse.CanApply(recordSerial, oldestRetainedRecordSerial))
        {
            return;
        }

        var page = RequireReuseAtlasPage(pendingPageReuse);
        var reusedPage = page.Handle;
        _activeAtlasPage = page.ResetForReuse();
        RemoveGlyphsForReusedPage(reusedPage);
        pendingPageReuse = default;
        _diagnostics = _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPages(_atlasPages.Count)
            .WithAtlasEviction();
    }

    private GlyphAtlasPage RequireReuseAtlasPage(GlyphAtlasPageReuseRequest request)
    {
        if (!TryResolveAtlasPage(request.Page, out var page))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.ApplyPendingAtlasPageEviction found a stale pending glyph atlas page reuse handle.");
        }

        return page;
    }

    private void RemoveGlyphsForReusedPage(GlyphAtlasPageHandle pageHandle)
    {
        var cachedGlyphCount = 0;
        for (var i = 0; i < _glyphEntries.Count; i++)
        {
            var entry = _glyphEntries[i];
            if (!entry.IsLive)
            {
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.ShouldClearGlyphForReusedPage(
                entry.IsLive,
                entry.Page.Index,
                entry.Page.Generation,
                pageHandle.Index,
                pageHandle.Generation))
            {
                _glyphs.Remove(entry.Key);
                _glyphEntries[i] = entry.Clear();
                _freeGlyphEntryIndices.Add(i);
                continue;
            }

            cachedGlyphCount++;
        }

        _cachedGlyphCount = cachedGlyphCount;
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

    private static bool TryMapCharacterToSimpleGlyph(CachedFontFace fontFace, char character, out GlyphAtom glyph)
    {
        glyph = default;
        var codePoint = (uint)character;
        var glyphIndex = stackalloc ushort[1];
        try
        {
            fontFace.Face->GetGlyphIndices(new ReadOnlySpan<uint>(&codePoint, 1), new Span<ushort>(glyphIndex, 1));
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.GetGlyphIndices(simple)",
                ex);
        }

        if (glyphIndex[0] == 0 && character != ' ')
        {
            return false;
        }

        glyph = GlyphAtom.SimpleCodePoint(codePoint, glyphIndex[0]);
        return true;
    }

    private bool TryProbeShapedRun(ReadOnlySpan<char> text, TextStyle style, float maxLineWidth, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)
    {
        shapedRun = default;
        unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
        if (text.IsEmpty || text.Length > ushort.MaxValue)
        {
            return false;
        }

        var requiresColorGlyph = GlyphAtlasTextCompositionHelpers.ContainsSurrogateOrVariationSelector(text);
        CachedFontFace fontFace;
        try
        {
            if (!TryGetFontFace(style, out fontFace))
            {
                unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
                return false;
            }
        }
        catch (GlyphAtlasRecordException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font lookup skipped: {ex.Phase}");
            return false;
        }

        if (!TryShapeRun(text, style, fontFace, maxLineWidth, requiresColorGlyph, out shapedRun, out unsupportedReason))
        {
            if (requiresColorGlyph)
            {
                unsupportedReason = GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ColorGlyph;
            }

            return false;
        }

        return true;
    }

    private bool TryShapeRun(ReadOnlySpan<char> text, TextStyle style, CachedFontFace baseFontFace, float maxLineWidth, bool requiresColorGlyph, out ShapedGlyphRun shapedRun, out GlyphAtlasFallbackReason unsupportedReason)
    {
        shapedRun = default;
        unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
        var maxGlyphCount = GlyphAtlasTextCompositionHelpers.EstimateShapedGlyphCapacity(text.Length);
        EnsureShapeScratch(text.Length, maxGlyphCount, MaxShapedRunSegments, text.Length + 1);
        var advances = _shapedTextAdvanceScratch.AsSpan(0, text.Length);
        advances.Clear();
        _shapeScriptScratch.AsSpan(0, text.Length).Clear();
        var paragraphReadingDirection = DetermineParagraphReadingDirection(text);
        _shapeBidiLevelScratch.AsSpan(0, text.Length).Fill(paragraphReadingDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT ? (byte)1 : (byte)0);
        var hasComplexScriptCandidate = GlyphAtlasTextCompositionHelpers.ContainsComplexScriptCandidate(text);
        if (!TryAnalyzeShapedText(text, paragraphReadingDirection))
        {
            unsupportedReason = hasComplexScriptCandidate ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ComplexScript : GlyphAtlasFallbackReason.NonAscii;
            return false;
        }

        var hasRightToLeftBidiLevel = HasRightToLeftBidiLevel(0, text.Length);
        unsupportedReason = hasComplexScriptCandidate || hasRightToLeftBidiLevel ? GlyphAtlasFallbackReason.NonAscii | GlyphAtlasFallbackReason.ComplexScript : GlyphAtlasFallbackReason.NonAscii;

        var glyphStart = 0;
        var segmentCount = 0;
        var index = 0;
        while (true)
        {
            var lineStart = index;
            while (index < text.Length && !GlyphAtlasTextCompositionHelpers.IsLineBreak(text, index, out _))
            {
                index++;
            }

            if (!TryShapeTextRange(text, lineStart, index - lineStart, style, baseFontFace, ref glyphStart, ref segmentCount))
            {
                return false;
            }

            if (index >= text.Length)
            {
                break;
            }

            GlyphAtlasTextCompositionHelpers.IsLineBreak(text, index, out var lineBreakWidth);
            index += lineBreakWidth;
            if (index == text.Length)
            {
                break;
            }
        }

        unsupportedReason = GlyphAtlasTextCompositionHelpers.PlanLines(
            text,
            advances,
            maxLineWidth,
            style.Wrapping,
            _shapedLayoutLineScratch.AsSpan(0, text.Length + 1),
            out var plannedLineCount);
        if (unsupportedReason != GlyphAtlasFallbackReason.None)
        {
            return false;
        }

        if (!TryBuildShapedLinesFromLayout(text, segmentCount, plannedLineCount, out var lineCount))
        {
            unsupportedReason = GlyphAtlasFallbackReason.NonAscii;
            return false;
        }

        shapedRun = new ShapedGlyphRun(
            _shapedGlyphScratch.AsSpan(0, glyphStart),
            _shapedSegmentScratch.AsSpan(0, segmentCount),
            _shapedLineScratch.AsSpan(0, lineCount),
            _shapeClusterScratch.AsSpan(0, text.Length),
            text.Length,
            requiresColorGlyph);
        return true;
    }

    private bool TryAnalyzeShapedText(ReadOnlySpan<char> text, DWRITE_READING_DIRECTION readingDirection)
    {
        if (_textAnalyzer == null || text.IsEmpty || text.Length > ushort.MaxValue)
        {
            return false;
        }

        fixed (char* textPtr = text)
        fixed (DWRITE_SCRIPT_ANALYSIS* scriptAnalysis = _shapeScriptScratch)
        fixed (byte* bidiLevels = _shapeBidiLevelScratch)
        {
            var locale = stackalloc char[6];
            locale[0] = 'e';
            locale[1] = 'n';
            locale[2] = '-';
            locale[3] = 'u';
            locale[4] = 's';
            locale[5] = '\0';

            var sourceVtbl = stackalloc void*[8];
            sourceVtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, Guid*, void**, HRESULT>)&TextAnalysisSourceQueryInterface;
            sourceVtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceAddRef;
            sourceVtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceRelease;
            sourceVtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextAtPosition;
            sourceVtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextBeforePosition;
            sourceVtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, DWRITE_READING_DIRECTION>)&TextAnalysisSourceGetParagraphReadingDirection;
            sourceVtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, ushort**, HRESULT>)&TextAnalysisSourceGetLocaleName;
            sourceVtbl[7] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, IDWriteNumberSubstitution**, HRESULT>)&TextAnalysisSourceGetNumberSubstitution;

            var sinkVtbl = stackalloc void*[7];
            sinkVtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, Guid*, void**, HRESULT>)&TextAnalysisSinkQueryInterface;
            sinkVtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint>)&TextAnalysisSinkAddRef;
            sinkVtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint>)&TextAnalysisSinkRelease;
            sinkVtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, DWRITE_SCRIPT_ANALYSIS*, HRESULT>)&TextAnalysisSinkSetScriptAnalysis;
            sinkVtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, DWRITE_LINE_BREAKPOINT*, HRESULT>)&TextAnalysisSinkSetLineBreakpoints;
            sinkVtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, byte, byte, HRESULT>)&TextAnalysisSinkSetBidiLevel;
            sinkVtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSinkShim*, uint, uint, IDWriteNumberSubstitution*, HRESULT>)&TextAnalysisSinkSetNumberSubstitution;

            var source = new TextAnalysisSourceShim
            {
                Vtbl = sourceVtbl,
                RefCount = 1,
                Text = textPtr,
                TextLength = (uint)text.Length,
                Locale = locale,
                ReadingDirection = readingDirection
            };
            var sink = new TextAnalysisSinkShim
            {
                Vtbl = sinkVtbl,
                RefCount = 1,
                TextLength = (uint)text.Length,
                ScriptAnalysis = scriptAnalysis,
                BidiLevels = bidiLevels
            };

            try
            {
                _textAnalyzer->AnalyzeScript((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
                _textAnalyzer->AnalyzeBidi((IDWriteTextAnalysisSource*)&source, 0, (uint)text.Length, (IDWriteTextAnalysisSink*)&sink);
                return true;
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape analysis failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }
        }
    }

    private bool HasRightToLeftBidiLevel(int textStart, int textLength)
    {
        var bidiLevels = _shapeBidiLevelScratch.AsSpan(textStart, textLength);
        foreach (var level in bidiLevels)
        {
            if ((level & 1) != 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryShapeTextRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        if (textLength == 0)
        {
            return true;
        }

        var textEnd = textStart + textLength;
        var position = textStart;
        while (position < textEnd)
        {
            if (GlyphAtlasTextCompositionHelpers.IsTab(text[position]))
            {
                if (!TryAppendShapedControlSegment(style, baseFontFace, position, glyphStart, ref segmentCount, GlyphAtlasTextCompositionHelpers.TabAdvanceSpaceCount))
                {
                    return false;
                }

                position++;
                continue;
            }

            if (GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(text[position]))
            {
                if (!TryAppendShapedControlSegment(style, baseFontFace, position, glyphStart, ref segmentCount, spaceCount: 1))
                {
                    return false;
                }

                position++;
                continue;
            }

            var segmentStart = position;
            while (position < textEnd && !GlyphAtlasTextCompositionHelpers.IsWrapWhitespace(text[position]))
            {
                position++;
            }

            if (!TryShapeTextSpan(text, segmentStart, position - segmentStart, style, baseFontFace, ref glyphStart, ref segmentCount))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryShapeTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        PromoteRtlSpanBaseLevel(text, textStart, textLength);
        if (!TryGetUniformBidiLevel(textStart, textLength, out var bidiLevel))
        {
            return TryShapeBidiLevelRuns(text, textStart, textLength, style, baseFontFace, ref glyphStart, ref segmentCount);
        }

        return TryShapeUniformBidiTextSpan(text, textStart, textLength, style, baseFontFace, bidiLevel, ref glyphStart, ref segmentCount);
    }

    private bool TryShapeBidiLevelRuns(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        var textEnd = textStart + textLength;
        var position = textStart;
        while (position < textEnd)
        {
            var runStart = position;
            var bidiLevel = _shapeBidiLevelScratch[position++];
            while (position < textEnd && _shapeBidiLevelScratch[position] == bidiLevel)
            {
                position++;
            }

            if (!TryShapeUniformBidiTextSpan(text, runStart, position - runStart, style, baseFontFace, bidiLevel, ref glyphStart, ref segmentCount))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryShapeUniformBidiTextSpan(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, byte bidiLevel, ref int glyphStart, ref int segmentCount)
    {
        var range = text.Slice(textStart, textLength);
        var initialGlyphStart = glyphStart;
        if (!TryShapeTextSegment(range, baseFontFace, style.FontSize, textStart, initialGlyphStart, _shapeScriptScratch[textStart], bidiLevel, out var glyphCount))
        {
            return false;
        }

        if (!HasMissingGlyph(initialGlyphStart, glyphCount))
        {
            if (segmentCount >= MaxShapedRunSegments)
            {
                return false;
            }

            _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(baseFontFace, style.FontSize, textStart, textLength, initialGlyphStart, glyphCount, ControlAdvance: 0, bidiLevel);
            if (!TryAssignShapedTextAdvances(textStart, textLength, initialGlyphStart, glyphCount))
            {
                return false;
            }

            glyphStart = initialGlyphStart + glyphCount;
            return true;
        }

        glyphStart = initialGlyphStart;
        return TryShapeSegmentedFallbackRange(text, textStart, textLength, style, baseFontFace, ref glyphStart, ref segmentCount);
    }

    private bool TryAppendShapedControlSegment(TextStyle style, CachedFontFace baseFontFace, int textStart, int glyphStart, ref int segmentCount, int spaceCount)
    {
        if (segmentCount >= MaxShapedRunSegments || !TryMeasureCharacterAdvance(baseFontFace, style.FontSize, ' ', out var spaceAdvance))
        {
            return false;
        }

        _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(
            baseFontFace,
            style.FontSize,
            textStart,
            TextLength: 1,
            glyphStart,
            GlyphCount: 0,
            spaceAdvance * spaceCount,
            _shapeBidiLevelScratch[textStart]);
        _shapedTextAdvanceScratch[textStart] = spaceAdvance * spaceCount;
        return true;
    }

    private bool TryShapeTextSegment(ReadOnlySpan<char> text, CachedFontFace fontFace, float fontEmSize, int textScratchStart, int glyphStart, DWRITE_SCRIPT_ANALYSIS scriptAnalysis, byte bidiLevel, out int glyphCount)
    {
        glyphCount = 0;
        var isRightToLeft = (bidiLevel & 1) != 0;

        fixed (char* textPtr = text)
        fixed (ushort* clusterMapBase = _shapeClusterScratch)
        fixed (DWRITE_SHAPING_TEXT_PROPERTIES* textPropsBase = _shapeTextPropsScratch)
        fixed (ushort* glyphIndicesBase = _shapeGlyphScratch)
        fixed (DWRITE_SHAPING_GLYPH_PROPERTIES* glyphPropsBase = _shapeGlyphPropsScratch)
        {
            var clusterMap = clusterMapBase + textScratchStart;
            var textProps = textPropsBase + textScratchStart;
            var glyphIndices = glyphIndicesBase + glyphStart;
            var glyphProps = glyphPropsBase + glyphStart;
            uint actualGlyphCount;
            try
            {
                _textAnalyzer->GetGlyphs(
                    new PCWSTR(textPtr),
                    (uint)text.Length,
                    fontFace.Face,
                    false,
                    isRightToLeft,
                    &scriptAnalysis,
                    default,
                    null,
                    null,
                    null,
                    0,
                    (uint)(_shapeGlyphScratch.Length - glyphStart),
                    clusterMap,
                    textProps,
                    glyphIndices,
                    glyphProps,
                    &actualGlyphCount);
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe GetGlyphs failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                return false;
            }

            if (actualGlyphCount == 0 || actualGlyphCount > _shapeGlyphScratch.Length - glyphStart)
            {
                return false;
            }

            fixed (float* advancesBase = _shapeAdvanceScratch)
            fixed (DWRITE_GLYPH_OFFSET* offsetsBase = _shapeOffsetScratch)
            {
                var advances = advancesBase + glyphStart;
                var offsets = offsetsBase + glyphStart;
                try
                {
                    _textAnalyzer->GetGlyphPlacements(
                        new PCWSTR(textPtr),
                        clusterMap,
                        textProps,
                        (uint)text.Length,
                        glyphIndices,
                        glyphProps,
                        actualGlyphCount,
                        fontFace.Face,
                        fontEmSize,
                        false,
                        isRightToLeft,
                        &scriptAnalysis,
                        default,
                        null,
                        null,
                        0,
                        advances,
                        offsets);
                }
                catch (COMException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe GetGlyphPlacements failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
                    return false;
                }
            }

            glyphCount = (int)actualGlyphCount;
            ProjectShapedGlyphs(glyphStart, glyphCount);
            return true;
        }
    }

    private bool TryShapeSegmentedFallbackRange(ReadOnlySpan<char> text, int textStart, int textLength, TextStyle style, CachedFontFace baseFontFace, ref int glyphStart, ref int segmentCount)
    {
        if (_fontFallback == null || textLength == 0 || text.Length > ushort.MaxValue)
        {
            return false;
        }

        var rangeSegmentStart = segmentCount;
        IDWriteFont* mappedFont = null;
        try
        {
            fixed (char* textPtr = text)
            fixed (char* baseFamilyName = ToDirectWriteFontFamily(style.FontFamily))
            {
                var locale = stackalloc char[6];
                locale[0] = 'e';
                locale[1] = 'n';
                locale[2] = '-';
                locale[3] = 'u';
                locale[4] = 's';
                locale[5] = '\0';

                var vtbl = stackalloc void*[8];
                vtbl[0] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, Guid*, void**, HRESULT>)&TextAnalysisSourceQueryInterface;
                vtbl[1] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceAddRef;
                vtbl[2] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint>)&TextAnalysisSourceRelease;
                vtbl[3] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextAtPosition;
                vtbl[4] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, ushort**, uint*, HRESULT>)&TextAnalysisSourceGetTextBeforePosition;
                vtbl[5] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, DWRITE_READING_DIRECTION>)&TextAnalysisSourceGetParagraphReadingDirection;
                vtbl[6] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, ushort**, HRESULT>)&TextAnalysisSourceGetLocaleName;
                vtbl[7] = (delegate* unmanaged[Stdcall]<TextAnalysisSourceShim*, uint, uint*, IDWriteNumberSubstitution**, HRESULT>)&TextAnalysisSourceGetNumberSubstitution;

                var source = new TextAnalysisSourceShim
                {
                    Vtbl = vtbl,
                    RefCount = 1,
                    Text = textPtr,
                    TextLength = (uint)text.Length,
                    Locale = locale,
                    ReadingDirection = DetermineParagraphReadingDirection(text)
                };

                var textPosition = (uint)textStart;
                var textEnd = (uint)(textStart + textLength);
                while (textPosition < textEnd)
                {
                    if (segmentCount >= MaxShapedRunSegments)
                    {
                        return false;
                    }

                    var mappedLength = 0u;
                    var scale = 1f;
                    _fontFallback->MapCharacters(
                        (IDWriteTextAnalysisSource*)&source,
                        textPosition,
                        textEnd - textPosition,
                        _fontCollection,
                        new PCWSTR(baseFamilyName),
                        ToDirectWriteFontWeight(style.FontWeight),
                        ToDirectWriteFontStyle(style.FontStyle),
                        ToDirectWriteFontStretch(style.FontStretch),
                        &mappedLength,
                        &mappedFont,
                        &scale);

                    if (mappedLength == 0 || mappedLength > textEnd - textPosition)
                    {
                        return false;
                    }

                    var fontFace = baseFontFace;
                    var fontEmSize = style.FontSize;
                    if (mappedFont != null)
                    {
                        if (scale <= 0 || !TryGetCachedFallbackFontFace(mappedFont, out fontFace))
                        {
                            return false;
                        }

                        fontEmSize *= scale;
                        mappedFont->Release();
                        mappedFont = null;
                    }

                    var segmentGlyphStart = glyphStart;
                    PromoteRtlSpanBaseLevel(text, (int)textPosition, (int)mappedLength);
                    var segmentBidiLevel = _shapeBidiLevelScratch[(int)textPosition];
                    if (!TryShapeTextSegment(
                        text.Slice((int)textPosition, (int)mappedLength),
                        fontFace,
                        fontEmSize,
                        (int)textPosition,
                        segmentGlyphStart,
                        _shapeScriptScratch[(int)textPosition],
                        segmentBidiLevel,
                        out var glyphCount))
                    {
                        return false;
                    }

                    _shapedSegmentScratch[segmentCount++] = new ShapedGlyphSegment(
                        fontFace,
                        fontEmSize,
                        (int)textPosition,
                        (int)mappedLength,
                        segmentGlyphStart,
                        glyphCount,
                        ControlAdvance: 0,
                        segmentBidiLevel);
                    if (!TryAssignShapedTextAdvances((int)textPosition, (int)mappedLength, segmentGlyphStart, glyphCount))
                    {
                        return false;
                    }

                    glyphStart = segmentGlyphStart + glyphCount;
                    textPosition += mappedLength;
                }
            }

            return segmentCount > rangeSegmentStart;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font fallback failed: 0x{unchecked((uint)ex.ErrorCode):X8}");
            return false;
        }
        catch (GlyphAtlasRecordException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] Shape probe font fallback skipped: {ex.Phase}");
            return false;
        }
        finally
        {
            if (mappedFont != null) mappedFont->Release();
        }
    }

    private bool TryGetCachedFallbackFontFace(IDWriteFont* font, out CachedFontFace fontFace)
    {
        fontFace = default!;
        IDWriteFontFace* face = null;
        IDWriteFontFace4* face4 = null;
        IUnknown* identity = null;
        try
        {
            var iid = IUnknownGuid;
            void* identityObject = null;
            font->QueryInterface(&iid, &identityObject).ThrowOnFailure();
            identity = (IUnknown*)identityObject;
            var key = (nint)identity;
            if (_fallbackFontFaces.TryGetValue(key, out fontFace!))
            {
                identity->Release();
                identity = null;
                return true;
            }

            font->CreateFontFace(&face);
            if (!GlyphAtlasTextCompositionHelpers.HasGlyphFontFaceResource(face != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.DirectWrite,
                    "D3D12GlyphAtlasTextRenderer.TryGetCachedFallbackFontFace found a missing DirectWrite font face.");
            }

            face->GetMetrics(out var metrics);
            face4 = TryQueryFontFace4(face);
            fontFace = new CachedFontFace(new FontFaceIdentity(_nextFontFaceIdentity++), face, metrics, face4, identity);
            _fallbackFontFaces.Add(key, fontFace);
            face = null;
            face4 = null;
            identity = null;
            return true;
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.TryGetCachedFallbackFontFace",
                ex);
        }
        finally
        {
            if (face4 != null) face4->Release();
            if (face != null) face->Release();
            if (identity != null) identity->Release();
        }
    }

    private static IDWriteFontFace4* TryQueryFontFace4(IDWriteFontFace* face)
    {
        if (face == null)
        {
            return null;
        }

        try
        {
            face->QueryInterface<IDWriteFontFace4>(out var face4).ThrowOnFailure();
            return face4;
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] DirectWrite font face4 query skipped: 0x{unchecked((uint)ex.ErrorCode):X8}");
            return null;
        }
    }

    private bool HasMissingGlyph(int glyphStart, int glyphCount)
    {
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        foreach (ref readonly var glyph in glyphs)
        {
            if (glyph.GlyphIndex == 0)
            {
                return true;
            }
        }

        return false;
    }

    private void PromoteRtlSpanBaseLevel(ReadOnlySpan<char> text, int textStart, int textLength)
    {
        if (textLength > 0 && IsRtlOnlyStrongSpan(text.Slice(textStart, textLength)))
        {
            _shapeBidiLevelScratch.AsSpan(textStart, textLength).Fill(1);
        }
    }

    private static bool IsRtlOnlyStrongSpan(ReadOnlySpan<char> text)
    {
        var hasRightToLeftStrong = false;
        foreach (var character in text)
        {
            if (GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character))
            {
                hasRightToLeftStrong = true;
                continue;
            }

            if (char.IsLetter(character))
            {
                return false;
            }
        }

        return hasRightToLeftStrong;
    }

    private float ComputeShapedGlyphAdvance(int glyphStart, int glyphCount)
    {
        var width = 0f;
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        foreach (ref readonly var glyph in glyphs)
        {
            width += glyph.Advance;
        }

        return width;
    }

    private bool TryGetUniformBidiLevel(int textStart, int textLength, out byte bidiLevel)
    {
        bidiLevel = 0;
        if (textLength == 0)
        {
            return true;
        }

        bidiLevel = _shapeBidiLevelScratch[textStart];
        var end = textStart + textLength;
        for (var i = textStart + 1; i < end; i++)
        {
            if (_shapeBidiLevelScratch[i] != bidiLevel)
            {
                return false;
            }
        }

        return true;
    }

    private float ComputeShapedLineAdvance(int segmentStart, int segmentCount)
    {
        var width = 0f;
        var segments = _shapedSegmentScratch.AsSpan(segmentStart, segmentCount);
        foreach (ref readonly var segment in segments)
        {
            width += ComputeShapedSegmentAdvance(segment);
        }

        return width;
    }

    private float ComputeShapedSegmentAdvance(ShapedGlyphSegment segment) => segment.ControlAdvance + ComputeShapedGlyphAdvance(segment.GlyphStart, segment.GlyphCount);

    private bool TryAssignShapedTextAdvances(int textStart, int textLength, int glyphStart, int glyphCount)
    {
        if ((_shapeBidiLevelScratch[textStart] & 1) != 0)
        {
            _shapedTextAdvanceScratch[textStart] = ComputeShapedGlyphAdvance(glyphStart, glyphCount);
            _shapedTextAdvanceScratch.AsSpan(textStart + 1, textLength - 1).Clear();
            return true;
        }

        for (var offset = 0; offset < textLength; offset++)
        {
            var textIndex = textStart + offset;
            var relativeGlyphStart = _shapeClusterScratch[textIndex];
            if (relativeGlyphStart > glyphCount)
            {
                return false;
            }

            if (offset > 0 && relativeGlyphStart == _shapeClusterScratch[textIndex - 1])
            {
                _shapedTextAdvanceScratch[textIndex] = 0;
                continue;
            }

            var relativeGlyphEnd = glyphCount;
            for (var nextOffset = offset + 1; nextOffset < textLength; nextOffset++)
            {
                var nextRelativeGlyphStart = _shapeClusterScratch[textStart + nextOffset];
                if (nextRelativeGlyphStart == relativeGlyphStart)
                {
                    continue;
                }

                if (nextRelativeGlyphStart < relativeGlyphStart || nextRelativeGlyphStart > glyphCount)
                {
                    return false;
                }

                relativeGlyphEnd = nextRelativeGlyphStart;
                break;
            }

            _shapedTextAdvanceScratch[textIndex] = ComputeShapedGlyphAdvance(glyphStart + relativeGlyphStart, relativeGlyphEnd - relativeGlyphStart);
        }

        return true;
    }

    private bool TryBuildShapedLinesFromLayout(ReadOnlySpan<char> text, int segmentCount, int plannedLineCount, out int lineCount)
    {
        lineCount = 0;
        var segmentIndex = 0;
        for (var i = 0; i < plannedLineCount; i++)
        {
            var plannedLine = _shapedLayoutLineScratch[i];
            while (segmentIndex < segmentCount && _shapedSegmentScratch[segmentIndex].TextEnd <= plannedLine.Start)
            {
                segmentIndex++;
            }

            var lineSegmentStart = segmentIndex;
            var lineGlyphStart = segmentIndex < segmentCount ? _shapedSegmentScratch[segmentIndex].GlyphStart : 0;
            var lineGlyphEnd = lineGlyphStart;
            var hasGlyphSegment = false;
            var firstGlyphSegmentBidiLevel = (byte)0;
            while (segmentIndex < segmentCount && _shapedSegmentScratch[segmentIndex].TextStart < plannedLine.End)
            {
                var segment = _shapedSegmentScratch[segmentIndex];
                if (segment.TextStart < plannedLine.Start || segment.TextEnd > plannedLine.End)
                {
                    return false;
                }

                if (segment.GlyphCount > 0)
                {
                    if (!hasGlyphSegment)
                    {
                        firstGlyphSegmentBidiLevel = segment.BidiLevel;
                    }

                    hasGlyphSegment = true;
                }

                lineGlyphEnd = Math.Max(lineGlyphEnd, segment.GlyphStart + segment.GlyphCount);
                segmentIndex++;
            }

            var lineSegmentCount = segmentIndex - lineSegmentStart;
            var lineIsRightToLeft = TryDetermineRangeReadingDirection(text, plannedLine.Start, plannedLine.End, out var lineDirection)
                ? lineDirection == DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT
                : hasGlyphSegment && (firstGlyphSegmentBidiLevel & 1) != 0;
            if (!lineIsRightToLeft)
            {
                ApplyShapedLineVisualOrder(lineSegmentStart, lineSegmentCount);
            }

            _shapedLineScratch[lineCount++] = new ShapedGlyphLine(
                lineSegmentStart,
                lineSegmentCount,
                lineGlyphStart,
                lineGlyphEnd - lineGlyphStart,
                plannedLine.Width,
                lineIsRightToLeft ? (byte)1 : (byte)0);
        }

        return true;
    }

    private void ApplyShapedLineVisualOrder(int segmentStart, int segmentCount)
    {
        if (segmentCount <= 1)
        {
            return;
        }

        var segmentEnd = segmentStart + segmentCount;
        var maxLevel = 0;
        var lowestOddLevel = int.MaxValue;
        for (var i = segmentStart; i < segmentEnd; i++)
        {
            var level = _shapedSegmentScratch[i].BidiLevel;
            maxLevel = Math.Max(maxLevel, level);
            if ((level & 1) != 0)
            {
                lowestOddLevel = Math.Min(lowestOddLevel, level);
            }
        }

        if (maxLevel == 0)
        {
            return;
        }

        if (lowestOddLevel == int.MaxValue)
        {
            lowestOddLevel = (maxLevel & 1) == 0 ? maxLevel - 1 : maxLevel;
        }

        for (var level = maxLevel; level >= lowestOddLevel; level--)
        {
            var index = segmentStart;
            while (index < segmentEnd)
            {
                while (index < segmentEnd && _shapedSegmentScratch[index].BidiLevel < level)
                {
                    index++;
                }

                var reverseStart = index;
                while (index < segmentEnd && _shapedSegmentScratch[index].BidiLevel >= level)
                {
                    index++;
                }

                ReverseShapedSegments(reverseStart, index - 1);
            }
        }
    }

    private void ReverseShapedSegments(int start, int end)
    {
        while (start < end)
        {
            (_shapedSegmentScratch[start], _shapedSegmentScratch[end]) = (_shapedSegmentScratch[end], _shapedSegmentScratch[start]);
            start++;
            end--;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceQueryInterface(TextAnalysisSourceShim* source, Guid* riid, void** ppvObject)
    {
        if (ppvObject == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var iUnknown = IUnknownGuid;
        if (riid != null && (*riid == iUnknown || *riid == IDWriteTextAnalysisSource.IID_Guid))
        {
            *ppvObject = source;
            source->RefCount++;
            return (HRESULT)0;
        }

        *ppvObject = null;
        return (HRESULT)unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSourceAddRef(TextAnalysisSourceShim* source) => (uint)++source->RefCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSourceRelease(TextAnalysisSourceShim* source)
    {
        if (source->RefCount > 0)
        {
            source->RefCount--;
        }

        return (uint)source->RefCount;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetTextAtPosition(TextAnalysisSourceShim* source, uint textPosition, ushort** textString, uint* textLength)
    {
        if (textString == null || textLength == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        if (textPosition >= source->TextLength)
        {
            *textString = null;
            *textLength = 0;
            return (HRESULT)0;
        }

        *textString = (ushort*)(source->Text + textPosition);
        *textLength = source->TextLength - textPosition;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetTextBeforePosition(TextAnalysisSourceShim* source, uint textPosition, ushort** textString, uint* textLength)
    {
        if (textString == null || textLength == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        if (textPosition == 0 || textPosition > source->TextLength)
        {
            *textString = null;
            *textLength = 0;
            return (HRESULT)0;
        }

        *textString = (ushort*)source->Text;
        *textLength = textPosition;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static DWRITE_READING_DIRECTION TextAnalysisSourceGetParagraphReadingDirection(TextAnalysisSourceShim* source) => source->ReadingDirection;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetLocaleName(TextAnalysisSourceShim* source, uint textPosition, uint* textLength, ushort** localeName)
    {
        if (textLength == null || localeName == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        *textLength = textPosition < source->TextLength ? source->TextLength - textPosition : 0;
        *localeName = (ushort*)source->Locale;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSourceGetNumberSubstitution(TextAnalysisSourceShim* source, uint textPosition, uint* textLength, IDWriteNumberSubstitution** numberSubstitution)
    {
        if (textLength == null || numberSubstitution == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        *textLength = textPosition < source->TextLength ? source->TextLength - textPosition : 0;
        *numberSubstitution = null;
        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkQueryInterface(TextAnalysisSinkShim* sink, Guid* riid, void** ppvObject)
    {
        if (ppvObject == null)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var iUnknown = IUnknownGuid;
        if (riid != null && (*riid == iUnknown || *riid == IDWriteTextAnalysisSink.IID_Guid))
        {
            *ppvObject = sink;
            sink->RefCount++;
            return (HRESULT)0;
        }

        *ppvObject = null;
        return (HRESULT)unchecked((int)0x80004002);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSinkAddRef(TextAnalysisSinkShim* sink) => (uint)++sink->RefCount;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint TextAnalysisSinkRelease(TextAnalysisSinkShim* sink)
    {
        if (sink->RefCount > 0)
        {
            sink->RefCount--;
        }

        return (uint)sink->RefCount;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetScriptAnalysis(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_SCRIPT_ANALYSIS* scriptAnalysis)
    {
        if (scriptAnalysis == null || sink->ScriptAnalysis == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        var end = (int)(textPosition + textLength);
        for (var i = start; i < end; i++)
        {
            sink->ScriptAnalysis[i] = *scriptAnalysis;
        }

        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetLineBreakpoints(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, DWRITE_LINE_BREAKPOINT* lineBreakpoints) => (HRESULT)0;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetBidiLevel(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, byte explicitLevel, byte resolvedLevel)
    {
        if (sink->BidiLevels == null || textPosition > sink->TextLength || textLength > sink->TextLength - textPosition)
        {
            return (HRESULT)unchecked((int)0x80004003);
        }

        var start = (int)textPosition;
        var end = (int)(textPosition + textLength);
        for (var i = start; i < end; i++)
        {
            sink->BidiLevels[i] = resolvedLevel;
        }

        return (HRESULT)0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static HRESULT TextAnalysisSinkSetNumberSubstitution(TextAnalysisSinkShim* sink, uint textPosition, uint textLength, IDWriteNumberSubstitution* numberSubstitution) => (HRESULT)0;

    private void ProjectShapedGlyphs(int glyphStart, int glyphCount)
    {
        var glyphs = _shapedGlyphScratch.AsSpan(glyphStart, glyphCount);
        var glyphIndices = _shapeGlyphScratch.AsSpan(glyphStart, glyphCount);
        var glyphProps = _shapeGlyphPropsScratch.AsSpan(glyphStart, glyphCount);
        var advances = _shapeAdvanceScratch.AsSpan(glyphStart, glyphCount);
        var offsets = _shapeOffsetScratch.AsSpan(glyphStart, glyphCount);
        for (var i = 0; i < glyphCount; i++)
        {
            glyphs[i] = ShapedGlyph.FromDirectWrite(glyphIndices[i], advances[i], offsets[i], glyphProps[i]);
        }
    }

    private void EnsureShapeScratch(int textLength, int glyphCapacity, int segmentCapacity, int lineCapacity)
    {
        var resized = false;
        if (_shapeClusterScratch.Length < textLength)
        {
            _shapeClusterScratch = new ushort[textLength];
            _shapeTextPropsScratch = new DWRITE_SHAPING_TEXT_PROPERTIES[textLength];
            _shapeScriptScratch = new DWRITE_SCRIPT_ANALYSIS[textLength];
            _shapeBidiLevelScratch = new byte[textLength];
            resized = true;
        }

        if (_shapeGlyphScratch.Length < glyphCapacity)
        {
            _shapeGlyphScratch = new ushort[glyphCapacity];
            _shapeGlyphPropsScratch = new DWRITE_SHAPING_GLYPH_PROPERTIES[glyphCapacity];
            _shapeAdvanceScratch = new float[glyphCapacity];
            _shapeOffsetScratch = new DWRITE_GLYPH_OFFSET[glyphCapacity];
            _shapedGlyphScratch = new ShapedGlyph[glyphCapacity];
            resized = true;
        }

        if (_shapedSegmentScratch.Length < segmentCapacity)
        {
            _shapedSegmentScratch = new ShapedGlyphSegment[segmentCapacity];
            resized = true;
        }

        if (_shapedLineScratch.Length < lineCapacity)
        {
            _shapedLineScratch = new ShapedGlyphLine[lineCapacity];
            resized = true;
        }

        if (_shapedLayoutLineScratch.Length < lineCapacity)
        {
            _shapedLayoutLineScratch = new GlyphAtlasLayoutLine[lineCapacity];
            resized = true;
        }

        if (_shapedTextAdvanceScratch.Length < textLength)
        {
            _shapedTextAdvanceScratch = new float[textLength];
            resized = true;
        }

        if (resized)
        {
            _shapeScratchResizeCount++;
        }
    }

    private int GetShapeScratchByteCount()
    {
        return checked(
            _shapeClusterScratch.Length * sizeof(ushort)
            + _shapeTextPropsScratch.Length * sizeof(DWRITE_SHAPING_TEXT_PROPERTIES)
            + _shapeScriptScratch.Length * sizeof(DWRITE_SCRIPT_ANALYSIS)
            + _shapeBidiLevelScratch.Length
            + _shapeGlyphScratch.Length * sizeof(ushort)
            + _shapeGlyphPropsScratch.Length * sizeof(DWRITE_SHAPING_GLYPH_PROPERTIES)
            + _shapeAdvanceScratch.Length * sizeof(float)
            + _shapeOffsetScratch.Length * sizeof(DWRITE_GLYPH_OFFSET)
            + _shapedGlyphScratch.Length * sizeof(ShapedGlyph)
            + _shapedSegmentScratch.Length * (IntPtr.Size + sizeof(float) * 2 + sizeof(int) * 4)
            + _shapedLineScratch.Length * (sizeof(int) * 4 + sizeof(float))
            + _shapedLayoutLineScratch.Length * (sizeof(int) * 2 + sizeof(float))
            + _shapedTextAdvanceScratch.Length * sizeof(float));
    }

    private Span<byte> RentClearTypeScratch(int byteCount)
    {
        if (_clearTypeScratch.Length < byteCount)
        {
            _clearTypeScratch = new byte[byteCount];
            _rasterScratchResizeCount++;
        }

        return _clearTypeScratch.AsSpan(0, byteCount);
    }

    private Span<byte> RentGrayscaleScratch(int byteCount)
    {
        if (_grayscaleScratch.Length < byteCount)
        {
            _grayscaleScratch = new byte[byteCount];
            _rasterScratchResizeCount++;
        }

        return _grayscaleScratch.AsSpan(0, byteCount);
    }

    private static float ComputeGlyphAdvance(CachedFontFace fontFace, float emSize, ushort glyphIndex)
    {
        var glyphIndices = stackalloc ushort[1];
        glyphIndices[0] = glyphIndex;
        var glyphMetrics = stackalloc DWRITE_GLYPH_METRICS[1];
        try
        {
            fontFace.Face->GetDesignGlyphMetrics(new ReadOnlySpan<ushort>(glyphIndices, 1), new Span<DWRITE_GLYPH_METRICS>(glyphMetrics, 1), false);
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.DirectWrite,
                "D3D12GlyphAtlasTextRenderer.GetDesignGlyphMetrics",
                ex);
        }

        return glyphMetrics[0].advanceWidth * emSize / fontFace.Metrics.designUnitsPerEm;
    }

    private static bool CanAllocateGlyph(GlyphAtlasPage page, int width, int height)
    {
        if (width + AtlasPadding * 2 > AtlasWidth || height + AtlasPadding * 2 > AtlasHeight)
        {
            return false;
        }

        var nextX = page.NextX;
        var nextY = page.NextY;
        var rowHeight = page.RowHeight;
        if (nextX + width + AtlasPadding > AtlasWidth)
        {
            nextX = AtlasPadding;
            nextY += rowHeight + AtlasPadding;
        }

        return nextY + height + AtlasPadding <= AtlasHeight;
    }

    private static bool TryAllocateGlyph(GlyphAtlasPage page, int width, int height, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!CanAllocateGlyph(page, width, height))
        {
            return false;
        }

        if (page.NextX + width + AtlasPadding > AtlasWidth)
        {
            page.NextX = AtlasPadding;
            page.NextY += page.RowHeight + AtlasPadding;
            page.RowHeight = 0;
        }

        if (page.NextY + height + AtlasPadding > AtlasHeight)
        {
            return false;
        }

        x = page.NextX;
        y = page.NextY;
        page.NextX += width + AtlasPadding;
        page.RowHeight = Math.Max(page.RowHeight, height);
        page.UsedPixels = checked(page.UsedPixels + width * height);
        page.AllocatedPixels = Math.Max(page.AllocatedPixels, page.ComputeAllocatedPixels());
        return true;
    }

    private static void CopyGlyphToAtlas(GlyphAtlasPage page, ReadOnlySpan<byte> glyphPixels, int width, int height, int atlasX, int atlasY)
    {
        var rowPitch = page.RowPitch;
        var bytesPerPixel = page.BytesPerPixel;
        var rowBytes = width * bytesPerPixel;
        var atlasByteX = atlasX * bytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            glyphPixels.Slice(row * rowBytes, rowBytes).CopyTo(page.Pixels.AsSpan((atlasY + row) * rowPitch + atlasByteX, rowBytes));
        }
    }

    private static void MarkAtlasDirty(GlyphAtlasPage page, int x, int y, int width, int height)
    {
        var dirtyRect = GlyphAtlasTextCompositionHelpers.MergeDirtyRect(
            page.IsDirty,
            page.DirtyLeft,
            page.DirtyTop,
            page.DirtyRight,
            page.DirtyBottom,
            x,
            y,
            width,
            height);
        page.IsDirty = dirtyRect.HasDirtyRect;
        page.DirtyLeft = dirtyRect.Left;
        page.DirtyTop = dirtyRect.Top;
        page.DirtyRight = dirtyRect.Right;
        page.DirtyBottom = dirtyRect.Bottom;
    }

    private static void ResetAtlasDirtyRect(GlyphAtlasPage page)
    {
        page.IsDirty = false;
        page.DirtyLeft = AtlasWidth;
        page.DirtyTop = AtlasHeight;
        page.DirtyRight = 0;
        page.DirtyBottom = 0;
    }

    private static float ComputeLineHeight(DWRITE_FONT_METRICS metrics, float emSize)
    {
        var scale = emSize / metrics.designUnitsPerEm;
        return (metrics.ascent + metrics.descent) * scale;
    }

    private static float ComputeFirstBaselineY(D3D12TextRun textRun, TextStyle style, DWRITE_FONT_METRICS metrics, float emSize, float lineHeight, int lineCount)
    {
        var scale = emSize / metrics.designUnitsPerEm;
        var ascent = metrics.ascent * scale;
        return ComputeFirstBaselineY(textRun, style, ascent, lineHeight, lineCount);
    }

    private static float ComputeFirstBaselineY(D3D12TextRun textRun, TextStyle style, float ascent, float lineHeight, int lineCount)
    {
        return GlyphAtlasTextCompositionHelpers.ComputeFirstBaselineY(textRun.Y, textRun.Height, style.VerticalAlignment, ascent, lineHeight, lineCount);
    }

    private static void AppendQuad(
        Vertex[] vertices,
        ref int vertexCount,
        float x1,
        float y1,
        float x2,
        float y2,
        GlyphEntry glyph,
        Vector4 color,
        int viewportWidth,
        int viewportHeight)
    {
        var p1 = ToNdc(x1, y1, viewportWidth, viewportHeight);
        var p2 = ToNdc(x2, y1, viewportWidth, viewportHeight);
        var p3 = ToNdc(x1, y2, viewportWidth, viewportHeight);
        var p4 = ToNdc(x2, y2, viewportWidth, viewportHeight);
        vertices[vertexCount++] = new Vertex { Position = p1, TexCoord = new Vector2(glyph.U1, glyph.V1), Color = color };
        vertices[vertexCount++] = new Vertex { Position = p2, TexCoord = new Vector2(glyph.U2, glyph.V1), Color = color };
        vertices[vertexCount++] = new Vertex { Position = p3, TexCoord = new Vector2(glyph.U1, glyph.V2), Color = color };
        vertices[vertexCount++] = new Vertex { Position = p2, TexCoord = new Vector2(glyph.U2, glyph.V1), Color = color };
        vertices[vertexCount++] = new Vertex { Position = p4, TexCoord = new Vector2(glyph.U2, glyph.V2), Color = color };
        vertices[vertexCount++] = new Vertex { Position = p3, TexCoord = new Vector2(glyph.U1, glyph.V2), Color = color };
    }

    private static Vector2 ToNdc(float x, float y, int viewportWidth, int viewportHeight)
    {
        return new Vector2(
            (x / viewportWidth) * 2f - 1f,
            1f - (y / viewportHeight) * 2f);
    }

    private static int AlignUp(int value, int alignment)
    {
        return ((value + alignment - 1) / alignment) * alignment;
    }

    private static int GetAtlasBytesPerPixel(GlyphAtlasPageFormat format) =>
        format switch
        {
            GlyphAtlasPageFormat.Bgra => BgraAtlasBytesPerPixel,
            _ => AlphaAtlasBytesPerPixel
        };

    private static int GetAtlasRowPitch(GlyphAtlasPageFormat format) => AtlasWidth * GetAtlasBytesPerPixel(format);

    private static int GetAtlasPixelBytes(GlyphAtlasPageFormat format) => checked(GetAtlasRowPitch(format) * AtlasHeight);

    private static DXGI_FORMAT GetDxgiFormat(GlyphAtlasPageFormat format) =>
        format switch
        {
            GlyphAtlasPageFormat.Bgra => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            _ => DXGI_FORMAT.DXGI_FORMAT_R8_UNORM
        };

    private void UploadVertices(ReadOnlySpan<Vertex> vertices, int frameResourceIndex)
    {
        var uploadSlot = frameResourceIndex % UploadFrameCount;
        var vbuf = _vbufs[uploadSlot];
        if (!GlyphAtlasTextCompositionHelpers.HasGlyphVertexUploadResource(vbuf != null))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.VertexBufferMap,
                "D3D12GlyphAtlasTextRenderer.UploadVertices found a missing vertex upload buffer.");
        }

        void* mapped = null;
        try
        {
            vbuf->Map(0, null, &mapped);
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.VertexBufferMap,
                "D3D12GlyphAtlasTextRenderer.Map(vertex buffer)",
                ex);
        }

        if (mapped == null)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.VertexBufferMap,
                "D3D12GlyphAtlasTextRenderer.Map(vertex buffer) returned null.");
        }

        try
        {
            vertices.CopyTo(new Span<Vertex>(mapped, MaxGlyphVertices));
        }
        finally
        {
            vbuf->Unmap(0, null);
        }
    }

    private void UploadAtlas(ID3D12GraphicsCommandList* list, GlyphAtlasPage page, int frameResourceIndex)
    {
        var dirtyWidth = page.DirtyRight - page.DirtyLeft;
        var dirtyHeight = page.DirtyBottom - page.DirtyTop;
        if (dirtyWidth <= 0 || dirtyHeight <= 0)
        {
            ResetAtlasDirtyRect(page);
            return;
        }

        var uploadSlot = frameResourceIndex % UploadFrameCount;
        var upload = page.Uploads[uploadSlot];
        if (!GlyphAtlasTextCompositionHelpers.HasAtlasUploadResources(page.Texture != null, upload != null))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasUploadMap,
                "D3D12GlyphAtlasTextRenderer.UploadAtlas found a missing atlas texture or upload buffer.");
        }

        var bytesPerPixel = page.BytesPerPixel;
        var dirtyRowBytes = dirtyWidth * bytesPerPixel;
        var uploadRowPitch = AlignUp(dirtyRowBytes, TextureDataPitchAlignment);
        var uploadBytes = uploadRowPitch * dirtyHeight;
        void* mapped = null;
        try
        {
            upload->Map(0, null, &mapped);
        }
        catch (COMException ex)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasUploadMap,
                "D3D12GlyphAtlasTextRenderer.Map(atlas upload buffer)",
                ex);
        }

        if (mapped == null)
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasUploadMap,
                "D3D12GlyphAtlasTextRenderer.Map(atlas upload buffer) returned null.");
        }

        try
        {
            var destination = new Span<byte>(mapped, uploadBytes);
            for (var row = 0; row < dirtyHeight; row++)
            {
                page.Pixels.AsSpan((page.DirtyTop + row) * page.RowPitch + page.DirtyLeft * bytesPerPixel, dirtyRowBytes)
                    .CopyTo(destination.Slice(row * uploadRowPitch, dirtyRowBytes));
            }
        }
        finally
        {
            upload->Unmap(0, null);
        }

        var toCopyDest = page.TransitionTexture(D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
        list->ResourceBarrier(1, &toCopyDest);

        var src = new D3D12_TEXTURE_COPY_LOCATION
        {
            pResource = upload,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT
        };
        src.Anonymous.PlacedFootprint = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT
        {
            Offset = 0,
            Footprint = new D3D12_SUBRESOURCE_FOOTPRINT
            {
                Format = page.DxgiFormat,
                Width = (uint)dirtyWidth,
                Height = (uint)dirtyHeight,
                Depth = 1,
                RowPitch = (uint)uploadRowPitch
            }
        };

        var dst = new D3D12_TEXTURE_COPY_LOCATION
        {
            pResource = page.Texture,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX
        };
        dst.Anonymous.SubresourceIndex = 0;
        list->CopyTextureRegion(dst, (uint)page.DirtyLeft, (uint)page.DirtyTop, 0, src, null);

        var toShaderResource = page.TransitionTexture(D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        list->ResourceBarrier(1, &toShaderResource);

        ResetAtlasDirtyRect(page);
        _diagnostics = _diagnostics.WithUploadedBytes(uploadBytes);
    }

    private void DrawGlyphs(ID3D12GraphicsCommandList* list, GlyphFrame frame, int viewportWidth, int viewportHeight, int frameResourceIndex)
    {
        if (!GlyphAtlasTextCompositionHelpers.HasGlyphPipelineResources(_pso != null && _bgraPso != null, _rootSig != null))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.Pipeline,
                "D3D12GlyphAtlasTextRenderer.DrawGlyphs found a missing pipeline state or root signature.");
        }

        var viewport = new D3D12_VIEWPORT { Width = viewportWidth, Height = viewportHeight, MaxDepth = 1.0f };
        list->RSSetViewports(1, &viewport);

        list->SetGraphicsRootSignature(_rootSig);
        list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var vbv = _vbvs[frameResourceIndex % UploadFrameCount];
        list->IASetVertexBuffers(0, 1, &vbv);

        ID3D12PipelineState* activePso = null;
        for (var i = 0; i < frame.BatchCount; i++)
        {
            var batch = _batches[i];
            var page = ResolveDrawBatchPage(batch.Page);
            var batchPso = SelectPipelineState(page.Format);
            if (batchPso != activePso)
            {
                list->SetPipelineState(batchPso);
                activePso = batchPso;
            }

            var heap = page.SrvHeap;
            if (!GlyphAtlasTextCompositionHelpers.HasAtlasDrawResources(heap != null))
            {
                throw CreateRecordException(
                    GlyphAtlasRecordFailurePhase.AtlasDraw,
                    "D3D12GlyphAtlasTextRenderer.DrawGlyphs found a missing atlas SRV heap.");
            }

            list->SetDescriptorHeaps(1, &heap);
            list->SetGraphicsRootDescriptorTable(0, page.SrvHeap->GetGPUDescriptorHandleForHeapStart());
            var scissor = ToRect(batch.Scissor);
            list->RSSetScissorRects(1, &scissor);
            list->DrawInstanced((uint)batch.VertexCount, 1, (uint)batch.StartVertex, 0);
        }
    }

    private ID3D12PipelineState* SelectPipelineState(GlyphAtlasPageFormat format) =>
        format switch
        {
            GlyphAtlasPageFormat.Bgra => _bgraPso,
            _ => _pso
        };

    private GlyphAtlasPage ResolveDrawBatchPage(GlyphAtlasPageHandle pageHandle)
    {
        if (!TryResolveAtlasPage(pageHandle, out var page))
        {
            throw CreateRecordException(
                GlyphAtlasRecordFailurePhase.AtlasPage,
                "D3D12GlyphAtlasTextRenderer.DrawGlyphs found a stale glyph atlas page handle.");
        }

        return page;
    }

    private static RECT ToRect(IntegerScissorRect scissor)
    {
        return new RECT
        {
            left = scissor.Left,
            top = scissor.Top,
            right = scissor.Right,
            bottom = scissor.Bottom
        };
    }

    private static D3D12_RESOURCE_BARRIER Transition(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
    {
        var barrier = new D3D12_RESOURCE_BARRIER
        {
            Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION
        };
        barrier.Anonymous.Transition.pResource = resource;
        barrier.Anonymous.Transition.StateBefore = before;
        barrier.Anonymous.Transition.StateAfter = after;
        barrier.Anonymous.Transition.Subresource = 0xFFFFFFFF;
        return barrier;
    }

    private static void RunInitializationPhase(GlyphAtlasInitializationPhase phase, Action action)
    {
        try
        {
            action();
        }
        catch (GlyphAtlasInitializationException)
        {
            throw;
        }
        catch (COMException ex)
        {
            throw GlyphAtlasTextCompositionHelpers.WrapInitializationException(phase, ex);
        }
        catch (InvalidOperationException ex)
        {
            throw GlyphAtlasTextCompositionHelpers.WrapInitializationException(phase, ex);
        }
    }

    private void CreateRootSignature()
    {
        var range = new D3D12_DESCRIPTOR_RANGE
        {
            RangeType = D3D12_DESCRIPTOR_RANGE_TYPE.D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
            NumDescriptors = 1,
            BaseShaderRegister = 0,
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0
        };

        var rootParameter = new D3D12_ROOT_PARAMETER
        {
            ParameterType = D3D12_ROOT_PARAMETER_TYPE.D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE,
            ShaderVisibility = D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_PIXEL
        };
        rootParameter.Anonymous.DescriptorTable.NumDescriptorRanges = 1;
        rootParameter.Anonymous.DescriptorTable.pDescriptorRanges = &range;

        var sampler = new D3D12_STATIC_SAMPLER_DESC
        {
            Filter = D3D12_FILTER.D3D12_FILTER_MIN_MAG_MIP_LINEAR,
            AddressU = D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressV = D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            AddressW = D3D12_TEXTURE_ADDRESS_MODE.D3D12_TEXTURE_ADDRESS_MODE_CLAMP,
            ComparisonFunc = D3D12_COMPARISON_FUNC.D3D12_COMPARISON_FUNC_ALWAYS,
            MinLOD = 0,
            MaxLOD = float.MaxValue,
            ShaderRegister = 0,
            RegisterSpace = 0,
            ShaderVisibility = D3D12_SHADER_VISIBILITY.D3D12_SHADER_VISIBILITY_PIXEL
        };

        var desc = new D3D12_ROOT_SIGNATURE_DESC
        {
            NumParameters = 1,
            pParameters = &rootParameter,
            NumStaticSamplers = 1,
            pStaticSamplers = &sampler,
            Flags = D3D12_ROOT_SIGNATURE_FLAGS.D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT
        };

        ID3DBlob* sig = null;
        ID3DBlob* err = null;
        try
        {
            try
            {
                PInvoke.D3D12SerializeRootSignature(desc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1, &sig, &err);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.D3D12SerializeRootSignature", ex);
            }

            RequirePointer(sig, "D3D12GlyphAtlasTextRenderer.D3D12SerializeRootSignature returned a null signature blob.");

            void* obj = null;
            try
            {
                _device->CreateRootSignature(0, sig->GetBufferPointer(), sig->GetBufferSize(), typeof(ID3D12RootSignature).GUID, out obj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateRootSignature", ex);
            }

            if (obj == null)
            {
                throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateRootSignature returned a null root signature.");
            }

            _rootSig = (ID3D12RootSignature*)obj;
        }
        finally
        {
            if (sig != null) sig->Release();
            if (err != null) err->Release();
        }
    }

    private void CreatePSO()
    {
        if (_vertexShaderBytecode.Length == 0 || _pixelShaderBytecode.Length == 0 || _bgraPixelShaderBytecode.Length == 0)
        {
            throw new InvalidOperationException("Glyph atlas embedded shader bytecode is empty.");
        }

        _pso = CreateGlyphPipelineState(_pixelShaderBytecode, "D3D12GlyphAtlasTextRenderer.CreateGraphicsPipelineState(alpha)");
        _bgraPso = CreateGlyphPipelineState(_bgraPixelShaderBytecode, "D3D12GlyphAtlasTextRenderer.CreateGraphicsPipelineState(bgra)");
    }

    private ID3D12PipelineState* CreateGlyphPipelineState(byte[] pixelShaderBytecode, string context)
    {
        fixed (byte* vs = _vertexShaderBytecode)
        fixed (byte* ps = pixelShaderBytecode)
        fixed (byte* posBytes = "POSITION"u8)
        fixed (byte* texBytes = "TEXCOORD"u8)
        fixed (byte* colBytes = "COLOR"u8)
        {
            var elems = stackalloc D3D12_INPUT_ELEMENT_DESC[3];
            elems[0] = new D3D12_INPUT_ELEMENT_DESC { SemanticName = (PCSTR)posBytes, Format = DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT };
            elems[1] = new D3D12_INPUT_ELEMENT_DESC { SemanticName = (PCSTR)texBytes, Format = DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT, AlignedByteOffset = 8 };
            elems[2] = new D3D12_INPUT_ELEMENT_DESC { SemanticName = (PCSTR)colBytes, Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT, AlignedByteOffset = 16 };

            var desc = new D3D12_GRAPHICS_PIPELINE_STATE_DESC();
            desc.pRootSignature = _rootSig;
            desc.InputLayout.pInputElementDescs = elems;
            desc.InputLayout.NumElements = 3;
            desc.VS.pShaderBytecode = vs;
            desc.VS.BytecodeLength = (nuint)_vertexShaderBytecode.Length;
            desc.PS.pShaderBytecode = ps;
            desc.PS.BytecodeLength = (nuint)pixelShaderBytecode.Length;
            desc.BlendState.RenderTarget._0.BlendEnable = true;
            desc.BlendState.RenderTarget._0.SrcBlend = D3D12_BLEND.D3D12_BLEND_SRC_ALPHA;
            desc.BlendState.RenderTarget._0.DestBlend = D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA;
            desc.BlendState.RenderTarget._0.BlendOp = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD;
            desc.BlendState.RenderTarget._0.SrcBlendAlpha = D3D12_BLEND.D3D12_BLEND_ONE;
            desc.BlendState.RenderTarget._0.DestBlendAlpha = D3D12_BLEND.D3D12_BLEND_INV_SRC_ALPHA;
            desc.BlendState.RenderTarget._0.BlendOpAlpha = D3D12_BLEND_OP.D3D12_BLEND_OP_ADD;
            desc.BlendState.RenderTarget._0.RenderTargetWriteMask = 0xF;
            desc.SampleMask = 0xFFFFFFFF;
            desc.RasterizerState.FillMode = D3D12_FILL_MODE.D3D12_FILL_MODE_SOLID;
            desc.RasterizerState.CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_NONE;
            desc.RasterizerState.DepthClipEnable = true;
            desc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE.D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
            desc.NumRenderTargets = 1;
            desc.RTVFormats._0 = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.SampleDesc.Count = 1;

            void* psoObj = null;
            try
            {
                _device->CreateGraphicsPipelineState(desc, typeof(ID3D12PipelineState).GUID, out psoObj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception(context, ex);
            }

            if (psoObj == null)
            {
                throw new InvalidOperationException($"{context} returned a null PSO.");
            }

            return (ID3D12PipelineState*)psoObj;
        }
    }

    private void LoadEmbeddedShaderBytecode()
    {
        var (vertexShader, pixelShader, bgraPixelShader) = DecodeEmbeddedShaderBytecode();
        _vertexShaderBytecode = vertexShader;
        _pixelShaderBytecode = pixelShader;
        _bgraPixelShaderBytecode = bgraPixelShader;
    }

    internal static (int VertexBytes, int PixelBytes, int BgraPixelBytes, byte[] VertexHeader, byte[] PixelHeader, byte[] BgraPixelHeader) GetEmbeddedShaderBytecodeLengths()
    {
        var (vertexShader, pixelShader, bgraPixelShader) = DecodeEmbeddedShaderBytecode();
        return (
            vertexShader.Length,
            pixelShader.Length,
            bgraPixelShader.Length,
            vertexShader[..Math.Min(4, vertexShader.Length)],
            pixelShader[..Math.Min(4, pixelShader.Length)],
            bgraPixelShader[..Math.Min(4, bgraPixelShader.Length)]);
    }

    private static (byte[] VertexShader, byte[] PixelShader, byte[] BgraPixelShader) DecodeEmbeddedShaderBytecode()
    {
        try
        {
            var vertexShader = Convert.FromBase64String(VertexShaderBytecodeBase64);
            var pixelShader = Convert.FromBase64String(PixelShaderBytecodeBase64);
            var bgraPixelShader = Convert.FromBase64String(BgraPixelShaderBytecodeBase64);
            if (vertexShader.Length == 0 || pixelShader.Length == 0 || bgraPixelShader.Length == 0)
            {
                throw new InvalidOperationException("Glyph atlas embedded shader bytecode is empty.");
            }

            return (vertexShader, pixelShader, bgraPixelShader);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Glyph atlas embedded shader bytecode is not valid base64.", ex);
        }
    }

    private void CreateAtlasResources()
    {
        var page = CreateAtlasPageResources(GlyphAtlasPageFormat.Alpha);
        _activeAtlasPage = page;
    }

    private GlyphAtlasPage? TryCreateAdditionalAtlasPage(GlyphAtlasPageFormat format)
    {
        if (_atlasPages.Count >= MaxAtlasPages)
        {
            return null;
        }

        var handle = CreateAtlasPageResources(format);
        return TryResolveAtlasPage(handle, out var page) ? page : null;
    }

    private GlyphAtlasPageHandle CreateAtlasPageResources(GlyphAtlasPageFormat format)
    {
        ID3D12Resource* atlasTexture = null;
        var atlasUploads = new ID3D12Resource*[UploadFrameCount];
        var atlasUploadsTransferred = false;
        ID3D12DescriptorHeap* srvHeap = null;
        var dxgiFormat = GetDxgiFormat(format);
        var atlasPixelBytes = GetAtlasPixelBytes(format);
        try
        {
            var defaultHeap = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT };
            var textureDesc = new D3D12_RESOURCE_DESC
            {
                Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
                Width = AtlasWidth,
                Height = AtlasHeight,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = dxgiFormat,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_UNKNOWN
            };

            RunInitializationPhase(GlyphAtlasInitializationPhase.AtlasTexture, () =>
            {
                void* textureObj = null;
                try
                {
                    _device->CreateCommittedResource(
                        defaultHeap,
                        D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                        textureDesc,
                        D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                        null,
                        typeof(ID3D12Resource).GUID,
                        out textureObj);
                }
                catch (COMException ex)
                {
                    throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(atlas texture)", ex);
                }

                if (textureObj == null)
                {
                    throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(atlas texture) returned a null resource.");
                }

                atlasTexture = (ID3D12Resource*)textureObj;
            });

            var uploadHeap = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD };
            var uploadDesc = new D3D12_RESOURCE_DESC
            {
                Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
                Width = (ulong)atlasPixelBytes,
                Height = 1,
                DepthOrArraySize = 1,
                MipLevels = 1,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR
            };
            RunInitializationPhase(GlyphAtlasInitializationPhase.UploadBuffer, () =>
            {
                for (var i = 0; i < UploadFrameCount; i++)
                {
                    void* uploadObj = null;
                    try
                    {
                        _device->CreateCommittedResource(
                            uploadHeap,
                            D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                            uploadDesc,
                            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
                            null,
                            typeof(ID3D12Resource).GUID,
                            out uploadObj);
                    }
                    catch (COMException ex)
                    {
                        throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(atlas upload buffer)", ex);
                    }

                    if (uploadObj == null)
                    {
                        throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(atlas upload buffer) returned a null resource.");
                    }

                    atlasUploads[i] = (ID3D12Resource*)uploadObj;
                }
            });

            var heapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
            {
                NumDescriptors = 1,
                Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
                Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE
            };
            RunInitializationPhase(GlyphAtlasInitializationPhase.DescriptorHeap, () =>
            {
                void* heapObj = null;
                try
                {
                    _device->CreateDescriptorHeap(heapDesc, typeof(ID3D12DescriptorHeap).GUID, out heapObj);
                }
                catch (COMException ex)
                {
                    throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateDescriptorHeap(SRV)", ex);
                }

                if (heapObj == null)
                {
                    throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateDescriptorHeap(SRV) returned a null heap.");
                }

                srvHeap = (ID3D12DescriptorHeap*)heapObj;
            });

            var srvDesc = new D3D12_SHADER_RESOURCE_VIEW_DESC
            {
                Format = dxgiFormat,
                ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
                Shader4ComponentMapping = Shader4ComponentMapping
            };
            srvDesc.Anonymous.Texture2D.MipLevels = 1;
            RunInitializationPhase(GlyphAtlasInitializationPhase.ShaderResourceView, () =>
            {
                _device->CreateShaderResourceView(atlasTexture, srvDesc, srvHeap->GetCPUDescriptorHandleForHeapStart());
            });

            var page = AddAtlasPage(format, atlasTexture, atlasUploads, srvHeap, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
            atlasTexture = null;
            atlasUploadsTransferred = true;
            srvHeap = null;
            return page;
        }
        finally
        {
            if (srvHeap != null) srvHeap->Release();
            if (!atlasUploadsTransferred)
            {
                for (var i = 0; i < UploadFrameCount; i++) if (atlasUploads[i] != null) atlasUploads[i]->Release();
            }

            if (atlasTexture != null) atlasTexture->Release();
        }
    }

    private void CreateVertexBuffers()
    {
        var heapProps = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD };
        var resDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = (ulong)(MaxGlyphVertices * sizeof(Vertex)),
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR
        };
        for (var i = 0; i < UploadFrameCount; i++)
        {
            void* resObj = null;
            try
            {
                _device->CreateCommittedResource(
                    heapProps,
                    D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                    resDesc,
                    D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
                    null,
                    typeof(ID3D12Resource).GUID,
                    out resObj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(vertex buffer)", ex);
            }

            if (resObj == null)
            {
                throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateCommittedResource(vertex buffer) returned a null resource.");
            }

            var vbuf = (ID3D12Resource*)resObj;
            _vbufs[i] = vbuf;
            _vbvs[i] = new D3D12_VERTEX_BUFFER_VIEW
            {
                BufferLocation = vbuf->GetGPUVirtualAddress(),
                SizeInBytes = (uint)(MaxGlyphVertices * sizeof(Vertex)),
                StrideInBytes = (uint)sizeof(Vertex)
            };
        }
    }

    private void RecordDegradation(int unsupportedRuns, GlyphAtlasFallbackReasonCounts reasons)
    {
        if (unsupportedRuns > 0)
        {
            _diagnostics = _diagnostics.WithDegradation(unsupportedRuns, reasons);
        }
    }

    private void RecordDegradation(int unsupportedRuns, GlyphAtlasFallbackReason reason)
    {
        if (unsupportedRuns > 0)
        {
            _diagnostics = _diagnostics.WithDegradation(unsupportedRuns, reason);
        }
    }

    private void DisableGlyphAtlasDegradation(
        GlyphAtlasFallbackReason reason,
        GlyphAtlasRecordFailurePhase phase,
        DeviceErrorDiagnostic diagnostic,
        int degradedRunCount)
    {
        _disabled = true;
        _deviceError = diagnostic.IsNone ? DeviceErrorDiagnostic.FromFailure(DeviceErrorSite.GlyphAtlasRecord) : diagnostic;
        _diagnostics = _diagnostics
            .WithDegradation(degradedRunCount, reason)
            .WithRecordFailure(phase);
        System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] {_deviceError}");
    }

    private static COMException WrapD3D12Exception(string context, COMException ex)
    {
        return new COMException($"{context} failed: 0x{unchecked((uint)ex.ErrorCode):X8}", ex.ErrorCode);
    }

    private static GlyphAtlasRecordException CreateRecordException(
        GlyphAtlasRecordFailurePhase phase,
        string context,
        COMException ex)
    {
        return new GlyphAtlasRecordException(phase, WrapD3D12Exception(context, ex));
    }

    private static GlyphAtlasRecordException CreateRecordException(
        GlyphAtlasRecordFailurePhase phase,
        string message)
    {
        return new GlyphAtlasRecordException(phase, new InvalidOperationException(message));
    }

    private static void RequirePointer(void* pointer, string message)
    {
        if (pointer == null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static DWRITE_FONT_WEIGHT ToDirectWriteFontWeight(TextFontWeight fontWeight)
    {
        return fontWeight switch
        {
            TextFontWeight.Bold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD,
            TextFontWeight.SemiBold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD,
            _ => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
        };
    }

    private static DWRITE_FONT_STYLE ToDirectWriteFontStyle(TextFontStyle fontStyle)
    {
        return fontStyle switch
        {
            TextFontStyle.Italic => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC,
            TextFontStyle.Oblique => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_OBLIQUE,
            _ => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL
        };
    }

    private static DWRITE_FONT_STRETCH ToDirectWriteFontStretch(TextFontStretch fontStretch)
    {
        return DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL;
    }

    private static string ToDirectWriteFontFamily(TextFontFamily fontFamily)
    {
        return fontFamily switch
        {
            TextFontFamily.SegoeUi => "Segoe UI",
            _ => "Segoe UI"
        };
    }

    private static DWRITE_READING_DIRECTION DetermineParagraphReadingDirection(ReadOnlySpan<char> text) =>
        TryDetermineRangeReadingDirection(text, 0, text.Length, out var direction) ? direction : DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;

    private static bool TryDetermineRangeReadingDirection(ReadOnlySpan<char> text, int start, int end, out DWRITE_READING_DIRECTION direction)
    {
        var clampedStart = Math.Clamp(start, 0, text.Length);
        var clampedEnd = Math.Clamp(end, clampedStart, text.Length);
        for (var i = clampedStart; i < clampedEnd; i++)
        {
            if (TryGetStrongReadingDirection(text[i], out direction))
            {
                return true;
            }
        }

        direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
        return false;
    }

    private static bool TryGetStrongReadingDirection(char character, out DWRITE_READING_DIRECTION direction)
    {
        if (GlyphAtlasTextCompositionHelpers.IsRightToLeftStrongCharacter(character))
        {
            direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT;
            return true;
        }

        if (char.IsLetter(character))
        {
            direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
            return true;
        }

        direction = DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT;
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var fontFace in _fontFaces.Values)
        {
            fontFace.Release();
        }

        foreach (var fontFace in _fallbackFontFaces.Values)
        {
            fontFace.Release();
        }

        for (var i = 0; i < _atlasPages.Count; i++)
        {
            _atlasPages[i].Release();
        }
        _atlasPages.Clear();

        for (var i = 0; i < _vbufs.Length; i++)
        {
            if (_vbufs[i] != null)
            {
                _vbufs[i]->Release();
                _vbufs[i] = null;
            }
        }
        if (_bgraPso != null) _bgraPso->Release();
        if (_pso != null) _pso->Release();
        if (_rootSig != null) _rootSig->Release();
        ReleaseWicFactory();
        if (_fontFallback != null) _fontFallback->Release();
        if (_textAnalyzer != null) _textAnalyzer->Release();
        if (_fontCollection != null) _fontCollection->Release();
        if (_dwriteFactory4 != null) _dwriteFactory4->Release();
        if (_dwriteFactory != null) _dwriteFactory->Release();
        _disposed = true;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
    }

    private struct TextAnalysisSourceShim
    {
        public void** Vtbl;
        public int RefCount;
        public char* Text;
        public uint TextLength;
        public char* Locale;
        public DWRITE_READING_DIRECTION ReadingDirection;
    }

    private struct TextAnalysisSinkShim
    {
        public void** Vtbl;
        public int RefCount;
        public uint TextLength;
        public DWRITE_SCRIPT_ANALYSIS* ScriptAnalysis;
        public byte* BidiLevels;
    }

    public readonly struct GlyphAtlasRecordResult(bool Recorded, int AtlasRuns, int DegradedRuns) : IEquatable<GlyphAtlasRecordResult>
    {
        public bool Recorded { get; } = Recorded;
        public int AtlasRuns { get; } = AtlasRuns;
        public int DegradedRuns { get; } = DegradedRuns;

        public static GlyphAtlasRecordResult Empty => new(true, 0, 0);

        public static GlyphAtlasRecordResult DegradedOnly(int degradedRuns) => new(false, 0, degradedRuns);

        public bool Equals(GlyphAtlasRecordResult other)
        {
            return Recorded == other.Recorded
                && AtlasRuns == other.AtlasRuns
                && DegradedRuns == other.DegradedRuns;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasRecordResult other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Recorded, AtlasRuns, DegradedRuns);

        public static bool operator ==(GlyphAtlasRecordResult left, GlyphAtlasRecordResult right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasRecordResult left, GlyphAtlasRecordResult right) => !left.Equals(right);
    }

    private readonly struct GlyphFrame(
        int VertexCount,
        int BatchCount,
        int AtlasRunCount,
        int DegradedRunCount,
        GlyphAtlasFallbackReasonCounts DegradationReasons)
    {
        public int VertexCount { get; } = VertexCount;
        public int BatchCount { get; } = BatchCount;
        public int AtlasRunCount { get; } = AtlasRunCount;
        public int DegradedRunCount { get; } = DegradedRunCount;
        public GlyphAtlasFallbackReasonCounts DegradationReasons { get; } = DegradationReasons;
    }

    private enum GlyphAtlasPageFormat : byte
    {
        Alpha,
        Bgra
    }

    private readonly struct GlyphDrawBatch(int StartVertex, int VertexCount, IntegerScissorRect Scissor, GlyphAtlasPageHandle Page)
    {
        public int StartVertex { get; } = StartVertex;
        public int VertexCount { get; } = VertexCount;
        public IntegerScissorRect Scissor { get; } = Scissor;
        public GlyphAtlasPageHandle Page { get; } = Page;
    }

    public enum GlyphAtlasInitializationPhase : byte
    {
        None,
        DirectWriteFactory,
        FontCollection,
        TextAnalyzer,
        FontFallback,
        RootSignature,
        ShaderCompile,
        PSO,
        AtlasTexture,
        UploadBuffer,
        DescriptorHeap,
        ShaderResourceView,
        VertexBuffer
    }

    public enum GlyphAtlasRecordFailurePhase : byte
    {
        None,
        Record,
        DirectWrite,
        VertexBufferMap,
        AtlasUploadMap,
        CommandList,
        AtlasPage,
        Pipeline,
        AtlasDraw
    }

    public sealed class GlyphAtlasInitializationException(
        GlyphAtlasInitializationPhase phase,
        Exception innerException) : InvalidOperationException($"Glyph atlas initialization failed during {phase}.", innerException)
    {
        public GlyphAtlasInitializationPhase Phase { get; } = phase;
    }

    private sealed class GlyphAtlasRecordException(
        GlyphAtlasRecordFailurePhase phase,
        Exception innerException) : InvalidOperationException($"Glyph atlas record failed during {phase}: {innerException.Message}", innerException)
    {
        public GlyphAtlasRecordFailurePhase Phase { get; } = phase;
    }

    [Flags]
    public enum GlyphAtlasFallbackReason
    {
        None = 0,
        NonAscii = 1 << 0,
        Clip = 1 << 1,
        Wrapping = 1 << 2,
        Alignment = 1 << 3,
        AtlasFull = 1 << 4,
        VertexLimit = 1 << 5,
        FontMissing = 1 << 6,
        CompileFailed = 1 << 7,
        BatchLimit = 1 << 8,
        InitializationFailed = 1 << 9,
        RecordFailed = 1 << 10,
        ColorGlyph = 1 << 11,
        ComplexScript = 1 << 12,
        ColorGlyphSvg = 1 << 13,
        ColorGlyphPng = 1 << 14,
        ColorGlyphJpeg = 1 << 15,
        ColorGlyphTiff = 1 << 16,
        ColorGlyphPremultipliedBgra = 1 << 17,
        ColorGlyphPaintTree = 1 << 18
    }

    public readonly struct GlyphAtlasFallbackReasonCounts(
        int NonAscii,
        int ColorGlyph,
        int ComplexScript,
        int ColorGlyphSvg,
        int ColorGlyphPng,
        int ColorGlyphJpeg,
        int ColorGlyphTiff,
        int ColorGlyphPremultipliedBgra,
        int ColorGlyphPaintTree,
        int Clip,
        int Wrapping,
        int Alignment,
        int AtlasFull,
        int VertexLimit,
        int FontMissing,
        int CompileFailed,
        int BatchLimit,
        int InitializationFailed,
        int RecordFailed) : IEquatable<GlyphAtlasFallbackReasonCounts>
    {
        public int NonAscii { get; } = NonAscii;
        public int ColorGlyph { get; } = ColorGlyph;
        public int ComplexScript { get; } = ComplexScript;
        public int ColorGlyphSvg { get; } = ColorGlyphSvg;
        public int ColorGlyphPng { get; } = ColorGlyphPng;
        public int ColorGlyphJpeg { get; } = ColorGlyphJpeg;
        public int ColorGlyphTiff { get; } = ColorGlyphTiff;
        public int ColorGlyphPremultipliedBgra { get; } = ColorGlyphPremultipliedBgra;
        public int ColorGlyphPaintTree { get; } = ColorGlyphPaintTree;
        public int Clip { get; } = Clip;
        public int Wrapping { get; } = Wrapping;
        public int Alignment { get; } = Alignment;
        public int AtlasFull { get; } = AtlasFull;
        public int VertexLimit { get; } = VertexLimit;
        public int FontMissing { get; } = FontMissing;
        public int CompileFailed { get; } = CompileFailed;
        public int BatchLimit { get; } = BatchLimit;
        public int InitializationFailed { get; } = InitializationFailed;
        public int RecordFailed { get; } = RecordFailed;

        public GlyphAtlasFallbackReasonCounts With(GlyphAtlasFallbackReason reason)
        {
            return new GlyphAtlasFallbackReasonCounts(
                NonAscii + (HasReason(reason, GlyphAtlasFallbackReason.NonAscii) ? 1 : 0),
                ColorGlyph + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyph) ? 1 : 0),
                ComplexScript + (HasReason(reason, GlyphAtlasFallbackReason.ComplexScript) ? 1 : 0),
                ColorGlyphSvg + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphSvg) ? 1 : 0),
                ColorGlyphPng + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPng) ? 1 : 0),
                ColorGlyphJpeg + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphJpeg) ? 1 : 0),
                ColorGlyphTiff + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphTiff) ? 1 : 0),
                ColorGlyphPremultipliedBgra + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPremultipliedBgra) ? 1 : 0),
                ColorGlyphPaintTree + (HasReason(reason, GlyphAtlasFallbackReason.ColorGlyphPaintTree) ? 1 : 0),
                Clip + (HasReason(reason, GlyphAtlasFallbackReason.Clip) ? 1 : 0),
                Wrapping + (HasReason(reason, GlyphAtlasFallbackReason.Wrapping) ? 1 : 0),
                Alignment + (HasReason(reason, GlyphAtlasFallbackReason.Alignment) ? 1 : 0),
                AtlasFull + (HasReason(reason, GlyphAtlasFallbackReason.AtlasFull) ? 1 : 0),
                VertexLimit + (HasReason(reason, GlyphAtlasFallbackReason.VertexLimit) ? 1 : 0),
                FontMissing + (HasReason(reason, GlyphAtlasFallbackReason.FontMissing) ? 1 : 0),
                CompileFailed + (HasReason(reason, GlyphAtlasFallbackReason.CompileFailed) ? 1 : 0),
                BatchLimit + (HasReason(reason, GlyphAtlasFallbackReason.BatchLimit) ? 1 : 0),
                InitializationFailed + (HasReason(reason, GlyphAtlasFallbackReason.InitializationFailed) ? 1 : 0),
                RecordFailed + (HasReason(reason, GlyphAtlasFallbackReason.RecordFailed) ? 1 : 0));
        }

        private static bool HasReason(GlyphAtlasFallbackReason reason, GlyphAtlasFallbackReason flag) => (reason & flag) != 0;

        public GlyphAtlasFallbackReasonCounts Add(GlyphAtlasFallbackReasonCounts other)
        {
            return new GlyphAtlasFallbackReasonCounts(
                NonAscii + other.NonAscii,
                ColorGlyph + other.ColorGlyph,
                ComplexScript + other.ComplexScript,
                ColorGlyphSvg + other.ColorGlyphSvg,
                ColorGlyphPng + other.ColorGlyphPng,
                ColorGlyphJpeg + other.ColorGlyphJpeg,
                ColorGlyphTiff + other.ColorGlyphTiff,
                ColorGlyphPremultipliedBgra + other.ColorGlyphPremultipliedBgra,
                ColorGlyphPaintTree + other.ColorGlyphPaintTree,
                Clip + other.Clip,
                Wrapping + other.Wrapping,
                Alignment + other.Alignment,
                AtlasFull + other.AtlasFull,
                VertexLimit + other.VertexLimit,
                FontMissing + other.FontMissing,
                CompileFailed + other.CompileFailed,
                BatchLimit + other.BatchLimit,
                InitializationFailed + other.InitializationFailed,
                RecordFailed + other.RecordFailed);
        }

        public bool Equals(GlyphAtlasFallbackReasonCounts other)
        {
            return NonAscii == other.NonAscii
                && ColorGlyph == other.ColorGlyph
                && ComplexScript == other.ComplexScript
                && ColorGlyphSvg == other.ColorGlyphSvg
                && ColorGlyphPng == other.ColorGlyphPng
                && ColorGlyphJpeg == other.ColorGlyphJpeg
                && ColorGlyphTiff == other.ColorGlyphTiff
                && ColorGlyphPremultipliedBgra == other.ColorGlyphPremultipliedBgra
                && ColorGlyphPaintTree == other.ColorGlyphPaintTree
                && Clip == other.Clip
                && Wrapping == other.Wrapping
                && Alignment == other.Alignment
                && AtlasFull == other.AtlasFull
                && VertexLimit == other.VertexLimit
                && FontMissing == other.FontMissing
                && CompileFailed == other.CompileFailed
                && BatchLimit == other.BatchLimit
                && InitializationFailed == other.InitializationFailed
                && RecordFailed == other.RecordFailed;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasFallbackReasonCounts other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(NonAscii);
            hash.Add(ColorGlyph);
            hash.Add(ComplexScript);
            hash.Add(ColorGlyphSvg);
            hash.Add(ColorGlyphPng);
            hash.Add(ColorGlyphJpeg);
            hash.Add(ColorGlyphTiff);
            hash.Add(ColorGlyphPremultipliedBgra);
            hash.Add(ColorGlyphPaintTree);
            hash.Add(Clip);
            hash.Add(Wrapping);
            hash.Add(Alignment);
            hash.Add(AtlasFull);
            hash.Add(VertexLimit);
            hash.Add(FontMissing);
            hash.Add(CompileFailed);
            hash.Add(BatchLimit);
            hash.Add(InitializationFailed);
            hash.Add(RecordFailed);
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return $"NonAscii={NonAscii}, ColorGlyph={ColorGlyph}, ComplexScript={ComplexScript}, ColorGlyphSvg={ColorGlyphSvg}, ColorGlyphPng={ColorGlyphPng}, ColorGlyphJpeg={ColorGlyphJpeg}, ColorGlyphTiff={ColorGlyphTiff}, ColorGlyphPremultipliedBgra={ColorGlyphPremultipliedBgra}, ColorGlyphPaintTree={ColorGlyphPaintTree}, Clip={Clip}, Wrapping={Wrapping}, Alignment={Alignment}, AtlasFull={AtlasFull}, VertexLimit={VertexLimit}, "
                + $"FontMissing={FontMissing}, CompileFailed={CompileFailed}, BatchLimit={BatchLimit}, InitializationFailed={InitializationFailed}, RecordFailed={RecordFailed}";
        }
    }

    private readonly struct FontFaceKey(TextFontFamily Family, TextFontWeight Weight, TextFontStyle Style, TextFontStretch Stretch, float EmSize) : IEquatable<FontFaceKey>
    {
        public TextFontFamily Family { get; } = Family;
        public TextFontWeight Weight { get; } = Weight;
        public TextFontStyle Style { get; } = Style;
        public TextFontStretch Stretch { get; } = Stretch;
        public float EmSize { get; } = EmSize;

        public bool Equals(FontFaceKey other)
        {
            return Family == other.Family
                && Weight == other.Weight
                && Style == other.Style
                && Stretch == other.Stretch
                && EmSize.Equals(other.EmSize);
        }

        public override bool Equals(object? obj) => obj is FontFaceKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Family, Weight, Style, Stretch, EmSize);
    }

    private readonly struct FontFaceIdentity(int Value) : IEquatable<FontFaceIdentity>
    {
        public int Value { get; } = Value;

        public bool Equals(FontFaceIdentity other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is FontFaceIdentity other && Equals(other);

        public override int GetHashCode() => Value;
    }

    private sealed class CachedFontFace(FontFaceIdentity identity, IDWriteFontFace* face, DWRITE_FONT_METRICS metrics, IDWriteFontFace4* face4 = null, IUnknown* fontIdentity = null)
    {
        public FontFaceIdentity Identity { get; } = identity;
        public IDWriteFontFace* Face { get; } = face;
        public IDWriteFontFace4* Face4 { get; } = face4;
        public DWRITE_FONT_METRICS Metrics { get; } = metrics;
        private IUnknown* FontIdentity { get; } = fontIdentity;

        public void Release()
        {
            if (Face != null)
            {
                Face->Release();
            }

            if (Face4 != null)
            {
                Face4->Release();
            }

            if (FontIdentity != null)
            {
                FontIdentity->Release();
            }
        }
    }

    private readonly struct ShapedGlyph(
        ushort GlyphIndex,
        float Advance,
        float AdvanceOffset,
        float AscenderOffset,
        bool IsClusterStart,
        bool IsDiacritic,
        bool IsZeroWidthSpace)
    {
        public ushort GlyphIndex { get; } = GlyphIndex;
        public float Advance { get; } = Advance;
        public float AdvanceOffset { get; } = AdvanceOffset;
        public float AscenderOffset { get; } = AscenderOffset;
        public bool IsClusterStart { get; } = IsClusterStart;
        public bool IsDiacritic { get; } = IsDiacritic;
        public bool IsZeroWidthSpace { get; } = IsZeroWidthSpace;

        public static ShapedGlyph Simple(ushort glyphIndex, float advance) => new(glyphIndex, advance, 0, 0, IsClusterStart: true, IsDiacritic: false, IsZeroWidthSpace: false);

        public static ShapedGlyph FromDirectWrite(
            ushort glyphIndex,
            float advance,
            DWRITE_GLYPH_OFFSET offset,
            DWRITE_SHAPING_GLYPH_PROPERTIES properties) =>
            new(
                glyphIndex,
                advance,
                offset.advanceOffset,
                offset.ascenderOffset,
                properties.isClusterStart,
                properties.isDiacritic,
                properties.isZeroWidthSpace);
    }

    private readonly struct ShapedGlyphSegment(
        CachedFontFace FontFace,
        float FontEmSize,
        int TextStart,
        int TextLength,
        int GlyphStart,
        int GlyphCount,
        float ControlAdvance,
        byte BidiLevel)
    {
        public CachedFontFace FontFace { get; } = FontFace;
        public float FontEmSize { get; } = FontEmSize;
        public int TextStart { get; } = TextStart;
        public int TextLength { get; } = TextLength;
        public int TextEnd => TextStart + TextLength;
        public int GlyphStart { get; } = GlyphStart;
        public int GlyphCount { get; } = GlyphCount;
        public float ControlAdvance { get; } = ControlAdvance;
        public byte BidiLevel { get; } = BidiLevel;
        public bool IsRightToLeft => (BidiLevel & 1) != 0;

        public float ComputeLineHeight() => D3D12GlyphAtlasTextRenderer.ComputeLineHeight(FontFace.Metrics, FontEmSize);

        public float ComputeAscent()
        {
            var scale = FontEmSize / FontFace.Metrics.designUnitsPerEm;
            return FontFace.Metrics.ascent * scale;
        }
    }

    private readonly struct ShapedGlyphLine(int SegmentStart, int SegmentCount, int GlyphStart, int GlyphCount, float Width, byte BidiLevel)
    {
        public int SegmentStart { get; } = SegmentStart;
        public int SegmentCount { get; } = SegmentCount;
        public int GlyphStart { get; } = GlyphStart;
        public int GlyphCount { get; } = GlyphCount;
        public float Width { get; } = Width;
        public byte BidiLevel { get; } = BidiLevel;
        public bool IsRightToLeft => (BidiLevel & 1) != 0;
    }

    private readonly ref struct ShapedGlyphRun(
        ReadOnlySpan<ShapedGlyph> Glyphs,
        ReadOnlySpan<ShapedGlyphSegment> Segments,
        ReadOnlySpan<ShapedGlyphLine> Lines,
        ReadOnlySpan<ushort> ClusterMap,
        int TextLength,
        bool RequiresColorGlyph)
    {
        public ReadOnlySpan<ShapedGlyph> Glyphs { get; } = Glyphs;
        public ReadOnlySpan<ShapedGlyphSegment> Segments { get; } = Segments;
        public ReadOnlySpan<ShapedGlyphLine> Lines { get; } = Lines;
        public ReadOnlySpan<ushort> ClusterMap { get; } = ClusterMap;
        public int TextLength { get; } = TextLength;
        public bool RequiresColorGlyph { get; } = RequiresColorGlyph;
        public int GlyphCount => Glyphs.Length;
        public int LineCount => Lines.Length;

        public float ComputeAdvance()
        {
            var width = 0f;
            foreach (ref readonly var line in Lines)
            {
                width = Math.Max(width, line.Width);
            }

            return width;
        }

        public float ComputeLineHeight()
        {
            var lineHeight = 0f;
            foreach (ref readonly var segment in Segments)
            {
                lineHeight = Math.Max(lineHeight, segment.ComputeLineHeight());
            }

            return lineHeight;
        }

        public float ComputeAscent()
        {
            var ascent = 0f;
            foreach (ref readonly var segment in Segments)
            {
                ascent = Math.Max(ascent, segment.ComputeAscent());
            }

            return ascent;
        }

        public bool HasMissingGlyph()
        {
            foreach (ref readonly var glyph in Glyphs)
            {
                if (glyph.GlyphIndex == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private readonly struct GlyphAtom(byte Kind, uint CodePoint, ushort GlyphIndex, byte Flags) : IEquatable<GlyphAtom>
    {
        private const byte SimpleCodePointKind = 1;
        private const byte ShapedPlacementKind = 2;
        private const byte ColorLayerKind = 3;
        private const byte BgraGlyphKind = 4;
        private const byte EncodedBitmapGlyphKind = 5;
        private const byte DiacriticFlag = 1 << 0;
        private const byte ZeroWidthSpaceFlag = 1 << 1;

        public byte Kind { get; } = Kind;
        public uint CodePoint { get; } = CodePoint;
        public ushort GlyphIndex { get; } = GlyphIndex;
        public byte Flags { get; } = Flags;

        public static GlyphAtom SimpleCodePoint(uint codePoint, ushort glyphIndex) => new(SimpleCodePointKind, codePoint, glyphIndex, Flags: 0);

        public static GlyphAtom ShapedPlacement(ushort glyphIndex, bool isDiacritic, bool isZeroWidthSpace)
        {
            var flags = (byte)((isDiacritic ? DiacriticFlag : 0) | (isZeroWidthSpace ? ZeroWidthSpaceFlag : 0));
            return new GlyphAtom(ShapedPlacementKind, 0, glyphIndex, flags);
        }

        public static GlyphAtom ColorLayer(ushort glyphIndex) => new(ColorLayerKind, 0, glyphIndex, Flags: 0);

        public static GlyphAtom BgraGlyph(ushort glyphIndex, uint pixelsPerEm) => new(BgraGlyphKind, pixelsPerEm, glyphIndex, Flags: 0);

        public static GlyphAtom EncodedBitmapGlyph(ushort glyphIndex, uint pixelsPerEm, byte formatId) => new(EncodedBitmapGlyphKind, pixelsPerEm, glyphIndex, formatId);

        public bool Equals(GlyphAtom other) => Kind == other.Kind && CodePoint == other.CodePoint && GlyphIndex == other.GlyphIndex && Flags == other.Flags;

        public override bool Equals(object? obj) => obj is GlyphAtom other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, CodePoint, GlyphIndex, Flags);
    }

    private readonly struct GlyphKey(FontFaceIdentity FontFace, float EmSize, GlyphAtom Glyph) : IEquatable<GlyphKey>
    {
        public FontFaceIdentity FontFace { get; } = FontFace;
        public float EmSize { get; } = EmSize;
        public GlyphAtom Glyph { get; } = Glyph;

        public bool Equals(GlyphKey other) => FontFace.Equals(other.FontFace) && EmSize.Equals(other.EmSize) && Glyph.Equals(other.Glyph);

        public override bool Equals(object? obj) => obj is GlyphKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FontFace, EmSize, Glyph);
    }

    private readonly struct GlyphAtlasPageHandle(int Index, int Generation) : IEquatable<GlyphAtlasPageHandle>
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public bool IsNone => Generation == 0;

        public GlyphAtlasPageHandle NextGeneration() => new(Index, checked(Generation + 1));

        public bool Equals(GlyphAtlasPageHandle other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is GlyphAtlasPageHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        public static bool operator ==(GlyphAtlasPageHandle left, GlyphAtlasPageHandle right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasPageHandle left, GlyphAtlasPageHandle right) => !left.Equals(right);
    }

    private readonly struct GlyphAtlasPageUsage(int UsedPixels, int FragmentedPixels, long OldestPageAge, long NewestPageAge)
    {
        public int UsedPixels { get; } = UsedPixels;
        public int FragmentedPixels { get; } = FragmentedPixels;
        public long OldestPageAge { get; } = OldestPageAge;
        public long NewestPageAge { get; } = NewestPageAge;
    }

    private readonly struct GlyphAtlasPageMutationState(
        GlyphAtlasPageHandle Handle,
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
        long LastUsedSerial)
    {
        public GlyphAtlasPageHandle Handle { get; } = Handle;
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
    }

    private readonly struct GlyphEntryMutationState(int Index, int Generation, long LastUsedSerial)
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public long LastUsedSerial { get; } = LastUsedSerial;
    }

    private readonly struct GlyphAtlasPageReuseRequest(GlyphAtlasPageHandle Page, long RequestedRecordSerial)
    {
        public GlyphAtlasPageHandle Page { get; } = Page;
        public long RequestedRecordSerial { get; } = RequestedRecordSerial;
        public bool IsNone => Page.IsNone;

        public bool CanApply(long recordSerial, long oldestRetainedRecordSerial) =>
            GlyphAtlasTextCompositionHelpers.CanApplyAtlasPageReuseRequest(!IsNone, RequestedRecordSerial, recordSerial, oldestRetainedRecordSerial);
    }

    private sealed unsafe class GlyphAtlasPage(
        GlyphAtlasPageHandle handle,
        GlyphAtlasPageFormat format,
        ID3D12Resource* texture,
        ID3D12Resource*[] uploads,
        ID3D12DescriptorHeap* srvHeap,
        D3D12_RESOURCE_STATES textureState,
        byte[] pixels)
    {
        private GlyphAtlasPageHandle _handle = handle;
        private D3D12_RESOURCE_STATES _textureState = textureState;
        private bool _released;

        public GlyphAtlasPageHandle Handle => _handle;
        public int Generation => Handle.Generation;
        public GlyphAtlasPageFormat Format { get; } = format;
        public int BytesPerPixel => GetAtlasBytesPerPixel(Format);
        public int RowPitch => GetAtlasRowPitch(Format);
        public DXGI_FORMAT DxgiFormat => GetDxgiFormat(Format);
        public ID3D12Resource* Texture { get; private set; } = texture;
        public ID3D12Resource*[] Uploads { get; } = uploads;
        public ID3D12DescriptorHeap* SrvHeap { get; private set; } = srvHeap;
        public byte[] Pixels { get; } = pixels;
        public int NextX { get; set; } = AtlasPadding;
        public int NextY { get; set; } = AtlasPadding;
        public int RowHeight { get; set; }
        public bool IsDirty { get; set; }
        public int DirtyLeft { get; set; } = AtlasWidth;
        public int DirtyTop { get; set; } = AtlasHeight;
        public int DirtyRight { get; set; }
        public int DirtyBottom { get; set; }
        public int UsedPixels { get; set; }
        public int AllocatedPixels { get; set; }
        public long LastUsedSerial { get; private set; }

        public D3D12_RESOURCE_BARRIER TransitionTexture(D3D12_RESOURCE_STATES after)
        {
            var barrier = Transition(Texture, _textureState, after);
            _textureState = after;
            return barrier;
        }

        public void Touch(long serial)
        {
            LastUsedSerial = Math.Max(LastUsedSerial, serial);
        }

        public GlyphAtlasPageMutationState CaptureMutationState() =>
            new(Handle, NextX, NextY, RowHeight, IsDirty, DirtyLeft, DirtyTop, DirtyRight, DirtyBottom, UsedPixels, AllocatedPixels, LastUsedSerial);

        public void RestoreMutationState(GlyphAtlasPageMutationState state)
        {
            NextX = state.NextX;
            NextY = state.NextY;
            RowHeight = state.RowHeight;
            IsDirty = state.IsDirty;
            DirtyLeft = state.DirtyLeft;
            DirtyTop = state.DirtyTop;
            DirtyRight = state.DirtyRight;
            DirtyBottom = state.DirtyBottom;
            UsedPixels = state.UsedPixels;
            AllocatedPixels = state.AllocatedPixels;
            LastUsedSerial = state.LastUsedSerial;
        }

        public GlyphAtlasPageHandle ResetForReuse()
        {
            _handle = _handle.NextGeneration();
            Array.Clear(Pixels);
            var resetState = GlyphAtlasTextCompositionHelpers.CreatePageReuseResetState(AtlasWidth, AtlasHeight, AtlasPadding);
            NextX = resetState.NextX;
            NextY = resetState.NextY;
            RowHeight = resetState.RowHeight;
            IsDirty = resetState.IsDirty;
            DirtyLeft = resetState.DirtyLeft;
            DirtyTop = resetState.DirtyTop;
            DirtyRight = resetState.DirtyRight;
            DirtyBottom = resetState.DirtyBottom;
            UsedPixels = resetState.UsedPixels;
            AllocatedPixels = resetState.AllocatedPixels;
            LastUsedSerial = resetState.LastUsedSerial;
            return _handle;
        }

        public int ComputeAllocatedPixels()
        {
            if (NextY <= AtlasPadding && NextX <= AtlasPadding && RowHeight == 0)
            {
                return 0;
            }

            var committedRows = Math.Max(0, NextY - AtlasPadding);
            var currentRowWidth = Math.Max(0, NextX - AtlasPadding);
            return checked((committedRows * AtlasWidth) + (currentRowWidth * RowHeight));
        }

        public int ComputeAvailablePixels()
        {
            var rowAvailable = Math.Max(0, AtlasWidth - AtlasPadding - NextX) * Math.Max(0, RowHeight);
            var rowsAvailable = Math.Max(0, AtlasHeight - AtlasPadding - NextY - RowHeight) * AtlasWidth;
            return checked(rowAvailable + rowsAvailable);
        }

        public void Release()
        {
            if (_released)
            {
                return;
            }

            for (var i = 0; i < Uploads.Length; i++)
            {
                if (Uploads[i] != null)
                {
                    Uploads[i]->Release();
                    Uploads[i] = null;
                }
            }

            if (Texture != null) Texture->Release();
            if (SrvHeap != null) SrvHeap->Release();
            Texture = null;
            SrvHeap = null;
            _released = true;
        }
    }

    private readonly struct GlyphAtlasEntryHandle(int Index, int Generation) : IEquatable<GlyphAtlasEntryHandle>
    {
        public int Index { get; } = Index;
        public int Generation { get; } = Generation;
        public bool IsNone => Generation == 0;

        public bool Equals(GlyphAtlasEntryHandle other) => Index == other.Index && Generation == other.Generation;

        public override bool Equals(object? obj) => obj is GlyphAtlasEntryHandle other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Index, Generation);

        public static bool operator ==(GlyphAtlasEntryHandle left, GlyphAtlasEntryHandle right) => left.Equals(right);

        public static bool operator !=(GlyphAtlasEntryHandle left, GlyphAtlasEntryHandle right) => !left.Equals(right);
    }

    private readonly struct GlyphEntry(
        GlyphKey Key,
        float Width,
        float Height,
        float OffsetX,
        float OffsetY,
        float Advance,
        float U1,
        float V1,
        float U2,
        float V2,
        GlyphAtlasPageHandle Page,
        int Generation = 0,
        long LastUsedSerial = 0)
    {
        public GlyphKey Key { get; } = Key;
        public float Width { get; } = Width;
        public float Height { get; } = Height;
        public float OffsetX { get; } = OffsetX;
        public float OffsetY { get; } = OffsetY;
        public float Advance { get; } = Advance;
        public float U1 { get; } = U1;
        public float V1 { get; } = V1;
        public float U2 { get; } = U2;
        public float V2 { get; } = V2;
        public GlyphAtlasPageHandle Page { get; } = Page;
        public int Generation { get; } = Generation;
        public long LastUsedSerial { get; } = LastUsedSerial;
        public bool IsLive => LastUsedSerial > 0;

        public GlyphEntry WithGeneration(int generation) => new(Key, Width, Height, OffsetX, OffsetY, Advance, U1, V1, U2, V2, Page, generation, LastUsedSerial);

        public GlyphEntry WithLastUsedSerial(long serial) => new(Key, Width, Height, OffsetX, OffsetY, Advance, U1, V1, U2, V2, Page, Generation, serial);

        public GlyphEntry Clear() => new(Key, 0, 0, 0, 0, 0, 0, 0, 0, 0, Page, Generation, 0);
    }

    public readonly struct GlyphAtlasTextRendererDiagnostics(
        int CachedGlyphs,
        long UploadedBytes,
        int DrawnGlyphs,
        int CacheHits,
        int CacheMisses,
        int FallbackFrames,
        int UnsupportedRuns,
        GlyphAtlasFallbackReasonCounts Reasons,
        GlyphAtlasInitializationPhase InitializationFailurePhase,
        GlyphAtlasRecordFailurePhase RecordFailurePhase,
        int RasterScratchBytes,
        int RasterScratchResizes,
        int AtlasPages = 0,
        int AtlasEvictions = 0,
        int AtlasUsedPixels = 0,
        int AtlasFragmentedPixels = 0,
        long AtlasRecordSerial = 0,
        long AtlasOldestPageAge = 0,
        long AtlasNewestPageAge = 0,
        int AtlasPendingPageReuses = 0,
        int AtlasPageReuseRequests = 0,
        int AtlasFullWithoutPageReuse = 0,
        int AtlasRuns = 0,
        int DegradedRuns = 0,
        int UploadedGlyphs = 0,
        int ShapedProbeRuns = 0,
        int ShapedProbeGlyphs = 0) : IEquatable<GlyphAtlasTextRendererDiagnostics>
    {
        public int CachedGlyphs { get; } = CachedGlyphs;
        public long UploadedBytes { get; } = UploadedBytes;
        public int DrawnGlyphs { get; } = DrawnGlyphs;
        public int CacheHits { get; } = CacheHits;
        public int CacheMisses { get; } = CacheMisses;
        public int FallbackFrames { get; } = FallbackFrames;
        public int UnsupportedRuns { get; } = UnsupportedRuns;
        public int ShapedProbeRuns { get; } = ShapedProbeRuns;
        public int ShapedProbeGlyphs { get; } = ShapedProbeGlyphs;
        public int AtlasPages { get; } = AtlasPages;
        public int AtlasBudgetPages => MaxAtlasPages;
        public int AtlasPageWidth => AtlasWidth;
        public int AtlasPageHeight => AtlasHeight;
        public int AtlasCapacityPixels => AtlasBudgetPixels;
        public int AtlasEvictions { get; } = AtlasEvictions;
        public int AtlasUsedPixels { get; } = AtlasUsedPixels;
        public int AtlasFragmentedPixels { get; } = AtlasFragmentedPixels;
        public long AtlasRecordSerial { get; } = AtlasRecordSerial;
        public long AtlasOldestPageAge { get; } = AtlasOldestPageAge;
        public long AtlasNewestPageAge { get; } = AtlasNewestPageAge;
        public int AtlasPendingPageReuses { get; } = AtlasPendingPageReuses;
        public int AtlasPageReuseRequests { get; } = AtlasPageReuseRequests;
        public int AtlasFullWithoutPageReuse { get; } = AtlasFullWithoutPageReuse;
        public int AtlasRuns { get; } = AtlasRuns;
        public int DegradedRuns { get; } = DegradedRuns;
        public int UploadedGlyphs { get; } = UploadedGlyphs;
        public GlyphAtlasFallbackReasonCounts Reasons { get; } = Reasons;
        public GlyphAtlasInitializationPhase InitializationFailurePhase { get; } = InitializationFailurePhase;
        public GlyphAtlasRecordFailurePhase RecordFailurePhase { get; } = RecordFailurePhase;
        public int RasterScratchBytes { get; } = RasterScratchBytes;
        public int RasterScratchResizes { get; } = RasterScratchResizes;

        public GlyphAtlasTextRendererDiagnostics WithCachedGlyphs(int cachedGlyphs) =>
            new(
                cachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithCacheHit() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits + 1,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithCacheMiss() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses + 1,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithDrawnGlyphs(int glyphs) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs + glyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPages(int atlasPages) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                atlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasEviction() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions + 1,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPendingPageReuse(int pendingPageReuses) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                pendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageReuseRequest() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests + 1,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasFullWithoutPageReuse() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse + 1,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasPageUsage(int usedPixels, int fragmentedPixels) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                usedPixels,
                fragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasTouchMetrics(long recordSerial, long oldestPageAge, long newestPageAge) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                recordSerial,
                oldestPageAge,
                newestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithAtlasRuns(int atlasRuns) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns + atlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithUploadedBytes(long bytes) =>
            new(
                CachedGlyphs,
                UploadedBytes + bytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithUploadedGlyph() =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs + 1,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithShapedGlyphProbe(int glyphCount) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns + 1,
                ShapedProbeGlyphs + glyphCount);

        public GlyphAtlasTextRendererDiagnostics WithRasterScratch(int bytes, int resizes) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                RecordFailurePhase,
                bytes,
                resizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithInitializationFailure(GlyphAtlasInitializationPhase phase) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                phase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithRecordFailure(GlyphAtlasRecordFailurePhase phase) =>
            new(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames,
                UnsupportedRuns,
                Reasons,
                InitializationFailurePhase,
                phase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);

        public GlyphAtlasTextRendererDiagnostics WithDegradation(int unsupportedRuns, GlyphAtlasFallbackReason reason)
        {
            var reasons = default(GlyphAtlasFallbackReasonCounts);
            if (reason != GlyphAtlasFallbackReason.None)
            {
                var reasonCount = unsupportedRuns > 0 ? unsupportedRuns : 1;
                for (var i = 0; i < reasonCount; i++)
                {
                    reasons = reasons.With(reason);
                }
            }

            return WithDegradation(unsupportedRuns, reasons);
        }

        public GlyphAtlasTextRendererDiagnostics WithDegradation(int unsupportedRuns, GlyphAtlasFallbackReasonCounts reasons)
        {
            return new GlyphAtlasTextRendererDiagnostics(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames + 1,
                UnsupportedRuns + unsupportedRuns,
                Reasons.Add(reasons),
                InitializationFailurePhase,
                RecordFailurePhase,
                RasterScratchBytes,
                RasterScratchResizes,
                AtlasPages,
                AtlasEvictions,
                AtlasUsedPixels,
                AtlasFragmentedPixels,
                AtlasRecordSerial,
                AtlasOldestPageAge,
                AtlasNewestPageAge,
                AtlasPendingPageReuses,
                AtlasPageReuseRequests,
                AtlasFullWithoutPageReuse,
                AtlasRuns,
                DegradedRuns + unsupportedRuns,
                UploadedGlyphs,
                ShapedProbeRuns,
                ShapedProbeGlyphs);
        }

        public string FormatSummary()
        {
            return $"cachedGlyphs={CachedGlyphs}, atlasPages={AtlasPages}, atlasBudgetPages={AtlasBudgetPages}, atlasPage={AtlasPageWidth}x{AtlasPageHeight}, atlasCapacity={AtlasCapacityPixels} px, atlasEvictions={AtlasEvictions}, atlasPendingPageReuses={AtlasPendingPageReuses}, atlasPageReuseRequests={AtlasPageReuseRequests}, atlasFullWithoutPageReuse={AtlasFullWithoutPageReuse}, atlasUsed={AtlasUsedPixels} px, atlasFragmented={AtlasFragmentedPixels} px, "
                + $"atlasRecordSerial={AtlasRecordSerial}, atlasOldestPageAge={AtlasOldestPageAge}, atlasNewestPageAge={AtlasNewestPageAge}, drawnGlyphs={DrawnGlyphs}, atlasRuns={AtlasRuns}, degradedRuns={DegradedRuns}, "
                + $"uploads={UploadedBytes} bytes, uploadedGlyphs={UploadedGlyphs}, shapedProbeRuns={ShapedProbeRuns}, shapedProbeGlyphs={ShapedProbeGlyphs}, hits={CacheHits}, misses={CacheMisses}, fallbacks={FallbackFrames}, unsupportedRuns={UnsupportedRuns}, reasons=[{Reasons}], "
                + $"initFailurePhase={InitializationFailurePhase}, recordFailurePhase={RecordFailurePhase}, rasterScratch={RasterScratchBytes} bytes/{RasterScratchResizes} resizes";
        }

        public bool Equals(GlyphAtlasTextRendererDiagnostics other)
        {
            return CachedGlyphs == other.CachedGlyphs
                && UploadedBytes == other.UploadedBytes
                && DrawnGlyphs == other.DrawnGlyphs
                && CacheHits == other.CacheHits
                && CacheMisses == other.CacheMisses
                && FallbackFrames == other.FallbackFrames
                && UnsupportedRuns == other.UnsupportedRuns
                && AtlasPages == other.AtlasPages
                && AtlasEvictions == other.AtlasEvictions
                && AtlasUsedPixels == other.AtlasUsedPixels
                && AtlasFragmentedPixels == other.AtlasFragmentedPixels
                && AtlasRecordSerial == other.AtlasRecordSerial
                && AtlasOldestPageAge == other.AtlasOldestPageAge
                && AtlasNewestPageAge == other.AtlasNewestPageAge
                && AtlasPendingPageReuses == other.AtlasPendingPageReuses
                && AtlasPageReuseRequests == other.AtlasPageReuseRequests
                && AtlasFullWithoutPageReuse == other.AtlasFullWithoutPageReuse
                && AtlasRuns == other.AtlasRuns
                && DegradedRuns == other.DegradedRuns
                && UploadedGlyphs == other.UploadedGlyphs
                && ShapedProbeRuns == other.ShapedProbeRuns
                && ShapedProbeGlyphs == other.ShapedProbeGlyphs
                && Reasons.Equals(other.Reasons)
                && InitializationFailurePhase == other.InitializationFailurePhase
                && RecordFailurePhase == other.RecordFailurePhase
                && RasterScratchBytes == other.RasterScratchBytes
                && RasterScratchResizes == other.RasterScratchResizes;
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasTextRendererDiagnostics other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(CachedGlyphs);
            hash.Add(UploadedBytes);
            hash.Add(DrawnGlyphs);
            hash.Add(CacheHits);
            hash.Add(CacheMisses);
            hash.Add(FallbackFrames);
            hash.Add(UnsupportedRuns);
            hash.Add(AtlasPages);
            hash.Add(AtlasEvictions);
            hash.Add(AtlasUsedPixels);
            hash.Add(AtlasFragmentedPixels);
            hash.Add(AtlasRecordSerial);
            hash.Add(AtlasOldestPageAge);
            hash.Add(AtlasNewestPageAge);
            hash.Add(AtlasPendingPageReuses);
            hash.Add(AtlasPageReuseRequests);
            hash.Add(AtlasFullWithoutPageReuse);
            hash.Add(AtlasRuns);
            hash.Add(DegradedRuns);
            hash.Add(UploadedGlyphs);
            hash.Add(ShapedProbeRuns);
            hash.Add(ShapedProbeGlyphs);
            hash.Add(Reasons);
            hash.Add(InitializationFailurePhase);
            hash.Add(RecordFailurePhase);
            hash.Add(RasterScratchBytes);
            hash.Add(RasterScratchResizes);
            return hash.ToHashCode();
        }
    }

}
