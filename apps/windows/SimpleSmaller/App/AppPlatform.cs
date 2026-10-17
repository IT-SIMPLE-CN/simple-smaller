using System.Runtime.InteropServices;

namespace SimpleSmaller;

/// 界面层对 IPlatform 的补充：前台判断、任务栏角标、缩略图。
internal sealed class AppPlatform : WindowsPlatform
{
    // MARK: 前台与任务栏（完成页 / 通知 agent）

    /// 前台窗口属于本进程（主窗口、设置、托盘小窗都算）。
    public override bool IsAppActive
    {
        get
        {
            var fg = Taskbar.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            Taskbar.GetWindowThreadProcessId(fg, out var pid);
            return pid == (uint)Environment.ProcessId;
        }
    }

    /// 任务栏按钮：「3/9」显示成进度条，「2」显示成右下角红色数字角标，null 都清掉。
    public override void SetBadge(string? text) => Taskbar.Set(App.MainHwnd, text);

    /// ITaskbarList3：进度条与角标（覆盖图标）。只在界面线程调用。
    private static class Taskbar
    {
        private static TaskbarList? list;
        private static IntPtr overlay;
        private static string? lastText;

        public static void Set(IntPtr hwnd, string? text)
        {
            if (hwnd == IntPtr.Zero || text == lastText) return;
            try
            {
                if (list is null)
                {
                    list = TaskbarList.Create();
                    list.HrInit();
                }
                var slash = text?.IndexOf('/') ?? -1;
                if (text is not null && slash > 0 && ulong.TryParse(text[..slash], out var done) && ulong.TryParse(text[(slash + 1)..], out var total) && total > 0)
                {
                    // 还没做完一个时进度条是空的，看不出在跑：先显示成走马灯。
                    if (done == 0)
                    {
                        list.SetProgressState(hwnd, 1 /* TBPF_INDETERMINATE */);
                    }
                    else
                    {
                        list.SetProgressState(hwnd, 2 /* TBPF_NORMAL */);
                        list.SetProgressValue(hwnd, done, total);
                    }
                    SetOverlay(hwnd, null);
                }
                else
                {
                    list.SetProgressState(hwnd, 0 /* TBPF_NOPROGRESS */);
                    SetOverlay(hwnd, text);
                }
                // 成功了才记下；任务栏按钮还没建好时调用会失败，下次同样的内容还要再试。
                lastText = text;
            }
            catch (Exception e)
            {
                Log.Error($"任务栏角标：{e.Message}");
            }
        }

        private static void SetOverlay(IntPtr hwnd, string? text)
        {
            var icon = string.IsNullOrEmpty(text) ? IntPtr.Zero : BadgeIcon(text.Length > 2 ? "99+" : text);
            list!.SetOverlayIcon(hwnd, icon, text is null ? null : $"{text} 个完成");
            if (overlay != IntPtr.Zero) DestroyIcon(overlay);
            overlay = icon;
        }

        /// 画一个品牌红圆点，中间白色数字（GDI 写字取覆盖率，圆形自己抗锯齿）。
        private static IntPtr BadgeIcon(string text)
        {
            var size = Math.Max(16, GetSystemMetrics(49 /* SM_CXSMICON */));
            var dc = CreateCompatibleDC(IntPtr.Zero);
            var textBmp = CreateDib(dc, size, out var textBits);
            var old = SelectObject(dc, textBmp);
            var font = CreateFont(-(int)(size * (text.Length > 1 ? 0.58 : 0.72)), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4 /* ANTIALIASED */, 0, "Segoe UI");
            var oldFont = SelectObject(dc, font);
            SetBkMode(dc, 1);
            SetTextColor(dc, 0x00FFFFFF);
            var rect = new RECT { Left = 0, Top = 0, Right = size, Bottom = size };
            DrawText(dc, text, -1, ref rect, 0x1 | 0x4 | 0x20 /* CENTER | VCENTER | SINGLELINE */);
            GdiFlush();
            var color = CreateDib(dc, size, out var colorBits);
            var pixels = new int[size * size];
            var textPx = new int[size * size];
            Marshal.Copy(textBits, textPx, 0, textPx.Length);
            var r = size / 2.0;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var d = Math.Sqrt(Math.Pow(x + 0.5 - r, 2) + Math.Pow(y + 0.5 - r, 2));
                    var a = Math.Clamp(r - d, 0, 1);
                    var t = (textPx[y * size + x] & 0xFF) / 255.0;
                    var cr = 0xEF + (255 - 0xEF) * t;
                    var cg = 0x39 + (255 - 0x39) * t;
                    var cb = 0x22 + (255 - 0x22) * t;
                    pixels[y * size + x] = ((int)(a * 255) << 24) | ((int)cr << 16) | ((int)cg << 8) | (int)cb;
                }
            }
            Marshal.Copy(pixels, 0, colorBits, pixels.Length);
            var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            var icon = CreateIconIndirect(ref info);
            SelectObject(dc, oldFont);
            SelectObject(dc, old);
            DeleteObject(font);
            DeleteObject(textBmp);
            DeleteObject(color);
            DeleteObject(mask);
            DeleteDC(dc);
            return icon;
        }

        private static IntPtr CreateDib(IntPtr dc, int size, out IntPtr bits)
        {
            var bi = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
            return CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
        }

        /// ITaskbarList3 直接按虚表调用，不经 .NET 的 COM 封装。
        private sealed unsafe class TaskbarList
        {
            private readonly IntPtr ptr;
            private TaskbarList(IntPtr p) => ptr = p;

            public static TaskbarList Create()
            {
                var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
                // 按 ITaskbarList4 取（它继承 ITaskbarList3，虚表前段相同）：实测这台系统上直接要 ITaskbarList3 返回 E_NOINTERFACE。
                var iid = new Guid("c43dc798-95d1-4bea-9030-bb99e2983a1a");
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, ref iid, out var p));
                return new TaskbarList(p);
            }

            private IntPtr Slot(int i) => (*(IntPtr**)ptr)[i];

            public void HrInit() => Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(3))(ptr));
            public void SetProgressValue(IntPtr hwnd, ulong done, ulong total) =>
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, ulong, ulong, int>)Slot(9))(ptr, hwnd, done, total));
            public void SetProgressState(IntPtr hwnd, int flags) =>
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int>)Slot(10))(ptr, hwnd, flags));
            public void SetOverlayIcon(IntPtr hwnd, IntPtr icon, string? description)
            {
                var d = description is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUni(description);
                try { Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr, int>)Slot(18))(ptr, hwnd, icon, d)); }
                finally { if (d != IntPtr.Zero) Marshal.FreeCoTaskMem(d); }
            }

            [DllImport("ole32.dll")]
            private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr obj);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth, biHeight;
            public ushort biPlanes, biBitCount;
            public uint biCompression, biSizeImage;
            public int biXPelsPerMeter, biYPelsPerMeter;
            public uint biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
            public int xHotspot, yHotspot;
            public IntPtr hbmMask, hbmColor;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref ICONINFO info);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(IntPtr dc, string text, int count, ref RECT rect, uint format);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateBitmap(int w, int h, uint planes, uint bits, IntPtr data);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    }
}
