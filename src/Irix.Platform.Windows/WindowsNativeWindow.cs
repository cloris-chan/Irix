using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Irix.Platform.Windows;

internal sealed class WindowsNativeWindow : INativeWindow
{
    private const int ContentPadding = 16;
    private const int ButtonBorderThickness = 1;

    private readonly HINSTANCE _instanceHandle;
    private readonly Action? _closedSink;
    private readonly Action? _displayChangedSink;
    private readonly GCHandle _gcHandle;
    private readonly Action<RawInputEvent>? _rawInputEventSink;
    private readonly nint _titlePointer;
    private readonly HWND _windowHandle;
    private readonly int _ownerThreadId;

    private WindowContentElement[] _contentElements = [];
    private bool _isDisposed;

    public WindowsNativeWindow(
        string title,
        ScreenRegion region,
        Action<RawInputEvent>? rawInputEventSink = null,
        Action? closedSink = null,
        Action? displayChangedSink = null)
    {
        Title = title;
        Region = region;
        _closedSink = closedSink;
        _displayChangedSink = displayChangedSink;
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

    public ScreenRegion Region { get; }

    public unsafe nint Handle => (nint)_windowHandle.Value;

    public void SetContentElements(IReadOnlyList<WindowContentElement> elements)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        EnsureAccess();
        _contentElements = [.. elements];
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

    private static unsafe void DrawElement(HDC deviceContext, WindowContentElement element)
    {
        var bounds = ToRect(element.Bounds);

        switch (element.Kind)
        {
            case WindowContentElementKind.Rectangle:
                _ = PInvoke.FillRect(deviceContext, &bounds, PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_HIGHLIGHT));
                return;
            case WindowContentElementKind.Button:
                var border = bounds;
                _ = PInvoke.FrameRect(deviceContext, &border, PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_WINDOWFRAME));

                var inner = bounds;
                inner.left += ButtonBorderThickness;
                inner.top += ButtonBorderThickness;
                inner.right -= ButtonBorderThickness;
                inner.bottom -= ButtonBorderThickness;
                _ = PInvoke.FillRect(deviceContext, &inner, PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_3DFACE));
                DrawText(deviceContext, inner, element.Text, DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_VCENTER | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                return;
            case WindowContentElementKind.Text:
                DrawText(deviceContext, bounds, element.Text, DRAW_TEXT_FORMAT.DT_LEFT | DRAW_TEXT_FORMAT.DT_TOP | DRAW_TEXT_FORMAT.DT_WORDBREAK | DRAW_TEXT_FORMAT.DT_NOPREFIX);
                return;
            default:
                return;
        }
    }

    private static unsafe void DrawText(HDC deviceContext, RECT bounds, string? text, DRAW_TEXT_FORMAT format)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        fixed (char* textPointer = text)
        {
            _ = PInvoke.DrawText(deviceContext, new PCWSTR(textPointer), text.Length, &bounds, format);
        }
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

    private static unsafe void HandleSizeChanged(HWND windowHandle)
    {
        PInvoke.InvalidateRect(windowHandle, null, true);
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
}
