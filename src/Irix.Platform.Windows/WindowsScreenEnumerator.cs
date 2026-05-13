using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Irix.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Irix.Platform.Windows;

internal static class WindowsScreenEnumerator
{
    public static IReadOnlyList<IScreenInfo> Enumerate()
    {
        var screens = new List<IScreenInfo>();
        var handle = GCHandle.Alloc(screens);

        try
        {
            unsafe
            {
                _ = PInvoke.EnumDisplayMonitors(default, null, &MonitorEnumCallback, GCHandle.ToIntPtr(handle));
            }
        }
        finally
        {
            handle.Free();
        }

        if (screens.Count == 0)
        {
            screens.Add(new ScreenInfo
            {
                Id = 0,
                DpiScale = 1.0f,
                Scale = DisplayScale.Identity,
                RefreshRateHz = 60,
                ColorSpace = ColorSpace.Srgb,
                PhysicalBounds = new PixelRectangle(0, 0, 1920, 1080)
            });
        }

        return screens;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe BOOL MonitorEnumCallback(HMONITOR monitorHandle, HDC deviceContext, RECT* clippingRectangle, LPARAM data)
    {
        var screens = (List<IScreenInfo>)GCHandle.FromIntPtr((IntPtr)data.Value).Target!;

        MONITORINFO monitorInfo = new()
        {
            cbSize = (uint)sizeof(MONITORINFO)
        };

        if (!PInvoke.GetMonitorInfo(monitorHandle, &monitorInfo))
        {
            return true;
        }

        var dpiScale = 1.0f;
        if (IsPerMonitorDpiAvailable()
            && PInvoke.GetDpiForMonitor(monitorHandle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _) >= 0)
        {
            dpiScale = dpiX / 96.0f;
        }

        var bounds = monitorInfo.rcMonitor;
        var refreshRateHz = GetCurrentDisplayRefreshRate(deviceContext);
        screens.Add(new ScreenInfo
        {
            Id = screens.Count,
            DpiScale = dpiScale,
            Scale = new DisplayScale(dpiScale, dpiScale),
            RefreshRateHz = refreshRateHz > 1 ? refreshRateHz : 60,
            ColorSpace = ColorSpace.Srgb,
            PhysicalBounds = new PixelRectangle(bounds.left, bounds.top, bounds.right - bounds.left, bounds.bottom - bounds.top)
        });

        return true;
    }

    [SupportedOSPlatformGuard("windows8.1")]
    private static bool IsPerMonitorDpiAvailable() => OperatingSystem.IsWindowsVersionAtLeast(6, 3);

    private static unsafe int GetCurrentDisplayRefreshRate(HDC deviceContext)
    {
        var mode = new DEVMODEW
        {
            dmSize = (ushort)sizeof(DEVMODEW)
        };

        if (PInvoke.EnumDisplaySettings(null!, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode)
            && mode.dmDisplayFrequency > 1
            && mode.dmDisplayFrequency <= int.MaxValue)
        {
            return (int)mode.dmDisplayFrequency;
        }

        var refreshRateHz = PInvoke.GetDeviceCaps(deviceContext, GET_DEVICE_CAPS_INDEX.VREFRESH);
        return refreshRateHz > 1 ? refreshRateHz : 60;
    }
}
