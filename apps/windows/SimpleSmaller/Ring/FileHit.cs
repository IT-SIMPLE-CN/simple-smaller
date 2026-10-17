using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SimpleSmaller.Ring;

/// 从屏幕坐标找出指针下的文件：资源管理器窗口（含多个标签页）与桌面。对应 mac 的 FileHit。
///
/// 两步：界面自动化（UI Automation）的 ElementFromPoint 看指针下是不是文件列表里的一项（拿到显示名）；
/// 再从 ShellWindows 找到这个窗口当前标签页的文件视图（IFolderView2），按显示名对出真实路径，并取多选。
/// 都是跨进程调用，放在自己的 STA 线程上做，界面线程和鼠标钩子都不等它。
/// COM 接口直接按虚表调用（同 AppPlatform 的 ITaskbarList），不依赖 .NET 的 COM 封装。
internal static unsafe class FileHit
{
    internal sealed record Hit(string File, MediaType Type, IReadOnlyList<string> Batch);

    private static readonly BlockingCollection<Action> queue = new();
    private static Thread? worker;
    private static IntPtr automation, walker;
    /// 按下那一刻的多选（松手时资源管理器会把多选收成被点的一项）。
    private static int snapshotSequence;
    private static List<string> snapshot = [];

    /// 在识别线程上执行。
    public static void Post(Action action)
    {
        Ensure();
        queue.Add(action);
    }

    private static void Ensure()
    {
        if (worker is not null) return;
        lock (queue)
        {
            if (worker is not null) return;
            worker = new Thread(() =>
            {
                CoInitializeEx(IntPtr.Zero, 2); // COINIT_APARTMENTTHREADED
                foreach (var action in queue.GetConsumingEnumerable())
                {
                    try { action(); }
                    catch (Exception e) { Log.Error($"圆环识别：{e.Message}"); }
                }
            })
            { IsBackground = true, Name = "圆环识别" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }
    }

    // MARK: 对外（都在识别线程上调用）

    /// 按下时记下当时的多选。
    public static void Remember(int x, int y, int sequence)
    {
        snapshotSequence = sequence;
        using var view = View(x, y);
        snapshot = view is null ? [] : Selection(view.Ptr);
        Log.Info($"圆环：按下时选中 {snapshot.Count} 项");
    }

    /// 指针下的文件，以及它所在的多选（同类文件，被点的排第一）；不是支持的文件返回 null。
    public static Hit? At(int x, int y, int sequence)
    {
        var name = ItemNameAt(x, y);
        if (name is null) return null;
        using var view = View(x, y);
        if (view is null) return null;
        var now = Selection(view.Ptr);
        var before = sequence == snapshotSequence ? snapshot : [];
        // 先在选中项里按显示名对（被点的一定已经选中），对不上再找整个文件夹。
        var file = before.Concat(now).FirstOrDefault(p => DisplayName(p) == name) ?? FindByName(view.Ptr, name);
        if (file is null || !File.Exists(file)) return null;
        if (MediaTypes.Of(file) is not { } type) return null;
        var pool = before.Count > 1 && Contains(before, file) ? before : now.Count > 1 && Contains(now, file) ? now : [file];
        var batch = new List<string> { file };
        batch.AddRange(pool.Where(p => !string.Equals(p, file, StringComparison.OrdinalIgnoreCase) && File.Exists(p) && MediaTypes.Of(p) == type));
        return new Hit(file, type, batch);
    }

    /// 对已选中的文件 Alt+单击，资源管理器过一会儿会进入改名；圆环开着时用户点别处或打字会改掉文件名。
    /// 正在改的就是这个文件时，只给资源管理器发一个 Esc 取消改名，别的情况不动。
    public static void CancelRename(int x, int y, string file)
    {
        var root = GetAncestor(WindowFromPoint(new RingHook.POINT { X = x, Y = y }), 2);
        if (root == IntPtr.Zero || GetForegroundWindow() != root) return;
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(GetWindowThreadProcessId(root, out _), ref info) || info.hwndFocus == IntPtr.Zero) return;
        if (RingHook.ClassOf(info.hwndFocus) != "Edit") return;
        var buffer = new char[520];
        var n = GetWindowText(info.hwndFocus, buffer, buffer.Length);
        var text = new string(buffer, 0, Math.Max(0, n));
        var fileName = Path.GetFileName(file);
        if (text != fileName && text != Path.GetFileNameWithoutExtension(file) && text != DisplayName(file)) return;
        Log.Info("圆环：取消资源管理器的改名");
        var inputs = new INPUT[2];
        inputs[0].type = inputs[1].type = 1; // INPUT_KEYBOARD
        inputs[0].ki.wVk = inputs[1].ki.wVk = 0x1B;
        inputs[1].ki.dwFlags = 2; // KEYEVENTF_KEYUP
        inputs[0].ki.dwExtraInfo = inputs[1].ki.dwExtraInfo = RingHook.OwnInput;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static bool Contains(List<string> list, string file) => list.Any(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase));

    // MARK: 界面自动化

    private const int ListItem = 50007, DataItem = 50029;

    /// 指针下文件列表项的显示名（图标、名字、详细信息里的其他列都算）；不是文件项返回 null。
    private static string? ItemNameAt(int x, int y)
    {
        if (!EnsureAutomation()) return null;
        IntPtr element;
        // IUIAutomation::ElementFromPoint(POINT, IUIAutomationElement**)
        var hr = ((delegate* unmanaged[Stdcall]<IntPtr, RingHook.POINT, IntPtr*, int>)Slot(automation, 7))(automation, new RingHook.POINT { X = x, Y = y }, &element);
        if (hr < 0 || element == IntPtr.Zero) return null;
        var current = element;
        try
        {
            for (var depth = 0; depth < 6 && current != IntPtr.Zero; depth++)
            {
                var type = ControlType(current);
                if (type is ListItem or DataItem)
                {
                    var name = Name(current);
                    return string.IsNullOrEmpty(name) ? null : name;
                }
                // 点到窗口、列表本身（空白处）就停。
                if (type is 50032 or 50008 or 50028 or 50033) return null; // Window / List / DataGrid / Pane
                IntPtr parent;
                // IUIAutomationTreeWalker::GetParentElement
                hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(walker, 3))(walker, current, &parent);
                Marshal.Release(current);
                current = hr >= 0 ? parent : IntPtr.Zero;
            }
            return null;
        }
        finally
        {
            if (current != IntPtr.Zero) Marshal.Release(current);
        }
    }

    private static bool EnsureAutomation()
    {
        if (automation != IntPtr.Zero) return true;
        var clsid = new Guid("ff48dba4-60ef-4201-aa87-54103eef594e");
        var iid = new Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");
        IntPtr a;
        if (CoCreateInstance(&clsid, IntPtr.Zero, 1, &iid, &a) < 0 || a == IntPtr.Zero) return false;
        IntPtr w;
        // IUIAutomation::get_ControlViewWalker
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(a, 14))(a, &w) < 0 || w == IntPtr.Zero)
        {
            Marshal.Release(a);
            return false;
        }
        automation = a;
        walker = w;
        return true;
    }

    private static int ControlType(IntPtr element)
    {
        int type;
        // IUIAutomationElement::get_CurrentControlType
        return ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(element, 21))(element, &type) < 0 ? 0 : type;
    }

    private static string? Name(IntPtr element)
    {
        IntPtr bstr;
        // IUIAutomationElement::get_CurrentName
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(element, 23))(element, &bstr) < 0 || bstr == IntPtr.Zero) return null;
        var s = Marshal.PtrToStringBSTR(bstr);
        Marshal.FreeBSTR(bstr);
        return s;
    }

    // MARK: 资源管理器的文件视图

    /// 持有一个 IFolderView2，用完释放。
    private sealed class FolderView(IntPtr ptr) : IDisposable
    {
        public IntPtr Ptr { get; private set; } = ptr;
        public void Dispose()
        {
            if (Ptr != IntPtr.Zero) Marshal.Release(Ptr);
            Ptr = IntPtr.Zero;
        }
    }

    /// 指针所在窗口当前显示的文件视图：资源管理器取可见的那个标签页，桌面取桌面视图。
    private static FolderView? View(int x, int y)
    {
        var root = GetAncestor(WindowFromPoint(new RingHook.POINT { X = x, Y = y }), 2);
        var cls = RingHook.ClassOf(root);
        var clsid = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        var iid = new Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"); // IShellWindows
        IntPtr windows;
        if (CoCreateInstance(&clsid, IntPtr.Zero, 4, &iid, &windows) < 0 || windows == IntPtr.Zero) return null; // CLSCTX_LOCAL_SERVER
        try
        {
            if (cls is "Progman" or "WorkerW")
            {
                var loc = new VARIANT { vt = 3 }; // VT_I4 = CSIDL_DESKTOP (0)
                var empty = new VARIANT();
                int hwnd;
                IntPtr disp;
                // IShellWindows::FindWindowSW(loc, locRoot, SWC_DESKTOP, &hwnd, SWFO_NEEDDISPATCH, &disp)
                var hr = ((delegate* unmanaged[Stdcall]<IntPtr, VARIANT*, VARIANT*, int, int*, int, IntPtr*, int>)Slot(windows, 15))(windows, &loc, &empty, 8, &hwnd, 1, &disp);
                if (hr < 0 || disp == IntPtr.Zero) return null;
                var view = ViewOf(disp, out _);
                Marshal.Release(disp);
                return view is { } v ? new FolderView(v) : null;
            }
            if (cls != "CabinetWClass") return null;
            int count;
            // IShellWindows::get_Count
            if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(windows, 7))(windows, &count) < 0) return null;
            IntPtr fallback = IntPtr.Zero;
            for (var i = 0; i < count; i++)
            {
                var index = new VARIANT { vt = 3, value = i };
                IntPtr disp;
                // IShellWindows::Item(VARIANT, IDispatch**)
                if (((delegate* unmanaged[Stdcall]<IntPtr, VARIANT, IntPtr*, int>)Slot(windows, 8))(windows, index, &disp) < 0 || disp == IntPtr.Zero) continue;
                var view = ViewOf(disp, out var viewHwnd);
                Marshal.Release(disp);
                if (view is not { } v) continue;
                if (GetAncestor(viewHwnd, 2) == root)
                {
                    // 多个标签页同属一个窗口：当前标签页的视图是可见的。
                    if (IsWindowVisible(viewHwnd))
                    {
                        if (fallback != IntPtr.Zero) Marshal.Release(fallback);
                        return new FolderView(v);
                    }
                    if (fallback == IntPtr.Zero) { fallback = v; continue; }
                }
                Marshal.Release(v);
            }
            return fallback == IntPtr.Zero ? null : new FolderView(fallback);
        }
        finally
        {
            Marshal.Release(windows);
        }
    }

    /// ShellWindows 里的一项 → IShellBrowser → 当前 IShellView → IFolderView2。
    private static IntPtr? ViewOf(IntPtr disp, out IntPtr viewHwnd)
    {
        viewHwnd = IntPtr.Zero;
        var iidSp = new Guid("6D5140C1-7436-11CE-8034-00AA006009FA");
        IntPtr sp;
        if (Marshal.QueryInterface(disp, in iidSp, out sp) < 0 || sp == IntPtr.Zero) return null;
        try
        {
            var sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
            var iidBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
            IntPtr browser;
            // IServiceProvider::QueryService
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Slot(sp, 3))(sp, &sid, &iidBrowser, &browser) < 0 || browser == IntPtr.Zero) return null;
            try
            {
                IntPtr shellView;
                // IShellBrowser::QueryActiveShellView
                if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(browser, 15))(browser, &shellView) < 0 || shellView == IntPtr.Zero) return null;
                try
                {
                    IntPtr h;
                    // IOleWindow::GetWindow
                    if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(shellView, 3))(shellView, &h) >= 0) viewHwnd = h;
                    var iidView = new Guid("1af3a467-214f-4298-908e-06b03e0b39f9"); // IFolderView2
                    return Marshal.QueryInterface(shellView, in iidView, out var view) >= 0 && view != IntPtr.Zero ? view : null;
                }
                finally
                {
                    Marshal.Release(shellView);
                }
            }
            finally
            {
                Marshal.Release(browser);
            }
        }
        finally
        {
            Marshal.Release(sp);
        }
    }

    /// 选中的项（按视图里的顺序），只要文件系统里的真实路径。
    private static List<string> Selection(IntPtr view)
    {
        var list = new List<string>();
        IntPtr array;
        // IFolderView2::GetSelection(BOOL fNoneImpliesFolder, IShellItemArray**)
        if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(view, 32))(view, 0, &array) < 0 || array == IntPtr.Zero) return list;
        try
        {
            uint count;
            // IShellItemArray::GetCount
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(array, 7))(array, &count) < 0) return list;
            for (uint i = 0; i < count && i < 2000; i++)
            {
                IntPtr item;
                // IShellItemArray::GetItemAt
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(array, 8))(array, i, &item) < 0 || item == IntPtr.Zero) continue;
                if (ItemName(item, SIGDN_FILESYSPATH) is { } path) list.Add(path);
                Marshal.Release(item);
            }
        }
        finally
        {
            Marshal.Release(array);
        }
        return list;
    }

    /// 在整个文件夹里按显示名找。
    private static string? FindByName(IntPtr view, string name)
    {
        int count;
        // IFolderView::ItemCount(SVGIO_ALLVIEW, int*)
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, int*, int>)Slot(view, 7))(view, 2, &count) < 0) return null;
        var iidItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"); // IShellItem
        for (var i = 0; i < count && i < 20000; i++)
        {
            IntPtr item;
            // IFolderView2::GetItem(int, REFIID, void**)
            if (((delegate* unmanaged[Stdcall]<IntPtr, int, Guid*, IntPtr*, int>)Slot(view, 29))(view, i, &iidItem, &item) < 0 || item == IntPtr.Zero) continue;
            try
            {
                if (ItemName(item, SIGDN_NORMALDISPLAY) == name) return ItemName(item, SIGDN_FILESYSPATH);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        return null;
    }

    private const uint SIGDN_NORMALDISPLAY = 0, SIGDN_FILESYSPATH = 0x80058000;

    private static string? ItemName(IntPtr item, uint sigdn)
    {
        IntPtr s;
        // IShellItem::GetDisplayName
        if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(item, 5))(item, sigdn, &s) < 0 || s == IntPtr.Zero) return null;
        var text = Marshal.PtrToStringUni(s);
        Marshal.FreeCoTaskMem(s);
        return text;
    }

    /// 文件在资源管理器里显示的名字（隐藏扩展名时不带扩展名）。
    private static string? DisplayName(string path)
    {
        var iid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, &iid, out var item) < 0 || item == IntPtr.Zero) return null;
        try { return ItemName(item, SIGDN_NORMALDISPLAY); }
        finally { Marshal.Release(item); }
    }

    private static IntPtr Slot(IntPtr obj, int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size);

    // MARK: Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct VARIANT
    {
        public ushort vt, r1, r2, r3;
        public long value;
        public IntPtr extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize, flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    /// INPUT 的联合体按最大的 MOUSEINPUT 留足大小。
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
        [FieldOffset(8)] private MOUSEPAD pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEPAD
    {
        public int dx, dy;
        public uint data, flags, time;
        public IntPtr extra;
    }

    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern int CoCreateInstance(Guid* clsid, IntPtr outer, uint context, Guid* iid, IntPtr* obj);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, Guid* iid, out IntPtr item);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(RingHook.POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, char[] text, int max);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
