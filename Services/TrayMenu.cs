using System.Runtime.InteropServices;

namespace WifiWatch.Services;

public static class TrayMenu
{
    // Win32 Menu Flags
    private const uint ItemFlag = 0x0;
    private const uint SeparatorFlag = 0x800;
    private const uint ReturnCommand = 0x100;
    private const uint RightButton = 0x2;
    private const uint BottomAlign = 0x20;
    private const int ForceDarkMode = 2;

    static TrayMenu()
    {
        // Ponytail: Undocumented Uxtheme Ordinals, Same As Explorer Uses
        _ = SetPreferredAppMode(ForceDarkMode);
        FlushMenuThemes();
    }

    public static string? Show(nint ownerWindow, params string?[] items)
    {
        var menu = CreatePopupMenu();
        for (var index = 0; index < items.Length; index++)
        {
            // A Null Item Draws A Separator
            _ = AppendMenuW(
                menu,
                items[index] is null ? SeparatorFlag : ItemFlag,
                (nuint)(index + 1),
                items[index]
            );
        }

        // The Owner Must Be Foreground Or The Menu Never Closes
        _ = GetCursorPos(out var cursor);
        _ = SetForegroundWindow(ownerWindow);
        var picked = TrackPopupMenuEx(
            menu,
            ReturnCommand | RightButton | BottomAlign,
            cursor.X,
            cursor.Y,
            ownerWindow,
            0
        );
        _ = PostMessageW(ownerWindow, 0, 0, 0);
        _ = DestroyMenu(menu);

        return picked > 0 ? items[picked - 1] : null;
    }

    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(nint menu, uint flags, nuint itemId, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(
        nint menu,
        uint flags,
        int x,
        int y,
        nint window,
        nint parameters
    );

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
