using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Platform.Windows.D3D12;

namespace Irix.Platform.Windows;

/// <summary>
/// Minimal D3D12 renderer: device + swap chain + clear color + present.
/// Phase 1 of D3D12 backend integration. Uses raw COM vtable calls.
/// </summary>
internal sealed class D3D12Renderer : IDisposable
{
    private const int FrameCount = 2;

    private static ref Guid GuidFactory4() => ref Unsafe.AsRef(in Unsafe.NullRef<Guid>()); // unused placeholder
    private static Guid G(string s) => new(s);

    private nint _device;
    private nint _queue;
    private nint _swapChain;
    private nint[] _renderTargets = [];
    private nint _rtvHeap;
    private uint _rtvSize;
    private nint[] _allocators = [];
    private nint _list;
    private nint _fence;
    private nint _fenceEvent;
    private ulong[] _fenceValues = [];
    private uint _frameIndex;
    private bool _disposed;

    public D3D12Renderer(nint hwnd, int width, int height)
    {
        unsafe
        {
            nint ptr;
            int hr;

            // DXGI factory
            var riid = G("1bc6ea02-ef36-464f-bf0c-21ca39e5168a");
            nint factory;
            hr = D3D12NativeMethods.CreateDXGIFactory1(&riid, (void**)&factory);
            if (hr < 0) throw new InvalidOperationException($"CreateDXGIFactory1 failed: 0x{hr:X8}");

            // D3D12 device
            riid = G("189819f1-1db6-4b57-be54-1821339b85f7");
            hr = D3D12NativeMethods.D3D12CreateDevice(0, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"D3D12CreateDevice failed: 0x{hr:X8}");
            _device = ptr;

            // Command queue
            riid = G("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
            var qd = new D3D12_COMMAND_QUEUE_DESC { Type = D3D12_COMMAND_LIST_TYPE.DIRECT };
            hr = D3D12Vtable.CreateCommandQueue(_device, &qd, &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"CreateCommandQueue failed: 0x{hr:X8}");
            _queue = ptr;

            // Swap chain
            var sd = new DXGI_SWAP_CHAIN_DESC1
            {
                Width = (uint)width, Height = (uint)height,
                Format = DXGI_FORMAT.R8G8B8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                BufferUsage = 0x20, BufferCount = FrameCount,
                Scaling = DXGI_SCALING.STRETCH,
                SwapEffect = DXGI_SWAP_EFFECT.FLIP_DISCARD
            };
            riid = G("790a45f7-0d42-4876-983a-0a55cfe6f4aa");
            nint sc1;
            hr = D3D12Vtable.CreateSwapChainForHwnd(factory, _queue, hwnd, &sd, null, null, (void**)&sc1);
            if (hr < 0) throw new InvalidOperationException($"CreateSwapChainForHwnd failed: 0x{hr:X8}");
            riid = G("94D99BDB-F1F8-4AB0-B236-7DA0170EDAB1");
            var qi = (delegate* unmanaged[Stdcall]<nint, Guid*, void**, int>)(*(void***)sc1)[0];
            hr = qi(sc1, &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"QueryInterface(IDXGISwapChain3) failed: 0x{hr:X8}");
            _swapChain = ptr;
            D3D12Vtable.Release(sc1);
            D3D12Vtable.Release(factory);

            // RTV heap
            riid = G("8efb471d-616c-4f49-90f7-127bb763fa51");
            var hd = new D3D12_DESCRIPTOR_HEAP_DESC { NumDescriptors = FrameCount, Type = D3D12_DESCRIPTOR_HEAP_TYPE.RTV };
            hr = D3D12Vtable.CreateDescriptorHeap(_device, &hd, &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"CreateDescriptorHeap failed: 0x{hr:X8}");
            _rtvHeap = ptr;
            Console.WriteLine($"[D3D12] _rtvHeap = 0x{_rtvHeap:X16}");
            _rtvSize = D3D12Vtable.GetDescriptorHandleIncrementSize(_device, D3D12_DESCRIPTOR_HEAP_TYPE.RTV);
            Console.WriteLine($"[D3D12] _rtvSize = {_rtvSize}");

            // Render targets
            _renderTargets = new nint[FrameCount];
            Console.WriteLine($"[D3D12] Calling GetCPUDescriptorHandleForHeapStart on 0x{_rtvHeap:X16}...");
            var rtv = D3D12Vtable.GetCPUDescriptorHandleForHeapStart(_rtvHeap);
            riid = G("696442be-a72e-4059-bc79-5b5c98040fad");
            fixed (nint* pRT = _renderTargets)
            {
                for (var i = 0; i < FrameCount; i++)
                {
                    hr = D3D12Vtable.GetBuffer(_swapChain, (uint)i, &riid, (void**)&pRT[i]);
                    if (hr < 0) throw new InvalidOperationException($"GetBuffer({i}) failed: 0x{hr:X8}");
                    D3D12Vtable.CreateRenderTargetView(_device, pRT[i], rtv);
                    rtv.ptr += _rtvSize;
                }
            }

            // Command allocators
            _allocators = new nint[FrameCount];
            riid = G("6102DEE4-AF59-4B09-B999-B44D73F09B24");
            fixed (nint* pA = _allocators)
            {
                for (var i = 0; i < FrameCount; i++)
                {
                    hr = D3D12Vtable.CreateCommandAllocator(_device, D3D12_COMMAND_LIST_TYPE.DIRECT, &riid, (void**)&pA[i]);
                    if (hr < 0) throw new InvalidOperationException($"CreateCommandAllocator({i}) failed: 0x{hr:X8}");
                }
            }

            // Command list
            riid = G("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
            fixed (nint* pA = _allocators)
                hr = D3D12Vtable.CreateCommandList(_device, 0, D3D12_COMMAND_LIST_TYPE.DIRECT, pA[0], &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"CreateCommandList failed: 0x{hr:X8}");
            _list = ptr;
            D3D12Vtable.Close(_list);

            // Fence
            riid = G("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
            hr = D3D12Vtable.CreateFence(_device, 0, 0, &riid, (void**)&ptr);
            if (hr < 0) throw new InvalidOperationException($"CreateFence failed: 0x{hr:X8}");
            _fence = ptr;
            _fenceValues = new ulong[FrameCount];
            _fenceEvent = CreateEventW(0, 1, 0, 0);
            if (_fenceEvent == 0) throw new InvalidOperationException("CreateEvent failed");

            _frameIndex = D3D12Vtable.GetCurrentBackBufferIndex(_swapChain);
        }
    }

    public void BeginFrame()
    {
        D3D12Vtable.ResetAllocator(_allocators[_frameIndex]);
        D3D12Vtable.ResetList(_list, _allocators[_frameIndex], 0);
    }

    public void ClearAndPresent(float r, float g, float b, float a = 1.0f)
    {
        unsafe
        {
            var barrier = new D3D12_RESOURCE_BARRIER
            {
                Type = 0,
                pResource = _renderTargets[_frameIndex],
                StateBefore = (uint)D3D12_RESOURCE_STATES.PRESENT,
                StateAfter = (uint)D3D12_RESOURCE_STATES.RENDER_TARGET,
                Subresource = 0xFFFFFFFF
            };
            D3D12Vtable.ResourceBarrier(_list, 1, &barrier);

            var rtv = D3D12Vtable.GetCPUDescriptorHandleForHeapStart(_rtvHeap);
            rtv.ptr += _frameIndex * _rtvSize;
            var color = stackalloc float[] { r, g, b, a };
            D3D12Vtable.ClearRenderTargetView(_list, rtv, color, 0, null);

            barrier.StateBefore = (uint)D3D12_RESOURCE_STATES.RENDER_TARGET;
            barrier.StateAfter = (uint)D3D12_RESOURCE_STATES.PRESENT;
            D3D12Vtable.ResourceBarrier(_list, 1, &barrier);

            D3D12Vtable.Close(_list);
            var pList = _list;
            D3D12Vtable.ExecuteCommandLists(_queue, 1, &pList);
            D3D12Vtable.Present(_swapChain, 1, 0);
            MoveToNextFrame();
        }
    }

    private void MoveToNextFrame()
    {
        var fence = _fenceValues[_frameIndex];
        D3D12Vtable.QueueSignal(_queue, _fence, fence);
        _frameIndex = D3D12Vtable.GetCurrentBackBufferIndex(_swapChain);
        if (D3D12Vtable.GetCompletedValue(_fence) < _fenceValues[_frameIndex])
        {
            D3D12Vtable.SetEventOnCompletion(_fence, _fenceValues[_frameIndex], _fenceEvent);
            WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
        }
        _fenceValues[_frameIndex] = fence + 1;
    }

    private void WaitForGpu()
    {
        var fence = _fenceValues[_frameIndex];
        D3D12Vtable.QueueSignal(_queue, _fence, fence);
        D3D12Vtable.SetEventOnCompletion(_fence, fence, _fenceEvent);
        WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
        for (var i = 0; i < FrameCount; i++) _fenceValues[i] = fence + 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        WaitForGpu();
        for (var i = 0; i < FrameCount; i++) { D3D12Vtable.Release(_renderTargets[i]); D3D12Vtable.Release(_allocators[i]); }
        D3D12Vtable.Release(_list);
        D3D12Vtable.Release(_rtvHeap);
        D3D12Vtable.Release(_fence);
        CloseHandle(_fenceEvent);
        D3D12Vtable.Release(_swapChain);
        D3D12Vtable.Release(_queue);
        D3D12Vtable.Release(_device);
        _disposed = true;
    }

    [DllImport("kernel32.dll")]
    private static extern nint CreateEventW(nint attr, int manual, int initial, nint name);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(nint handle, uint ms);

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(nint handle);
}
