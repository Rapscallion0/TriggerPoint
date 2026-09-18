using System;
using System.Runtime.InteropServices;

namespace TriggerPoint.Infrastructure.Win32;

public enum BackdropType
{
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4
}

public static class DwmHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int SM_REMOTESESSION = 0x1000;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int smIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    private static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);
    }

    public static bool IsRemoteSession()
    {
        try
        {
            return GetSystemMetrics(SM_REMOTESESSION) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindows11OrGreater()
    {
        return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
    }

    public static bool ApplyBackdrop(IntPtr hWnd, BackdropType backdropType, bool isDarkMode)
    {
        if (hWnd == IntPtr.Zero) return false;

        try
        {
            // Set dark or light mode title/chrome
            int darkFlag = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

            // Layered windows (WPF AllowsTransparency="True") do NOT support DWM system backdrops or frame extension.
            // Extending DWM frame into a layered window causes DWM to paint an opaque rectangular frame (grey box),
            // destroying the transparent alpha channel and rounded corners.
            if ((GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_LAYERED) != 0)
            {
                return false;
            }

            // Skip DWM materials if Remote Desktop session or not Windows 11+
            if (IsRemoteSession() || !IsWindows11OrGreater())
            {
                return false;
            }

            if (backdropType == BackdropType.None)
            {
                int none = (int)BackdropType.None;
                DwmSetWindowAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
                return true;
            }

            // Extend frame for backdrop rendering
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hWnd, ref margins);

            int type = (int)backdropType;
            int hr = DwmSetWindowAttribute(hWnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }
}
