using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WifiWatch.Services;

public static class WindowCaptionTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int CaptionColor = 35;

    // Chrome Surface As A COLORREF, Same In Any Byte Order
    private const int ChromeSurface = 0x141414;

    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
            return;

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));

        // Title Bar Blends Into The App Bar
        var color = ChromeSurface;
        _ = DwmSetWindowAttribute(handle, CaptionColor, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint window,
        int attribute,
        ref int value,
        int size
    );
}
