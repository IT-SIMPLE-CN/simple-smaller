using System.Runtime.InteropServices;

namespace SimpleSmaller.Auto;

/// 看不见的 Win32 窗口：收通知区域图标的点按、剪贴板变化（AddClipboardFormatListener）、
/// 资源管理器重启后的「TaskbarCreated」（图标要重新加）。主窗口关到托盘后它照样在。
internal sealed class MessageWindow : IDisposable
{
    public const uint TrayCallback = 0x8000 + 1; // WM_APP + 1
    private const uint WM_CLIPBOARDUPDATE = 0x031D;

    private static MessageWindow? shared;
    public static MessageWindow Shared => shared ??= new MessageWindow();

    public IntPtr Hwnd { get; }
    /// 通知区域图标的消息：(事件, 锚点 x, 锚点 y)。
    public event Action<uint, int, int>? Tray;
    public event Action? ClipboardChanged;
    public event Action? TaskbarCreated;

    private readonly WndProc proc;
    private readonly uint taskbarCreated;

    private MessageWindow()
    {
        proc = Handle;
        var cls = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandle(null),
            lpszClassName = "SimpleSmaller.Messages",
        };
        RegisterClassEx(ref cls);
        Hwnd = CreateWindowEx(0, cls.lpszClassName, "简单小一点", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, cls.hInstance, IntPtr.Zero);
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        AddClipboardFormatListener(Hwnd);
    }

    private IntPtr Handle(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == TrayCallback)
            {
                var w = wParam.ToInt64();
                Tray?.Invoke((uint)(lParam.ToInt64() & 0xFFFF), (short)(w & 0xFFFF), (short)((w >> 16) & 0xFFFF));
                return IntPtr.Zero;
            }
            if (msg == WM_CLIPBOARDUPDATE)
            {
                ClipboardChanged?.Invoke();
                return IntPtr.Zero;
            }
            if (msg == taskbarCreated && taskbarCreated != 0)
            {
                TaskbarCreated?.Invoke();
                return IntPtr.Zero;
            }
        }
        catch (Exception e)
        {
            Log.Error($"消息窗口：{e.Message}");
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        RemoveClipboardFormatListener(Hwnd);
        DestroyWindow(Hwnd);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
