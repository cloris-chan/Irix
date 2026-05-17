using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
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

    private readonly ID3D12Device* _device;
    private readonly IDWriteFactory* _dwriteFactory;
    private IDWriteFontCollection* _fontCollection;
    private ID3D12RootSignature* _rootSig;
    private ID3D12PipelineState* _pso;
    private ID3D12DescriptorHeap* _srvHeap;
    private ID3D12Resource* _atlasTexture;
    private ID3D12Resource* _atlasUpload;
    private ID3D12Resource* _vbuf;
    private D3D12_VERTEX_BUFFER_VIEW _vbv;
    private readonly byte[] _atlasPixels = new byte[AtlasRowPitch * AtlasHeight];
    private readonly Vertex[] _vertices = new Vertex[MaxGlyphVertices];
    private readonly GlyphDrawBatch[] _batches = new GlyphDrawBatch[MaxGlyphDrawBatches];
    private readonly Dictionary<FontFaceKey, CachedFontFace> _fontFaces = [];
    private readonly Dictionary<GlyphKey, GlyphEntry> _glyphs = [];
    private int _nextX = AtlasPadding;
    private int _nextY = AtlasPadding;
    private int _rowHeight;
    private bool _atlasDirty;
    private int _dirtyLeft = AtlasWidth;
    private int _dirtyTop = AtlasHeight;
    private int _dirtyRight;
    private int _dirtyBottom;
    private bool _disposed;
    private bool _deviceRemoved;
    private string? _deviceErrorReason;
    private GlyphAtlasTextRendererDiagnostics _diagnostics;

    public D3D12GlyphAtlasTextRenderer(ID3D12Device* device)
    {
        _device = device;

        PInvoke.DWriteCreateFactory(
            DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
            typeof(IDWriteFactory).GUID,
            out var dwriteFactoryObject).ThrowOnFailure();
        _dwriteFactory = (IDWriteFactory*)dwriteFactoryObject;
        IDWriteFontCollection* fontCollection;
        _dwriteFactory->GetSystemFontCollection(&fontCollection, false);
        _fontCollection = fontCollection;

        CreateRootSignature();
        CreatePSO();
        CreateAtlasResources();
        CreateVertexBuffer();
    }

    public bool IsDeviceRemoved => _deviceRemoved;
    public string? DeviceErrorReason => _deviceErrorReason;

    public GlyphAtlasTextRendererDiagnostics GetDiagnostics() => _diagnostics.WithCachedGlyphs(_glyphs.Count);

    public void ResetDiagnostics()
    {
        _diagnostics = new GlyphAtlasTextRendererDiagnostics(
            _glyphs.Count,
            UploadedBytes: 0,
            DrawnGlyphs: 0,
            CacheHits: 0,
            CacheMisses: 0,
            FallbackFrames: 0,
            UnsupportedRuns: 0,
            Reasons: default);
    }

    public bool TryRecord(
        ID3D12GraphicsCommandList* list,
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources,
        int viewportWidth,
        int viewportHeight)
    {
        if (_deviceRemoved || textRuns.Length == 0)
        {
            return false;
        }

        try
        {
            var frame = BuildFrame(textRuns, resources, viewportWidth, viewportHeight);
            if (!frame.CanUseGlyphAtlas)
            {
                RecordFallback(frame.UnsupportedRunCount, frame.UnsupportedReason);
                return false;
            }

            if (frame.VertexCount == 0)
            {
                return true;
            }

            UploadVertices(_vertices.AsSpan(0, frame.VertexCount));

            if (_atlasDirty)
            {
                UploadAtlas(list);
            }

            DrawGlyphs(list, frame, viewportWidth, viewportHeight);
            _diagnostics = _diagnostics.WithDrawnGlyphs(frame.VertexCount / 6);
            return true;
        }
        catch (COMException ex)
        {
            MarkDeviceRemoved($"Glyph atlas COMException: 0x{ex.ErrorCode:X8}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            MarkDeviceRemoved($"Glyph atlas invalid operation: {ex.Message}");
            return false;
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
        var unsupportedReason = GlyphAtlasFallbackReason.None;

        foreach (var textRun in textRuns)
        {
            var runResolver = textRun.Resolver ?? resources;
            var text = runResolver.Resolve(textRun.Text);
            if (text.IsEmpty || textRun.Width <= 0 || textRun.Height <= 0)
            {
                continue;
            }

            var style = (textRun.ResolvedStyle != default ? textRun.ResolvedStyle : runResolver.ResolveTextStyle(textRun.Style)).Normalize();
            unsupportedReason = GetUnsupportedReason(text, style);
            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                break;
            }

            if (!TryGetFontFace(style, out var fontFace))
            {
                unsupportedReason = GlyphAtlasFallbackReason.FontMissing;
                break;
            }

            var baselineY = ComputeBaselineY(textRun, style, fontFace.Metrics);
            if (!TextMetricsFit(textRun, fontFace.Metrics, style.FontSize))
            {
                unsupportedReason = GlyphAtlasFallbackReason.Clip;
                break;
            }

            var lineWidth = ComputeLineWidth(text, fontFace, style, out unsupportedReason);
            if (unsupportedReason != GlyphAtlasFallbackReason.None)
            {
                break;
            }

            if (lineWidth > textRun.Width)
            {
                unsupportedReason = GlyphAtlasFallbackReason.Clip;
                break;
            }

            var penX = ComputeAlignedPenX(textRun, style, lineWidth);
            var maxX = textRun.X + textRun.Width;
            var scissor = ResolveRunScissor(textRun, viewportWidth, viewportHeight);
            if (scissor.IsEmpty)
            {
                unsupportedReason = GlyphAtlasFallbackReason.Clip;
                break;
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
                break;
            }

            if (vertexCount > batchStart)
            {
                if (batchCount >= MaxGlyphDrawBatches)
                {
                    unsupportedReason = GlyphAtlasFallbackReason.BatchLimit;
                    break;
                }

                _batches[batchCount++] = new GlyphDrawBatch(batchStart, vertexCount - batchStart, scissor);
            }
        }

        return new GlyphFrame(vertexCount, batchCount, unsupportedReason);
    }

    private static bool TextMetricsFit(D3D12TextRenderer.TextData textRun, DWRITE_FONT_METRICS metrics, float emSize)
    {
        var scale = emSize / metrics.designUnitsPerEm;
        return (metrics.ascent + metrics.descent) * scale <= textRun.Height;
    }

    private static GlyphAtlasFallbackReason GetUnsupportedReason(ReadOnlySpan<char> text, TextStyle style)
    {
        if (style.Wrapping != TextWrapping.NoWrap)
        {
            return GlyphAtlasFallbackReason.Wrapping;
        }

        foreach (var character in text)
        {
            if (character is < ' ' or > '~')
            {
                return GlyphAtlasFallbackReason.NonAscii;
            }
        }

        return GlyphAtlasFallbackReason.None;
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

    private static float ComputeAlignedPenX(D3D12TextRenderer.TextData textRun, TextStyle style, float lineWidth)
    {
        return style.HorizontalAlignment switch
        {
            TextHorizontalAlignment.Center => textRun.X + MathF.Max(0, (textRun.Width - lineWidth) * 0.5f),
            TextHorizontalAlignment.Trailing => textRun.X + MathF.Max(0, textRun.Width - lineWidth),
            _ => textRun.X
        };
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
            _fontCollection->FindFamilyName(style.FontFamily, out var familyIndex, out var exists);
            if (!exists)
            {
                _fontCollection->FindFamilyName(TextStyle.Default.FontFamily, out familyIndex, out exists);
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
        if (_glyphs.TryGetValue(key, out glyph))
        {
            _diagnostics = _diagnostics.WithCacheHit();
            return true;
        }

        _diagnostics = _diagnostics.WithCacheMiss();
        if (!RasterizeGlyph(fontFace, style.FontSize, character, out glyph, out unsupportedReason))
        {
            return false;
        }

        _glyphs.Add(key, glyph);
        _diagnostics = _diagnostics.WithCachedGlyphs(_glyphs.Count);
        return true;
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
            var clearType = new byte[clearTypeBytes];
            analysis->CreateAlphaTexture(DWRITE_TEXTURE_TYPE.DWRITE_TEXTURE_CLEARTYPE_3x1, bounds, clearType);

            var grayscale = new byte[width * height];
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

    private void CopyGlyphToAtlas(byte[] glyphPixels, int width, int height, int atlasX, int atlasY)
    {
        for (var row = 0; row < height; row++)
        {
            glyphPixels.AsSpan(row * width, width).CopyTo(_atlasPixels.AsSpan((atlasY + row) * AtlasRowPitch + atlasX, width));
        }
    }

    private void MarkAtlasDirty(int x, int y, int width, int height)
    {
        _atlasDirty = true;
        _dirtyLeft = Math.Min(_dirtyLeft, x);
        _dirtyTop = Math.Min(_dirtyTop, y);
        _dirtyRight = Math.Max(_dirtyRight, x + width);
        _dirtyBottom = Math.Max(_dirtyBottom, y + height);
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
        void* mapped;
        _vbuf->Map(0, null, &mapped);
        vertices.CopyTo(new Span<Vertex>(mapped, MaxGlyphVertices));
        _vbuf->Unmap(0, null);
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
        void* mapped;
        _atlasUpload->Map(0, null, &mapped);
        var destination = new Span<byte>(mapped, uploadBytes);
        for (var row = 0; row < dirtyHeight; row++)
        {
            _atlasPixels.AsSpan((_dirtyTop + row) * AtlasRowPitch + _dirtyLeft, dirtyWidth)
                .CopyTo(destination.Slice(row * uploadRowPitch, dirtyWidth));
        }
        _atlasUpload->Unmap(0, null);

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

        ID3DBlob* sig;
        ID3DBlob* err;
        Check(PInvoke.D3D12SerializeRootSignature(desc, D3D_ROOT_SIGNATURE_VERSION.D3D_ROOT_SIGNATURE_VERSION_1, &sig, &err), "D3D12SerializeRootSignature");
        try
        {
            _device->CreateRootSignature(0, sig->GetBufferPointer(), sig->GetBufferSize(), typeof(ID3D12RootSignature).GUID, out var obj);
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
        var vs = CompileShader(VsHlsl, "VSMain", "vs_5_0");
        var ps = CompileShader(PsHlsl, "PSMain", "ps_5_0");
        try
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
                desc.VS.pShaderBytecode = vs->GetBufferPointer();
                desc.VS.BytecodeLength = vs->GetBufferSize();
                desc.PS.pShaderBytecode = ps->GetBufferPointer();
                desc.PS.BytecodeLength = ps->GetBufferSize();
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

                _device->CreateGraphicsPipelineState(desc, typeof(ID3D12PipelineState).GUID, out var psoObj);
                _pso = (ID3D12PipelineState*)psoObj;
            }
        }
        finally
        {
            if (vs != null) vs->Release();
            if (ps != null) ps->Release();
        }
    }

    private ID3DBlob* CompileShader(string source, string entryPoint, string profile)
    {
        var srcBytes = System.Text.Encoding.UTF8.GetBytes(source);
        fixed (byte* pSrc = srcBytes)
        {
            var ep = stackalloc sbyte[entryPoint.Length + 1];
            for (var i = 0; i < entryPoint.Length; i++) ep[i] = (sbyte)entryPoint[i];
            ep[entryPoint.Length] = 0;

            var prof = stackalloc sbyte[profile.Length + 1];
            for (var i = 0; i < profile.Length; i++) prof[i] = (sbyte)profile[i];
            prof[profile.Length] = 0;

            ID3DBlob* blob;
            ID3DBlob* err;
            var hr = D3DCompile(pSrc, (nuint)srcBytes.Length, null, null, null, ep, prof, 0, 0, &blob, &err);
            if (hr < 0)
            {
                var msg = err != null ? new string((sbyte*)err->GetBufferPointer()) : $"D3DCompile failed: 0x{hr:X8}";
                if (err != null) err->Release();
                throw new InvalidOperationException(msg);
            }

            return blob;
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

        _device->CreateCommittedResource(
            defaultHeap,
            D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
            textureDesc,
            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
            null,
            typeof(ID3D12Resource).GUID,
            out var textureObj);
        _atlasTexture = (ID3D12Resource*)textureObj;

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
        _device->CreateCommittedResource(
            uploadHeap,
            D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
            uploadDesc,
            D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
            null,
            typeof(ID3D12Resource).GUID,
            out var uploadObj);
        _atlasUpload = (ID3D12Resource*)uploadObj;

        var heapDesc = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            NumDescriptors = 1,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV,
            Flags = D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE
        };
        _device->CreateDescriptorHeap(heapDesc, typeof(ID3D12DescriptorHeap).GUID, out var heapObj);
        _srvHeap = (ID3D12DescriptorHeap*)heapObj;

        var srvDesc = new D3D12_SHADER_RESOURCE_VIEW_DESC
        {
            Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
            ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
            Shader4ComponentMapping = Shader4ComponentMapping
        };
        srvDesc.Anonymous.Texture2D.MipLevels = 1;
        _device->CreateShaderResourceView(_atlasTexture, srvDesc, _srvHeap->GetCPUDescriptorHandleForHeapStart());
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
        _device->CreateCommittedResource(heapProps, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
            resDesc, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ,
            null, typeof(ID3D12Resource).GUID, out var resObj);
        _vbuf = (ID3D12Resource*)resObj;
        _vbv.BufferLocation = _vbuf->GetGPUVirtualAddress();
        _vbv.SizeInBytes = (uint)(MaxGlyphVertices * sizeof(Vertex));
        _vbv.StrideInBytes = (uint)sizeof(Vertex);
    }

    private void RecordFallback(int unsupportedRuns, GlyphAtlasFallbackReason reason)
    {
        _diagnostics = _diagnostics.WithFallback(unsupportedRuns, reason);
    }

    private void MarkDeviceRemoved(string reason)
    {
        _deviceRemoved = true;
        _deviceErrorReason = reason;
        System.Diagnostics.Debug.WriteLine($"[D3D12GlyphAtlasTextRenderer] {reason}");
    }

    private static void Check(HRESULT hr, string context)
    {
        if (hr.Failed)
        {
            throw new COMException($"{context} failed: 0x{unchecked((uint)hr.Value):X8}", hr.Value);
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

    [DllImport("d3dcompiler_47.dll")]
    private static extern int D3DCompile(
        void* pSrcData, nuint srcDataSize,
        void* pSourceName, void* pDefines, void* pInclude,
        sbyte* pEntrypoint, sbyte* pTarget,
        uint flags1, uint flags2,
        ID3DBlob** ppCode, ID3DBlob** ppErrorMsgs);

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
    }

    private readonly struct GlyphFrame(int VertexCount, int BatchCount, GlyphAtlasFallbackReason UnsupportedReason)
    {
        public int VertexCount { get; } = VertexCount;
        public int BatchCount { get; } = BatchCount;
        public bool CanUseGlyphAtlas { get; } = UnsupportedReason == GlyphAtlasFallbackReason.None;
        public int UnsupportedRunCount { get; } = UnsupportedReason == GlyphAtlasFallbackReason.None ? 0 : 1;
        public GlyphAtlasFallbackReason UnsupportedReason { get; } = UnsupportedReason;
    }

    private readonly struct GlyphDrawBatch(int StartVertex, int VertexCount, IntegerScissorRect Scissor)
    {
        public int StartVertex { get; } = StartVertex;
        public int VertexCount { get; } = VertexCount;
        public IntegerScissorRect Scissor { get; } = Scissor;
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
        InitializationFailed = 1 << 9
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
        int InitializationFailed) : IEquatable<GlyphAtlasFallbackReasonCounts>
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
                InitializationFailed + (reason.HasFlag(GlyphAtlasFallbackReason.InitializationFailed) ? 1 : 0));
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
                && InitializationFailed == other.InitializationFailed;
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
            return hash.ToHashCode();
        }

        public override string ToString()
        {
            return $"NonAscii={NonAscii}, Clip={Clip}, Wrapping={Wrapping}, Alignment={Alignment}, AtlasFull={AtlasFull}, VertexLimit={VertexLimit}, FontMissing={FontMissing}, CompileFailed={CompileFailed}, BatchLimit={BatchLimit}, InitializationFailed={InitializationFailed}";
        }
    }

    private readonly struct FontFaceKey(string Family, TextFontWeight Weight, TextFontStyle Style, TextFontStretch Stretch, float EmSize) : IEquatable<FontFaceKey>
    {
        public string Family { get; } = Family;
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

    private readonly struct GlyphEntry(
        float Width,
        float Height,
        float OffsetX,
        float OffsetY,
        float Advance,
        float U1,
        float V1,
        float U2,
        float V2)
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
    }

    public readonly struct GlyphAtlasTextRendererDiagnostics(
        int CachedGlyphs,
        long UploadedBytes,
        int DrawnGlyphs,
        int CacheHits,
        int CacheMisses,
        int FallbackFrames,
        int UnsupportedRuns,
        GlyphAtlasFallbackReasonCounts Reasons) : IEquatable<GlyphAtlasTextRendererDiagnostics>
    {
        public int CachedGlyphs { get; } = CachedGlyphs;
        public long UploadedBytes { get; } = UploadedBytes;
        public int DrawnGlyphs { get; } = DrawnGlyphs;
        public int CacheHits { get; } = CacheHits;
        public int CacheMisses { get; } = CacheMisses;
        public int FallbackFrames { get; } = FallbackFrames;
        public int UnsupportedRuns { get; } = UnsupportedRuns;
        public GlyphAtlasFallbackReasonCounts Reasons { get; } = Reasons;

        public GlyphAtlasTextRendererDiagnostics WithCachedGlyphs(int cachedGlyphs) => new(cachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons);
        public GlyphAtlasTextRendererDiagnostics WithCacheHit() => new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits + 1, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons);
        public GlyphAtlasTextRendererDiagnostics WithCacheMiss() => new(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses + 1, FallbackFrames, UnsupportedRuns, Reasons);
        public GlyphAtlasTextRendererDiagnostics WithDrawnGlyphs(int glyphs) => new(CachedGlyphs, UploadedBytes, DrawnGlyphs + glyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons);
        public GlyphAtlasTextRendererDiagnostics WithUploadedBytes(long bytes) => new(CachedGlyphs, UploadedBytes + bytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons);
        public GlyphAtlasTextRendererDiagnostics WithFallback(int unsupportedRuns, GlyphAtlasFallbackReason reason)
        {
            return new GlyphAtlasTextRendererDiagnostics(
                CachedGlyphs,
                UploadedBytes,
                DrawnGlyphs,
                CacheHits,
                CacheMisses,
                FallbackFrames + 1,
                UnsupportedRuns + unsupportedRuns,
                reason == GlyphAtlasFallbackReason.None ? Reasons : Reasons.With(reason));
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
                && Reasons.Equals(other.Reasons);
        }

        public override bool Equals(object? obj) => obj is GlyphAtlasTextRendererDiagnostics other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(CachedGlyphs, UploadedBytes, DrawnGlyphs, CacheHits, CacheMisses, FallbackFrames, UnsupportedRuns, Reasons);
    }

    private const string VsHlsl = @"
struct VS_IN { float2 pos : POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
struct VS_OUT { float4 pos : SV_POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
VS_OUT VSMain(VS_IN i)
{
    VS_OUT o;
    o.pos = float4(i.pos, 0.0f, 1.0f);
    o.uv = i.uv;
    o.col = i.col;
    return o;
}
";

    private const string PsHlsl = @"
Texture2D<float> Atlas : register(t0);
SamplerState AtlasSampler : register(s0);
struct PS_IN { float4 pos : SV_POSITION; float2 uv : TEXCOORD; float4 col : COLOR; };
float4 PSMain(PS_IN i) : SV_TARGET
{
    float coverage = Atlas.Sample(AtlasSampler, i.uv);
    return float4(i.col.rgb, i.col.a * coverage);
}
";
}
