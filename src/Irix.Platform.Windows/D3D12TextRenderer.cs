using System.Runtime.InteropServices;
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
    private const int MaxCachedTextFormats = 64;
    private const int MaxCachedTextLayouts = 256;

    private readonly ID3D11Device* _d3d11Device;
    private readonly ID3D11DeviceContext* _d3d11Context;
    private readonly ID3D11On12Device* _d3d11On12Device;
    private readonly ID2D1Factory3* _d2dFactory;
    private readonly ID2D1Device2* _d2dDevice;
    private readonly ID2D1DeviceContext2* _d2dContext;
    private readonly IDWriteFactory* _dwriteFactory;
    private readonly Dictionary<TextStyle, CachedTextFormat> _textFormats = [];
    private readonly Queue<CachedTextFormat> _textFormatOrder = [];
    private readonly Dictionary<TextLayoutCacheKey, List<CachedTextLayout>> _textLayouts = [];
    private readonly Queue<CachedTextLayout> _textLayoutOrder = [];
    private readonly ID2D1SolidColorBrush* _textBrush;
    private readonly ID3D11Query* _overlayCompletionQuery;
    private ID3D11Resource*[] _wrappedBackBuffers = [];
    private ID2D1Bitmap1*[] _renderTargets = [];
    private bool _disposed;

    // Diagnostic counters
    private int _diagnosticFormatHits;
    private int _diagnosticFormatMisses;
    private int _diagnosticLayoutHits;
    private int _diagnosticLayoutMisses;
    private int _diagnosticFormatEvictions;
    private int _diagnosticLayoutEvictions;
    private bool _deviceRemoved;
    private string? _deviceErrorReason;

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

        var initialBrushColor = new D2D1_COLOR_F { r = 1, g = 1, b = 1, a = 1 };
        ID2D1SolidColorBrush* textBrush;
        _d2dContext->CreateSolidColorBrush(&initialBrushColor, null, &textBrush);
        _textBrush = textBrush;

        var queryDesc = new D3D11_QUERY_DESC { Query = D3D11_QUERY.D3D11_QUERY_EVENT };
        ID3D11Query* overlayCompletionQuery;
        _d3d11Device->CreateQuery(&queryDesc, &overlayCompletionQuery);
        _overlayCompletionQuery = overlayCompletionQuery;

        RecreateFrameResources(backBuffers);
    }

    public bool IsDeviceRemoved => _deviceRemoved;
    public string? DeviceErrorReason => _deviceErrorReason;

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

    public bool Render(uint frameIndex, ReadOnlySpan<TextData> textRuns, IFrameResourceResolver resources)
    {
        if (textRuns.Length == 0 || frameIndex >= _wrappedBackBuffers.Length)
        {
            return true;
        }
        if (_deviceRemoved)
        {
            return false;
        }

        try
        {
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
                    var runResolver = textRun.Resolver ?? resources;
                    var text = runResolver.Resolve(textRun.Text);
                    if (text.IsEmpty || textRun.Width <= 0 || textRun.Height <= 0)
                    {
                        continue;
                    }

                    var color = new D2D1_COLOR_F { r = textRun.R, g = textRun.G, b = textRun.B, a = textRun.A };
                    _textBrush->SetColor(&color);

                    var style = (textRun.ResolvedStyle != default ? textRun.ResolvedStyle : runResolver.ResolveTextStyle(textRun.Style)).Normalize();
                    var layout = GetTextLayout(text, style, textRun.Width, textRun.Height);
                    var origin = new D2D_POINT_2F
                    {
                        x = textRun.X,
                        y = textRun.Y
                    };

                    var clipPushed = textRun.ClipEnabled && !textRun.EffectiveClip.IsEmpty;
                    if (clipPushed)
                    {
                        var clipRect = ToD2DClipRect(textRun.EffectiveClip);
                        _d2dContext->PushAxisAlignedClip(&clipRect, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
                    }

                    try
                    {
                        _d2dContext->DrawTextLayout(
                            origin,
                            layout,
                            (ID2D1Brush*)_textBrush,
                            D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP);
                    }
                    finally
                    {
                        if (clipPushed)
                        {
                            _d2dContext->PopAxisAlignedClip();
                        }
                    }
                }

                endDrawResult = _d2dContext->EndDraw(null, null);
            }
            finally
            {
                _d3d11On12Device->ReleaseWrappedResources(wrappedResourceList, 1);
                _d3d11Context->Flush();
            }

            if (!SucceededOrMarkDeviceRemoved(endDrawResult, "EndDraw"))
            {
                return false;
            }
        }
        catch (COMException ex)
        {
            MarkDeviceRemoved($"TextRenderer COMException: 0x{ex.ErrorCode:X8}");
            return false;
        }

        return true;
    }

    public bool WaitForOverlayCompletionQuery()
    {
        if (_deviceRemoved)
        {
            return false;
        }

        if (_overlayCompletionQuery == null)
        {
            return true;
        }

        try
        {
            var async = (ID3D11Asynchronous*)_overlayCompletionQuery;
            _d3d11Context->End(async);
            _d3d11Context->Flush();

            while (true)
            {
                var result = _d3d11Context->GetDataResult(async, null, 0, 0);
                if (result.Value == 0)
                {
                    return true;
                }

                if (result.Value < 0)
                {
                    return SucceededOrMarkDeviceRemoved(result, "D3D11 overlay completion query");
                }

                Thread.Yield();
            }
        }
        catch (COMException ex)
        {
            MarkDeviceRemoved($"D3D11 overlay completion query COMException: 0x{ex.ErrorCode:X8}");
            return false;
        }
    }

    private bool SucceededOrMarkDeviceRemoved(HRESULT hr, string context)
    {
        if (hr.Succeeded) return true;
        _deviceRemoved = true;
        _deviceErrorReason = $"{context}: HRESULT 0x{unchecked((uint)hr.Value):X8}";
        System.Diagnostics.Debug.WriteLine($"[D3D12TextRenderer] {_deviceErrorReason}");
        return false;
    }

    private void MarkDeviceRemoved(string reason)
    {
        _deviceRemoved = true;
        _deviceErrorReason = reason;
        System.Diagnostics.Debug.WriteLine($"[D3D12TextRenderer] {reason}");
    }

    private static D2D_RECT_F ToD2DClipRect(EffectiveScissor effectiveClip)
    {
        var bounds = effectiveClip.Bounds;
        return new D2D_RECT_F
        {
            left = bounds.X,
            top = bounds.Y,
            right = bounds.X + bounds.Width,
            bottom = bounds.Y + bounds.Height
        };
    }

    private IDWriteTextLayout* GetTextLayout(ReadOnlySpan<char> text, TextStyle style, float width, float height)
    {
        var key = new TextLayoutCacheKey(ComputeTextHash(text), text.Length, style, width, height);
        if (_textLayouts.TryGetValue(key, out var entries))
        {
            foreach (var entry in entries)
            {
                if (text.SequenceEqual(entry.Text.AsSpan()))
                {
                    _diagnosticLayoutHits++;
                    return (IDWriteTextLayout*)entry.Layout;
                }
            }
        }

        _diagnosticLayoutMisses++;

        var layout = CreateTextLayout(text, style, width, height);
        var cached = new CachedTextLayout(key, text.ToString(), (nint)layout);
        if (entries == null)
        {
            entries = [];
            _textLayouts.Add(key, entries);
        }

        entries.Add(cached);
        _textLayoutOrder.Enqueue(cached);

        if (_textLayoutOrder.Count > MaxCachedTextLayouts)
        {
            EvictOldestTextLayout();
            _diagnosticLayoutEvictions++;
        }

        return layout;
    }

    private IDWriteTextLayout* CreateTextLayout(ReadOnlySpan<char> text, TextStyle style, float width, float height)
    {
        var textFormat = GetTextFormat(style);
        IDWriteTextLayout* textLayout;
        fixed (char* textPointer = text)
        {
            _dwriteFactory->CreateTextLayout(
                (PCWSTR)textPointer,
                (uint)text.Length,
                textFormat,
                width,
                height,
                &textLayout);
        }

        return textLayout;
    }

    private IDWriteTextFormat* GetTextFormat(TextStyle style)
    {
        style = style.Normalize();
        if (_textFormats.TryGetValue(style, out var cachedTextFormat))
        {
            _diagnosticFormatHits++;
            return (IDWriteTextFormat*)cachedTextFormat.Format;
        }

        _diagnosticFormatMisses++;

        IDWriteTextFormat* createdFormat;
        _dwriteFactory->CreateTextFormat(
            style.FontFamily,
            null,
            ToDirectWriteFontWeight(style.FontWeight),
            ToDirectWriteFontStyle(style.FontStyle),
            ToDirectWriteFontStretch(style.FontStretch),
            style.FontSize,
            string.Empty,
            &createdFormat);
        createdFormat->SetTextAlignment(ToDirectWriteTextAlignment(style.HorizontalAlignment));
        createdFormat->SetParagraphAlignment(ToDirectWriteParagraphAlignment(style.VerticalAlignment));
        createdFormat->SetWordWrapping(ToDirectWriteWordWrapping(style.Wrapping));

        cachedTextFormat = new CachedTextFormat(style, (nint)createdFormat);
        _textFormats.Add(style, cachedTextFormat);
        _textFormatOrder.Enqueue(cachedTextFormat);

        if (_textFormatOrder.Count > MaxCachedTextFormats)
        {
            EvictOldestTextFormat();
            _diagnosticFormatEvictions++;
        }

        return createdFormat;
    }

    private void EvictOldestTextFormat()
    {
        var entry = _textFormatOrder.Dequeue();
        if (_textFormats.TryGetValue(entry.Style, out var currentEntry)
            && ReferenceEquals(entry, currentEntry))
        {
            _textFormats.Remove(entry.Style);
        }

        ((IDWriteTextFormat*)entry.Format)->Release();
    }

    private void EvictOldestTextLayout()
    {
        var entry = _textLayoutOrder.Dequeue();
        if (_textLayouts.TryGetValue(entry.Key, out var entries))
        {
            entries.Remove(entry);
            if (entries.Count == 0)
            {
                _textLayouts.Remove(entry.Key);
            }
        }

        ((IDWriteTextLayout*)entry.Layout)->Release();
    }

    private void ReleaseTextResources()
    {
        while (_textLayoutOrder.Count > 0)
        {
            EvictOldestTextLayout();
        }

        while (_textFormatOrder.Count > 0)
        {
            EvictOldestTextFormat();
        }

        _textFormats.Clear();
    }

    private static ulong ComputeTextHash(ReadOnlySpan<char> text)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static DWRITE_FONT_WEIGHT ToDirectWriteFontWeight(TextFontWeight fontWeight)
    {
        return fontWeight switch
        {
            TextFontWeight.Bold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_BOLD,
            TextFontWeight.SemiBold => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD,
            _ => DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL
        };
    }

    private static DWRITE_FONT_STYLE ToDirectWriteFontStyle(TextFontStyle fontStyle)
    {
        return fontStyle switch
        {
            TextFontStyle.Italic => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC,
            TextFontStyle.Oblique => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_OBLIQUE,
            _ => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL
        };
    }

    private static DWRITE_FONT_STRETCH ToDirectWriteFontStretch(TextFontStretch fontStretch)
    {
        return DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL;
    }

    private static DWRITE_TEXT_ALIGNMENT ToDirectWriteTextAlignment(TextHorizontalAlignment alignment)
    {
        return alignment switch
        {
            TextHorizontalAlignment.Center => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            TextHorizontalAlignment.Trailing => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING
        };
    }

    private static DWRITE_PARAGRAPH_ALIGNMENT ToDirectWriteParagraphAlignment(TextVerticalAlignment alignment)
    {
        return alignment switch
        {
            TextVerticalAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_FAR,
            TextVerticalAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER
        };
    }

    private static DWRITE_WORD_WRAPPING ToDirectWriteWordWrapping(TextWrapping wrapping)
    {
        return wrapping switch
        {
            TextWrapping.Wrap => DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_WRAP,
            _ => DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP
        };
    }

    private void ReleaseFrameResources()
    {
        // Clear D2D context target before releasing render targets
        if (_d2dContext != null)
        {
            _d2dContext->SetTarget(null);
        }

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

        // Flush D3D11 to ensure all references are released
        if (_d3d11Context != null)
        {
            _d3d11Context->Flush();
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
        if (_overlayCompletionQuery != null) _overlayCompletionQuery->Release();
        ReleaseTextResources();
        if (_dwriteFactory != null) _dwriteFactory->Release();
        if (_d2dContext != null) _d2dContext->Release();
        if (_d2dDevice != null) _d2dDevice->Release();
        if (_d2dFactory != null) _d2dFactory->Release();
        if (_d3d11On12Device != null) _d3d11On12Device->Release();
        if (_d3d11Context != null) _d3d11Context->Release();
        if (_d3d11Device != null) _d3d11Device->Release();
        _disposed = true;
    }

    public TextRendererDiagnostics GetDiagnostics()
    {
        return new TextRendererDiagnostics(
            _diagnosticFormatHits,
            _diagnosticFormatMisses,
            _diagnosticLayoutHits,
            _diagnosticLayoutMisses,
            _diagnosticFormatEvictions,
            _diagnosticLayoutEvictions,
            _textFormats.Count,
            _textLayoutOrder.Count);
    }

    public void ResetDiagnostics()
    {
        _diagnosticFormatHits = 0;
        _diagnosticFormatMisses = 0;
        _diagnosticLayoutHits = 0;
        _diagnosticLayoutMisses = 0;
        _diagnosticFormatEvictions = 0;
        _diagnosticLayoutEvictions = 0;
    }

    public readonly record struct TextRendererDiagnostics(
        int FormatHits,
        int FormatMisses,
        int LayoutHits,
        int LayoutMisses,
        int FormatEvictions,
        int LayoutEvictions,
        int CachedFormats,
        int CachedLayouts);

    public readonly record struct TextData(
        float X,
        float Y,
        float Width,
        float Height,
        float R,
        float G,
        float B,
        float A,
        TextSlice Text,
        ResourceHandle Style,
        EffectiveScissor EffectiveClip,
        bool ClipEnabled,
        TextStyle ResolvedStyle = default,
        IFrameResourceResolver? Resolver = null);

    private sealed class CachedTextFormat(TextStyle style, nint format)
    {
        public TextStyle Style { get; } = style;

        public nint Format { get; } = format;
    }

    private readonly record struct TextLayoutCacheKey(
        ulong TextHash,
        int TextLength,
        TextStyle Style,
        float Width,
        float Height);

    private sealed class CachedTextLayout(TextLayoutCacheKey key, string text, nint layout)
    {
        public TextLayoutCacheKey Key { get; } = key;

        public string Text { get; } = text;

        public nint Layout { get; } = layout;
    }
}
