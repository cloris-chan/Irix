using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Irix.Platform.Windows;

internal static class WindowsWindowClassRegistry
{
    private const int ArrowCursorId = 32512;

    private static readonly Lock SyncRoot = new();
    private static readonly string WindowClassName = "IrixWindowClass";

    private static int _referenceCount;

    public static string ClassName => WindowClassName;

    public static unsafe void AddReference(
        HINSTANCE instanceHandle,
        delegate* unmanaged[Stdcall]<HWND, uint, WPARAM, LPARAM, LRESULT> windowProcedure)
    {
        lock (SyncRoot)
        {
            if (_referenceCount == 0)
            {
                fixed (char* classNameValue = WindowClassName)
                {
                    var windowClass = new WNDCLASSW
                    {
                        style = WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW,
                        lpfnWndProc = windowProcedure,
                        hInstance = instanceHandle,
                        hCursor = PInvoke.LoadCursor(default, new PCWSTR((char*)ArrowCursorId)),
                        hbrBackground = PInvoke.GetSysColorBrush(SYS_COLOR_INDEX.COLOR_WINDOW),
                        lpszClassName = new PCWSTR(classNameValue)
                    };

                    var classAtom = PInvoke.RegisterClass(windowClass);
                    if (classAtom == 0)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register the Win32 window class.");
                    }
                }
            }

            _referenceCount++;
        }
    }

    public static unsafe void RemoveReference(HINSTANCE instanceHandle)
    {
        lock (SyncRoot)
        {
            if (_referenceCount == 0)
            {
                return;
            }

            _referenceCount--;
            if (_referenceCount != 0)
            {
                return;
            }

            fixed (char* classNameValue = WindowClassName)
            {
                _ = PInvoke.UnregisterClass(new PCWSTR(classNameValue), instanceHandle);
            }
        }
    }
}
