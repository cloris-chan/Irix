using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal sealed unsafe partial class D3D12GlyphAtlasTextRenderer
{
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
        if (_atlasPages.Count >= AtlasPageBudget)
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
}

