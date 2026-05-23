using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer : IDisposable
{
    private static readonly Guid IUnknownGuid = new(0x00000000, 0x0000, 0x0000, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46);
    private const int UploadFrameCount = 2;
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;
    private const int AtlasPadding = 1;
    internal const int AtlasPageBudget = 48;
    private const int AtlasPagePixels = AtlasWidth * AtlasHeight;
    private const int AtlasBudgetPixels = AtlasPageBudget * AtlasPagePixels;
    private const int MaxGlyphQuads = 4096;
    private const int MaxGlyphVertices = MaxGlyphQuads * 6;
    private const int MaxGlyphDrawBatches = 1024;
    private const int MaxShapedRunSegments = 64;
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
    private readonly List<GlyphAtlasPage> _atlasPages = new(AtlasPageBudget);
    private readonly GlyphAtlasPageMutationState[] _runPageStates = new GlyphAtlasPageMutationState[AtlasPageBudget];
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
    private DeviceErrorDiagnostic _deviceError = DeviceErrorDiagnostic.None;
    private GlyphAtlasTextRendererDiagnostics _diagnostics;
    private GlyphAtlasTextRendererDiagnostics _runDiagnostics;

    internal D3D12GlyphAtlasTextRenderer(ID3D12Device* device)
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

    internal bool IsDisabled => _disabled;
    internal DeviceErrorDiagnostic DeviceError => _deviceError;

    internal GlyphAtlasTextRendererDiagnostics GetDiagnostics()
    {
        var pageUsage = GetAtlasPageUsage();
        CountPendingAtlasPageReuseRequests(out var pendingAlphaReuses, out var pendingBgraReuses);
        return _diagnostics
            .WithCachedGlyphs(_cachedGlyphCount)
            .WithAtlasPendingPageReuse(pendingAlphaReuses, pendingBgraReuses)
            .WithAtlasPageUsage(pageUsage.UsedPixels, pageUsage.FragmentedPixels, pageUsage.AlphaUsedPixels, pageUsage.BgraUsedPixels, pageUsage.AlphaFragmentedPixels, pageUsage.BgraFragmentedPixels)
            .WithAtlasTouchMetrics(_glyphRecordSerial, pageUsage.OldestPageAge, pageUsage.NewestPageAge, pageUsage.OldestAlphaPageAge, pageUsage.OldestBgraPageAge)
            .WithAtlasPageCounts(_atlasPages.Count, pageUsage.AlphaPageCount, pageUsage.BgraPageCount)
            .WithRasterScratch(
                _clearTypeScratch.Length + _grayscaleScratch.Length + GetShapeScratchByteCount(),
                _rasterScratchResizeCount + _shapeScratchResizeCount);
    }

    internal void ResetDiagnostics()
    {
        var pageUsage = GetAtlasPageUsage();
        CountPendingAtlasPageReuseRequests(out var pendingAlphaReuses, out var pendingBgraReuses);
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
            RasterScratchBytes: _clearTypeScratch.Length + _grayscaleScratch.Length + GetShapeScratchByteCount(),
            RasterScratchResizes: 0,
            AtlasPages: _atlasPages.Count,
            AtlasAlphaPages: pageUsage.AlphaPageCount,
            AtlasBgraPages: pageUsage.BgraPageCount,
            AtlasUsedPixels: pageUsage.UsedPixels,
            AtlasFragmentedPixels: pageUsage.FragmentedPixels,
            AtlasAlphaUsedPixels: pageUsage.AlphaUsedPixels,
            AtlasBgraUsedPixels: pageUsage.BgraUsedPixels,
            AtlasAlphaFragmentedPixels: pageUsage.AlphaFragmentedPixels,
            AtlasBgraFragmentedPixels: pageUsage.BgraFragmentedPixels,
            AtlasRecordSerial: _glyphRecordSerial,
            AtlasOldestPageAge: pageUsage.OldestPageAge,
            AtlasNewestPageAge: pageUsage.NewestPageAge,
            AtlasOldestAlphaPageAge: pageUsage.OldestAlphaPageAge,
            AtlasOldestBgraPageAge: pageUsage.OldestBgraPageAge,
            AtlasPendingPageReuses: pendingAlphaReuses + pendingBgraReuses,
            AtlasPendingAlphaPageReuses: pendingAlphaReuses,
            AtlasPendingBgraPageReuses: pendingBgraReuses);
        _rasterScratchResizeCount = 0;
        _shapeScratchResizeCount = 0;
    }

    internal GlyphAtlasRecordResult TryRecord(
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
                .WithAtlasRuns(frame.AtlasRunCount)
                .WithColorGlyphRuns(frame.ColorLayerRunCount, frame.ColorBitmapRunCount);
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
        var colorLayerRunCount = 0;
        var colorBitmapRunCount = 0;
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
                    if (TryBuildShapedAtlasRun(text, textRun, style, shapedRun, viewportWidth, viewportHeight, recordSerial, ref vertexCount, ref batchCount, ref colorLayerRunCount, ref colorBitmapRunCount, out unsupportedReason))
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

        return new GlyphFrame(vertexCount, batchCount, atlasRunCount, degradedRunCount, degradationReasons, colorLayerRunCount, colorBitmapRunCount);
    }
}
