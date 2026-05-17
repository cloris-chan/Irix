using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal enum TextCompositionMode
{
    Overlay,
    GlyphAtlas
}

/// <summary>
/// D3D12 renderer using CsWin32 generated raw-pointer COM wrappers.
/// All vtable offsets and GUIDs are managed by CsWin32 — no manual bindings.
/// </summary>
internal sealed unsafe class D3D12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const int EOutOfMemory = unchecked((int)0x8007000E);

    private ID3D12Device* _device;
    private ID3D12CommandQueue* _queue;
    private IDXGISwapChain3* _swapChain;
    private ID3D12Resource*[] _renderTargets;
    private ID3D12DescriptorHeap* _rtvHeap;
    private uint _rtvSize;
    private ID3D12CommandAllocator*[] _allocators;
    private ID3D12GraphicsCommandList* _list;
    private ID3D12Fence* _fence;
    private SafeHandle? _fenceEventOwner;
    private HANDLE _fenceEvent;
    private ulong[] _fenceValues;
    private uint _frameIndex;
    private D3D12Renderer2D? _renderer2D;
    private D3D12TextRenderer? _textRenderer;
    private D3D12GlyphAtlasTextRenderer? _glyphAtlasTextRenderer;
    private D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics _glyphAtlasTextDiagnostics;
    private readonly nint _hwnd;
    private readonly Lock _resizeLock = new();
    private int _width;
    private int _height;
    private bool _disposed;
    private bool _deviceRemoved;
    private string? _deviceErrorReason;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _pendingResize;

    // Frame serial diagnostics
    private long _frameSerial;
    private long _presentSerial;
    private long _syncWaitCount;
    private long _syncWaitTicks;

    /// <summary>
    /// When true, inserts a GPU fence wait after D2D text overlay rendering
    /// and before Present. Forces all queued GPU work (D3D12 rects + D3D11/D2D text)
    /// to complete before the swap chain presents. Default: true.
    /// Disable with --no-sync-text-overlay for performance comparison.
    /// </summary>
    public bool SyncTextOverlay { get; set; } = true;

    internal TextOverlaySyncStrategy TextOverlaySyncStrategy { get; set; } = TextOverlaySyncStrategy.D3D12FenceAfterOverlay;

    internal TextCompositionMode TextCompositionMode { get; set; } = TextCompositionMode.Overlay;

    public D3D12Renderer(nint hwnd, int width, int height)
    {
        _hwnd = hwnd;
        _width = width;
        _height = height;

        // Enable D3D12 debug layer when a debugger is attached
        if (System.Diagnostics.Debugger.IsAttached)
        {
            ID3D12Debug* debugController;
            if (PInvoke.D3D12GetDebugInterface(typeof(ID3D12Debug).GUID, out var debugObj).Succeeded)
            {
                debugController = (ID3D12Debug*)debugObj;
                debugController->EnableDebugLayer();
                debugController->Release();
            }
        }

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
            Scaling = DXGI_SCALING.DXGI_SCALING_NONE,
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
        _fenceEventOwner = PInvoke.CreateEvent(null, false, false, null);
        if (_fenceEventOwner.IsInvalid)
        {
            throw new InvalidOperationException("Failed to create the D3D12 fence event.");
        }

        _fenceEvent = new HANDLE(_fenceEventOwner.DangerousGetHandle());

        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
        _renderer2D = new D3D12Renderer2D(_device);
        _textRenderer = new D3D12TextRenderer(_device, _queue, _renderTargets);
    }

    public D3D12Renderer2D Renderer2D => _renderer2D!;

    public int Width => Volatile.Read(ref _width);
    public int Height => Volatile.Read(ref _height);

    public bool IsDeviceRemoved => _deviceRemoved || (_textRenderer?.IsDeviceRemoved ?? false) || (_glyphAtlasTextRenderer?.IsDeviceRemoved ?? false);

    public string? DeviceErrorReason => _deviceErrorReason ?? _textRenderer?.DeviceErrorReason ?? _glyphAtlasTextRenderer?.DeviceErrorReason;

    public event Action? DeviceLost;

    public D3D12TextRenderer.TextRendererDiagnostics? GetTextDiagnostics()
    {
        return _textRenderer?.GetDiagnostics();
    }

    public D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics? GetGlyphAtlasTextDiagnostics()
    {
        return _glyphAtlasTextRenderer?.GetDiagnostics() ?? (_glyphAtlasTextDiagnostics.FallbackFrames > 0 ? _glyphAtlasTextDiagnostics : null);
    }

    public void ResetTextDiagnostics()
    {
        _textRenderer?.ResetDiagnostics();
        _glyphAtlasTextRenderer?.ResetDiagnostics();
        _glyphAtlasTextDiagnostics = default;
    }

    /// <summary>Snapshot of frame serial counters for diagnostics.</summary>
    internal readonly struct FrameSerialDiagnostics(
        long FrameSerial,
        long PresentSerial,
        long SyncWaitCount,
        long SyncWaitTicks,
        uint BackBufferIndex,
        TextOverlaySyncStrategy SyncStrategy) : IEquatable<FrameSerialDiagnostics>
    {
        public long FrameSerial { get; } = FrameSerial;
        public long PresentSerial { get; } = PresentSerial;
        public long SyncWaitCount { get; } = SyncWaitCount;
        public long SyncWaitTicks { get; } = SyncWaitTicks;
        public uint BackBufferIndex { get; } = BackBufferIndex;
        public TextOverlaySyncStrategy SyncStrategy { get; } = SyncStrategy;

        public double SyncWaitMs => SyncWaitTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000;

        public bool Equals(FrameSerialDiagnostics other)
        {
            return FrameSerial == other.FrameSerial
                && PresentSerial == other.PresentSerial
                && SyncWaitCount == other.SyncWaitCount
                && SyncWaitTicks == other.SyncWaitTicks
                && BackBufferIndex == other.BackBufferIndex
                && SyncStrategy == other.SyncStrategy;
        }

        public override bool Equals(object? obj) => obj is FrameSerialDiagnostics other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FrameSerial, PresentSerial, SyncWaitCount, SyncWaitTicks, BackBufferIndex, SyncStrategy);

        public static bool operator ==(FrameSerialDiagnostics left, FrameSerialDiagnostics right) => left.Equals(right);

        public static bool operator !=(FrameSerialDiagnostics left, FrameSerialDiagnostics right) => !left.Equals(right);
    }

    internal FrameSerialDiagnostics GetFrameSerialDiagnostics()
    {
        return new FrameSerialDiagnostics(
            Volatile.Read(ref _frameSerial),
            Volatile.Read(ref _presentSerial),
            Volatile.Read(ref _syncWaitCount),
            Volatile.Read(ref _syncWaitTicks),
            _frameIndex,
            TextOverlaySyncStrategy);
    }

    public bool HasPendingResize
    {
        get
        {
            lock (_resizeLock)
            {
                return _pendingResize;
            }
        }
    }

    public int PendingWidth
    {
        get
        {
            lock (_resizeLock)
            {
                return _pendingWidth;
            }
        }
    }

    public int PendingHeight
    {
        get
        {
            lock (_resizeLock)
            {
                return _pendingHeight;
            }
        }
    }

    public void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0 || newHeight <= 0) return;
        if (IsDeviceRemoved) return;

        lock (_resizeLock)
        {
            if (newWidth == Volatile.Read(ref _width) && newHeight == Volatile.Read(ref _height))
            {
                _pendingResize = false;
                return;
            }

            _pendingWidth = newWidth;
            _pendingHeight = newHeight;
            _pendingResize = true;
        }
    }

    public bool ApplyPendingResize()
    {
        if (IsDeviceRemoved) return false;

        int newWidth;
        int newHeight;
        lock (_resizeLock)
        {
            if (!_pendingResize)
            {
                return false;
            }

            newWidth = _pendingWidth;
            newHeight = _pendingHeight;
            _pendingResize = false;
        }

        return ApplyResize(newWidth, newHeight);
    }

    private bool ApplyResize(int newWidth, int newHeight)
    {
        if (newWidth == Volatile.Read(ref _width) && newHeight == Volatile.Read(ref _height))
        {
            return false;
        }

        if (!WaitForGpu())
        {
            return false;
        }

        _textRenderer?.ReleaseFrameResourcesForResize();

        // Release all back buffer references before ResizeBuffers
        for (var i = 0; i < FrameCount; i++)
        {
            if (_renderTargets[i] != null)
            {
                _renderTargets[i]->Release();
                _renderTargets[i] = null;
            }
        }

        try
        {
            _swapChain->ResizeBuffers(FrameCount, (uint)newWidth, (uint)newHeight, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM, 0);

            var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
            for (var i = 0; i < FrameCount; i++)
            {
                _swapChain->GetBuffer((uint)i, typeof(ID3D12Resource).GUID, out var resObj);
                _renderTargets[i] = (ID3D12Resource*)resObj;
                _device->CreateRenderTargetView(_renderTargets[i], null, rtv);
                rtv.ptr += _rtvSize;
            }

            Volatile.Write(ref _width, newWidth);
            Volatile.Write(ref _height, newHeight);
            _frameIndex = _swapChain->GetCurrentBackBufferIndex();
            _textRenderer?.RecreateFrameResources(_renderTargets);
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HandleDeviceError(ex, "Resize resource recreation");
            return false;
        }
    }

    public bool BeginFrame()
    {
        if (_deviceRemoved) return false;
        if (TryResetFrameCommands(out var firstResetError)) return true;
        if (!WaitForGpu()) return false;
        if (TryResetFrameCommands(out var retryResetError)) return true;

        HandleDeviceError(retryResetError ?? firstResetError, "BeginFrame command reset");
        return false;
    }

    public void ClearAndPresent(float r, float g, float b, float a = 1.0f)
    {
        if (_deviceRemoved) return;
        var barrier = new D3D12_RESOURCE_BARRIER
        {
            Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION
        };
        barrier.Anonymous.Transition.pResource = _renderTargets[_frameIndex];
        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.Subresource = 0xFFFFFFFF;
        _list->ResourceBarrier(1, &barrier);

        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        rtv.ptr += _frameIndex * _rtvSize;
        var color = stackalloc float[] { r, g, b, a };
        _list->ClearRenderTargetView(rtv, new ReadOnlySpan<float>(color, 4));

        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        _list->ResourceBarrier(1, &barrier);

        _list->Close();

        var pList = (ID3D12CommandList*)_list;
        _queue->ExecuteCommandLists(1, &pList);

        if (!Present()) return;
        _ = MoveToNextFrame();
    }

    /// <summary>
    /// Render colored rectangles using D3D12Renderer2D, then present.
    /// </summary>
    public void RenderRectangles(ReadOnlySpan<D3D12Renderer2D.RectData> rects)
    {
        RenderFrame(rects, [], FrameDrawingResources.Empty, 0.1f, 0.1f, 0.1f, 1.0f);
    }

    /// <summary>
    /// Render colored rectangles with an optional DirectWrite text overlay, then present.
    /// </summary>
    public void RenderFrame(
        ReadOnlySpan<D3D12Renderer2D.RectData> rects,
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources,
        float clearR,
        float clearG,
        float clearB,
        float clearA)
    {
        if (_deviceRemoved) return;
        // Transition to render target
        var barrier = new D3D12_RESOURCE_BARRIER
        {
            Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION
        };
        barrier.Anonymous.Transition.pResource = _renderTargets[_frameIndex];
        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.Subresource = 0xFFFFFFFF;
        _list->ResourceBarrier(1, &barrier);

        // Set render target
        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        rtv.ptr += _frameIndex * _rtvSize;
        _list->OMSetRenderTargets(1, &rtv, false, null);

        var bgColor = stackalloc float[] { clearR, clearG, clearB, clearA };
        _list->ClearRenderTargetView(rtv, new ReadOnlySpan<float>(bgColor, 4));

        var width = Width;
        var height = Height;

        // Set viewport and scissor from actual window dimensions
        var viewport = new D3D12_VIEWPORT { Width = width, Height = height, MaxDepth = 1.0f };
        _list->RSSetViewports(1, &viewport);
        var scissor = new RECT { right = width, bottom = height };
        _list->RSSetScissorRects(1, &scissor);

        _renderer2D!.RenderRectangles(_list, rects, width, height);

        var textRenderer = _textRenderer;
        var hasText = textRuns.Length > 0 && textRenderer != null;
        var renderTextWithOverlayFallback = hasText;
        if (hasText && TextCompositionMode == TextCompositionMode.GlyphAtlas)
        {
            renderTextWithOverlayFallback = !TryRecordGlyphAtlasTextPass(textRuns, resources);
            if (_deviceRemoved)
            {
                return;
            }
        }

        if (!renderTextWithOverlayFallback)
        {
            // Transition to present when Direct2D is not handling the wrapped back buffer.
            barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
            barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
            _list->ResourceBarrier(1, &barrier);
        }

        _list->Close();

        var pList = (ID3D12CommandList*)_list;
        _queue->ExecuteCommandLists(1, &pList);
        Interlocked.Increment(ref _frameSerial);

        if (renderTextWithOverlayFallback)
        {
            if (!RenderOverlayTextAndMaybeSync(textRenderer!, textRuns, resources)) return;
        }

        if (!Present()) return;
        _ = MoveToNextFrame();
    }

    private bool TryRecordGlyphAtlasTextPass(ReadOnlySpan<D3D12TextRenderer.TextData> textRuns, IFrameResourceResolver resources)
    {
        D3D12GlyphAtlasTextRenderer glyphAtlasTextRenderer;
        try
        {
            glyphAtlasTextRenderer = _glyphAtlasTextRenderer ??= new D3D12GlyphAtlasTextRenderer(_device);
        }
        catch (COMException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] Glyph atlas initialization failed, falling back to overlay: 0x{ex.ErrorCode:X8}");
            RecordGlyphAtlasInitializationFallback(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.InitializationFailed);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] Glyph atlas initialization failed, falling back to overlay: {ex.Message}");
            RecordGlyphAtlasInitializationFallback(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.CompileFailed);
            return false;
        }

        var recorded = glyphAtlasTextRenderer.TryRecord(_list, textRuns, resources, Width, Height);
        if (!recorded && glyphAtlasTextRenderer.IsDeviceRemoved)
        {
            MarkDeviceRemoved(glyphAtlasTextRenderer.DeviceErrorReason ?? "Glyph atlas text pass failed");
        }

        return recorded;
    }

    private void RecordGlyphAtlasInitializationFallback(D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason reason)
    {
        _glyphAtlasTextDiagnostics = _glyphAtlasTextDiagnostics.WithFallback(1, reason);
    }

    private bool RenderOverlayTextAndMaybeSync(
        D3D12TextRenderer textRenderer,
        ReadOnlySpan<D3D12TextRenderer.TextData> textRuns,
        IFrameResourceResolver resources)
    {
        if (!textRenderer.Render(_frameIndex, textRuns, resources))
        {
            if (!_deviceRemoved)
            {
                _deviceRemoved = true;
                _deviceErrorReason = textRenderer.DeviceErrorReason ?? "TextRenderer render failed";
                System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceErrorReason}");
                DeviceLost?.Invoke();
            }
            return false;
        }

        // Diagnostic sync: wait for all GPU work (D3D12 rects + D3D11/D2D text)
        // to complete before presenting. Confirms whether text-lag is a sync issue.
        if (SyncTextOverlay)
        {
            return WaitForTextOverlaySync(textRenderer);
        }

        return true;
    }

    private bool Present()
    {
        try
        {
            _swapChain->Present(1, 0);
            Interlocked.Increment(ref _presentSerial);
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, "Present");
            return false;
        }
    }

    private bool MoveToNextFrame()
    {
        try
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
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, "MoveToNextFrame");
            return false;
        }
    }

    private void HandleDeviceError(Exception? ex, string context = "Unknown")
    {
        MarkDeviceRemoved(FormatDeviceError(ex, context));
    }

    private void MarkDeviceRemoved(string reason)
    {
        if (_deviceRemoved) return;
        _deviceRemoved = true;
        _deviceErrorReason = reason;
        System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceErrorReason}");
        DeviceLost?.Invoke();
    }

    private static string FormatDeviceError(Exception? ex, string context)
    {
        return ex switch
        {
            COMException { ErrorCode: EOutOfMemory } comException => $"{context}: E_OUTOFMEMORY (0x{comException.ErrorCode:X8})",
            COMException comException => $"{context}: COMException 0x{comException.ErrorCode:X8}",
            null => $"{context}: unknown device error",
            _ => $"{context}: {ex.GetType().Name}: {ex.Message}"
        };
    }

    private bool TryResetFrameCommands(out COMException? error)
    {
        try
        {
            _allocators[_frameIndex]->Reset();
            _list->Reset(_allocators[_frameIndex], null);
            error = null;
            return true;
        }
        catch (COMException ex)
        {
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Check HRESULT from D3D/DXGI/D2D calls that return it directly.
    /// Sets _deviceRemoved on any failure.
    /// </summary>
    internal bool SucceededOrMarkDeviceRemoved(HRESULT hr, string context)
    {
        if (hr.Succeeded) return true;
        if (!_deviceRemoved)
        {
            _deviceRemoved = true;
            _deviceErrorReason = $"{context}: HRESULT 0x{unchecked((uint)hr.Value):X8}";
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceErrorReason}");
            DeviceLost?.Invoke();
        }
        return false;
    }

    private bool WaitForGpu()
    {
        try
        {
            var fence = _fenceValues[_frameIndex];
            _queue->Signal(_fence, fence);
            _fence->SetEventOnCompletion(fence, _fenceEvent);
            PInvoke.WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
            for (var i = 0; i < FrameCount; i++) _fenceValues[i] = fence + 1;
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, "WaitForGpu");
            return false;
        }
    }

    /// <summary>
    /// Signal the D3D12 command queue and wait for all pending GPU work to complete.
    /// Used for diagnostic synchronization between D3D12 rect pass and D3D11/D2D text overlay.
    /// </summary>
    private bool WaitForQueueIdle()
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var fenceValue = _fenceValues[_frameIndex] + 1;
            _queue->Signal(_fence, fenceValue);
            _fence->SetEventOnCompletion(fenceValue, _fenceEvent);
            PInvoke.WaitForSingleObject(_fenceEvent, 0xFFFFFFFF);
            _fenceValues[_frameIndex] = fenceValue + 1;
            sw.Stop();
            RecordSyncWait(sw.ElapsedTicks);
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, "WaitForQueueIdle");
            return false;
        }
    }

    private bool WaitForTextOverlaySync(D3D12TextRenderer textRenderer)
    {
        return TextOverlaySyncStrategy switch
        {
            TextOverlaySyncStrategy.D3D11Query => WaitForD3D11OverlayQuery(textRenderer),
            _ => WaitForQueueIdle()
        };
    }

    private bool WaitForD3D11OverlayQuery(D3D12TextRenderer textRenderer)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var completed = textRenderer.WaitForOverlayCompletionQuery();
        sw.Stop();
        RecordSyncWait(sw.ElapsedTicks);
        if (completed)
        {
            return true;
        }

        if (!_deviceRemoved)
        {
            _deviceRemoved = true;
            _deviceErrorReason = textRenderer.DeviceErrorReason ?? "D3D11 overlay completion query failed";
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceErrorReason}");
            DeviceLost?.Invoke();
        }

        return false;
    }

    private void RecordSyncWait(long elapsedTicks)
    {
        Interlocked.Increment(ref _syncWaitCount);
        Interlocked.Add(ref _syncWaitTicks, elapsedTicks);
    }

    /// <summary>
    /// Attempt to recover from device-lost by releasing all GPU resources
    /// and reinitializing from scratch. Returns true if recovery succeeds.
    /// </summary>
    public bool TryRecover()
    {
        if (!_deviceRemoved) return true;

        System.Diagnostics.Debug.WriteLine("[D3D12Renderer] Attempting device-lost recovery...");

        // Phase 1: Release all GPU resources (same order as Dispose, but don't set _disposed)
        try
        {
            _textRenderer?.Dispose();
            _textRenderer = null;
            _glyphAtlasTextRenderer?.Dispose();
            _glyphAtlasTextRenderer = null;
            _renderer2D?.Dispose();
            _renderer2D = null;

            for (var i = 0; i < FrameCount; i++)
            {
                if (_renderTargets[i] != null)
                {
                    _renderTargets[i]->Release();
                    _renderTargets[i] = null;
                }
                if (_allocators[i] != null)
                {
                    _allocators[i]->Release();
                    _allocators[i] = null;
                }
            }

            if (_list != null) { _list->Release(); _list = null; }
            if (_rtvHeap != null) { _rtvHeap->Release(); _rtvHeap = null; }
            if (_fence != null) { _fence->Release(); _fence = null; }
            _fenceEventOwner?.Dispose();
            _fenceEventOwner = null;
            _fenceEvent = default;
            if (_swapChain != null) { _swapChain->Release(); _swapChain = null; }
            if (_queue != null) { _queue->Release(); _queue = null; }
            if (_device != null) { _device->Release(); _device = null; }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] Recovery release failed: {ex.Message}");
            return false;
        }

        // Phase 2: Reinitialize all resources
        try
        {
            var width = Volatile.Read(ref _width);
            var height = Volatile.Read(ref _height);

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
                Scaling = DXGI_SCALING.DXGI_SCALING_NONE,
                SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD
            };
            IDXGISwapChain1* sc1;
            factory->CreateSwapChainForHwnd((global::Windows.Win32.System.Com.IUnknown*)_queue, new HWND(_hwnd), &sd, null, null, &sc1);
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
            _fenceEventOwner = PInvoke.CreateEvent(null, false, false, null);
            if (_fenceEventOwner.IsInvalid)
            {
                throw new InvalidOperationException("Failed to create the D3D12 fence event during recovery.");
            }
            _fenceEvent = new HANDLE(_fenceEventOwner.DangerousGetHandle());

            _frameIndex = _swapChain->GetCurrentBackBufferIndex();
            _renderer2D = new D3D12Renderer2D(_device);
            _textRenderer = new D3D12TextRenderer(_device, _queue, _renderTargets);

            // Reset device-lost state
            _deviceRemoved = false;
            _deviceErrorReason = null;
            _pendingResize = false;
            _frameSerial = 0;
            _presentSerial = 0;
            _syncWaitCount = 0;
            _syncWaitTicks = 0;

            System.Diagnostics.Debug.WriteLine("[D3D12Renderer] Device-lost recovery succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            _deviceRemoved = true;
            _deviceErrorReason = FormatDeviceError(ex, "Recovery reinitialize");
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceErrorReason}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _ = WaitForGpu();
        _textRenderer?.Dispose();
        _textRenderer = null;
        _glyphAtlasTextRenderer?.Dispose();
        _glyphAtlasTextRenderer = null;
        _renderer2D?.Dispose();
        _renderer2D = null;
        for (var i = 0; i < FrameCount; i++)
        {
            if (_renderTargets[i] != null)
            {
                _renderTargets[i]->Release();
            }

            if (_allocators[i] != null)
            {
                _allocators[i]->Release();
            }
        }
        _list->Release();
        _rtvHeap->Release();
        _fence->Release();
        _fenceEventOwner?.Dispose();
        _fenceEventOwner = null;
        _fenceEvent = default;
        _swapChain->Release();
        _queue->Release();
        _device->Release();
        _disposed = true;
    }
}
