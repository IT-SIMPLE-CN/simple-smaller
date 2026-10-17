using System.Runtime.InteropServices;

namespace SimpleSmaller.Ring;

/// 全局低级鼠标钩子（WH_MOUSE_LL），跑在自己的线程上：界面线程忙的时候，全系统的鼠标也不会跟着卡。
///
/// 只监听、不拦截：每个事件都原样往下传。回调里只做几个 user32 的小判断，再把事情投递出去
/// （识别文件要跨进程问资源管理器，交给 FileHit 的线程做）。
/// - 按住 Alt（不带 Ctrl / Shift / Win）在资源管理器或桌面上按下左键：记下按下点，让 FileHit 先记住当时的多选；
/// - 拖动超过 4 像素：是 Alt 拖动（创建快捷方式），不弹；
/// - 原地松开且 Alt 还按着：弹圆环；
/// - 双击间隔内在同一处又按下：是 Alt+双击（属性），收起圆环、不再弹；
/// - 圆环开着时，在圆环外按下任意键或滚轮：收起；Esc（键盘钩子只在圆环开着时装）：收起。
internal sealed class RingHook
{
    public static RingHook Shared { get; } = new();

    /// 按下（屏幕物理像素、序号）。在钩子线程回调。
    public Action<int, int, int>? Pressed;
    /// 松开，可以弹圆环了。在钩子线程回调。
    public Action<int, int, int>? Released;
    /// 该收起圆环了（点在圆环外、Esc、Alt+双击）。在钩子线程回调。
    public Action? Dismiss;

    private Thread? thread;
    private uint threadId;
    private IntPtr mouseHook, keyHook;
    private readonly HookProc mouseProc, keyProc;

    // 按下状态（只在钩子线程读写）。
    private bool pressing, dragged;
    private int pressX, pressY;
    private volatile int sequence;

    /// 最近一次按下的序号（识别慢于下一次按下时，旧的结果作废）。
    public int Sequence => sequence;
    private uint lastUpTime;
    private int lastUpX, lastUpY;

    // 圆环位置（界面线程写，钩子线程读）：圆心与内外半径，物理像素。
    private volatile RingArea? area;

    private sealed record RingArea(int X, int Y, double Inner, double Outer);

    /// 自己发出的按键带这个标记（dwExtraInfo）。
    public static readonly IntPtr OwnInput = new(0x53534D4C);

    private const uint WM_APP_KEYS_ON = 0x8000 + 21, WM_APP_KEYS_OFF = 0x8000 + 22, WM_QUIT = 0x0012;

    private RingHook()
    {
        mouseProc = MouseProc;
        keyProc = KeyProc;
    }

    public bool Running => thread is not null;

    public void Start()
    {
        if (thread is not null) return;
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            mouseHook = SetWindowsHookEx(14, mouseProc, GetModuleHandle(null), 0); // WH_MOUSE_LL
            if (mouseHook == IntPtr.Zero) Log.Error($"圆环：装鼠标钩子失败 {Marshal.GetLastWin32Error()}");
            ready.Set();
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_APP_KEYS_ON && keyHook == IntPtr.Zero)
                    keyHook = SetWindowsHookEx(13, keyProc, GetModuleHandle(null), 0); // WH_KEYBOARD_LL
                else if (msg.message == WM_APP_KEYS_OFF && keyHook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(keyHook);
                    keyHook = IntPtr.Zero;
                }
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
            if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            keyHook = mouseHook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "圆环钩子" };
        thread.Start();
        ready.Wait(2000);
    }

    public void Stop()
    {
        if (thread is null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        thread = null;
        area = null;
    }

    /// 圆环出现：记下位置，装上 Esc 监听。hole 以内（镂空处）和外圈以外都算圆环外。
    public void RingShown(int centerX, int centerY, double hole, double outer)
    {
        area = new RingArea(centerX, centerY, hole, outer);
        if (thread is not null) PostThreadMessage(threadId, WM_APP_KEYS_ON, IntPtr.Zero, IntPtr.Zero);
    }

    public void RingHidden()
    {
        area = null;
        if (thread is not null) PostThreadMessage(threadId, WM_APP_KEYS_OFF, IntPtr.Zero, IntPtr.Zero);
    }

    // MARK: 回调

    private IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            try { OnMouse((uint)wParam, Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam)); }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204,
        WM_MBUTTONDOWN = 0x0207, WM_MOUSEWHEEL = 0x020A, WM_XBUTTONDOWN = 0x020B, WM_MOUSEHWHEEL = 0x020E;

    private void OnMouse(uint msg, MSLLHOOKSTRUCT info)
    {
        int x = info.pt.X, y = info.pt.Y;
        switch (msg)
        {
            case WM_MOUSEMOVE:
                if (pressing && !dragged && (Math.Abs(x - pressX) > 4 || Math.Abs(y - pressY) > 4)) dragged = true;
                return;
            case WM_LBUTTONDOWN:
            {
                var outside = area is { } a && !Inside(a, x, y);
                pressing = false;
                if (AltOnly() && IsDoubleClick(x, y, info.time))
                {
                    // Alt+双击：第二下落在圆环的镂空处，交给资源管理器打开属性；圆环收起，也不再弹。
                    sequence++;
                    lastUpTime = 0;
                    Dismiss?.Invoke();
                    return;
                }
                if (outside) Dismiss?.Invoke();
                if (!AltOnly() || !IsExplorerAt(x, y)) return;
                pressing = true;
                dragged = false;
                pressX = x;
                pressY = y;
                sequence++;
                Pressed?.Invoke(x, y, sequence);
                return;
            }
            case WM_LBUTTONUP:
            {
                if (!pressing) return;
                pressing = false;
                lastUpTime = info.time;
                lastUpX = x;
                lastUpY = y;
                // 松手处离按下处远也算拖动（有的拖动不经过钩子报移动）。
                if (Math.Abs(x - pressX) > 4 || Math.Abs(y - pressY) > 4) dragged = true;
                if (dragged || !AltOnly()) return;
                Released?.Invoke(pressX, pressY, sequence);
                return;
            }
            case WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN or WM_MOUSEWHEEL or WM_MOUSEHWHEEL:
                if (area is { } r && !Inside(r, x, y)) Dismiss?.Invoke();
                return;
        }
    }

    private bool IsDoubleClick(int x, int y, uint time)
    {
        if (lastUpTime == 0 || time - lastUpTime > GetDoubleClickTime()) return false;
        return Math.Abs(x - lastUpX) <= GetSystemMetrics(36) && Math.Abs(y - lastUpY) <= GetSystemMetrics(37); // SM_CXDOUBLECLK / SM_CYDOUBLECLK
    }

    private static bool Inside(RingArea a, int x, int y)
    {
        var d = Math.Sqrt((double)(x - a.X) * (x - a.X) + (double)(y - a.Y) * (y - a.Y));
        return d >= a.Inner && d <= a.Outer;
    }

    private IntPtr KeyProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && ((uint)wParam == 0x0100 || (uint)wParam == 0x0104)) // WM_KEYDOWN / WM_SYSKEYDOWN
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            // 自己发出的 Esc（取消资源管理器的改名，带着 OwnInput 标记）不算。
            if (info.vkCode == 0x1B && info.extra != OwnInput && area is not null) Dismiss?.Invoke();
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private static bool AltOnly() =>
        Down(0x12) && !Down(0x11) && !Down(0x10) && !Down(0x5B) && !Down(0x5C); // Alt，且没按 Ctrl / Shift / Win

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// 指针下是资源管理器窗口或桌面。
    internal static bool IsExplorerAt(int x, int y)
    {
        var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2); // GA_ROOT
        return ClassOf(root) is "CabinetWClass" or "Progman" or "WorkerW";
    }

    internal static string ClassOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var buffer = new char[64];
        var n = GetClassName(hwnd, buffer, buffer.Length);
        return n > 0 ? new string(buffer, 0, n) : "";
    }

    // MARK: Win32

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
        public uint priv;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, char[] name, int max);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
