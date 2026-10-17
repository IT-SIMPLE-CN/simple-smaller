using System.Runtime.InteropServices;
using SimpleSmaller.Auto;

// 命名空间不叫 Views.Windows：会遮住系统的 Windows.* 命名空间。
namespace SimpleSmaller.Views.Sub;

/// 通知区域图标（mac 的菜单栏图标）：左键打开小窗，右键菜单（打开主窗口、粘贴剪贴板、设置、退出）。
/// 用 Shell_NotifyIcon 自己做，不另加 NuGet 包。设置里「托盘图标」关掉就移除。
internal static class TrayIcon
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_SHOWTIP = 0x80;
    private const uint NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, WM_CONTEXTMENU = 0x7B;

    private static bool shown;
    private static bool hooked;
    private static IntPtr icon;

    public static bool IsShown => shown;

    public static void Refresh()
    {
        var mw = MessageWindow.Shared;
        if (!hooked)
        {
            hooked = true;
            mw.Tray += OnTray;
            // 资源管理器重启后图标会丢，要重新加。
            mw.TaskbarCreated += () => { if (shown) { shown = false; Add(); } };
        }
        if (AppSettings.Shared.MenuBarIcon) Add(); else Remove();
    }

    private static NOTIFYICONDATA Data()
    {
        if (icon == IntPtr.Zero)
        {
            var size = GetSystemMetrics(49);
            icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"), 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        }
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = MessageWindow.Shared.Hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = MessageWindow.TrayCallback,
            hIcon = icon,
            szTip = "简单小一点",
            uVersion = 4,
        };
    }

    private static void Add()
    {
        if (shown) return;
        var d = Data();
        if (Shell_NotifyIcon(NIM_ADD, ref d))
        {
            Shell_NotifyIcon(NIM_SETVERSION, ref d);
            shown = true;
        }
    }

    public static void Remove()
    {
        if (!shown) return;
        var d = Data();
        Shell_NotifyIcon(NIM_DELETE, ref d);
        shown = false;
    }

    private static void OnTray(uint message, int x, int y)
    {
        switch (message)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                TrayView.Toggle();
                break;
            case WM_CONTEXTMENU:
                ShowMenu(x, y);
                break;
        }
    }

    private static void ShowMenu(int x, int y)
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, 1, "打开主窗口");
        AppendMenu(menu, 0, 2, "粘贴剪贴板");
        AppendMenu(menu, 0x800 /* MF_SEPARATOR */, 0, null);
        AppendMenu(menu, 0, 3, "设置…");
        AppendMenu(menu, 0, 4, "关于简单小一点");
        AppendMenu(menu, 0x800, 0, null);
        AppendMenu(menu, 0, 5, "退出");
        var hwnd = MessageWindow.Shared.Hwnd;
        // 不先把消息窗口设成前台，点菜单外面菜单不会收起。
        SetForegroundWindow(hwnd);
        var cmd = TrackPopupMenuEx(menu, 0x100 /* TPM_RETURNCMD */ | 0x80 /* TPM_BOTTOMALIGN */ | 0x8 /* TPM_RIGHTALIGN */, x, y, hwnd, IntPtr.Zero);
        PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
        switch (cmd)
        {
            case 1: App.ShowMain(); break;
            case 2: TrayView.PasteClipboard(); break;
            case 3: App.OpenSettings(); break;
            case 4: App.OpenAbout(); break;
            case 5: App.Quit(); break;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
}
