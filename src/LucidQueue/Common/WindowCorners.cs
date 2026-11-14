using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LucidQueue.Common;

/// <summary>
/// 用 DWM 给无边框窗口加圆角。只在窗口生命周期内生效，不写入任何系统设置。
/// Windows 10 不支持该属性，调用会失败，窗口保持直角。
/// </summary>
internal static class WindowCorners
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        if (window.IsInitialized)
        {
            ApplyCore(window);
            return;
        }

        window.SourceInitialized += (_, _) => ApplyCore(window);
    }

    private static void ApplyCore(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var preference = DwmcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
        catch
        {
            // 旧版 Windows 没有这个属性，忽略即可。
        }
    }
}
