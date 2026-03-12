using System.Collections.Concurrent;
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
    private const int ArrowCursorId = 32512;

    private static readonly ConcurrentDictionary<nint, WindowsNativeWindow> WindowsByHandle = new();

    private readonly HINSTANCE _instanceHandle;
    private readonly Action<RawInputEvent>? _rawInputEventSink;
    private readonly string _windowClassName;
    private readonly nint _titlePointer;
    private readonly nint _windowClassNamePointer;
    private readonly HWND _windowHandle;
    private readonly int _ownerThreadId;

    private bool _isDisposed;

    public WindowsNativeWindow(string title, ScreenRegion region, Action<RawInputEvent>? rawInputEventSink = null)
    {
        Title = title;
        Region = region;
        _rawInputEventSink = rawInputEventSink;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        var moduleHandle = PInvoke.GetModuleHandle((PCWSTR)null);
        unsafe
        {
            _instanceHandle = new HINSTANCE((nint)moduleHandle.Value);
        }
        _windowClassName = $"IrixWindowClass_{Guid.NewGuid():N}";
        _titlePointer = Marshal.StringToHGlobalUni(Title);
        _windowClassNamePointer = Marshal.StringToHGlobalUni(_windowClassName);
        var windowClassRegistered = false;

        try
        {
            unsafe
            {
                var className = new PCWSTR((char*)_windowClassNamePointer);
                var windowClass = new WNDCLASSW
                {
                    style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                    lpfnWndProc = &HandleWindowMessage,
                    hInstance = _instanceHandle,
                    hCursor = PInvoke.LoadCursor(default, new PCWSTR((char*)ArrowCursorId)),
                    hbrBackground = PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_WINDOW),
                    lpszClassName = className
                };

                var classAtom = PInvoke.RegisterClass(windowClass);
                if (classAtom == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the Win32 window class.");
                }

                windowClassRegistered = true;

                _windowHandle = PInvoke.CreateWindowEx(
                    default,
                    className,
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

            if (_windowHandle == HWND.Null)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create the Win32 window.");
            }

            WindowsByHandle[Handle] = this;
        }
        catch
        {
            if (windowClassRegistered)
            {
                unsafe
                {
                    PInvoke.UnregisterClass(new PCWSTR((char*)_windowClassNamePointer), _instanceHandle);
                }
            }

            Marshal.FreeHGlobal(_titlePointer);
            Marshal.FreeHGlobal(_windowClassNamePointer);
            throw;
        }
    }

    public string Title { get; }

    public ScreenRegion Region { get; }

    public unsafe nint Handle => (nint)_windowHandle.Value;

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
        WindowsByHandle.TryRemove(Handle, out _);

        if (PInvoke.IsWindow(_windowHandle))
        {
            PInvoke.DestroyWindow(_windowHandle);
        }

        unsafe
        {
            PInvoke.UnregisterClass(new PCWSTR((char*)_windowClassNamePointer), _instanceHandle);
        }
        Marshal.FreeHGlobal(_titlePointer);
        Marshal.FreeHGlobal(_windowClassNamePointer);
        _isDisposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HandleWindowMessage(HWND windowHandle, uint message, WPARAM wParam, LPARAM lParam)
    {
        unsafe
        {
            var handle = (nint)windowHandle.Value;
            if (WindowsByHandle.TryGetValue(handle, out var window))
            {
                return window.HandleWindowMessageCore(windowHandle, message, wParam, lParam);
            }
        }

        if (message == WindowMessages.Destroy)
        {
            PInvoke.PostQuitMessage(0);
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
                PublishPointerEvent(RawInputEventKind.PointerPressed, lParam);
                return new LRESULT(0);
            case WindowMessages.LeftButtonUp:
                PublishPointerEvent(RawInputEventKind.PointerReleased, lParam);
                return new LRESULT(0);
            case WindowMessages.KeyDown:
                PublishKeyEvent(RawInputEventKind.KeyPressed, wParam);
                return new LRESULT(0);
            case WindowMessages.KeyUp:
                PublishKeyEvent(RawInputEventKind.KeyReleased, wParam);
                return new LRESULT(0);
            case WindowMessages.Destroy:
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            default:
                return PInvoke.DefWindowProc(windowHandle, message, wParam, lParam);
        }
    }

    private static unsafe LRESULT HandlePaint(HWND windowHandle)
    {
        var deviceContext = PInvoke.BeginPaint(windowHandle, out var paintStruct);
        PInvoke.GetClientRect(windowHandle, out var clientRectangle);
        _ = PInvoke.FillRect(deviceContext, &clientRectangle, PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_WINDOW));
        PInvoke.EndPaint(windowHandle, paintStruct);
        return new LRESULT(0);
    }

    private static unsafe void HandleSizeChanged(HWND windowHandle)
    {
        PInvoke.InvalidateRect(windowHandle, null, true);
    }

    private void PublishPointerEvent(RawInputEventKind kind, LPARAM lParam)
    {
        _rawInputEventSink?.Invoke(new RawInputEvent(
            kind,
            Stopwatch.GetTimestamp(),
            GetPointerX(lParam),
            GetPointerY(lParam)));
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
}
