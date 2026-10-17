using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Graphics;

namespace SimpleSmaller.Ring;

/// 资源管理器圆环的总调度（对应 mac 的 RingController）：
/// 按住 Alt 单击资源管理器或桌面上的图片 / 视频 / PDF → 在点击处弹圆环 → 选中即直接处理 → 指针旁给结果卡。
///
/// 只监听、不拦截：资源管理器照样收到这次点击（Alt 单击在资源管理器里就是普通单击）。
/// Alt 拖动（超过 4 像素）不弹；Alt+双击照样打开属性：圆环中间镂空处不属于圆环窗口，第二下直接落到资源管理器上。
///
/// 圆环窗口：WinUI 窗口 + 全透明底（ClearBackdrop）+ 圆环形状的窗口区域。选它而不是自绘的分层窗口，
/// 是为了和其他界面共用 XAML 文字排版、色板与 Composition 动效；窗口区域让镂空处和外圈以外的点击穿透到资源管理器。
/// 不抢焦点（WS_EX_NOACTIVATE）：资源管理器一直在前台，选中状态不变。
internal static class RingController
{
    private static Window? ringWindow;
    private static Window? cardWindow;
    private static DispatcherQueueTimer? cardTimer;
    private static readonly List<QuickRun> runs = [];
    private static bool registered;
    private static int shownSequence;

    /// 按开关装卸钩子。
    public static void Refresh()
    {
        if (AppSettings.Shared.RingEnabled)
        {
            if (RingHook.Shared.Running) return;
            var hook = RingHook.Shared;
            hook.Pressed = (x, y, seq) => FileHit.Post(() => FileHit.Remember(x, y, seq));
            hook.Released = (x, y, seq) => FileHit.Post(() =>
            {
                var hit = FileHit.At(x, y, seq);
                Log.Info(hit is null ? "圆环：不是支持的文件" : $"圆环：{hit.File}，共 {hit.Batch.Count} 个");
                if (hit is not null) Ui.Post(() => Open(hit, x, y, seq));
            });
            hook.Dismiss = () => Ui.Post(Dismiss);
            hook.Start();
            Log.Info("圆环：已开启");
        }
        else
        {
            RingHook.Shared.Stop();
            Dismiss();
            Log.Info("圆环：已关闭");
        }
    }

    // MARK: 圆环

    private static void Open(FileHit.Hit hit, int x, int y, int sequence)
    {
        // 识别期间又按下了（Alt+双击的第二下、又点了别处）：这一次不弹。
        if (sequence != RingHook.Shared.Sequence) return;
        Dismiss();
        shownSequence = sequence;
        var type = hit.Type;
        var urls = hit.Batch;
        var unit = type == MediaType.Image ? "张" : type == MediaType.Video ? "段" : "个";
        var badge = urls.Count > 1 ? $"{urls.Count} {unit}" : "";
        var slots = RingMenu.Slots(type, AppModel.Shared.Store);

        EnsureRing();
        var window = ringWindow!;
        var view = new RingView(slots, badge, c => Choose(c, urls, type, x, y), Dismiss);
        var host = (UserControl)window.Content;
        host.Content = view;
        host.RequestedTheme = SubWindow.MainTheme();

        var scale = ScaleAt(x, y);
        var size = (int)Math.Ceiling(RingView.Size * scale);
        // 圆心落在点击处；贴近屏幕边缘时整体挪进可见区域。
        var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).WorkArea;
        var left = Math.Clamp(x - size / 2, area.X, Math.Max(area.X, area.X + area.Width - size));
        var top = Math.Clamp(y - size / 2, area.Y, Math.Max(area.Y, area.Y + area.Height - size));
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        window.AppWindow.MoveAndResize(new RectInt32(left, top, size, size));
        // 窗口区域：只留圆环本身。镂空处（略小于画出来的镂空圈，留出「4 张」小标）与外圈以外都穿透。
        var cx = size / 2;
        var holeR = (int)Math.Round((RingView.Hole - 12) * scale);
        // 外沿放到窗口边：外圈的投影（向下 10、模糊 24）也要画得出来。
        var outerR = size / 2;
        var outerRgn = CreateEllipticRgn(cx - outerR, cx - outerR, cx + outerR + 1, cx + outerR + 1);
        var holeRgn = CreateEllipticRgn(cx - holeR, cx - holeR, cx + holeR + 1, cx + holeR + 1);
        CombineRgn(outerRgn, outerRgn, holeRgn, 4); // RGN_DIFF
        DeleteObject(holeRgn);
        SetWindowRgn(hwnd, outerRgn, true); // 系统接管区域句柄

        Motion.VisualOf(host).Opacity = 1;
        window.AppWindow.Show(false);
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040); // HWND_TOPMOST，不激活
        ringOpen = true;
        view.Reveal();
        RingHook.Shared.RingShown(left + cx, top + cx, holeR, outerR);

        // 对已选中的文件 Alt+单击，资源管理器过一会儿会进入改名；弹出后查两次，正在改的是这个文件才取消。
        foreach (var delay in new[] { 700, 1300 })
        {
            _ = Task.Delay(delay).ContinueWith(_ => Ui.Post(() =>
            {
                if (shownSequence != sequence || !ringOpen) return;
                FileHit.Post(() => FileHit.CancelRename(x, y, hit.File));
            }));
        }
    }

    public static void Dismiss()
    {
        RingHook.Shared.RingHidden();
        if (!ringOpen) return;
        ringOpen = false;
        Log.Info("圆环：收起");
        if (ringWindow is not { } window || !window.AppWindow.IsVisible) return;
        var host = (UIElement)window.Content;
        if (!Motion.Enabled)
        {
            window.AppWindow.Hide();
            return;
        }
        var visual = Motion.VisualOf(host);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Opacity", fade);
        hideTimer ??= CreateHideTimer();
        hideTimer.Stop();
        hideTimer.Start();
    }

    private static DispatcherQueueTimer? hideTimer;

    /// 淡出结束后藏起窗口（收起期间又弹了新的圆环就别藏）。
    private static DispatcherQueueTimer CreateHideTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(160);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => { if (!ringOpen) ringWindow?.AppWindow.Hide(); };
        return timer;
    }

    private static bool ringOpen;

#if DEBUG
    /// 只看样子（--window 结果卡:<文件>）：不经过资源管理器，按第一个配方直接处理，在屏幕中间出结果卡。
    internal static void Preview(string path)
    {
        if (MediaTypes.Of(path) is not { } type || AppModel.Shared.Store.All.FirstOrDefault(r => r.Type == type) is not { } r) return;
        var area = DisplayArea.Primary.WorkArea;
        try { Choose(new RingChoice(r.Name, r.Summary, r), [path], type, area.X + area.Width / 3, area.Y + area.Height / 3); }
        catch (Exception e) { CrashLog.Write("结果卡预览", e); }
    }
#endif

    private static void Choose(RingChoice choice, IReadOnlyList<string> urls, MediaType type, int x, int y)
    {
        Dismiss();
        var model = AppModel.Shared;
        if (choice.Recipe is not { } recipe)
        {
            model.SelectedType = type;
            model.Add(urls, type);
            App.ShowMain();
            return;
        }
        var run = new QuickRun(recipe, urls, type);
        Track(run, x, y);
        run.Start();
    }

    /// 给一个直接处理的任务配上结果卡。
    private static void Track(QuickRun run, int x, int y)
    {
        var model = AppModel.Shared;
        if (!registered)
        {
            registered = true;
            model.OtherBusy.Add(() => runs.Any(r => !r.IsFinished));
        }
        runs.RemoveAll(r => r.IsFinished);
        runs.Add(run);
        ShowCard(run, x, y);
        model.BeginActivity();
        run.OnFinished = () =>
        {
            ScheduleCardClose();
            model.NoteFinishedInBackground(run.Outputs.Count == 0 ? 0 : run.Paths.Count);
            model.EndActivityIfIdle();
        };
    }

    // MARK: 结果卡

    private static void ShowCard(QuickRun run, int x, int y)
    {
        CloseCard();
        EnsureCard();
        var window = cardWindow!;
        var host = (UserControl)window.Content;
        host.RequestedTheme = SubWindow.MainTheme();
        if (window.SystemBackdrop is ActiveAcrylicBackdrop acrylic) acrylic.Theme = host.ActualTheme;
        var card = new ResultCard(run, CloseCard);
        // 放在 Canvas 里不受窗口高度限制，内容变高时 SizeChanged 才拿得到真实高度，再把窗口撑高。
        host.Content = new Canvas { Children = { card } };
        var scale = ScaleAt(x, y);
        var area = DisplayArea.GetFromPoint(new PointInt32(x, y), DisplayAreaFallback.Nearest).WorkArea;

        void fit()
        {
            card.Measure(new Windows.Foundation.Size(ResultCard.CardWidth, double.PositiveInfinity));
            var w = (int)Math.Ceiling(ResultCard.CardWidth * scale);
            var h = (int)Math.Ceiling(Math.Max(60, card.DesiredSize.Height) * scale);
            // 卡片左上角在指针右侧一点、与指针齐平；贴边时挪进屏幕。
            var left = Math.Clamp(x + (int)(32 * scale), area.X, Math.Max(area.X, area.X + area.Width - w));
            var top = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));
            // 按客户区定大小：带边框的窗口外框比客户区大几像素。
            window.AppWindow.Move(new PointInt32(left, top));
            window.AppWindow.ResizeClient(new SizeInt32(w, h));
            // 有边框没标题栏时 ResizeClient 会按留着标题栏算，多出一截：按实际客户区再校正一次。
            var client = window.AppWindow.ClientSize;
            if (client.Width != w || client.Height != h)
                window.AppWindow.ResizeClient(new SizeInt32(w - (client.Width - w), h - (client.Height - h)));
        }
        fit();
        card.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5) fit(); };
        card.PointerEntered += (_, _) => cardTimer?.Stop();
        card.PointerExited += (_, _) => { if (run.IsFinished) ScheduleCardClose(); };
        window.AppWindow.Show(false);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
        Motion.Appear(card);
    }

    private static void ScheduleCardClose()
    {
        if (cardTimer is null) return;
        cardTimer.Stop();
        cardTimer.Start();
    }

    public static void CloseCard()
    {
        cardTimer?.Stop();
        if (cardWindow is { } w && w.AppWindow.IsVisible)
        {
            w.AppWindow.Hide();
            ((UserControl)w.Content).Content = null;
        }
        runs.RemoveAll(r => r.IsFinished);
    }

    // MARK: 窗口

    private static void EnsureRing()
    {
        if (ringWindow is not null) return;
        var window = new Window { Title = "简单小一点 圆环", SystemBackdrop = new ClearBackdrop() };
        window.Content = new UserControl { FontFamily = (FontFamily)Application.Current.Resources["BentoFont"] };
        Popup(window, rounded: false);
        ringWindow = window;
    }

    private static void EnsureCard()
    {
        if (cardWindow is not null) return;
        var window = new Window { Title = "简单小一点 结果", SystemBackdrop = new ActiveAcrylicBackdrop() };
        window.Content = new UserControl { FontFamily = (FontFamily)Application.Current.Resources["BentoFont"] };
        Popup(window, rounded: true);
        cardWindow = window;
        cardTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        cardTimer.Interval = TimeSpan.FromSeconds(6);
        cardTimer.IsRepeating = false;
        cardTimer.Tick += (_, _) => CloseCard();
    }

    /// 无边框、置顶、不抢焦点、不进 Alt+Tab 与任务栏的浮窗。
    private static void Popup(Window window, bool rounded)
    {
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(rounded, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        window.AppWindow.IsShownInSwitchers = false;
        // Alt+F4 只是收起。
        window.AppWindow.Closing += (s, e) => { e.Cancel = true; s.Hide(); };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SetWindowLongPtr(hwnd, -20, (IntPtr)(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000 | 0x80)); // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        var corner = rounded ? 2 : 1; // DWMWCP_ROUND / DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        if (!rounded)
        {
            var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE：不画窗口描边
            DwmSetWindowAttribute(hwnd, 34, ref none, sizeof(int));
        }
    }

    /// 指针所在显示器的缩放比例。
    private static double ScaleAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new RingHook.POINT { X = x, Y = y }, 2);
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(RingHook.POINT p, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
