using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

/// <summary>
/// D3D12 renderer using CsWin32 generated raw-pointer COM wrappers.
/// All vtable offsets and GUIDs are managed by CsWin32 — no manual bindings.
/// </summary>
internal sealed unsafe class D3D12Renderer : IDisposable
{
    private const int FrameCount = 2;

    private ID3D12Device* _device;
    private ID3D12CommandQueue* _queue;
    private IDXGISwapChain3* _swapChain;
    private ID3D12Resource*[] _renderTargets;
    private ID3D12DescriptorHeap* _rtvHeap;
    private uint _rtvSize;
    private ID3D12CommandAllocator*[] _allocators;
    private ID3D12GraphicsCommandList* _list;
    private ID3D12Fence* _fence;
    private HANDLE _fenceEvent;
    private ulong[] _fenceValues;
    private uint _frameIndex;
    private bool _disposed;

    public D3D12Renderer(nint hwnd, int width, int height)
    {
        // DXGI factory
        PInvoke.CreateDXGIFactory1(typeof(IDXGIFactory4).GUID, out var factoryObj);
        var factory = (IDXGIFactory4*)factoryObj;

        // D3D12 device
        PInvoke.D3D12CreateDevice(null, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, typeof(ID3D12Device).GUID, out var deviceObj);
        _device = (ID3D12Device*)deviceObj;

        // Command queue
        var qd = new D3D12_COMMAND_QUEUE_DESC { Type = D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT };
        _device->CreateCommandQueue(qd, typeof(ID3D12CommandQueue).GUID, out var queueObj);
        _queue = (ID3D12CommandQueue*)queueObj;

        // Swap chain
        var sd = new DXGI_SWAP_CHAIN_DESC1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            BufferUsage = DXGI_USAGE.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount = FrameCount,
            Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
            SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD
        };
        IDXGISwapChain1* sc1;
        factory->CreateSwapChainForHwnd((global::Windows.Win32.System.Com.IUnknown*)_queue, new HWND(hwnd), &sd, null, null, &sc1);
        sc1->QueryInterface(typeof(IDXGISwapChain3).GUID, out var sc3Obj);
        _swapChain = (IDXGISwapChain3*)sc3Obj;
        sc1->Release();
        factory->Release();

        // RTV heap
        var hd = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            NumDescriptors = FrameCount,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV
        };
        _device->CreateDescriptorHeap(hd, typeof(ID3D12DescriptorHeap).GUID, out var heapObj);
        _rtvHeap = (ID3D12DescriptorHeap*)heapObj;
        _rtvSize = _device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

        // Render targets
        _renderTargets = new ID3D12Resource*[FrameCount];
        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        for (var i = 0; i < FrameCount; i++)
        {
            _swapChain->GetBuffer((uint)i, typeof(ID3D12Resource).GUID, out var resObj);
            _renderTargets[i] = (ID3D12Resource*)resObj;
            _device->CreateRenderTargetView(_renderTargets[i], null, rtv);
            rtv.ptr += _rtvSize;
        }

        // Command allocators
        _allocators = new ID3D12CommandAllocator*[FrameCount];
        for (var i = 0; i < FrameCount; i++)
        {
            _device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT, typeof(ID3D12CommandAllocator).GUID, out var allocObj);
            _allocators[i] = (ID3D12CommandAllocator*)allocObj;
        }

        // Command list
        _device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT, _allocators[0], null, typeof(ID3D12GraphicsCommandList).GUID, out var listObj);
        _list = (ID3D12GraphicsCommandList*)listObj;
        _list->Close();

        // Fence
        _device->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, typeof(ID3D12Fence).GUID, out var fenceObj);
        _fence = (ID3D12Fence*)fenceObj;
        _fenceValues = new ulong[FrameCount];
        _fenceEvent = new HANDLE(PInvoke.CreateEvent(null, true, false, null).DangerousGetHandle());

        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
    }

    public void BeginFrame()
    {
        _allocators[_frameIndex]->Reset();
        _list->Reset(_allocators[_frameIndex], null);
    }

    public void ClearAndPresent(float r, float g, float b, float a = 1.0f)
    {
        // Transition to render target
        var barrier = new D3D12_RESOURCE_BARRIER();
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Anonymous.Transition.pResource = _renderTargets[_frameIndex];
        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.Subresource = 0xFFFFFFFF;
        _list->ResourceBarrier(1, &barrier);

        // Clear
        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        rtv.ptr += _frameIndex * _rtvSize;
        var color = stackalloc float[] { r, g, b, a };
        _list->ClearRenderTargetView(rtv, new ReadOnlySpan<float>(color, 4));

        // Transition to present
        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        _list->ResourceBarrier(1, &barrier);

        _list->Close();

        var pList = (ID3D12CommandList*)_list;
        _queue->ExecuteCommandLists(1, &pList);

        _swapChain->Present(1, 0);
        MoveToNextFrame();
    }

    private void MoveToNextFrame()
    {
        var fence = _fenceValues[_frameIndex];
        _queue->Signal(_fence, fence);
        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
        if (_fence->GetCompletedValue() < _fenceValues[_frameIndex])
        {
            _fence->SetEventOnCompletion(_fenceValues[_frameIndex], _fenceEvent);
            PInvoke.WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
        }
        _fenceValues[_frameIndex] = fence + 1;
    }

    private void WaitForGpu()
    {
        var fence = _fenceValues[_frameIndex];
        _queue->Signal(_fence, fence);
        _fence->SetEventOnCompletion(fence, _fenceEvent);
        PInvoke.WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
        for (var i = 0; i < FrameCount; i++) _fenceValues[i] = fence + 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        WaitForGpu();
        for (var i = 0; i < FrameCount; i++)
        {
            _renderTargets[i]->Release();
            _allocators[i]->Release();
        }
        _list->Release();
        _rtvHeap->Release();
        _fence->Release();
        PInvoke.CloseHandle(_fenceEvent);
        _swapChain->Release();
        _queue->Release();
        _device->Release();
        _disposed = true;
    }
}
