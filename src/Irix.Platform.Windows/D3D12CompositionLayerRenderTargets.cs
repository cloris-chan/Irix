using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Rendering;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal readonly struct D3D12CompositionLayerRenderTargetRequest(
    D3D12CompositionLayerContentCacheKey Key,
    D3D12CompositionLayerContent Content,
    CompositionLayer Layer,
    DrawingBackendClipMode ClipMode) : IEquatable<D3D12CompositionLayerRenderTargetRequest>
{
    public D3D12CompositionLayerContentCacheKey Key { get; } = Key;
    public D3D12CompositionLayerContent Content { get; } = Content;
    public CompositionLayer Layer { get; } = Layer;
    public DrawingBackendClipMode ClipMode { get; } = ClipMode;

    public bool Equals(D3D12CompositionLayerRenderTargetRequest other) =>
        Key == other.Key && ReferenceEquals(Content, other.Content) && Layer == other.Layer && ClipMode == other.ClipMode;

    public override bool Equals(object? obj) => obj is D3D12CompositionLayerRenderTargetRequest other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Key, RuntimeHelpers.GetHashCode(Content), Layer, ClipMode);

    public static bool operator ==(D3D12CompositionLayerRenderTargetRequest left, D3D12CompositionLayerRenderTargetRequest right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionLayerRenderTargetRequest left, D3D12CompositionLayerRenderTargetRequest right) => !left.Equals(right);
}

internal readonly struct D3D12CompositionRenderTargetCacheDiagnostics(
    bool RenderTargetBacked,
    int RenderTargetCacheHits,
    int RenderTargetCacheMisses,
    int CachedRenderTargetCommands) : IEquatable<D3D12CompositionRenderTargetCacheDiagnostics>
{
    public bool RenderTargetBacked { get; } = RenderTargetBacked;
    public int RenderTargetCacheHits { get; } = RenderTargetCacheHits;
    public int RenderTargetCacheMisses { get; } = RenderTargetCacheMisses;
    public int CachedRenderTargetCommands { get; } = CachedRenderTargetCommands;

    public bool Equals(D3D12CompositionRenderTargetCacheDiagnostics other)
    {
        return RenderTargetBacked == other.RenderTargetBacked
            && RenderTargetCacheHits == other.RenderTargetCacheHits
            && RenderTargetCacheMisses == other.RenderTargetCacheMisses
            && CachedRenderTargetCommands == other.CachedRenderTargetCommands;
    }

    public override bool Equals(object? obj) => obj is D3D12CompositionRenderTargetCacheDiagnostics other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RenderTargetBacked, RenderTargetCacheHits, RenderTargetCacheMisses, CachedRenderTargetCommands);

    public static bool operator ==(D3D12CompositionRenderTargetCacheDiagnostics left, D3D12CompositionRenderTargetCacheDiagnostics right) => left.Equals(right);

    public static bool operator !=(D3D12CompositionRenderTargetCacheDiagnostics left, D3D12CompositionRenderTargetCacheDiagnostics right) => !left.Equals(right);
}

internal sealed unsafe class D3D12CompositionLayerRenderTargetCache : IDisposable
{
    private const int MaxEntries = 16;
    private readonly ID3D12Device* _device;
    private readonly Entry[] _entries = new Entry[MaxEntries];
    private int _count;
    private int _nextReplaceIndex;
    private bool _disposed;

    public D3D12CompositionLayerRenderTargetCache(ID3D12Device* device)
    {
        _device = device;
    }

    public int Count => _count;

    public D3D12CompositionLayerRenderTarget GetOrCreate(
        in D3D12CompositionLayerContentCacheKey key,
        D3D12CompositionLayerContent content,
        int width,
        int height,
        out bool hit)
    {
        for (var i = 0; i < _count; i++)
        {
            ref var entry = ref _entries[i];
            var existingTarget = entry.Target;
            if (existingTarget is not null && entry.Matches(key, content, width, height))
            {
                hit = true;
                return existingTarget;
            }
        }

        hit = false;
        var target = D3D12CompositionLayerRenderTarget.Create(_device, width, height);
        Store(key, content, target);
        return target;
    }

    public void Clear()
    {
        for (var i = 0; i < _count; i++)
        {
            _entries[i].Dispose();
            _entries[i] = default;
        }

        _count = 0;
        _nextReplaceIndex = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }

    private void Store(
        in D3D12CompositionLayerContentCacheKey key,
        D3D12CompositionLayerContent content,
        D3D12CompositionLayerRenderTarget target)
    {
        if (_count < _entries.Length)
        {
            _entries[_count++] = new Entry(key, content, target);
            return;
        }

        _entries[_nextReplaceIndex].Dispose();
        _entries[_nextReplaceIndex] = new Entry(key, content, target);
        _nextReplaceIndex = (_nextReplaceIndex + 1) % _entries.Length;
    }

    private struct Entry(
        D3D12CompositionLayerContentCacheKey Key,
        D3D12CompositionLayerContent Content,
        D3D12CompositionLayerRenderTarget Target)
    {
        public D3D12CompositionLayerContentCacheKey Key { get; } = Key;
        public D3D12CompositionLayerContent? Content { get; private set; } = Content;
        public D3D12CompositionLayerRenderTarget? Target { get; private set; } = Target;

        public bool Matches(in D3D12CompositionLayerContentCacheKey key, D3D12CompositionLayerContent content, int width, int height)
        {
            var current = Target;
            return current is not null
                && Key == key
                && ReferenceEquals(Content, content)
                && current.Width == width
                && current.Height == height;
        }

        public void Dispose()
        {
            Target?.Dispose();
            Content = null;
            Target = null;
        }
    }
}

internal sealed unsafe class D3D12CompositionLayerRenderTarget : IDisposable
{
    private const uint Shader4ComponentMapping = 0u | (1u << 3) | (2u << 6) | (3u << 9) | (1u << 12);
    private const DXGI_FORMAT TextureFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
    private bool _disposed;

    private D3D12CompositionLayerRenderTarget(
        ID3D12Resource* texture,
        ID3D12DescriptorHeap* rtvHeap,
        ID3D12DescriptorHeap* srvHeap,
        int width,
        int height)
    {
        Texture = texture;
        RtvHeap = rtvHeap;
        SrvHeap = srvHeap;
        Width = width;
        Height = height;
        State = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    public ID3D12Resource* Texture { get; private set; }
    public ID3D12DescriptorHeap* RtvHeap { get; private set; }
    public ID3D12DescriptorHeap* SrvHeap { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public D3D12_RESOURCE_STATES State { get; set; }

    public D3D12_CPU_DESCRIPTOR_HANDLE Rtv => RtvHeap->GetCPUDescriptorHandleForHeapStart();
    public D3D12_GPU_DESCRIPTOR_HANDLE SrvGpu => SrvHeap->GetGPUDescriptorHandleForHeapStart();

    public static D3D12CompositionLayerRenderTarget Create(ID3D12Device* device, int width, int height)
    {
        ID3D12Resource* texture = null;
        ID3D12DescriptorHeap* rtvHeap = null;
        ID3D12DescriptorHeap* srvHeap = null;
        try
        {
            var heapProps = new D3D12_HEAP_PROPERTIES { Type = D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT };
            var textureDesc = new D3D12_RESOURCE_DESC
            {
                Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
                Width = (ulong)Math.Max(width, 1),
                Height = (uint)Math.Max(height, 1),
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = TextureFormat,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_UNKNOWN,
                Flags = D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET
            };

            void* textureObj = null;
            try
            {
                device->CreateCommittedResource(
                    heapProps,
                    D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE,
                    textureDesc,
                    D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE,
                    null,
                    typeof(ID3D12Resource).GUID,
                    out textureObj);
            }
            catch (COMException ex)
            {
                throw WrapD3D12Exception("D3D12CompositionLayerRenderTarget.CreateCommittedResource(texture)", ex);
            }

            texture = (ID3D12Resource*)RequirePointer(textureObj, "D3D12CompositionLayerRenderTarget.CreateCommittedResource(texture) returned a null resource.");
            rtvHeap = CreateDescriptorHeap(device, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE, "RTV");
            srvHeap = CreateDescriptorHeap(device, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, "SRV");

            device->CreateRenderTargetView(texture, null, rtvHeap->GetCPUDescriptorHandleForHeapStart());
            var srvDesc = new D3D12_SHADER_RESOURCE_VIEW_DESC
            {
                Format = TextureFormat,
                ViewDimension = D3D12_SRV_DIMENSION.D3D12_SRV_DIMENSION_TEXTURE2D,
                Shader4ComponentMapping = Shader4ComponentMapping
            };
            srvDesc.Anonymous.Texture2D.MipLevels = 1;
            device->CreateShaderResourceView(texture, srvDesc, srvHeap->GetCPUDescriptorHandleForHeapStart());

            var target = new D3D12CompositionLayerRenderTarget(texture, rtvHeap, srvHeap, Math.Max(width, 1), Math.Max(height, 1));
            texture = null;
            rtvHeap = null;
            srvHeap = null;
            return target;
        }
        finally
        {
            if (srvHeap != null) srvHeap->Release();
            if (rtvHeap != null) rtvHeap->Release();
            if (texture != null) texture->Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (SrvHeap != null) { SrvHeap->Release(); SrvHeap = null; }
        if (RtvHeap != null) { RtvHeap->Release(); RtvHeap = null; }
        if (Texture != null) { Texture->Release(); Texture = null; }
        _disposed = true;
    }

    private static ID3D12DescriptorHeap* CreateDescriptorHeap(
        ID3D12Device* device,
        D3D12_DESCRIPTOR_HEAP_TYPE type,
        D3D12_DESCRIPTOR_HEAP_FLAGS flags,
        string label)
    {
        var desc = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            NumDescriptors = 1,
            Type = type,
            Flags = flags
        };

        void* heapObj = null;
        try
        {
            device->CreateDescriptorHeap(desc, typeof(ID3D12DescriptorHeap).GUID, out heapObj);
        }
        catch (COMException ex)
        {
            throw WrapD3D12Exception($"D3D12CompositionLayerRenderTarget.CreateDescriptorHeap({label})", ex);
        }

        return (ID3D12DescriptorHeap*)RequirePointer(heapObj, $"D3D12CompositionLayerRenderTarget.CreateDescriptorHeap({label}) returned a null heap.");
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
}
