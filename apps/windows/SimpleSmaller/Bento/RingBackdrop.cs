using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SimpleSmaller.Bento;

/// 全透明的窗口底：圆环窗口只画扇区，其余地方露出后面的资源管理器。
/// 系统底层用 Windows.UI.Composition 的透明画刷当窗口底（WinUI 自带的只有 Mica / 亚克力）。
public sealed class ClearBackdrop : SystemBackdrop
{
    private static Windows.UI.Composition.Compositor? compositor;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        EnsureSystemDispatcherQueue();
        compositor ??= new Windows.UI.Composition.Compositor();
        target.SystemBackdrop = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        // 让 DWM 按像素透明度合成这个窗口（否则透明处是白底）：开「背后模糊」但模糊区域给一个窗口外的空区域。
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        var rgn = CreateRectRgn(-2, -2, -1, -1);
        var blur = new DwmBlurBehind { dwFlags = 0x1 | 0x2, fEnable = true, hRgnBlur = rgn };
        DwmEnableBlurBehindWindow(hwnd, ref blur);
        DeleteObject(rgn);
        // 窗口类的底色是白的：擦底时改涂黑色，开了背后模糊后黑色（alpha 0）就是透明。
        SetWindowSubclass(hwnd, eraseProc, 1, IntPtr.Zero);
    }

    private static readonly SubclassProc eraseProc = (hwnd, msg, wParam, lParam, id, data) =>
    {
        if (msg == 0x0014) // WM_ERASEBKGND
        {
            if (GetClientRect(hwnd, out var rect)) FillRect(wParam, ref rect, GetStockObject(4)); // BLACK_BRUSH
            return 1;
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    };

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern int FillRect(IntPtr hdc, ref RECT rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)] public bool fTransitionOnMaximized;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blur);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        target.SystemBackdrop = null;
        base.OnTargetDisconnected(target);
    }

    /// Windows.UI.Composition 要当前线程有 Windows.System 的 DispatcherQueue。
    private static void EnsureSystemDispatcherQueue()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new DispatcherQueueOptions { dwSize = Marshal.SizeOf<DispatcherQueueOptions>(), threadType = 2, apartmentType = 2 };
        CreateDispatcherQueueController(options, out _);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize, threadType, apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);
}

/// 一直按「激活」显示的亚克力：结果卡不抢焦点，系统自带的 DesktopAcrylicBackdrop 在窗口没激活时会退成纯色。
public sealed class ActiveAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? controller;
    private SystemBackdropConfiguration? configuration;
    private SystemBackdropTheme theme = SystemBackdropTheme.Light;

    /// 深浅色跟着窗口内容走（内容的 RequestedTheme 由调用方设好后再告诉这里）。
    public ElementTheme Theme
    {
        set
        {
            theme = value == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
            if (configuration is not null) configuration.Theme = theme;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        configuration = new SystemBackdropConfiguration { IsInputActive = true, Theme = theme };
        controller = new DesktopAcrylicController();
        controller.AddSystemBackdropTarget(target);
        controller.SetSystemBackdropConfiguration(configuration);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
        base.OnTargetDisconnected(target);
    }
}
