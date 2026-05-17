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

/// <summary>
/// D3D12 2D rectangle renderer using vertex buffers and embedded shader bytecode.
/// Renders colored rectangles by uploading per-rect vertex data.
/// </summary>
internal sealed unsafe class D3D12Renderer2D : IDisposable
{
    private static readonly byte[] VertexShaderBytecode = Convert.FromBase64String(
        "RFhCQ75/+kn3zuYAeIeJeqg0Y2YBAAAAdAIAAAUAAAA0AAAAoAAAAPAAAABEAQAA2AEAAFJERUZkAAAAAAAAAAAAAAAAAAAAPAAAAAAF/v8AAQAAPAAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAElTR05IAAAAAgAAAAgAAAA4AAAAAAAAAAAAAAADAAAAAAAAAAMDAABBAAAAAAAAAAAAAAADAAAAAQAAAA8PAABQT1NJVElPTgBDT0xPUgCrT1NHTkwAAAACAAAACAAAADgAAAAAAAAAAQAAAAMAAAAAAAAADwAAAEQAAAAAAAAAAAAAAAMAAAABAAAADwAAAFNWX1BPU0lUSU9OAENPTE9SAKurU0hFWIwAAABQAAEAIwAAAGoIAAFfAAADMhAQAAAAAABfAAAD8hAQAAEAAABnAAAE8iAQAAAAAAABAAAAZQAAA/IgEAABAAAANgAABTIgEAAAAAAARhAQAAAAAAA2AAAIwiAQAAAAAAACQAAAAAAAAAAAAAAAAAAAAACAPzYAAAXyIBAAAQAAAEYeEAABAAAAPgAAAVNUQVSUAAAABAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
    private static readonly byte[] PixelShaderBytecode = Convert.FromBase64String(
        "RFhCQ8/OkxZrdEO2Qwx2MjIPOqgBAAAACAIAAAUAAAA0AAAAoAAAAPQAAAAoAQAAbAEAAFJERUZkAAAAAAAAAAAAAAAAAAAAPAAAAAAF//8AAQAAPAAAAFJEMTE8AAAAGAAAACAAAAAoAAAAJAAAAAwAAAAAAAAATWljcm9zb2Z0IChSKSBITFNMIFNoYWRlciBDb21waWxlciAxMC4xAElTR05MAAAAAgAAAAgAAAA4AAAAAAAAAAEAAAADAAAAAAAAAA8AAABEAAAAAAAAAAAAAAADAAAAAQAAAA8PAABTVl9QT1NJVElPTgBDT0xPUgCrq09TR04sAAAAAQAAAAgAAAAgAAAAAAAAAAAAAAADAAAAAAAAAA8AAABTVl9UQVJHRVQAq6tTSEVYPAAAAFAAAAAPAAAAaggAAWIQAAPyEBAAAQAAAGUAAAPyIBAAAAAAADYAAAXyIBAAAAAAAEYeEAABAAAAPgAAAVNUQVSUAAAAAgAAAAAAAAAAAAAAAgAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");

    private readonly ID3D12Device* _device;
    private ID3D12RootSignature* _rootSig;
    private ID3D12PipelineState* _pso;
    private ID3D12Resource* _vbuf;
    private D3D12_VERTEX_BUFFER_VIEW _vbv;
    private const int MaxVerts = 6 * 1024; // 1024 quads
    private bool _disposed;

    public D3D12Renderer2D(ID3D12Device* device)
    {
        _device = device;
        try
        {
            CreateRootSignature();
            CreatePSO();
            CreateVertexBuffer();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static (int VertexBytes, int PixelBytes, byte[] VertexHeader, byte[] PixelHeader) GetEmbeddedShaderBytecodeLengths()
    {
        return (
            VertexShaderBytecode.Length,
            PixelShaderBytecode.Length,
            VertexShaderBytecode[..Math.Min(4, VertexShaderBytecode.Length)],
            PixelShaderBytecode[..Math.Min(4, PixelShaderBytecode.Length)]);
    }

    private void CreateRootSignature()
    {
        var desc = new D3D12_ROOT_SIGNATURE_DESC
        {
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
                throw WrapD3D12Exception("D3D12Renderer2D.D3D12SerializeRootSignature", ex);
            }

            RequirePointer(sig, "D3D12Renderer2D.D3D12SerializeRootSignature returned a null signature blob.");

            void* obj = null;
            try
            {
                _device->CreateRootSignature(0, sig->GetBufferPointer(), sig->GetBufferSize(), typeof(ID3D12RootSignature).GUID, out obj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12Renderer2D.CreateRootSignature", ex);
            }

            if (obj == null)
            {
                throw new InvalidOperationException("D3D12Renderer2D.CreateRootSignature returned a null root signature.");
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
        if (VertexShaderBytecode.Length == 0 || PixelShaderBytecode.Length == 0)
        {
            throw new InvalidOperationException("D3D12Renderer2D embedded shader bytecode is empty.");
        }

        fixed (byte* vs = VertexShaderBytecode)
        fixed (byte* ps = PixelShaderBytecode)
        {
            // Input layout: POSITION(float2) + COLOR(float4)
            fixed (byte* posBytes = "POSITION"u8)
            fixed (byte* colBytes = "COLOR"u8)
            {
                var elems = stackalloc D3D12_INPUT_ELEMENT_DESC[2];
                elems[0] = new D3D12_INPUT_ELEMENT_DESC { SemanticName = (PCSTR)posBytes, Format = DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT };
                elems[1] = new D3D12_INPUT_ELEMENT_DESC { SemanticName = (PCSTR)colBytes, Format = DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT, AlignedByteOffset = 8 };

                var desc = new D3D12_GRAPHICS_PIPELINE_STATE_DESC();
                desc.pRootSignature = _rootSig;
                desc.InputLayout.pInputElementDescs = elems;
                desc.InputLayout.NumElements = 2;
                desc.VS.pShaderBytecode = vs;
                desc.VS.BytecodeLength = (nuint)VertexShaderBytecode.Length;
                desc.PS.pShaderBytecode = ps;
                desc.PS.BytecodeLength = (nuint)PixelShaderBytecode.Length;
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
                    throw WrapD3D12Exception("D3D12Renderer2D.CreateGraphicsPipelineState", ex);
                }

                if (psoObj == null)
                {
                    throw new InvalidOperationException("D3D12Renderer2D.CreateGraphicsPipelineState returned a null PSO.");
                }

                _pso = (ID3D12PipelineState*)psoObj;
            }
        }
    }

    private void CreateVertexBuffer()
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
            throw WrapD3D12Exception("D3D12Renderer2D.CreateCommittedResource(vertex buffer)", ex);
        }

        if (resObj == null)
        {
            throw new InvalidOperationException("D3D12Renderer2D.CreateCommittedResource(vertex buffer) returned a null resource.");
        }

        _vbuf = (ID3D12Resource*)resObj;
        _vbv.BufferLocation = _vbuf->GetGPUVirtualAddress();
        _vbv.SizeInBytes = (uint)(MaxVerts * sizeof(Vertex));
        _vbv.StrideInBytes = (uint)sizeof(Vertex);
    }

    /// <summary>
    /// Render colored rectangles. Returns the number of quads drawn.
    /// Coordinates are in screen pixels, converted to NDC internally.
    /// </summary>
    public int RenderRectangles(ID3D12GraphicsCommandList* list, ReadOnlySpan<RectData> rects, float vpW, float vpH)
    {
        if (rects.Length == 0) return 0;

        void* mapped = null;
        try
        {
            _vbuf->Map(0, null, &mapped);
        }
        catch (COMException ex)
        {
            throw WrapD3D12Exception("D3D12Renderer2D.Map(vertex buffer)", ex);
        }

        if (mapped == null)
        {
            throw new InvalidOperationException("D3D12Renderer2D.Map(vertex buffer) returned null.");
        }

        var verts = new Span<Vertex>(mapped, MaxVerts);
        var count = 0;

        for (var i = 0; i < rects.Length && count + 6 <= MaxVerts; i++)
        {
            var r = rects[i];
            var x1 = (r.X / vpW) * 2f - 1f;
            var y1 = 1f - (r.Y / vpH) * 2f;
            var x2 = ((r.X + r.Width) / vpW) * 2f - 1f;
            var y2 = 1f - ((r.Y + r.Height) / vpH) * 2f;
            var c = new Vector4(r.R, r.G, r.B, r.A);

            verts[count++] = new Vertex { Position = new Vector2(x1, y1), Color = c };
            verts[count++] = new Vertex { Position = new Vector2(x2, y1), Color = c };
            verts[count++] = new Vertex { Position = new Vector2(x1, y2), Color = c };
            verts[count++] = new Vertex { Position = new Vector2(x2, y1), Color = c };
            verts[count++] = new Vertex { Position = new Vector2(x2, y2), Color = c };
            verts[count++] = new Vertex { Position = new Vector2(x1, y2), Color = c };
        }

        _vbuf->Unmap(0, null);

        list->SetPipelineState(_pso);
        list->SetGraphicsRootSignature(_rootSig);
        list->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var vbv = _vbv;
        list->IASetVertexBuffers(0, 1, &vbv);
        var rectCount = count / 6;
        var runStart = 0;
        var runScissor = rects[0].Scissor;
        for (var i = 1; i < rectCount; i++)
        {
            var scissor = rects[i].Scissor;
            if (scissor == runScissor)
            {
                continue;
            }

            DrawRun(runStart, i - runStart, runScissor);
            runStart = i;
            runScissor = scissor;
        }

        DrawRun(runStart, rectCount - runStart, runScissor);

        return rectCount;

        void DrawRun(int startRect, int rectCountInRun, IntegerScissorRect integerScissor)
        {
            var scissor = new RECT
            {
                left = integerScissor.Left,
                top = integerScissor.Top,
                right = integerScissor.Right,
                bottom = integerScissor.Bottom
            };
            list->RSSetScissorRects(1, &scissor);
            list->DrawInstanced((uint)(rectCountInRun * 6), 1, (uint)(startRect * 6), 0);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_vbuf != null) _vbuf->Release();
        if (_pso != null) _pso->Release();
        if (_rootSig != null) _rootSig->Release();
        _disposed = true;
    }

    private static COMException WrapD3D12Exception(string context, COMException ex)
    {
        return new COMException($"{context} failed: 0x{unchecked((uint)ex.ErrorCode):X8}", ex.ErrorCode);
    }

    private static void RequirePointer(void* pointer, string message)
    {
        if (pointer == null)
        {
            throw new InvalidOperationException(message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Vertex
    {
        public Vector2 Position;
        public Vector4 Color;
    }

    public readonly struct RectData(float X, float Y, float Width, float Height, float R, float G, float B, float A, IntegerScissorRect Scissor) : IEquatable<RectData>
    {
        public float X { get; } = X;
        public float Y { get; } = Y;
        public float Width { get; } = Width;
        public float Height { get; } = Height;
        public float R { get; } = R;
        public float G { get; } = G;
        public float B { get; } = B;
        public float A { get; } = A;
        public IntegerScissorRect Scissor { get; } = Scissor;

        public bool Equals(RectData other)
        {
            return X.Equals(other.X)
                && Y.Equals(other.Y)
                && Width.Equals(other.Width)
                && Height.Equals(other.Height)
                && R.Equals(other.R)
                && G.Equals(other.G)
                && B.Equals(other.B)
                && A.Equals(other.A)
                && Scissor == other.Scissor;
        }

        public override bool Equals(object? obj) => obj is RectData other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(X);
            hash.Add(Y);
            hash.Add(Width);
            hash.Add(Height);
            hash.Add(R);
            hash.Add(G);
            hash.Add(B);
            hash.Add(A);
            hash.Add(Scissor);
            return hash.ToHashCode();
        }

        public static bool operator ==(RectData left, RectData right) => left.Equals(right);

        public static bool operator !=(RectData left, RectData right) => !left.Equals(right);
    }
}
