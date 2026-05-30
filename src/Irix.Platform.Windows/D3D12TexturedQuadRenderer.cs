using System.Numerics;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal sealed unsafe class D3D12TexturedQuadRenderer : IDisposable
{
    private const int UploadFrameCount = 2;
    private const int MaxQuads = 256;
    private const int MaxVerts = MaxQuads * 6;
    private const string VertexShaderBytecodeBase64 =
        "RFhCQ0hOwF2N9gtTu/HrKRNRsV8BAAAA4AIAAAUAAAA0AAAAoAAAABABAACEAQAARAIAAFJERUZkAAAAAAAAAAAAAAAAAAAAPAAAAAAF/v8AgQAAPAAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAElTR05oAAAAAwAAAAgAAABQAAAAAAAAAAAAAAADAAAAAAAAAAMDAABZAAAAAAAAAAAAAAADAAAAAQAAAAMDAABiAAAAAAAAAAAAAAADAAAAAgAAAA8PAABQT1NJVElPTgBURVhDT09SRABDT0xPUgBPU0dObAAAAAMAAAAIAAAAUAAAAAAAAAABAAAAAwAAAAAAAAAPAAAAXAAAAAAAAAAAAAAAAwAAAAEAAAADDAAAZQAAAAAAAAAAAAAAAwAAAAIAAAAPAAAAU1ZfUE9TSVRJT04AVEVYQ09PUkQAQ09MT1IAq1NIRVi4AAAAUAABAC4AAABqCAABXwAAAzIQEAAAAAAAXwAAAzIQEAABAAAAXwAAA/IQEAACAAAAZwAABPIgEAAAAAAAAQAAAGUAAAMyIBAAAQAAAGUAAAPyIBAAAgAAADYAAAUyIBAAAAAAAEYQEAAAAAAANgAACMIgEAAAAAAAAkAAAAAAAAAAAAAAAAAAAAAAgD82AAAFMiAQAAEAAABGEBAAAQAAADYAAAXyIBAAAgAAAEYeEAACAAAAPgAAAVNUQVSUAAAABQAAAAAAAAAAAAAABgAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
    private const string PixelShaderBytecodeBase64 =
        "RFhCQ6RFWgCEelPeWMrtfrsGtrUBAAAAPAMAAAUAAAA0AAAA9AAAAGgBAACcAQAAoAIAAFJERUa4AAAAAAAAAAAAAAACAAAAPAAAAAAF//8AAQAAjwAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAAfAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAEAAACJAAAAAgAAAAUAAAAEAAAA/////wAAAAABAAAADQAAAEF0bGFzU2FtcGxlcgBBdGxhcwBNaWNyb3NvZnQgKFIpIEhMU0wgU2hhZGVyIENvbXBpbGVyIDEwLjEAq0lTR05sAAAAAwAAAAgAAABQAAAAAAAAAAEAAAADAAAAAAAAAA8AAABcAAAAAAAAAAAAAAADAAAAAQAAAAMDAABlAAAAAAAAAAAAAAADAAAAAgAAAA8IAABTVl9QT1NJVElPTgBURVhDT09SRABDT0xPUgCrT1NHTiwAAAABAAAACAAAACAAAAAAAAAAAAAAAAMAAAAAAAAADwAAAFNWX1RBUkdFVACrq1NIRVj8AAAAUAAAAD8AAABqCAABWgAAAwBgEAAAAAAAWBgABABwEAAAAAAAVVUAAGIQAAMyEBAAAQAAAGIQAAOCEBAAAgAAAGUAAAPyIBAAAAAAAGgAAAICAAAARQAAi8IAAIBDVRUA8gAQAAAAAABGEBAAAQAAAEZ+EAAAAAAAAGAQAAAAAAAxAAAHEgAQAAEAAAABQAAAAAAAADoAEAAAAAAADgAAB+IAEAABAAAABgkQAAAAAAD2DxAAAAAAADcAAAlyIBAAAAAAAAYAEAABAAAAlgcQAAEAAABGAhAAAAAAADgAAAeCIBAAAAAAADoAEAAAAAAAOhAQAAIAAAA+AAABU1RBVJQAAAAGAAAAAgAAAAAAAAADAAAAAwAAAAAAAAAAAAAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly ID3D12Device* _device;
    private readonly ID3D12Resource*[] _vbufs = new ID3D12Resource*[UploadFrameCount];
    private readonly D3D12_VERTEX_BUFFER_VIEW[] _vbvs = new D3D12_VERTEX_BUFFER_VIEW[UploadFrameCount];
    private readonly int[] _usedVertices = new int[UploadFrameCount];
    private ID3D12RootSignature* _rootSig;
    private ID3D12PipelineState* _pso;
    private bool _disposed;

    public D3D12TexturedQuadRenderer(ID3D12Device* device)
    {
        _device = device;
        try
        {
            CreateRootSignature();
            CreatePSO();
            CreateVertexBuffers();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static (int VertexBytes, int PixelBytes, byte[] VertexHeader, byte[] PixelHeader) GetEmbeddedShaderBytecodeLengths()
    {
        var vertexShader = Convert.FromBase64String(VertexShaderBytecodeBase64);
        var pixelShader = Convert.FromBase64String(PixelShaderBytecodeBase64);
        return (
            vertexShader.Length,
            pixelShader.Length,
            vertexShader[..Math.Min(4, vertexShader.Length)],
            pixelShader[..Math.Min(4, pixelShader.Length)]);
    }

    public void BeginFrame(int frameResourceIndex)
    {
        _usedVertices[frameResourceIndex % UploadFrameCount] = 0;
    }

    public bool RenderQuad(
        ID3D12GraphicsCommandList* list,
        D3D12CompositionLayerRenderTarget target,
        float x,
        float y,
        float width,
        float height,
        float opacity,
        IntegerScissorRect scissor,
        float vpW,
        float vpH,
        int frameResourceIndex)
    {
        var uploadSlot = frameResourceIndex % UploadFrameCount;
        var baseVertex = _usedVertices[uploadSlot];
        if (MaxVerts - baseVertex < 6)
        {
            return false;
        }

        var vbuf = _vbufs[uploadSlot];
        void* mapped = null;
        try
        {
            vbuf->Map(0, null, &mapped);
        }
        catch (COMException ex)
        {
            throw WrapD3D12Exception("D3D12TexturedQuadRenderer.Map(vertex buffer)", ex);
        }

        if (mapped == null)
        {
            throw new InvalidOperationException("D3D12TexturedQuadRenderer.Map(vertex buffer) returned null.");
        }

        var x1 = (x / vpW) * 2f - 1f;
        var y1 = 1f - (y / vpH) * 2f;
        var x2 = ((x + width) / vpW) * 2f - 1f;
        var y2 = 1f - ((y + height) / vpH) * 2f;
        var color = new Vector4(1f, 1f, 1f, Math.Clamp(opacity, 0f, 1f));
        try
        {
            var verts = new Span<Vertex>(mapped, MaxVerts);
            verts[baseVertex] = new Vertex { Position = new Vector2(x1, y1), TexCoord = new Vector2(0f, 0f), Color = color };
            verts[baseVertex + 1] = new Vertex { Position = new Vector2(x2, y1), TexCoord = new Vector2(1f, 0f), Color = color };
            verts[baseVertex + 2] = new Vertex { Position = new Vector2(x1, y2), TexCoord = new Vector2(0f, 1f), Color = color };
            verts[baseVertex + 3] = new Vertex { Position = new Vector2(x2, y1), TexCoord = new Vector2(1f, 0f), Color = color };
            verts[baseVertex + 4] = new Vertex { Position = new Vector2(x2, y2), TexCoord = new Vector2(1f, 1f), Color = color };
            verts[baseVertex + 5] = new Vertex { Position = new Vector2(x1, y2), TexCoord = new Vector2(0f, 1f), Color = color };
        }
        finally
        {
            vbuf->Unmap(0, null);
        }

        _usedVertices[uploadSlot] = baseVertex + 6;
        list->SetPipelineState(_pso);
        list->SetGraphicsRootSignature(_rootSig);
        list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var vbv = _vbvs[uploadSlot];
        list->IASetVertexBuffers(0, 1, &vbv);
        var heap = target.SrvHeap;
        list->SetDescriptorHeaps(1, &heap);
        list->SetGraphicsRootDescriptorTable(0, target.SrvGpu);
        var rect = new RECT
        {
            left = scissor.Left,
            top = scissor.Top,
            right = scissor.Right,
            bottom = scissor.Bottom
        };
        list->RSSetScissorRects(1, &rect);
        list->DrawInstanced(6, 1, (uint)baseVertex, 0);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        for (var i = 0; i < _vbufs.Length; i++)
        {
            if (_vbufs[i] != null)
            {
                _vbufs[i]->Release();
                _vbufs[i] = null;
            }
        }

        if (_pso != null) { _pso->Release(); _pso = null; }
        if (_rootSig != null) { _rootSig->Release(); _rootSig = null; }
        _disposed = true;
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
                throw WrapD3D12Exception("D3D12TexturedQuadRenderer.D3D12SerializeRootSignature", ex);
            }

            RequirePointer(sig, "D3D12TexturedQuadRenderer.D3D12SerializeRootSignature returned a null signature blob.");
            void* rootObj = null;
            try
            {
                _device->CreateRootSignature(0, sig->GetBufferPointer(), sig->GetBufferSize(), typeof(ID3D12RootSignature).GUID, out rootObj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12TexturedQuadRenderer.CreateRootSignature", ex);
            }

            _rootSig = (ID3D12RootSignature*)RequirePointer(rootObj, "D3D12TexturedQuadRenderer.CreateRootSignature returned a null root signature.");
        }
        finally
        {
            if (sig != null) sig->Release();
            if (err != null) err->Release();
        }
    }

    private void CreatePSO()
    {
        var vertexShader = Convert.FromBase64String(VertexShaderBytecodeBase64);
        var pixelShader = Convert.FromBase64String(PixelShaderBytecodeBase64);
        if (vertexShader.Length == 0 || pixelShader.Length == 0)
        {
            throw new InvalidOperationException("D3D12TexturedQuadRenderer embedded shader bytecode is empty.");
        }

        fixed (byte* vs = vertexShader)
        fixed (byte* ps = pixelShader)
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
            desc.VS.BytecodeLength = (nuint)vertexShader.Length;
            desc.PS.pShaderBytecode = ps;
            desc.PS.BytecodeLength = (nuint)pixelShader.Length;
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
                throw WrapD3D12Exception("D3D12TexturedQuadRenderer.CreateGraphicsPipelineState", ex);
            }

            _pso = (ID3D12PipelineState*)RequirePointer(psoObj, "D3D12TexturedQuadRenderer.CreateGraphicsPipelineState returned a null PSO.");
        }
    }

    private void CreateVertexBuffers()
    {
        var heapProps = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD };
        var resDesc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = (ulong)(MaxVerts * sizeof(Vertex)),
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
                throw WrapD3D12Exception("D3D12TexturedQuadRenderer.CreateCommittedResource(vertex buffer)", ex);
            }

            var vbuf = (ID3D12Resource*)RequirePointer(resObj, "D3D12TexturedQuadRenderer.CreateCommittedResource(vertex buffer) returned a null resource.");
            _vbufs[i] = vbuf;
            _vbvs[i] = new D3D12_VERTEX_BUFFER_VIEW
            {
                BufferLocation = vbuf->GetGPUVirtualAddress(),
                SizeInBytes = (uint)(MaxVerts * sizeof(Vertex)),
                StrideInBytes = (uint)sizeof(Vertex)
            };
        }
    }

    private static COMException WrapD3D12Exception(string context, COMException ex) =>
        new($"{context} failed: 0x{unchecked((uint)ex.ErrorCode):X8}", ex.ErrorCode);

    private static void* RequirePointer(void* pointer, string message)
    {
        if (pointer == null)
        {
            throw new InvalidOperationException(message);
        }

        return pointer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
    }
}
