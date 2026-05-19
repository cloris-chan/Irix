using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe class D3D12GlyphAtlasTextRenderer : IDisposable
{
    private const int AtlasWidth = 1024;
    private const int AtlasHeight = 1024;
    private const int AtlasPadding = 1;
    private const int MaxGlyphQuads = 4096;
    private const int MaxGlyphVertices = MaxGlyphQuads * 6;
    private const int MaxGlyphDrawBatches = 1024;
    private const int AtlasRowPitch = 1024;
    private const int TextureDataPitchAlignment = 256;
    private const uint Shader4ComponentMapping = 0u | (1u << 3) | (2u << 6) | (3u << 9) | (1u << 12);
    private const string VertexShaderBytecodeBase64 =
        "RFhCQ0hOwF2N9gtTu/HrKRNRsV8BAAAA4AIAAAUAAAA0AAAAoAAAABABAACEAQAARAIAAFJERUZkAAAAAAAAAAAAAAAAAAAAPAAAAAAF/v8AgQAAPAAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAElTR05oAAAAAwAAAAgAAABQAAAAAAAAAAAAAAADAAAAAAAAAAMDAABZAAAAAAAAAAAAAAADAAAAAQAAAAMDAABiAAAAAAAAAAAAAAADAAAAAgAAAA8PAABQT1NJVElPTgBURVhDT09SRABDT0xPUgBPU0dObAAAAAMAAAAIAAAAUAAAAAAAAAABAAAAAwAAAAAAAAAPAAAAXAAAAAAAAAAAAAAAAwAAAAEAAAADDAAAZQAAAAAAAAAAAAAAAwAAAAIAAAAPAAAAU1ZfUE9TSVRJT04AVEVYQ09PUkQAQ09MT1IAq1NIRVi4AAAAUAABAC4AAABqCAABXwAAAzIQEAAAAAAAXwAAAzIQEAABAAAAXwAAA/IQEAACAAAAZwAABPIgEAAAAAAAAQAAAGUAAAMyIBAAAQAAAGUAAAPyIBAAAgAAADYAAAUyIBAAAAAAAEYQEAAAAAAANgAACMIgEAAAAAAAAkAAAAAAAAAAAAAAAAAAAAAAgD82AAAFMiAQAAEAAABGEBAAAQAAADYAAAXyIBAAAgAAAEYeEAACAAAAPgAAAVNUQVSUAAAABQAAAAAAAAAAAAAABgAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
    private const string PixelShaderBytecodeBase64 =
        "RFhCQw3tOgpD5F95IlkFLIOjg2ABAAAA9AIAAAUAAAA0AAAA9AAAAGgBAACcAQAAWAIAAFJERUa4AAAAAAAAAAAAAAACAAAAPAAAAAAF//8AgQAAjwAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAAfAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAEAAACJAAAAAgAAAAUAAAAEAAAA/////wAAAAABAAAAAQAAAEF0bGFzU2FtcGxlcgBBdGxhcwBNaWNyb3NvZnQgKFIpIEhMU0wgU2hhZGVyIENvbXBpbGVyIDEwLjEAq0lTR05sAAAAAwAAAAgAAABQAAAAAAAAAAEAAAADAAAAAAAAAA8AAABcAAAAAAAAAAAAAAADAAAAAQAAAAMDAABlAAAAAAAAAAAAAAADAAAAAgAAAA8PAABTVl9QT1NJVElPTgBURVhDT09SRABDT0xPUgCrT1NHTiwAAAABAAAACAAAACAAAAAAAAAAAAAAAAMAAAAAAAAADwAAAFNWX1RBUkdFVACrq1NIRVi0AAAAUAAAAC0AAABqCAABWgAAAwBgEAAAAAAAWBgABABwEAAAAAAAVVUAAGIQAAMyEBAAAQAAAGIQAAPyEBAAAgAAAGUAAAPyIBAAAAAAAGgAAAIBAAAARQAAi8IAAIBDVRUAEgAQAAAAAABGEBAAAQAAAEZ+EAAAAAAAAGAQAAAAAAA4AAAHgiAQAAAAAAAKABAAAAAAADoQEAACAAAANgAABXIgEAAAAAAARhIQAAIAAAA+AAABU1RBVJQAAAAEAAAAAQAAAAAAAAADAAAAAQAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly ID3D12Device* _device;
    private IDWriteFactory* _dwriteFactory;
    private IDWriteFontCollection* _fontCollection;
    private ID3D12RootSignature* _rootSig;
    private ID3D12PipelineState* _pso;
    private ID3D12DescriptorHeap* _srvHeap;
    private ID3D12Resource* _atlasTexture;
    private ID3D12Resource* _atlasUpload;
    private ID3D12Resource* _vbuf;
    private D3D12_VERTEX_BUFFER_VIEW _vbv;
    private byte[] _vertexShaderBytecode = [];
    private byte[] _pixelShaderBytecode = [];
    private readonly byte[] _atlasPixels = new byte[AtlasRowPitch * AtlasHeight];
    private byte[] _clearTypeScratch = [];
    private byte[] _grayscaleScratch = [];
    private int _rasterScratchResizeCount;
    private readonly Vertex[] _vertices = new Vertex[MaxGlyphVertices];
    private readonly GlyphDrawBatch[] _batches = new GlyphDrawBatch[MaxGlyphDrawBatches];
    private readonly Dictionary<FontFaceKey, CachedFontFace> _fontFaces = [];
    private readonly Dictionary<GlyphKey, GlyphAtlasEntryHandle> _glyphs = [];
    private readonly List<GlyphEntry> _glyphEntries = new(512);
    private int _nextX = AtlasPadding;
    private int _nextY = AtlasPadding;
    private int _rowHeight;
    private bool _atlasDirty;
    private int _dirtyLeft = AtlasWidth;
    private int _dirtyTop = AtlasHeight;
    private int _dirtyRight;
    private int _dirtyBottom;
    private bool _disposed;
    private bool _disabled;
    private DeviceErrorDiagnostic _deviceError = DeviceErrorDiagnostic.None;
    private GlyphAtlasTextRendererDiagnostics _diagnostics;

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

            RunInitializationPhase(GlyphAtlasInitializationPhase.RootSignature, CreateRootSignature);
            RunInitializationPhase(GlyphAtlasInitializationPhase.ShaderCompile, LoadEmbeddedShaderBytecode);
            RunInitializationPhase(GlyphAtlasInitializationPhase.PSO, CreatePSO);
            CreateAtlasResources();
            RunInitializationPhase(GlyphAtlasInitializationPhase.VertexBuffer, CreateVertexBuffer);
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
        return _diagnostics
            .WithCachedGlyphs(_glyphEntries.Count)
            .WithRasterScratch(_clearTypeScratch.Length + _grayscaleScratch.Length, _rasterScratchResizeCount);
    }

    public void ResetDiagnostics()
    {
        _diagnostics = new GlyphAtlasTextRendererDiagnostics(
            _glyphEntries.Count,
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
            RasterScratchResizes: 0);
        _rasterScratchResizeCount = 0;
    }

    public GlyphAtlasRecordResult TryRecord(
        ID3D12GraphicsCommandList* list,
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources,
        int viewportWidth,
        int viewportHeight)
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
            var frame = BuildFrame(textRuns, resources, viewportWidth, viewportHeight);

            if (frame.VertexCount == 0)
            {
                _diagnostics = _diagnostics.WithAtlasRuns(frame.AtlasRunCount);
                RecordDegradation(frame.DegradedRunCount, frame.DegradationReasons);
                return new GlyphAtlasRecordResult(true, frame.AtlasRunCount, frame.DegradedRunCount);
            }

            UploadVertices(_vertices.AsSpan(0, frame.VertexCount));

            if (_atlasDirty)
            {
                UploadAtlas(list);
            }

            DrawGlyphs(list, frame, viewportWidth, viewportHeight);
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
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources,
        int viewportWidth,
        int viewportHeight)
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
            var unsupportedReason = GlyphAtlasTextCompositionHelpers.GetUnsupportedReason(text, style);
            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            if (!TryGetFontFace(style, out var fontFace))
            {
                AddDegradedRun(GlyphAtlasFallbackReason.FontMissing, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var baselineY = ComputeBaselineY(textRun, style, fontFace.Metrics);
            if (!TextMetricsFit(textRun, fontFace.Metrics, style.FontSize))
            {
                AddDegradedRun(GlyphAtlasFallbackReason.Clip, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var lineWidth = ComputeLineWidth(text, fontFace, style, out unsupportedReason);
            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            if (lineWidth > textRun.Width)
            {
                AddDegradedRun(GlyphAtlasFallbackReason.Clip, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var penX = GlyphAtlasTextCompositionHelpers.ComputeAlignedPenX(textRun.X, textRun.Width, style.HorizontalAlignment, lineWidth);
            var maxX = textRun.X + textRun.Width;
            var scissor = ResolveRunScissor(textRun, viewportWidth, viewportHeight);
            if (scissor.IsEmpty)
            {
                AddDegradedRun(GlyphAtlasFallbackReason.Clip, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            var color = new Vector4(textRun.R, textRun.G, textRun.B, textRun.A);
            var batchStart = vertexCount;

            foreach (var character in text)
            {
                if (!TryGetGlyph(fontFace, style, character, out var glyph, out unsupportedReason))
                {
                    break;
                }

                if (penX + glyph.Advance > maxX)
                {
                    unsupportedReason = GlyphAtlasFallbackReason.Clip;
                    break;
                }

                if (glyph.Width > 0 && glyph.Height > 0)
                {
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
                vertexCount = batchStart;
                AddDegradedRun(unsupportedReason, ref degradedRunCount, ref degradationReasons);
                continue;
            }

            if (vertexCount > batchStart)
            {
                if (batchCount >= MaxGlyphDrawBatches)
                {
                    vertexCount = batchStart;
                    AddDegradedRun(GlyphAtlasFallbackReason.BatchLimit, ref degradedRunCount, ref degradationReasons);
                    continue;
                }

                _batches[batchCount++] = new GlyphDrawBatch(batchStart, vertexCount - batchStart, scissor);
            }

            atlasRunCount++;
        }

        return new GlyphFrame(vertexCount, batchCount, atlasRunCount, degradedRunCount, degradationReasons);
    }

    private static void AddDegradedRun(
        GlyphAtlasFallbackReason reason,
        ref int degradedRunCount,
        ref GlyphAtlasFallbackReasonCounts degradationReasons)
    {
        degradedRunCount++;
        degradationReasons = degradationReasons.With(reason);
    }

    private static bool TextMetricsFit(D3D12TextRenderer.TextData textRun, DWRITE_FONT_METRICS metrics, float emSize)
    {
        var scale = emSize / metrics.designUnitsPerEm;
        return (metrics.ascent + metrics.descent) * scale <= textRun.Height;
    }

    private float ComputeLineWidth(ReadOnlySpan<char> text, CachedFontFace fontFace, TextStyle style, out GlyphAtlasFallbackReason unsupportedReason)
    {
        var width = 0f;
        foreach (var character in text)
        {
            if (!TryGetGlyph(fontFace, style, character, out var glyph, out unsupportedReason))
            {
                return 0;
            }

            width += glyph.Advance;
        }

        unsupportedReason = GlyphAtlasFallbackReason.None;
        return width;
    }

    private static IntegerScissorRect ResolveRunScissor(D3D12TextRenderer.TextData textRun, int viewportWidth, int viewportHeight)
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
            return true;
        }

        IDWriteFontFamily* family = null;
        IDWriteFont* font = null;
        IDWriteFontFace* face = null;

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
            family->GetFirstMatchingFont(
                ToDirectWriteFontWeight(style.FontWeight),
                ToDirectWriteFontStretch(style.FontStretch),
                ToDirectWriteFontStyle(style.FontStyle),
                &font);
            font->CreateFontFace(&face);
            face->GetMetrics(out var metrics);
            fontFace = new CachedFontFace(key, face, metrics);
            _fontFaces.Add(key, fontFace);
            face = null;
            return true;
        }
        finally
        {
            if (face != null) face->Release();
            if (font != null) font->Release();
            if (family != null) family->Release();
        }
    }

    private bool TryGetGlyph(
        CachedFontFace fontFace,
        TextStyle style,
        char character,
        out GlyphEntry glyph,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var key = new GlyphKey(fontFace.Key, character);
        if (_glyphs.TryGetValue(key, out var handle) && TryResolveGlyph(handle, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(fontFace, style.FontSize, character, out glyph, out unsupportedReason))
        {
            return false;
        }

        handle = AddGlyphEntry(glyph);
        _glyphs[key] = handle;
        _diagnostics = _diagnostics.WithCachedGlyphs(_glyphEntries.Count);
        return true;
    }

    private GlyphAtlasEntryHandle AddGlyphEntry(in GlyphEntry entry)
    {
        var handle = new GlyphAtlasEntryHandle(_glyphEntries.Count, 1);
        _glyphEntries.Add(entry.WithGeneration(handle.Generation));
        return handle;
    }

    private bool TryResolveGlyph(GlyphAtlasEntryHandle handle, out GlyphEntry entry)
    {
        if (handle.IsNone || (uint)handle.Index >= (uint)_glyphEntries.Count)
        {
            entry = default;
            return false;
        }

        entry = _glyphEntries[handle.Index];
        return entry.Generation == handle.Generation;
    }

    private bool RasterizeGlyph(
        CachedFontFace fontFace,
        float emSize,
        char character,
        out GlyphEntry entry,
        out GlyphAtlasFallbackReason unsupportedReason)
    {
        entry = default;
        unsupportedReason = GlyphAtlasFallbackReason.None;
        var codePoint = (uint)character;
        var glyphIndex = stackalloc ushort[1];
        fontFace.Face->GetGlyphIndices(new ReadOnlySpan<uint>(&codePoint, 1), new Span<ushort>(glyphIndex, 1));
        if (glyphIndex[0] == 0 && character != ' ')
        {
            unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
            return false;
        }

        var advances = stackalloc float[1];
        advances[0] = ComputeGlyphAdvance(fontFace, emSize, glyphIndex[0]);

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

            analysis->GetAlphaTextureBounds(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, out var bounds);
            var width = bounds.right - bounds.left;
            var height = bounds.bottom - bounds.top;
            if (width <= 0 || height <= 0)
            {
                entry = new GlyphEntry(0, 0, 0, 0, advances[0], 0, 0, 0, 0);
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

            if (!TryAllocateGlyph(width, height, out var atlasX, out var atlasY))
            {
                unsupportedReason = GlyphAtlasFallbackReason.AtlasFull;
                return false;
            }

            CopyGlyphToAtlas(grayscale, width, height, atlasX, atlasY);
            var u1 = atlasX / (float)AtlasWidth;
            var v1 = atlasY / (float)AtlasHeight;
            var u2 = (atlasX + width) / (float)AtlasWidth;
            var v2 = (atlasY + height) / (float)AtlasHeight;
            entry = new GlyphEntry(
                width,
                height,
                bounds.left,
                bounds.top,
                advances[0],
                u1,
                v1,
                u2,
                v2);
            MarkAtlasDirty(atlasX, atlasY, width, height);
            return true;
        }
        finally
        {
            if (analysis != null) analysis->Release();
        }
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
        fontFace.Face->GetDesignGlyphMetrics(new ReadOnlySpan<ushort>(glyphIndices, 1), new Span<DWRITE_GLYPH_METRICS>(glyphMetrics, 1), false);
        return glyphMetrics[0].advanceWidth * emSize / fontFace.Metrics.designUnitsPerEm;
    }

    private bool TryAllocateGlyph(int width, int height, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (width + AtlasPadding * 2 > AtlasWidth || height + AtlasPadding * 2 > AtlasHeight)
        {
            return false;
        }

        if (_nextX + width + AtlasPadding > AtlasWidth)
        {
            _nextX = AtlasPadding;
            _nextY += _rowHeight + AtlasPadding;
            _rowHeight = 0;
        }

        if (_nextY + height + AtlasPadding > AtlasHeight)
        {
            return false;
        }

        x = _nextX;
        y = _nextY;
        _nextX += width + AtlasPadding;
        _rowHeight = Math.Max(_rowHeight, height);
        return true;
    }

    private void CopyGlyphToAtlas(ReadOnlySpan<byte> glyphPixels, int width, int height, int atlasX, int atlasY)
    {
        for (var row = 0; row < height; row++)
        {
            glyphPixels.Slice(row * width, width).CopyTo(_atlasPixels.AsSpan((atlasY + row) * AtlasRowPitch + atlasX, width));
        }
    }

    private void MarkAtlasDirty(int x, int y, int width, int height)
    {
        var dirtyRect = GlyphAtlasTextCompositionHelpers.MergeDirtyRect(
            _atlasDirty,
            _dirtyLeft,
            _dirtyTop,
            _dirtyRight,
            _dirtyBottom,
            x,
            y,
            width,
            height);
        _atlasDirty = dirtyRect.HasDirtyRect;
        _dirtyLeft = dirtyRect.Left;
        _dirtyTop = dirtyRect.Top;
        _dirtyRight = dirtyRect.Right;
        _dirtyBottom = dirtyRect.Bottom;
    }

    private void ResetAtlasDirtyRect()
    {
        _atlasDirty = false;
        _dirtyLeft = AtlasWidth;
        _dirtyTop = AtlasHeight;
        _dirtyRight = 0;
        _dirtyBottom = 0;
    }

    private static float ComputeBaselineY(D3D12TextRenderer.TextData textRun, TextStyle style, DWRITE_FONT_METRICS metrics)
    {
        var emSize = style.FontSize;
        var scale = emSize / metrics.designUnitsPerEm;
        var ascent = metrics.ascent * scale;
        var descent = metrics.descent * scale;
        var textHeight = ascent + descent;
        return textRun.Y + style.VerticalAlignment switch
        {
            TextVerticalAlignment.Top => ascent,
            TextVerticalAlignment.Bottom => Math.Max(ascent, textRun.Height - descent),
            _ => ((textRun.Height - textHeight) * 0.5f) + ascent
        };
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

    private void UploadVertices(ReadOnlySpan<Vertex> vertices)
    {
        void* mapped = null;
        try
        {
            _vbuf->Map(0, null, &mapped);
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
            _vbuf->Unmap(0, null);
        }
    }

    private void UploadAtlas(ID3D12GraphicsCommandList* list)
    {
        var dirtyWidth = _dirtyRight - _dirtyLeft;
        var dirtyHeight = _dirtyBottom - _dirtyTop;
        if (dirtyWidth <= 0 || dirtyHeight <= 0)
        {
            ResetAtlasDirtyRect();
            return;
        }

        var uploadRowPitch = AlignUp(dirtyWidth, TextureDataPitchAlignment);
        var uploadBytes = uploadRowPitch * dirtyHeight;
        void* mapped = null;
        try
        {
            _atlasUpload->Map(0, null, &mapped);
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
                _atlasPixels.AsSpan((_dirtyTop + row) * AtlasRowPitch + _dirtyLeft, dirtyWidth)
                    .CopyTo(destination.Slice(row * uploadRowPitch, dirtyWidth));
            }
        }
        finally
        {
            _atlasUpload->Unmap(0, null);
        }

        var toCopyDest = Transition(_atlasTexture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
        list->ResourceBarrier(1, &toCopyDest);

        var src = new D3D12_TEXTURE_COPY_LOCATION
        {
            pResource = _atlasUpload,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT
        };
        src.Anonymous.PlacedFootprint = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT
        {
            Offset = 0,
            Footprint = new D3D12_SUBRESOURCE_FOOTPRINT
            {
                Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
                Width = (uint)dirtyWidth,
                Height = (uint)dirtyHeight,
                Depth = 1,
                RowPitch = (uint)uploadRowPitch
            }
        };

        var dst = new D3D12_TEXTURE_COPY_LOCATION
        {
            pResource = _atlasTexture,
            Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX
        };
        dst.Anonymous.SubresourceIndex = 0;
        list->CopyTextureRegion(dst, (uint)_dirtyLeft, (uint)_dirtyTop, 0, src, null);

        var toShaderResource = Transition(_atlasTexture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        list->ResourceBarrier(1, &toShaderResource);

        ResetAtlasDirtyRect();
        _diagnostics = _diagnostics.WithUploadedBytes(uploadBytes);
    }

    private void DrawGlyphs(ID3D12GraphicsCommandList* list, GlyphFrame frame, int viewportWidth, int viewportHeight)
    {
        var viewport = new D3D12_VIEWPORT { Width = viewportWidth, Height = viewportHeight, MaxDepth = 1.0f };
        list->RSSetViewports(1, &viewport);

        list->SetPipelineState(_pso);
        list->SetGraphicsRootSignature(_rootSig);
        var heap = _srvHeap;
        list->SetDescriptorHeaps(1, &heap);
        list->SetGraphicsRootDescriptorTable(0, _srvHeap->GetGPUDescriptorHandleForHeapStart());
        list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var vbv = _vbv;
        list->IASetVertexBuffers(0, 1, &vbv);

        for (var i = 0; i < frame.BatchCount; i++)
        {
            var batch = _batches[i];
            var scissor = ToRect(batch.Scissor);
            list->RSSetScissorRects(1, &scissor);
            list->DrawInstanced((uint)batch.VertexCount, 1, (uint)batch.StartVertex, 0);
        }
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
        if (_vertexShaderBytecode.Length == 0 || _pixelShaderBytecode.Length == 0)
        {
            throw new InvalidOperationException("Glyph atlas embedded shader bytecode is empty.");
        }

        fixed (byte* vs = _vertexShaderBytecode)
        fixed (byte* ps = _pixelShaderBytecode)
        {
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
                desc.PS.BytecodeLength = (nuint)_pixelShaderBytecode.Length;
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
                    throw WrapD3D12Exception("D3D12GlyphAtlasTextRenderer.CreateGraphicsPipelineState", ex);
                }

                if (psoObj == null)
                {
                    throw new InvalidOperationException("D3D12GlyphAtlasTextRenderer.CreateGraphicsPipelineState returned a null PSO.");
                }

                _pso = (ID3D12PipelineState*)psoObj;
            }
        }
    }

    private void LoadEmbeddedShaderBytecode()
    {
        var (vertexShader, pixelShader) = DecodeEmbeddedShaderBytecode();
        _vertexShaderBytecode = vertexShader;
        _pixelShaderBytecode = pixelShader;
    }

    internal static (int VertexBytes, int PixelBytes, byte[] VertexHeader, byte[] PixelHeader) GetEmbeddedShaderBytecodeLengths()
    {
        var (vertexShader, pixelShader) = DecodeEmbeddedShaderBytecode();
        return (
            vertexShader.Length,
            pixelShader.Length,
            vertexShader[..Math.Min(4, vertexShader.Length)],
            pixelShader[..Math.Min(4, pixelShader.Length)]);
    }

    private static (byte[] VertexShader, byte[] PixelShader) DecodeEmbeddedShaderBytecode()
    {
        try
        {
            var vertexShader = Convert.FromBase64String(VertexShaderBytecodeBase64);
            var pixelShader = Convert.FromBase64String(PixelShaderBytecodeBase64);
            if (vertexShader.Length == 0 || pixelShader.Length == 0)
            {
                throw new InvalidOperationException("Glyph atlas embedded shader bytecode is empty.");
            }

            return (vertexShader, pixelShader);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Glyph atlas embedded shader bytecode is not valid base64.", ex);
        }
    }

    private void CreateAtlasResources()
    {
        var defaultHeap = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT };
        var textureDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = AtlasWidth,
            Height = AtlasHeight,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
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

            _atlasTexture = (ID3D12Resource*)textureObj;
        });

        var uploadHeap = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD };
        var uploadDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = (ulong)_atlasPixels.Length,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR
        };
        RunInitializationPhase(GlyphAtlasInitializationPhase.UploadBuffer, () =>
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

            _atlasUpload = (ID3D12Resource*)uploadObj;
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

            _srvHeap = (ID3D12DescriptorHeap*)heapObj;
        });

        var srvDesc = new D3D12_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
            ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping = Shader4ComponentMapping
        };
        srvDesc.Anonymous.Texture2D.MipLevels = 1;
        RunInitializationPhase(GlyphAtlasInitializationPhase.ShaderResourceView, () =>
        {
            _device->CreateShaderResourceView(_atlasTexture, srvDesc, _srvHeap->GetCPUDescriptorHandleForHeapStart());
        });
    }

    private void CreateVertexBuffer()
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

        _vbuf = (ID3D12Resource*)resObj;
        _vbv.BufferLocation = _vbuf->GetGPUVirtualAddress();
        _vbv.SizeInBytes = (uint)(MaxGlyphVertices * sizeof(Vertex));
        _vbv.StrideInBytes = (uint)sizeof(Vertex);
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var fontFace in _fontFaces.Values)
        {
            fontFace.Face->Release();
        }

        if (_vbuf != null) _vbuf->Release();
        if (_atlasUpload != null) _atlasUpload->Release();
        if (_atlasTexture != null) _atlasTexture->Release();
        if (_srvHeap != null) _srvHeap->Release();
        if (_pso != null) _pso->Release();
        if (_rootSig != null) _rootSig->Release();
        if (_fontCollection != null) _fontCollection->Release();
        if (_dwriteFactory != null) _dwriteFactory->Release();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
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

    private readonly struct GlyphDrawBatch(int StartVertex, int VertexCount, IntegerScissorRect Scissor)
    {
        public int StartVertex { get; } = StartVertex;
        public int VertexCount { get; } = VertexCount;
        public IntegerScissorRect Scissor { get; } = Scissor;
    }

    public enum GlyphAtlasInitializationPhase : byte
    {
        None,
        DirectWriteFactory,
        FontCollection,
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
        VertexBufferMap,
        AtlasUploadMap
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
    public enum GlyphAtlasFallbackReason : ushort
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
        RecordFailed = 1 << 10
    }

    public readonly struct GlyphAtlasFallbackReasonCounts(
        int NonAscii,
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
                NonAscii + (reason.HasFlag(GlyphAtlasFallbackReason.NonAscii) ? 1 : 0),
                Clip + (reason.HasFlag(GlyphAtlasFallbackReason.Clip) ? 1 : 0),
                Wrapping + (reason.HasFlag(GlyphAtlasFallbackReason.Wrapping) ? 1 : 0),
                Alignment + (reason.HasFlag(GlyphAtlasFallbackReason.Alignment) ? 1 : 0),
                AtlasFull + (reason.HasFlag(GlyphAtlasFallbackReason.AtlasFull) ? 1 : 0),
                VertexLimit + (reason.HasFlag(GlyphAtlasFallbackReason.VertexLimit) ? 1 : 0),
                FontMissing + (reason.HasFlag(GlyphAtlasFallbackReason.FontMissing) ? 1 : 0),
                CompileFailed + (reason.HasFlag(GlyphAtlasFallbackReason.CompileFailed) ? 1 : 0),
                BatchLimit + (reason.HasFlag(GlyphAtlasFallbackReason.BatchLimit) ? 1 : 0),
                InitializationFailed + (reason.HasFlag(GlyphAtlasFallbackReason.InitializationFailed) ? 1 : 0),
                RecordFailed + (reason.HasFlag(GlyphAtlasFallbackReason.RecordFailed) ? 1 : 0));
        }

        public GlyphAtlasFallbackReasonCounts Add(GlyphAtlasFallbackReasonCounts other)
        {
            return new GlyphAtlasFallbackReasonCounts(
                NonAscii + other.NonAscii,
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
            return $"NonAscii={NonAscii}, Clip={Clip}, Wrapping={Wrapping}, Alignment={Alignment}, AtlasFull={AtlasFull}, VertexLimit={VertexLimit}, FontMissing={FontMissing}, CompileFailed={CompileFailed}, BatchLimit={BatchLimit}, InitializationFailed={InitializationFailed}, RecordFailed={RecordFailed}";
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

    private sealed class CachedFontFace(FontFaceKey key, IDWriteFontFace* face, DWRITE_FONT_METRICS metrics)
    {
        public FontFaceKey Key { get; } = key;
        public IDWriteFontFace* Face { get; } = face;
        public DWRITE_FONT_METRICS Metrics { get; } = metrics;
    }

    private readonly struct GlyphKey(FontFaceKey FontFace, char Character) : IEquatable<GlyphKey>
    {
        public FontFaceKey FontFace { get; } = FontFace;
        public char Character { get; } = Character;

        public bool Equals(GlyphKey other) => FontFace.Equals(other.FontFace) && Character == other.Character;

        public override bool Equals(object? obj) => obj is GlyphKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FontFace, Character);
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
        float Width,
        float Height,
        float OffsetX,
        float OffsetY,
        float Advance,
        float U1,
        float V1,
        float U2,
        float V2,
        int Generation = 0)
    {
        public float Width { get; } = Width;
        public float Height { get; } = Height;
        public float OffsetX { get; } = OffsetX;
        public float OffsetY { get; } = OffsetY;
        public float Advance { get; } = Advance;
        public float U1 { get; } = U1;
        public float V1 { get; } = V1;
        public float U2 { get; } = U2;
        public float V2 { get; } = V2;
        public int Generation { get; } = Generation;

        public GlyphEntry WithGeneration(int generation) => new(Width, Height, OffsetX, OffsetY, Advance, U1, V1, U2, V2, generation);
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
        int AtlasRuns = 0,
        int OverlayFallbackRuns = 0,
        int DegradedRuns = 0) : IEquatable<GlyphAtlasTextRendererDiagnostics>
    {
        public int CachedGlyphs { get; } = CachedGlyphs;
        public long UploadedBytes { get; } = UploadedBytes;
        public int DrawnGlyphs { get; } = DrawnGlyphs;
        public int CacheHits { get; } = CacheHits;
        public int CacheMisses { get; } = CacheMisses;
        public int FallbackFrames { get; } = FallbackFrames;
        public int UnsupportedRuns { get; } = UnsupportedRuns;
        public int AtlasRuns { get; } = AtlasRuns;
        public int OverlayFallbackRuns { get; } = OverlayFallbackRuns;
        public int DegradedRuns { get; } = DegradedRuns;
        public GlyphAtlasFallbackReasonCounts Reasons { get; } = Reasons;
        public GlyphAtlasInitializationPhase InitializationFailurePhase { get; } = InitializationFailurePhase;
        public GlyphAtlasRecordFailurePhase RecordFailurePhase { get; } = RecordFailurePhase;
        public int RasterScratchBytes { get; } = RasterScratchBytes;
        public int RasterScratchResizes { get; } = RasterScratchResizes;

        public GlyphAtlasTextRendererDiagnostics WithCachedGlyphs(int cachedGlyphs) =>
            new(cachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithCacheHit() =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits + 1, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithCacheMiss() =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses + 1, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithDrawnGlyphs(int glyphs) =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs + glyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithAtlasRuns(int atlasRuns) =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns + atlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithUploadedBytes(long bytes) =>
            new(CachedGlyphs, UploadedBytes + bytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithRasterScratch(int bytes, int resizes) =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, RecordFailurePhase, bytes, resizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithInitializationFailure(GlyphAtlasInitializationPhase phase) =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, phase, RecordFailurePhase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

        public GlyphAtlasTextRendererDiagnostics WithRecordFailure(GlyphAtlasRecordFailurePhase phase) =>
            new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons, InitializationFailurePhase, phase, RasterScratchBytes, RasterScratchResizes, AtlasRuns, OverlayFallbackRuns, DegradedRuns);

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
                AtlasRuns,
                OverlayFallbackRuns,
                DegradedRuns + unsupportedRuns);
        }

        public string FormatSummary()
        {
            return $"cachedGlyphs={CachedGlyphs}, drawnGlyphs={DrawnGlyphs}, atlasRuns={AtlasRuns}, overlayFallbackRuns={OverlayFallbackRuns}, degradedRuns={DegradedRuns}, "
                + $"uploads={UploadedBytes} bytes, hits={CacheHits}, misses={CacheMisses}, fallbacks={FallbackFrames}, unsupportedRuns={UnsupportedRuns}, reasons=[{Reasons}], "
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
                && AtlasRuns == other.AtlasRuns
                && OverlayFallbackRuns == other.OverlayFallbackRuns
                && DegradedRuns == other.DegradedRuns
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
            hash.Add(AtlasRuns);
            hash.Add(OverlayFallbackRuns);
            hash.Add(DegradedRuns);
            hash.Add(Reasons);
            hash.Add(InitializationFailurePhase);
            hash.Add(RecordFailurePhase);
            hash.Add(RasterScratchBytes);
            hash.Add(RasterScratchResizes);
            return hash.ToHashCode();
        }
    }

}
