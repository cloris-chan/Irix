using System.Runtime.InteropServices;

namespace Irix.Platform.Windows.D3D12;

// ═══════════════════════════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════════════════════════

internal enum D3D_FEATURE_LEVEL { D3D_FEATURE_LEVEL_11_0 = 0xb000 }
internal enum D3D12_COMMAND_LIST_TYPE { DIRECT = 0 }
internal enum D3D12_DESCRIPTOR_HEAP_TYPE { RTV = 2 }
internal enum D3D12_RESOURCE_STATES { COMMON = 0, RENDER_TARGET = 4, PRESENT = 0 }
internal enum DXGI_FORMAT { R8G8B8A8_UNORM = 28 }
internal enum DXGI_SWAP_EFFECT { FLIP_DISCARD = 4 }
internal enum DXGI_SCALING { STRETCH = 0, NONE = 1 }

// ═══════════════════════════════════════════════════════════════════
// Structs
// ═══════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12_COMMAND_QUEUE_DESC
{
    public D3D12_COMMAND_LIST_TYPE Type;
    public int Priority;
    public int Flags;
    public uint NodeMask;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12_DESCRIPTOR_HEAP_DESC
{
    public D3D12_DESCRIPTOR_HEAP_TYPE Type;
    public uint NumDescriptors;
    public int Flags;
    public uint NodeMask;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12_CPU_DESCRIPTOR_HANDLE
{
    public nuint ptr;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12_RESOURCE_BARRIER
{
    public int Type;       // D3D12_RESOURCE_BARRIER_TYPE_TRANSITION = 0
    public int Flags;
    public nint pResource;
    public uint Subresource;
    public uint StateBefore;
    public uint StateAfter;
    private readonly int _pad0;
    private readonly int _pad1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SAMPLE_DESC
{
    public uint Count;
    public uint Quality;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DXGI_SWAP_CHAIN_DESC1
{
    public uint Width;
    public uint Height;
    public DXGI_FORMAT Format;
    public int Stereo;
    public DXGI_SAMPLE_DESC SampleDesc;
    public uint BufferUsage;
    public uint BufferCount;
    public DXGI_SCALING Scaling;
    public DXGI_SWAP_EFFECT SwapEffect;
    public uint AlphaMode;
    public uint Flags;
}

// ═══════════════════════════════════════════════════════════════════
// Raw COM vtable dispatchers
// All COM objects are nint. Methods called via vtable function pointers.
// ═══════════════════════════════════════════════════════════════════

internal static unsafe class D3D12Vtable
{
    private static void** Vtbl(nint obj) => *(void***)obj;

    public static uint Release(nint obj)
        => ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(obj)[2])(obj);

    // ── ID3D12Device ── vtable: IUnknown(0-2) ID3D12Object(3-6) ID3D12Device(7+)
    public static int CreateCommandQueue(nint dev, D3D12_COMMAND_QUEUE_DESC* desc, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, D3D12_COMMAND_QUEUE_DESC*, Guid*, void**, int>)Vtbl(dev)[8])(dev, desc, riid, pp);

    public static int CreateCommandAllocator(nint dev, D3D12_COMMAND_LIST_TYPE type, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, D3D12_COMMAND_LIST_TYPE, Guid*, void**, int>)Vtbl(dev)[9])(dev, type, riid, pp);

    public static int CreateCommandList(nint dev, uint nodeMask, D3D12_COMMAND_LIST_TYPE type, nint alloc, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, uint, D3D12_COMMAND_LIST_TYPE, nint, nint, Guid*, void**, int>)Vtbl(dev)[12])(dev, nodeMask, type, alloc, 0, riid, pp);

    public static int CreateDescriptorHeap(nint dev, D3D12_DESCRIPTOR_HEAP_DESC* desc, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, D3D12_DESCRIPTOR_HEAP_DESC*, Guid*, void**, int>)Vtbl(dev)[14])(dev, desc, riid, pp);

    public static uint GetDescriptorHandleIncrementSize(nint dev, D3D12_DESCRIPTOR_HEAP_TYPE type)
        => ((delegate* unmanaged[Stdcall]<nint, D3D12_DESCRIPTOR_HEAP_TYPE, uint>)Vtbl(dev)[15])(dev, type);

    public static void CreateRenderTargetView(nint dev, nint resource, D3D12_CPU_DESCRIPTOR_HANDLE dest)
        => ((delegate* unmanaged[Stdcall]<nint, nint, nint, D3D12_CPU_DESCRIPTOR_HANDLE, void>)Vtbl(dev)[20])(dev, resource, 0, dest);

    public static int CreateFence(nint dev, ulong init, int flags, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, ulong, int, Guid*, void**, int>)Vtbl(dev)[36])(dev, init, flags, riid, pp);

    // ── ID3D12CommandQueue ── vtable: IUnknown(0-2) ID3D12Object(3-6) ID3D12DeviceChild(7) ID3D12CommandQueue(8+)
    // UpdateTileMappings=8, CopyTileMappings=9, ExecuteCommandLists=10, ..., Signal=14
    public static void ExecuteCommandLists(nint q, uint count, nint* lists)
        => ((delegate* unmanaged[Stdcall]<nint, uint, nint*, void>)Vtbl(q)[10])(q, count, lists);

    public static int QueueSignal(nint q, nint fence, ulong val)
        => ((delegate* unmanaged[Stdcall]<nint, nint, ulong, int>)Vtbl(q)[14])(q, fence, val);

    // ── ID3D12CommandAllocator ── vtable: ...(7) ID3D12CommandAllocator(8)
    public static int ResetAllocator(nint a)
        => ((delegate* unmanaged[Stdcall]<nint, int>)Vtbl(a)[8])(a);

    // ── ID3D12GraphicsCommandList ── vtable: ...(7,8) ID3D12GraphicsCommandList(9+)
    public static int Close(nint l)
        => ((delegate* unmanaged[Stdcall]<nint, int>)Vtbl(l)[9])(l);

    public static int ResetList(nint l, nint alloc, nint state)
        => ((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Vtbl(l)[10])(l, alloc, state);

    public static void ResourceBarrier(nint l, uint count, D3D12_RESOURCE_BARRIER* barriers)
        => ((delegate* unmanaged[Stdcall]<nint, uint, D3D12_RESOURCE_BARRIER*, void>)Vtbl(l)[26])(l, count, barriers);

    // ClearDepthStencilView=47, ClearRenderTargetView=48
    public static void ClearRenderTargetView(nint l, D3D12_CPU_DESCRIPTOR_HANDLE rtv, float* color, uint rects, void* pRects)
        => ((delegate* unmanaged[Stdcall]<nint, D3D12_CPU_DESCRIPTOR_HANDLE, float*, uint, void*, void>)Vtbl(l)[48])(l, rtv, color, rects, pRects);

    // ── ID3D12DescriptorHeap ── vtable: IUnknown(0-2) ID3D12Object(3-6) ID3D12DeviceChild(7) ID3D12DescriptorHeap(8+)
    // GetDesc=8, GetCPUDescriptorHandleForHeapStart=9, GetGPUDescriptorHandleForHeapStart=10, GetDescriptorHandleIncrementSize=11
    // Note: CsWin32 uses [Stdcall,MemberFunction] for struct-returning methods
    public static D3D12_CPU_DESCRIPTOR_HANDLE GetCPUDescriptorHandleForHeapStart(nint h)
        => ((delegate* unmanaged[Stdcall,MemberFunction]<nint, D3D12_CPU_DESCRIPTOR_HANDLE>)Vtbl(h)[9])(h);

    // ── ID3D12Fence ── vtable: ...(7) ID3D12Fence(8+)
    public static ulong GetCompletedValue(nint f)
        => ((delegate* unmanaged[Stdcall]<nint, ulong>)Vtbl(f)[8])(f);

    public static int SetEventOnCompletion(nint f, ulong val, nint evt)
        => ((delegate* unmanaged[Stdcall]<nint, ulong, nint, int>)Vtbl(f)[9])(f, val, evt);

    public static int FenceSignal(nint f, ulong val)
        => ((delegate* unmanaged[Stdcall]<nint, ulong, int>)Vtbl(f)[10])(f, val);

    // ── IDXGISwapChain3 ── vtable: IUnknown(0-2) IDXGIObject(3-6) IDXGIDeviceSubObject(7) IDXGISwapChain(8+)
    // Present=8, GetBuffer=9, ..., ResizeBuffers=13, ...
    // IDXGISwapChain1(18+) IDXGISwapChain2(29+) IDXGISwapChain3(36+)
    // GetCurrentBackBufferIndex=36
    public static int Present(nint sc, uint sync, uint flags)
        => ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Vtbl(sc)[8])(sc, sync, flags);

    public static int GetBuffer(nint sc, uint buf, Guid* riid, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, uint, Guid*, void**, int>)Vtbl(sc)[9])(sc, buf, riid, pp);

    public static int ResizeBuffers(nint sc, uint count, uint w, uint h, DXGI_FORMAT fmt, uint flags)
        => ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, DXGI_FORMAT, uint, int>)Vtbl(sc)[13])(sc, count, w, h, fmt, flags);

    public static uint GetCurrentBackBufferIndex(nint sc)
        => ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(sc)[36])(sc);

    // ── IDXGIFactory4 ── vtable: IUnknown(0-2) IDXGIObject(3-6) IDXGIFactory(7+) IDXGIFactory1(12+) IDXGIFactory2(14+)
    // CreateSwapChainForHwnd=15
    public static int CreateSwapChainForHwnd(nint f, nint dev, nint hwnd, DXGI_SWAP_CHAIN_DESC1* desc, void* fs, void* restrict, void** pp)
        => ((delegate* unmanaged[Stdcall]<nint, nint, nint, DXGI_SWAP_CHAIN_DESC1*, void*, void*, void**, int>)Vtbl(f)[15])(f, dev, hwnd, desc, fs, restrict, pp);
}

// ═══════════════════════════════════════════════════════════════════
// P/Invoke
// ═══════════════════════════════════════════════════════════════════

internal static class D3D12NativeMethods
{
    [DllImport("d3d12.dll")]
    internal static extern unsafe int D3D12CreateDevice(nint adapter, D3D_FEATURE_LEVEL level, Guid* riid, void** ppDevice);

    [DllImport("dxgi.dll")]
    internal static extern unsafe int CreateDXGIFactory1(Guid* riid, void** ppFactory);
}
