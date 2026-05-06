using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Direct3D11on12;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.DirectWrite;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.Com;

namespace Irix.Platform.Windows;

internal sealed unsafe class D3D12TextRenderer : IDisposable
{
    private readonly ID3D11Device* _d3d11Device;
    private readonly ID3D11DeviceContext* _d3d11Context;
    private readonly ID3D11On12Device* _d3d11On12Device;
    private readonly ID2D1Factory3* _d2dFactory;
    private readonly ID2D1Device2* _d2dDevice;
    private readonly ID2D1DeviceContext2* _d2dContext;
    private readonly IDWriteFactory* _dwriteFactory;
    private readonly IDWriteTextFormat* _textFormat;
    private ID2D1SolidColorBrush* _textBrush;
    private ID3D11Resource*[] _wrappedBackBuffers = [];
    private ID2D1Bitmap1*[] _renderTargets = [];
    private bool _disposed;

    public D3D12TextRenderer(ID3D12Device* d3d12Device, ID3D12CommandQueue* commandQueue, ID3D12Resource*[] backBuffers)
    {
        var queues = stackalloc IUnknown*[1];
        queues[0] = (IUnknown*)commandQueue;

        var featureLevels = stackalloc D3D_FEATURE_LEVEL[1];
        featureLevels[0] = D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0;

        ID3D11Device* d3d11Device;
        ID3D11DeviceContext* d3d11Context;
        var create11Result = PInvoke.D3D11On12CreateDevice(
            (IUnknown*)d3d12Device,
            (uint)D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            featureLevels,
            1,
            queues,
            1,
            0,
            &d3d11Device,
            &d3d11Context,
            null);
        create11Result.ThrowOnFailure();

        _d3d11Device = d3d11Device;
        _d3d11Context = d3d11Context;

        _d3d11Device->QueryInterface(typeof(ID3D11On12Device).GUID, out var on12DeviceObject).ThrowOnFailure();
        _d3d11On12Device = (ID3D11On12Device*)on12DeviceObject;

        PInvoke.D2D1CreateFactory(
            D2D1_FACTORY_TYPE.D2D1_FACTORY_TYPE_SINGLE_THREADED,
            typeof(ID2D1Factory3).GUID,
            null,
            out var d2dFactoryObject).ThrowOnFailure();
        _d2dFactory = (ID2D1Factory3*)d2dFactoryObject;

        _d3d11On12Device->QueryInterface(typeof(IDXGIDevice).GUID, out var dxgiDeviceObject).ThrowOnFailure();
        var dxgiDevice = (IDXGIDevice*)dxgiDeviceObject;
        ID2D1Device2* d2dDevice;
        _d2dFactory->CreateDevice(dxgiDevice, &d2dDevice);
        _d2dDevice = d2dDevice;
        dxgiDevice->Release();

        ID2D1DeviceContext* d2dContextBase;
        _d2dDevice->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS.D2D1_DEVICE_CONTEXT_OPTIONS_NONE, &d2dContextBase);
        d2dContextBase->QueryInterface(typeof(ID2D1DeviceContext2).GUID, out var d2dContextObject).ThrowOnFailure();
        _d2dContext = (ID2D1DeviceContext2*)d2dContextObject;
        d2dContextBase->Release();

        PInvoke.DWriteCreateFactory(
            DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED,
            typeof(IDWriteFactory).GUID,
            out var dwriteFactoryObject).ThrowOnFailure();
        _dwriteFactory = (IDWriteFactory*)dwriteFactoryObject;

        IDWriteTextFormat* textFormat;
        _dwriteFactory->CreateTextFormat(
            "Segoe UI",
            null,
            DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL,
            DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            16.0f,
            "",
            &textFormat);
        _textFormat = textFormat;
        _textFormat->SetTextAlignment(DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING);
        _textFormat->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        _textFormat->SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP);

        var initialBrushColor = new D2D1_COLOR_F { r = 1, g = 1, b = 1, a = 1 };
        ID2D1SolidColorBrush* textBrush;
        _d2dContext->CreateSolidColorBrush(&initialBrushColor, null, &textBrush);
        _textBrush = textBrush;

        RecreateFrameResources(backBuffers);
    }

    public void RecreateFrameResources(ID3D12Resource*[] backBuffers)
    {
        ReleaseFrameResources();

        _wrappedBackBuffers = new ID3D11Resource*[backBuffers.Length];
        _renderTargets = new ID2D1Bitmap1*[backBuffers.Length];

        var bitmapProperties = new D2D1_BITMAP_PROPERTIES1
        {
            pixelFormat = new D2D1_PIXEL_FORMAT
            {
                format = DXGI_FORMAT.DXGI_FORMAT_UNKNOWN,
                alphaMode = D2D1_ALPHA_MODE.D2D1_ALPHA_MODE_PREMULTIPLIED
            },
            dpiX = 96,
            dpiY = 96,
            bitmapOptions = D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_TARGET | D2D1_BITMAP_OPTIONS.D2D1_BITMAP_OPTIONS_CANNOT_DRAW
        };

        var resourceFlags = new D3D11_RESOURCE_FLAGS
        {
            BindFlags = (uint)D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET
        };

        for (var index = 0; index < backBuffers.Length; index++)
        {
            _d3d11On12Device->CreateWrappedResource(
                (IUnknown*)backBuffers[index],
                resourceFlags,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT,
                typeof(ID3D11Resource).GUID,
                out var wrappedResourceObject);
            var wrappedResource = (ID3D11Resource*)wrappedResourceObject;
            _wrappedBackBuffers[index] = wrappedResource;

            wrappedResource->QueryInterface(typeof(IDXGISurface).GUID, out var surfaceObject).ThrowOnFailure();
            var surface = (IDXGISurface*)surfaceObject;
            ID2D1Bitmap1* renderTarget;
            _d2dContext->CreateBitmapFromDxgiSurface(surface, &bitmapProperties, &renderTarget);
            _renderTargets[index] = renderTarget;
            surface->Release();
        }
    }

    public void ReleaseFrameResourcesForResize()
    {
        ReleaseFrameResources();
    }

    public void Render(uint frameIndex, ReadOnlySpan<TextData> textRuns, ITextResolver textResolver)
    {
        if (textRuns.Length == 0 || frameIndex >= _wrappedBackBuffers.Length)
        {
            return;
        }

        var wrappedResource = _wrappedBackBuffers[frameIndex];
        var wrappedResourceList = &wrappedResource;
        _d3d11On12Device->AcquireWrappedResources(wrappedResourceList, 1);

        var endDrawResult = default(HRESULT);
        try
        {
            _d2dContext->SetTarget((ID2D1Image*)_renderTargets[frameIndex]);
            _d2dContext->BeginDraw();
            var identity = CreateIdentityTransform();
            _d2dContext->SetTransform(&identity);

            foreach (var textRun in textRuns)
            {
                var text = textResolver.Resolve(textRun.Text);
                if (text.IsEmpty)
                {
                    continue;
                }

                var color = new D2D1_COLOR_F { r = textRun.R, g = textRun.G, b = textRun.B, a = textRun.A };
                _textBrush->SetColor(&color);

                var layoutRect = new D2D_RECT_F
                {
                    left = textRun.X,
                    top = textRun.Y,
                    right = textRun.X + textRun.Width,
                    bottom = textRun.Y + textRun.Height
                };

                fixed (char* textPointer = text)
                {
                    _d2dContext->DrawText(
                        (PCWSTR)textPointer,
                        (uint)text.Length,
                        _textFormat,
                        &layoutRect,
                        (ID2D1Brush*)_textBrush,
                        D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP,
                        DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
                }
            }

            endDrawResult = _d2dContext->EndDraw(null, null);
        }
        finally
        {
            _d3d11On12Device->ReleaseWrappedResources(wrappedResourceList, 1);
            _d3d11Context->Flush();
        }

        endDrawResult.ThrowOnFailure();
    }

    private void ReleaseFrameResources()
    {
        foreach (var renderTarget in _renderTargets)
        {
            if (renderTarget != null)
            {
                renderTarget->Release();
            }
        }

        foreach (var wrappedBackBuffer in _wrappedBackBuffers)
        {
            if (wrappedBackBuffer != null)
            {
                wrappedBackBuffer->Release();
            }
        }

        _renderTargets = [];
        _wrappedBackBuffers = [];
    }

    private static D2D_MATRIX_3X2_F CreateIdentityTransform()
    {
        var matrix = new D2D_MATRIX_3X2_F();
        matrix.Anonymous.Anonymous1.m11 = 1;
        matrix.Anonymous.Anonymous1.m22 = 1;
        return matrix;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseFrameResources();
        if (_textBrush != null) _textBrush->Release();
        if (_textFormat != null) _textFormat->Release();
        if (_dwriteFactory != null) _dwriteFactory->Release();
        if (_d2dContext != null) _d2dContext->Release();
        if (_d2dDevice != null) _d2dDevice->Release();
        if (_d2dFactory != null) _d2dFactory->Release();
        if (_d3d11On12Device != null) _d3d11On12Device->Release();
        if (_d3d11Context != null) _d3d11Context->Release();
        if (_d3d11Device != null) _d3d11Device->Release();
        _disposed = true;
    }

    public readonly record struct TextData(
        float X,
        float Y,
        float Width,
        float Height,
        float R,
        float G,
        float B,
        float A,
        TextSlice Text);
}
