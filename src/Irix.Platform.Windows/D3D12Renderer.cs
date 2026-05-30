using System.Runtime.InteropServices;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Irix.Platform.Windows;

internal enum TextCompositionMode
{
    GlyphAtlas
}

/// <summary>
/// D3D12 renderer using CsWin32 generated raw-pointer COM wrappers.
/// All vtable offsets and GUIDs are managed by CsWin32 — no manual bindings.
/// </summary>
internal sealed unsafe class D3D12Renderer : IDisposable
{
    private const int FrameCount = 2;
    private const uint InfiniteWaitMilliseconds = 0xFFFFFFFF;

    private ID3D12Device* _device;
    private ID3D12CommandQueue* _queue;
    private IDXGISwapChain3* _swapChain;
    private ID3D12Resource*[] _renderTargets = new ID3D12Resource*[FrameCount];
    private ID3D12DescriptorHeap* _rtvHeap;
    private uint _rtvSize;
    private ID3D12CommandAllocator*[] _allocators = new ID3D12CommandAllocator*[FrameCount];
    private ID3D12GraphicsCommandList* _list;
    private ID3D12Fence* _fence;
    private SafeHandle? _fenceEventOwner;
    private HANDLE _fenceEvent;
    private ulong[] _fenceValues = new ulong[FrameCount];
    private uint _frameIndex;
    private D3D12Renderer2D? _renderer2D;
    private D3D12TexturedQuadRenderer? _textureQuadRenderer;
    private D3D12CompositionLayerRenderTargetCache? _compositionLayerRenderTargetCache;
    private D3D12GlyphAtlasTextRenderer? _glyphAtlasTextRenderer;
    private D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics _glyphAtlasTextDiagnostics;
    private readonly FrameRenderList<D3D12Renderer2D.RectData> _layerRenderTargetRects = new();
    private readonly FrameRenderList<D3D12TextRun> _layerRenderTargetTexts = new();
    private readonly nint _hwnd;
    private readonly Lock _resizeLock = new();
    private int _width;
    private int _height;
    private bool _disposed;
    private bool _deviceRemoved;
    private DeviceErrorDiagnostic _deviceError = DeviceErrorDiagnostic.None;
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _pendingResize;

    // Frame serial diagnostics
    private long _frameSerial;
    private long _presentSerial;
    private long _syncWaitCount;
    private long _syncWaitTicks;

    internal TextCompositionMode TextCompositionMode { get; set; } = TextCompositionMode.GlyphAtlas;

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

        try
        {
            InitializeDeviceResources(width, height);
        }
        catch
        {
            ReleaseDeviceResources(waitForGpu: false);
            throw;
        }
    }

    public D3D12Renderer2D Renderer2D => _renderer2D!;

    public int Width => Volatile.Read(ref _width);
    public int Height => Volatile.Read(ref _height);

    public bool IsDeviceRemoved => _deviceRemoved;

    public DeviceErrorDiagnostic DeviceError
    {
        get
        {
            if (!_deviceError.IsNone) return _deviceError;
            return _glyphAtlasTextRenderer?.DeviceError ?? DeviceErrorDiagnostic.None;
        }
    }

    public event Action? DeviceLost;

    public D3D12GlyphAtlasTextRenderer.GlyphAtlasTextRendererDiagnostics? GetGlyphAtlasTextDiagnostics()
    {
        return _glyphAtlasTextRenderer?.GetDiagnostics() ?? (_glyphAtlasTextDiagnostics.FallbackFrames > 0 ? _glyphAtlasTextDiagnostics : null);
    }

    public void ResetTextDiagnostics()
    {
        _glyphAtlasTextRenderer?.ResetDiagnostics();
        _glyphAtlasTextDiagnostics = default;
    }

    /// <summary>Snapshot of frame serial counters for diagnostics.</summary>
    internal readonly struct FrameSerialDiagnostics(
        long FrameSerial,
        long PresentSerial,
        long SyncWaitCount,
        long SyncWaitTicks,
        uint BackBufferIndex) : IEquatable<FrameSerialDiagnostics>
    {
        public long FrameSerial { get; } = FrameSerial;
        public long PresentSerial { get; } = PresentSerial;
        public long SyncWaitCount { get; } = SyncWaitCount;
        public long SyncWaitTicks { get; } = SyncWaitTicks;
        public uint BackBufferIndex { get; } = BackBufferIndex;

        public double SyncWaitMs => SyncWaitTicks / (double)System.Diagnostics.Stopwatch.Frequency * 1000;

        public bool Equals(FrameSerialDiagnostics other)
        {
            return FrameSerial == other.FrameSerial
                && PresentSerial == other.PresentSerial
                && SyncWaitCount == other.SyncWaitCount
                && SyncWaitTicks == other.SyncWaitTicks
                && BackBufferIndex == other.BackBufferIndex;
        }

        public override bool Equals(object? obj) => obj is FrameSerialDiagnostics other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FrameSerial, PresentSerial, SyncWaitCount, SyncWaitTicks, BackBufferIndex);

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
            _frameIndex);
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
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HandleDeviceError(ex, DeviceErrorSite.ResizeResourceRecreation);
            return false;
        }
    }

    public bool BeginFrame()
    {
        if (_deviceRemoved) return false;
        if (TryResetFrameCommands(out var firstResetError))
        {
            BeginUploadFrame();
            return true;
        }

        if (!WaitForGpu()) return false;
        if (TryResetFrameCommands(out var retryResetError))
        {
            BeginUploadFrame();
            return true;
        }

        HandleDeviceError(retryResetError ?? firstResetError, DeviceErrorSite.BeginFrameCommandReset);
        return false;
    }

    private void BeginUploadFrame()
    {
        var frameResourceIndex = (int)_frameIndex;
        _renderer2D?.BeginFrame(frameResourceIndex);
        _textureQuadRenderer?.BeginFrame(frameResourceIndex);
        _glyphAtlasTextRenderer?.BeginFrame(frameResourceIndex);
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
    /// Render colored rectangles and D3D12 glyph-atlas text, then present.
    /// </summary>
    public void RenderFrame(
        ReadOnlySpan<D3D12Renderer2D.RectData> rects,
        ReadOnlySpan<D3D12TextRun> textRuns,
        IFrameResourceResolver resources,
        float clearR,
        float clearG,
        float clearB,
        float clearA)
    {
        RenderFrame(rects, textRuns, [], resources, DisplayScale.Identity, clearR, clearG, clearB, clearA);
    }

    public void RenderFrame(
        ReadOnlySpan<D3D12Renderer2D.RectData> rects,
        ReadOnlySpan<D3D12TextRun> textRuns,
        ReadOnlySpan<D3D12CompositionLayerRenderTargetRequest> layerRenderTargets,
        IFrameResourceResolver resources,
        DisplayScale scale,
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

        var frameResourceIndex = (int)_frameIndex;
        _renderer2D!.RenderRectangles(_list, rects, width, height, frameResourceIndex);

        var hasText = textRuns.Length > 0;
        if (hasText && TextCompositionMode == TextCompositionMode.GlyphAtlas)
        {
            _ = TryRecordGlyphAtlasTextPass(textRuns, resources, frameResourceIndex, width, height);
            if (_deviceRemoved)
            {
                return;
            }
        }

        if (layerRenderTargets.Length > 0)
        {
            CompositeLayerRenderTargets(layerRenderTargets, resources, scale, width, height, frameResourceIndex);
            if (_deviceRemoved)
            {
                return;
            }
        }

        barrier.Anonymous.Transition.StateBefore = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Anonymous.Transition.StateAfter = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT;
        _list->ResourceBarrier(1, &barrier);

        _list->Close();

        var pList = (ID3D12CommandList*)_list;
        _queue->ExecuteCommandLists(1, &pList);
        Interlocked.Increment(ref _frameSerial);

        if (!Present()) return;
        _ = MoveToNextFrame();
    }

    internal D3D12CompositionRenderTargetCacheDiagnostics PrepareCompositionLayerRenderTargets(
        ReadOnlySpan<D3D12CompositionLayerRenderTargetRequest> requests,
        IFrameResourceResolver resources,
        DisplayScale scale)
    {
        if (_deviceRemoved || requests.Length == 0)
        {
            return default;
        }

        var cache = _compositionLayerRenderTargetCache ??= new D3D12CompositionLayerRenderTargetCache(_device);
        var hits = 0;
        var misses = 0;
        var cachedCommands = 0;
        var width = Width;
        var height = Height;
        var frameResourceIndex = (int)_frameIndex;
        try
        {
            for (var i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                var target = cache.GetOrCreate(request.Key, request.Content, width, height, out var hit);
                if (hit)
                {
                    hits++;
                }
                else
                {
                    misses++;
                    RecordLayerRenderTarget(target, request, resources, scale, frameResourceIndex);
                }

                cachedCommands += request.Content.SourceCommandCount;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HandleDeviceError(ex, DeviceErrorSite.LayerRenderTargetRecord);
        }

        return new D3D12CompositionRenderTargetCacheDiagnostics(
            RenderTargetBacked: !_deviceRemoved && requests.Length > 0,
            RenderTargetCacheHits: hits,
            RenderTargetCacheMisses: misses,
            CachedRenderTargetCommands: cachedCommands);
    }

    private D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordResult TryRecordGlyphAtlasTextPass(
        ReadOnlySpan<D3D12TextRun> textRuns,
        IFrameResourceResolver resources,
        int frameResourceIndex,
        int viewportWidth,
        int viewportHeight)
    {
        D3D12GlyphAtlasTextRenderer glyphAtlasTextRenderer;
        try
        {
            glyphAtlasTextRenderer = _glyphAtlasTextRenderer ??= new D3D12GlyphAtlasTextRenderer(_device);
        }
        catch (D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] Glyph atlas initialization failed; degrading unsupported text runs in the D3D12 path: {ex.Message}");
            var degradedRunCount = GlyphAtlasTextCompositionHelpers.CountRenderableRuns(textRuns, resources);
            RecordGlyphAtlasInitializationDegradation(
                ex.Phase == D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase.ShaderCompile
                    ? D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.CompileFailed
                    : D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason.InitializationFailed,
                ex.Phase,
                degradedRunCount);
            return D3D12GlyphAtlasTextRenderer.GlyphAtlasRecordResult.DegradedOnly(degradedRunCount);
        }

        return glyphAtlasTextRenderer.TryRecord(_list, textRuns, resources, viewportWidth, viewportHeight, frameResourceIndex);
    }

    private void RecordLayerRenderTarget(
        D3D12CompositionLayerRenderTarget target,
        in D3D12CompositionLayerRenderTargetRequest request,
        IFrameResourceResolver resources,
        DisplayScale scale,
        int frameResourceIndex)
    {
        TransitionResource(target.Texture, target.State, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
        target.State = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET;

        var rtv = target.Rtv;
        _list->OMSetRenderTargets(1, &rtv, false, null);
        var transparent = stackalloc float[] { 0f, 0f, 0f, 0f };
        _list->ClearRenderTargetView(rtv, new ReadOnlySpan<float>(transparent, 4));

        var viewport = new D3D12_VIEWPORT { Width = target.Width, Height = target.Height, MaxDepth = 1.0f };
        _list->RSSetViewports(1, &viewport);
        var scissor = new RECT { right = target.Width, bottom = target.Height };
        _list->RSSetScissorRects(1, &scissor);

        _layerRenderTargetRects.Reset();
        _layerRenderTargetTexts.Reset();
        D3D12DrawingBackend.AppendLayerSourceRectContent(
            request.Content,
            request.ClipMode,
            new DrawRect(0, 0, target.Width, target.Height),
            scale,
            _layerRenderTargetRects);
        _renderer2D!.RenderRectangles(_list, _layerRenderTargetRects.Span, target.Width, target.Height, frameResourceIndex);

        D3D12DrawingBackend.AppendLayerSourceTextContent(
            request.Content,
            request.ClipMode,
            new DrawRect(0, 0, target.Width, target.Height),
            scale,
            resources,
            _layerRenderTargetTexts);
        if (_layerRenderTargetTexts.Count > 0 && TextCompositionMode == TextCompositionMode.GlyphAtlas)
        {
            _ = TryRecordGlyphAtlasTextPass(_layerRenderTargetTexts.Span, resources, frameResourceIndex, target.Width, target.Height);
        }

        TransitionResource(target.Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
        target.State = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }

    private void CompositeLayerRenderTargets(
        ReadOnlySpan<D3D12CompositionLayerRenderTargetRequest> requests,
        IFrameResourceResolver resources,
        DisplayScale scale,
        int width,
        int height,
        int frameResourceIndex)
    {
        var cache = _compositionLayerRenderTargetCache ??= new D3D12CompositionLayerRenderTargetCache(_device);
        var textureRenderer = _textureQuadRenderer ??= new D3D12TexturedQuadRenderer(_device);
        scale = scale.Normalize();
        try
        {
            for (var i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                var target = cache.GetOrCreate(request.Key, request.Content, width, height, out var hit);
                if (!hit)
                {
                    RecordLayerRenderTarget(target, request, resources, scale, frameResourceIndex);
                }

                SetCurrentBackBufferRenderTarget(width, height);

                if (target.State != D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE)
                {
                    TransitionResource(target.Texture, target.State, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
                    target.State = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                }

                var layer = request.Layer;
                var transform = layer.Transform.Scale(scale);
                var scissor = ResolveLayerCompositeScissor(layer, scale, width, height);
                if (scissor.IsEmpty)
                {
                    continue;
                }

                _ = textureRenderer.RenderQuad(
                    _list,
                    target,
                    transform.TranslateX,
                    transform.TranslateY,
                    target.Width,
                    target.Height,
                    layer.Opacity.Normalized,
                    scissor,
                    width,
                    height,
                    frameResourceIndex);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            HandleDeviceError(ex, DeviceErrorSite.LayerRenderTargetRecord);
        }
    }

    private static IntegerScissorRect ResolveLayerCompositeScissor(
        in CompositionLayer layer,
        DisplayScale scale,
        int width,
        int height)
    {
        if (!layer.HasFixedClip)
        {
            return new IntegerScissorRect(0, 0, width, height);
        }

        var clip = ScaleRect(layer.ClipBounds, scale);
        var effective = DrawingScissor.ResolveEffectiveScissor(new DrawRect(0, 0, width, height), clip);
        return DrawingScissor.ToIntegerScissorRect(effective, width, height);
    }

    private static DrawRect ScaleRect(in DrawRect rect, DisplayScale scale)
    {
        scale = scale.Normalize();
        return scale.IsIdentity
            ? rect
            : new DrawRect(rect.X * scale.ScaleX, rect.Y * scale.ScaleY, rect.Width * scale.ScaleX, rect.Height * scale.ScaleY);
    }

    private void SetCurrentBackBufferRenderTarget(int width, int height)
    {
        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        rtv.ptr += _frameIndex * _rtvSize;
        _list->OMSetRenderTargets(1, &rtv, false, null);
        var viewport = new D3D12_VIEWPORT { Width = width, Height = height, MaxDepth = 1.0f };
        _list->RSSetViewports(1, &viewport);
    }

    private void TransitionResource(
        ID3D12Resource* resource,
        D3D12_RESOURCE_STATES before,
        D3D12_RESOURCE_STATES after)
    {
        if (before == after)
        {
            return;
        }

        var barrier = new D3D12_RESOURCE_BARRIER
        {
            Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION
        };
        barrier.Anonymous.Transition.pResource = resource;
        barrier.Anonymous.Transition.StateBefore = before;
        barrier.Anonymous.Transition.StateAfter = after;
        barrier.Anonymous.Transition.Subresource = 0xFFFFFFFF;
        _list->ResourceBarrier(1, &barrier);
    }

    private void RecordGlyphAtlasInitializationDegradation(
        D3D12GlyphAtlasTextRenderer.GlyphAtlasFallbackReason reason,
        D3D12GlyphAtlasTextRenderer.GlyphAtlasInitializationPhase phase,
        int degradedRunCount)
    {
        _glyphAtlasTextDiagnostics = _glyphAtlasTextDiagnostics
            .WithDegradation(degradedRunCount, reason)
            .WithInitializationFailure(phase);
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
            HandleDeviceError(ex, DeviceErrorSite.Present);
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
            if (!WaitForFence(_fenceValues[_frameIndex], DeviceErrorSite.MoveToNextFrame, countSyncWait: false))
            {
                return false;
            }

            _fenceValues[_frameIndex] = fence + 1;
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, DeviceErrorSite.MoveToNextFrame);
            return false;
        }
    }

    private void HandleDeviceError(Exception? ex, DeviceErrorSite site)
    {
        MarkDeviceRemoved(DeviceErrorDiagnostic.FromException(site, ex));
    }

    private void MarkDeviceRemoved(DeviceErrorDiagnostic reason)
    {
        if (_deviceRemoved) return;
        _deviceRemoved = true;
        _deviceError = reason;
        System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceError}");
        DeviceLost?.Invoke();
    }

    private void InitializeDeviceResources(int width, int height)
    {
        _device = CreateDevice();
        _queue = CreateCommandQueue();
        _swapChain = CreateSwapChain(_hwnd, width, height);
        CreateRenderTargetHeap();
        CreateRenderTargets();
        CreateFrameCommands();
        CreateFenceResources();

        _frameIndex = _swapChain->GetCurrentBackBufferIndex();
        InitializeFrameFenceValues();
        _renderer2D = new D3D12Renderer2D(_device);
        _textureQuadRenderer = new D3D12TexturedQuadRenderer(_device);
        _compositionLayerRenderTargetCache = new D3D12CompositionLayerRenderTargetCache(_device);
    }

    private static ID3D12Device* CreateDevice()
    {
        PInvoke.D3D12CreateDevice(null, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, typeof(ID3D12Device).GUID, out var deviceObj).ThrowOnFailure();
        return (ID3D12Device*)RequirePointer(deviceObj, "D3D12Renderer.D3D12CreateDevice returned a null device.");
    }

    private ID3D12CommandQueue* CreateCommandQueue()
    {
        var qd = new D3D12_COMMAND_QUEUE_DESC { Type = D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT };
        _device->CreateCommandQueue(qd, typeof(ID3D12CommandQueue).GUID, out var queueObj);
        return (ID3D12CommandQueue*)RequirePointer(queueObj, "D3D12Renderer.CreateCommandQueue returned a null queue.");
    }

    private IDXGISwapChain3* CreateSwapChain(nint hwnd, int width, int height)
    {
        IDXGIFactory4* factory = null;
        IDXGISwapChain1* sc1 = null;
        try
        {
            PInvoke.CreateDXGIFactory1(typeof(IDXGIFactory4).GUID, out var factoryObj).ThrowOnFailure();
            factory = (IDXGIFactory4*)RequirePointer(factoryObj, "D3D12Renderer.CreateDXGIFactory1 returned a null factory.");

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

            factory->CreateSwapChainForHwnd((global::Windows.Win32.System.Com.IUnknown*)_queue, new HWND(hwnd), &sd, null, null, &sc1);
            RequirePointer(sc1, "D3D12Renderer.CreateSwapChainForHwnd returned a null swap chain.");

            sc1->QueryInterface(typeof(IDXGISwapChain3).GUID, out var sc3Obj).ThrowOnFailure();
            return (IDXGISwapChain3*)RequirePointer(sc3Obj, "D3D12Renderer.QueryInterface(IDXGISwapChain3) returned a null swap chain.");
        }
        finally
        {
            if (sc1 != null) sc1->Release();
            if (factory != null) factory->Release();
        }
    }

    private void CreateRenderTargetHeap()
    {
        var hd = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            NumDescriptors = FrameCount,
            Type = D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV
        };
        _device->CreateDescriptorHeap(hd, typeof(ID3D12DescriptorHeap).GUID, out var heapObj);
        _rtvHeap = (ID3D12DescriptorHeap*)RequirePointer(heapObj, "D3D12Renderer.CreateDescriptorHeap(RTV) returned a null heap.");
        _rtvSize = _device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    private void CreateRenderTargets()
    {
        var rtv = _rtvHeap->GetCPUDescriptorHandleForHeapStart();
        for (var i = 0; i < FrameCount; i++)
        {
            _swapChain->GetBuffer((uint)i, typeof(ID3D12Resource).GUID, out var resObj);
            _renderTargets[i] = (ID3D12Resource*)RequirePointer(resObj, "D3D12Renderer.GetBuffer returned a null render target.");
            _device->CreateRenderTargetView(_renderTargets[i], null, rtv);
            rtv.ptr += _rtvSize;
        }
    }

    private void CreateFrameCommands()
    {
        for (var i = 0; i < FrameCount; i++)
        {
            _device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT, typeof(ID3D12CommandAllocator).GUID, out var allocObj);
            _allocators[i] = (ID3D12CommandAllocator*)RequirePointer(allocObj, "D3D12Renderer.CreateCommandAllocator returned a null allocator.");
        }

        _device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE.D3D12_COMMAND_LIST_TYPE_DIRECT, _allocators[0], null, typeof(ID3D12GraphicsCommandList).GUID, out var listObj);
        _list = (ID3D12GraphicsCommandList*)RequirePointer(listObj, "D3D12Renderer.CreateCommandList returned a null command list.");
        _list->Close();
    }

    private void CreateFenceResources()
    {
        _device->CreateFence(0, D3D12_FENCE_FLAGS.D3D12_FENCE_FLAG_NONE, typeof(ID3D12Fence).GUID, out var fenceObj);
        _fence = (ID3D12Fence*)RequirePointer(fenceObj, "D3D12Renderer.CreateFence returned a null fence.");
        _fenceValues = new ulong[FrameCount];
        _fenceEventOwner = PInvoke.CreateEvent(null, false, false, null);
        if (_fenceEventOwner.IsInvalid)
        {
            throw new InvalidOperationException("D3D12Renderer.CreateEvent returned an invalid fence event.");
        }

        _fenceEvent = new HANDLE(_fenceEventOwner.DangerousGetHandle());
    }

    private void InitializeFrameFenceValues()
    {
        for (var i = 0; i < FrameCount; i++) _fenceValues[i] = 0;
        _fenceValues[_frameIndex] = 1;
    }

    private static void* RequirePointer(void* pointer, string message)
    {
        if (pointer == null)
        {
            throw new InvalidOperationException(message);
        }

        return pointer;
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
    /// Check HRESULT from D3D/DXGI calls that return it directly.
    /// Sets _deviceRemoved on any failure.
    /// </summary>
    internal bool SucceededOrMarkDeviceRemoved(HRESULT hr, DeviceErrorSite site)
    {
        if (hr.Succeeded) return true;
        if (!_deviceRemoved)
        {
            _deviceRemoved = true;
            _deviceError = DeviceErrorDiagnostic.FromHResult(site, hr.Value);
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceError}");
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
            if (!WaitForFence(fence, DeviceErrorSite.WaitForGpu, countSyncWait: true))
            {
                return false;
            }

            for (var i = 0; i < FrameCount; i++) _fenceValues[i] = fence + 1;
            return true;
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, DeviceErrorSite.WaitForGpu);
            return false;
        }
    }

    private bool WaitForFence(ulong fenceValue, DeviceErrorSite site, bool countSyncWait)
    {
        if (fenceValue == 0 || _fence->GetCompletedValue() >= fenceValue)
        {
            return true;
        }

        var start = countSyncWait ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        try
        {
            _fence->SetEventOnCompletion(fenceValue, _fenceEvent);
            PInvoke.WaitForSingleObject(_fenceEvent, InfiniteWaitMilliseconds);
        }
        catch (COMException ex)
        {
            HandleDeviceError(ex, site);
            return false;
        }

        if (countSyncWait)
        {
            Interlocked.Increment(ref _syncWaitCount);
            Interlocked.Add(ref _syncWaitTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
        }

        return true;
    }

    private void ReleaseDeviceResources(bool waitForGpu)
    {
        if (waitForGpu && _queue != null && _fence != null && _fenceEventOwner is { IsInvalid: false })
        {
            _ = WaitForGpu();
        }

        _glyphAtlasTextRenderer?.Dispose();
        _glyphAtlasTextRenderer = null;
        _compositionLayerRenderTargetCache?.Dispose();
        _compositionLayerRenderTargetCache = null;
        _textureQuadRenderer?.Dispose();
        _textureQuadRenderer = null;
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

        _rtvSize = 0;
        _frameIndex = 0;
        for (var i = 0; i < FrameCount; i++) _fenceValues[i] = 0;
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
            ReleaseDeviceResources(waitForGpu: false);
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
            InitializeDeviceResources(width, height);

            // Reset device-lost state
            _deviceRemoved = false;
            _deviceError = DeviceErrorDiagnostic.None;
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
            ReleaseDeviceResources(waitForGpu: false);
            _deviceRemoved = true;
            _deviceError = DeviceErrorDiagnostic.FromException(DeviceErrorSite.RecoveryReinitialize, ex);
            System.Diagnostics.Debug.WriteLine($"[D3D12Renderer] {_deviceError}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseDeviceResources(waitForGpu: true);
        _layerRenderTargetTexts.Dispose();
        _layerRenderTargetRects.Dispose();
        _disposed = true;
    }
}
