using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Irix.Platform.Windows;

internal sealed class WindowsNativeWindow : INativeWindow
{
    private const int ContentPadding = 16;
    private readonly HINSTANCE _instanceHandle;
    private readonly Action? _closedSink;
    private readonly Action? _displayChangedSink;
    private readonly Action<DisplayScale>? _dpiChangedSink;
    private readonly GCHandle _gcHandle;
    private readonly Action<RawInputEvent>? _rawInputEventSink;
    private readonly nint _titlePointer;
    private readonly HWND _windowHandle;
    private readonly int _ownerThreadId;
    private readonly FrameTextArena _contentTextArena = new();

    private WindowContentElement[] _contentElements = [];
    private bool _isDisposed;

    public WindowsNativeWindow(
        string title,
        ScreenRegion region,
        Action<RawInputEvent>? rawInputEventSink = null,
        Action? closedSink = null,
        Action? displayChangedSink = null,
        Action<DisplayScale>? dpiChangedSink = null)
    {
        Title = title;
        Region = region;
        _closedSink = closedSink;
        _displayChangedSink = displayChangedSink;
        _dpiChangedSink = dpiChangedSink;
        _rawInputEventSink = rawInputEventSink;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        var moduleHandle = PInvoke.GetModuleHandle((PCWSTR)null);
        unsafe
        {
            _instanceHandle = new HINSTANCE((nint)moduleHandle.Value);
        }
        _titlePointer = Marshal.StringToHGlobalUni(Title);
        var windowClassRegistered = false;

        try
        {
            RegisterWindowClass();
            windowClassRegistered = true;

            unsafe
            {
                fixed (char* classNameValue = WindowsWindowClassRegistry.ClassName)
                {

                    _windowHandle = PInvoke.CreateWindowEx(
                        default,
                        classNameValue,
                        new PCWSTR((char*)_titlePointer),
                        WINDOW_STYLE.WS_OVERLAPPEDWINDOW,
                        Region.PhysicalBounds.X,
                        Region.PhysicalBounds.Y,
                        Region.PhysicalBounds.Width,
                        Region.PhysicalBounds.Height,
                        default,
                        default,
                        _instanceHandle,
                        null);
                }
            }

            if (_windowHandle == HWND.Null)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the Win32 window.");
            }

            _gcHandle = GCHandle.Alloc(this);
            _ = PInvoke.SetWindowLongPtr(_windowHandle, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, GCHandle.ToIntPtr(_gcHandle));
        }
        catch
        {
            if (windowClassRegistered)
            {
                UnregisterWindowClass();
            }

            Marshal.FreeHGlobal(_titlePointer);
            throw;
        }
    }

    public string Title { get; }

    public ScreenRegion Region { get; set; }

    public bool ExternalRenderingEnabled { get; set; }

    public unsafe nint Handle => (nint)_windowHandle.Value;

    public void SetContentElements(IReadOnlyList<WindowContentElement> elements, ITextResolver textResolver)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        EnsureAccess();
        _contentTextArena.Reset();
        var copied = new WindowContentElement[elements.Count];
        for (var index = 0; index < elements.Count; index++)
        {
            copied[index] = CopyElementText(elements[index], textResolver);
        }

        _contentTextArena.Seal();
        _contentElements = copied;
        PInvoke.InvalidateRect(_windowHandle, null, true);
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        EnsureAccess();
        PInvoke.ShowWindow(_windowHandle, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
        PInvoke.UpdateWindow(_windowHandle);
    }

    public void RunMessageLoop()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        EnsureAccess();

        while (PInvoke.GetMessage(out var message, default, 0, 0).Value > 0)
        {
            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    public event Action<int, int>? SizeChanged;

    public event Action<DisplayScale>? DpiChanged;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        EnsureAccess();

        _ = PInvoke.SetWindowLongPtr(_windowHandle, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, IntPtr.Zero);

        if (PInvoke.IsWindow(_windowHandle))
        {
            PInvoke.DestroyWindow(_windowHandle);
        }

        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }

        UnregisterWindowClass();
        Marshal.FreeHGlobal(_titlePointer);
        _contentTextArena.Dispose();
        _isDisposed = true;
    }


    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HandleWindowMessage(HWND windowHandle, uint message, WPARAM wParam, LPARAM lParam)
    {
        var window = GetWindow(windowHandle);
        if (window is not null)
        {
            return window.HandleWindowMessageCore(windowHandle, message, wParam, lParam);
        }

        if (message == WindowMessages.Destroy)
        {
            return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(windowHandle, message, wParam, lParam);
    }

    private LRESULT HandleWindowMessageCore(HWND windowHandle, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case WindowMessages.Paint:
                return HandlePaint(windowHandle);
            case WindowMessages.Size:
                HandleSizeChanged(windowHandle);
                return new LRESULT(0);
            case WindowMessages.MouseMove:
                PublishPointerEvent(RawInputEventKind.PointerMoved, lParam);
                return new LRESULT(0);
            case WindowMessages.LeftButtonDown:
                PublishPointerEvent(RawInputEventKind.PointerPressed, lParam, PointerButton.Left);
                return new LRESULT(0);
            case WindowMessages.LeftButtonUp:
                PublishPointerEvent(RawInputEventKind.PointerReleased, lParam, PointerButton.Left);
                return new LRESULT(0);
            case WindowMessages.RightButtonDown:
                PublishPointerEvent(RawInputEventKind.PointerPressed, lParam, PointerButton.Right);
                return new LRESULT(0);
            case WindowMessages.RightButtonUp:
                PublishPointerEvent(RawInputEventKind.PointerReleased, lParam, PointerButton.Right);
                return new LRESULT(0);
            case WindowMessages.MiddleButtonDown:
                PublishPointerEvent(RawInputEventKind.PointerPressed, lParam, PointerButton.Middle);
                return new LRESULT(0);
            case WindowMessages.MiddleButtonUp:
                PublishPointerEvent(RawInputEventKind.PointerReleased, lParam, PointerButton.Middle);
                return new LRESULT(0);
            case WindowMessages.KeyDown:
                PublishKeyEvent(RawInputEventKind.KeyPressed, wParam);
                return new LRESULT(0);
            case WindowMessages.KeyUp:
                PublishKeyEvent(RawInputEventKind.KeyReleased, wParam);
                return new LRESULT(0);
            case WindowMessages.Character:
                PublishCharacterEvent(wParam);
                return new LRESULT(0);
            case WindowMessages.MouseWheel:
                PublishWheelEvent(wParam, lParam);
                return new LRESULT(0);
            case WindowMessages.FocusGained:
                PublishFocusEvent(RawInputEventKind.FocusGained);
                return new LRESULT(0);
            case WindowMessages.FocusLost:
                PublishFocusEvent(RawInputEventKind.FocusLost);
                return new LRESULT(0);
            case WindowMessages.DisplayChange:
                _displayChangedSink?.Invoke();
                return new LRESULT(0);
            case WindowMessages.DpiChanged:
                HandleDpiChanged(windowHandle, wParam, lParam);
                return new LRESULT(0);
            case WindowMessages.Destroy:
                _closedSink?.Invoke();
                return new LRESULT(0);
            default:
                return PInvoke.DefWindowProc(windowHandle, message, wParam, lParam);
        }
    }

    private static unsafe LRESULT HandlePaint(HWND windowHandle)
    {
        var window = GetWindow(windowHandle);
        return window is null ? new LRESULT(0) : window.PaintCore(windowHandle);
    }

    private unsafe LRESULT PaintCore(HWND windowHandle)
    {
        PAINTSTRUCT paintStruct;
        var deviceContext = PInvoke.BeginPaint(windowHandle, &paintStruct);
        if (ExternalRenderingEnabled)
        {
            PInvoke.EndPaint(windowHandle, paintStruct);
            return new LRESULT(0);
        }

        RECT clientRectangle;
        PInvoke.GetClientRect(windowHandle, &clientRectangle);
        _ = PInvoke.FillRect(deviceContext, &clientRectangle, PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_WINDOW));

        foreach (var element in _contentElements)
        {
            DrawElement(deviceContext, element);
        }

        PInvoke.EndPaint(windowHandle, paintStruct);
        return new LRESULT(0);
    }

    private unsafe void DrawElement(HDC deviceContext, WindowContentElement element)
    {
        DrawElement(deviceContext, element, _contentTextArena);
    }

    private static unsafe void DrawElement(HDC deviceContext, WindowContentElement element, ITextResolver textResolver)
    {
        var bounds = ToRect(element.Bounds);

        switch (element.Kind)
        {
            case WindowContentElementKind.Rectangle:
                if (element.BackgroundColor.A > 0)
                {
                    FillRectangle(deviceContext, bounds, element.BackgroundColor);
                }

                DrawBorderRectangle(deviceContext, bounds, element.BorderColor, element.BorderThickness);
                return;
            case WindowContentElementKind.Button:
                FillRectangle(deviceContext, bounds, element.BackgroundColor);
                var borderThickness = ResolveBorderThickness(bounds, element.BorderThickness);
                DrawBorderRectangle(deviceContext, bounds, element.BorderColor, borderThickness);
                var inner = bounds;
                inner.left += borderThickness;
                inner.top += borderThickness;
                inner.right -= borderThickness;
                inner.bottom -= borderThickness;
                DrawText(
                    deviceContext,
                    inner,
                    textResolver.Resolve(element.Text),
                    element.ForegroundColor,
                    DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                return;
            case WindowContentElementKind.Text:
                DrawText(
                    deviceContext,
                    bounds,
                    textResolver.Resolve(element.Text),
                    element.ForegroundColor,
                    DRAW_TEXT_FORMAT.DT_LEFT | DRAW_TEXT_FORMAT.DT_TOP | DRAW_TEXT_FORMAT.DT_WORDBREAK | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                return;
            default:
                return;
        }
    }

    private static unsafe void DrawText(HDC deviceContext, RECT bounds, ReadOnlySpan<char> text, WindowColor color, DRAW_TEXT_FORMAT format)
    {
        if (text.IsEmpty)
        {
            return;
        }

        _ = PInvoke.SetBkMode(deviceContext, BACKGROUND_MODE.TRANSPARENT);
        _ = PInvoke.SetTextColor(deviceContext, ToColorRef(color));

        fixed (char* textPointer = text)
        {
            _ = PInvoke.DrawText(deviceContext, new PCWSTR(textPointer), text.Length, &bounds, format);
        }
    }

    private WindowContentElement CopyElementText(WindowContentElement element, ITextResolver textResolver)
    {
        if ((element.Kind != WindowContentElementKind.Text && element.Kind != WindowContentElementKind.Button) || !element.Text.IsValid)
        {
            return element;
        }

        var text = textResolver.Resolve(element.Text);
        if (text.IsEmpty)
        {
            return new WindowContentElement(
                element.Kind,
                element.Bounds,
                default,
                element.ForegroundColor,
                element.BackgroundColor,
                element.BorderColor,
                element.BorderThickness);
        }

        return new WindowContentElement(
            element.Kind,
            element.Bounds,
            _contentTextArena.Add(text),
            element.ForegroundColor,
            element.BackgroundColor,
            element.BorderColor,
            element.BorderThickness);
    }

    private static unsafe void FillRectangle(HDC deviceContext, RECT bounds, WindowColor color)
    {
        using var brush = new ScopedBrush(color);
        _ = PInvoke.FillRect(deviceContext, &bounds, brush.Handle);
    }

    private static unsafe void DrawBorderRectangle(
        HDC deviceContext,
        RECT bounds,
        WindowColor color,
        int requestedThickness)
    {
        var thickness = ResolveBorderThickness(bounds, requestedThickness);
        if (thickness == 0 || color.A == 0)
        {
            return;
        }

        FillRectangle(deviceContext, new RECT { left = bounds.left, top = bounds.top, right = bounds.right, bottom = bounds.top + thickness }, color);
        FillRectangle(deviceContext, new RECT { left = bounds.left, top = bounds.bottom - thickness, right = bounds.right, bottom = bounds.bottom }, color);

        var innerTop = bounds.top + thickness;
        var innerBottom = bounds.bottom - thickness;
        if (innerBottom > innerTop)
        {
            FillRectangle(deviceContext, new RECT { left = bounds.left, top = innerTop, right = bounds.left + thickness, bottom = innerBottom }, color);
            FillRectangle(deviceContext, new RECT { left = bounds.right - thickness, top = innerTop, right = bounds.right, bottom = innerBottom }, color);
        }
    }

    private static int ResolveBorderThickness(RECT bounds, int requestedThickness)
    {
        if (requestedThickness <= 0)
        {
            return 0;
        }

        var width = Math.Max(bounds.right - bounds.left, 0);
        var height = Math.Max(bounds.bottom - bounds.top, 0);
        return Math.Min(requestedThickness, Math.Min(width / 2, height / 2));
    }

    private static RECT ToRect(PixelRectangle rectangle)
    {
        return new RECT
        {
            left = rectangle.X,
            top = rectangle.Y,
            right = rectangle.X + rectangle.Width,
            bottom = rectangle.Y + rectangle.Height
        };
    }

    private static global::Windows.Win32.Foundation.COLORREF ToColorRef(WindowColor color)
    {
        return new global::Windows.Win32.Foundation.COLORREF((uint)(color.R | (color.G << 8) | (color.B << 16)));
    }

    private static unsafe void HandleSizeChanged(HWND windowHandle)
    {
        var window = GetWindow(windowHandle);
        if (window is null) return;

        RECT clientRect;
        PInvoke.GetClientRect(windowHandle, &clientRect);
        var width = clientRect.right - clientRect.left;
        var height = clientRect.bottom - clientRect.top;

        if (width > 0 && height > 0)
        {
            // Update Region immediately so layout pipeline sees correct viewport
            window.Region = new ScreenRegion(
                window.Region.ScreenId,
                new PixelRectangle(
                    window.Region.PhysicalBounds.X,
                    window.Region.PhysicalBounds.Y,
                    width,
                    height));

            window.SizeChanged?.Invoke(width, height);
        }

        PInvoke.InvalidateRect(windowHandle, null, !window.ExternalRenderingEnabled);
    }

    private unsafe void HandleDpiChanged(HWND windowHandle, WPARAM wParam, LPARAM lParam)
    {
        var newDpi = (int)(wParam.Value & 0xFFFF);
        var newScale = new DisplayScale(newDpi / 96f, newDpi / 96f).Normalize();

        _dpiChangedSink?.Invoke(newScale);
        DpiChanged?.Invoke(newScale);

        // Apply the suggested window rect — this triggers WM_SIZE → HandleSizeChanged
        var suggestedRect = (RECT*)lParam.Value;
        PInvoke.SetWindowPos(
            windowHandle,
            HWND.Null,
            suggestedRect->left,
            suggestedRect->top,
            suggestedRect->right - suggestedRect->left,
            suggestedRect->bottom - suggestedRect->top,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private void PublishPointerEvent(RawInputEventKind kind, LPARAM lParam, PointerButton button = PointerButton.None)
    {
        _rawInputEventSink?.Invoke(new RawInputEvent(
            kind,
            Stopwatch.GetTimestamp(),
            GetPointerX(lParam),
            GetPointerY(lParam),
            Button: button));
    }

    private void PublishKeyEvent(RawInputEventKind kind, WPARAM wParam)
    {
        _rawInputEventSink?.Invoke(new RawInputEvent(
            kind,
            Stopwatch.GetTimestamp(),
            0,
            0,
            GetKeyCode(wParam)));
    }

    private void PublishCharacterEvent(WPARAM wParam)
    {
        var character = unchecked((char)GetKeyCode(wParam));
        _rawInputEventSink?.Invoke(new RawInputEvent(
            RawInputEventKind.CharacterInput,
            Stopwatch.GetTimestamp(),
            0,
            0,
            KeyCode: character,
            Character: character));
    }

    private void PublishWheelEvent(WPARAM wParam, LPARAM lParam)
    {
        _rawInputEventSink?.Invoke(new RawInputEvent(
            RawInputEventKind.PointerWheel,
            Stopwatch.GetTimestamp(),
            GetPointerX(lParam),
            GetPointerY(lParam),
            Delta: GetWheelDelta(wParam)));
    }

    private void PublishFocusEvent(RawInputEventKind kind)
    {
        _rawInputEventSink?.Invoke(new RawInputEvent(kind, Stopwatch.GetTimestamp(), 0, 0));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void EnsureAccess()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("WindowsNativeWindow must be accessed from the creating thread.");
        }
    }

    private static unsafe int GetPointerX(LPARAM lParam)
    {
        return unchecked((short)(nint)lParam.Value);
    }

    private static unsafe int GetPointerY(LPARAM lParam)
    {
        return unchecked((short)((nint)lParam.Value >> 16));
    }

    private static unsafe int GetKeyCode(WPARAM wParam)
    {
        return unchecked((int)(nuint)wParam.Value);
    }

    private static unsafe int GetWheelDelta(WPARAM wParam)
    {
        return unchecked((short)((nuint)wParam.Value >> 16));
    }

    private void RegisterWindowClass()
    {
        unsafe
        {
            WindowsWindowClassRegistry.AddReference(_instanceHandle, &HandleWindowMessage);
        }
    }

    private void UnregisterWindowClass()
    {
        WindowsWindowClassRegistry.RemoveReference(_instanceHandle);
    }

    private static WindowsNativeWindow? GetWindow(HWND windowHandle)
    {
        var instancePointer = PInvoke.GetWindowLongPtr(windowHandle, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
        if (instancePointer == IntPtr.Zero)
        {
            return null;
        }

        var gcHandle = GCHandle.FromIntPtr(instancePointer);
        return gcHandle.Target as WindowsNativeWindow;
    }

    private readonly ref struct ScopedBrush(WindowColor color)
    {
        public HBRUSH Handle { get; } = PInvoke.CreateSolidBrush(ToColorRef(color));

        public void Dispose()
        {
            if (Handle != HBRUSH.Null)
            {
                _ = PInvoke.DeleteObject(Handle);
            }
        }
    }
}
