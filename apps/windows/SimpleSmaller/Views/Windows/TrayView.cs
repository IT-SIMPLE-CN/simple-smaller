using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Auto;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace SimpleSmaller.Views.Sub;

/// 托盘小窗（设计稿 07，mac 的菜单栏小窗）：拖入或 Ctrl+Shift+V 直接按选中的配方处理；下面是最近的结果；
/// 最底下打开主窗口、设置、退出。点通知区域图标打开，点别处收起。
internal static class TrayView
{
    private const double Width = 300;
    private static Window? window;
    private static Grid? root;
    private static StackPanel? body;
    private static string recipeId = "img.web";
    private static DateTime hiddenAt;
    private static readonly List<QuickRun> runs = [];
    private static bool registered;
    /// 验证用（--window 托盘）：失去焦点也不收起，方便截图。
    public static bool Pinned { get; set; }

    public static void Toggle()
    {
        if (window is not null && window.AppWindow.IsVisible)
        {
            window.AppWindow.Hide();
            return;
        }
        // 点图标时小窗先失去焦点被收起，紧接着又收到点按：刚收起的不再打开。
        if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
        Show();
    }

    public static void Show()
    {
        Ensure();
        Build();
        ((FrameworkElement)window!.Content).RequestedTheme = SubWindow.MainTheme();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        GetCursorPos(out var cursor);
        area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        anchorX = cursor.X;
        // 贴着任务栏一角（通常右下），像系统的托盘弹窗。
        anchorBottom = cursor.Y > area.Y + area.Height / 2;
        Fit(body!.ActualHeight > 0 ? body.ActualHeight : 420);
        window!.AppWindow.Show();
        window.Activate();
        SetForegroundWindow(hwnd);
        Motion.Appear(root);
    }

    private static RectInt32 area;
    private static int anchorX;
    private static bool anchorBottom;

    /// 窗口高度跟着内容走（最近记录多少不一样），贴底时底边不动。
    private static void Fit(double height)
    {
        if (window is null || height <= 0) return;
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
        var w = (int)(Width * scale);
        // 窗口外框占掉几像素，多留一点免得最后一行贴边。
        var h = (int)Math.Ceiling((height + 4) * scale);
        var x = Math.Clamp(anchorX - w / 2, area.X + 12, area.X + area.Width - w - 12);
        var y = anchorBottom ? area.Y + area.Height - h - 12 : area.Y + 12;
        window.AppWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }

    private static void Ensure()
    {
        if (window is not null) return;
        window = new Window { Title = "简单小一点 · 托盘", SystemBackdrop = new DesktopAcrylicBackdrop() };
        root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        body.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5) Fit(e.NewSize.Height); };
        root.Children.Add(body);
        window.Content = new UserControl { FontFamily = (FontFamily)Application.Current.Resources["BentoFont"], Content = root };
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        window.AppWindow.IsShownInSwitchers = false;
        window.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && window.AppWindow.IsVisible && !Pinned)
            {
                window.AppWindow.Hide();
                hiddenAt = DateTime.Now;
            }
        };
        // 关闭按钮（Alt+F4）只是收起。
        window.AppWindow.Closing += (s, e) => { e.Cancel = true; s.Hide(); };
        var paste = new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift };
        paste.Invoked += (_, e) => { e.Handled = true; PasteClipboard(); };
        var esc = new KeyboardAccelerator { Key = VirtualKey.Escape };
        esc.Invoked += (_, e) => { e.Handled = true; window.AppWindow.Hide(); };
        root.KeyboardAccelerators.Add(paste);
        root.KeyboardAccelerators.Add(esc);
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        AppModel.Shared.Recent.CollectionChanged += (_, _) => { if (window.AppWindow.IsVisible) Build(); };
    }

    private static void Build()
    {
        var model = AppModel.Shared;
        body!.Children.Clear();
        body.Children.Add(DropArea());

        // 配方。
        var recipeRow = new Grid { Padding = new Thickness(14, 0, 14, 10) };
        recipeRow.ColumnDefinitions.Add(new ColumnDefinition());
        recipeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Themed.Text("配方", 12, "BentoSecondary");
        label.VerticalAlignment = VerticalAlignment.Center;
        var picker = RecipePicker.Create(recipeId, r => recipeId = r.Id);
        picker.MaxWidth = 170;
        Grid.SetColumn(picker, 1);
        recipeRow.Children.Add(label);
        recipeRow.Children.Add(picker);
        body.Children.Add(recipeRow);
        body.Children.Add(Divider());

        var recent = Themed.Text("最近", 11, "BentoSecondary", semibold: true);
        recent.Margin = new Thickness(14, 10, 14, 4);
        AutomationProperties.SetHeadingLevel(recent, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        body.Children.Add(recent);
        if (model.Recent.Count == 0)
        {
            var empty = Themed.Text("还没有处理过文件", 12, "BentoSecondary");
            empty.Opacity = 0.75;
            empty.Margin = new Thickness(14, 10, 14, 10);
            body.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel { Padding = new Thickness(6, 0, 6, 0) };
            foreach (var job in model.Recent.Take(5)) list.Children.Add(RecentRow(job));
            body.Children.Add(list);
        }
        var d = Divider();
        d.Margin = new Thickness(0, 6, 0, 0);
        body.Children.Add(d);
        var menu = new StackPanel { Padding = new Thickness(6) };
        menu.Children.Add(MenuRow("粘贴剪贴板", "Ctrl+Shift+V", PasteClipboard));
        menu.Children.Add(MenuRow("打开主窗口", null, () => { Hide(); App.ShowMain(); }));
        menu.Children.Add(MenuRow("设置…", null, () => { Hide(); App.OpenSettings(); }));
        menu.Children.Add(MenuRow("退出", null, () => { Hide(); App.Quit(); }));
        body.Children.Add(menu);
    }

    private static void Hide() => window?.AppWindow.Hide();

    private static Border Divider()
    {
        var b = new Border { Height = 1 };
        Themed.On(b, () => b.Background = Palette.Get(b, "BentoLine"));
        return b;
    }

    private static FrameworkElement DropArea()
    {
        var grid = new Grid { Height = 104, Margin = new Thickness(12), AllowDrop = true };
        var frame = new Rectangle { RadiusX = 10, RadiusY = 10, StrokeThickness = 1.2, StrokeDashArray = [4, 3.3] };
        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var icon = new FontIcon { Glyph = "", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        var title = Themed.Text("拖到这里", 13, semibold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var note = Themed.Text("或 Ctrl+Shift+V 处理剪贴板里的图片", 11, "BentoSecondary");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(icon);
        stack.Children.Add(title);
        stack.Children.Add(note);
        grid.Children.Add(frame);
        grid.Children.Add(stack);
        AutomationProperties.SetName(grid, "拖入文件直接处理");
        var targeted = false;
        void paint()
        {
            var secondary = Palette.Get(grid, "BentoSecondary");
            icon.Foreground = targeted ? Palette.Brand : secondary;
            frame.Stroke = targeted ? Palette.Brand : secondary;
            frame.Opacity = targeted ? 1 : 0.35;
            frame.Fill = targeted ? Themed.BrandTint(0.06) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        Themed.On(grid, paint);
        grid.DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "直接处理";
            if (!targeted) { targeted = true; paint(); }
        };
        grid.DragLeave += (_, _) => { targeted = false; paint(); };
        grid.Drop += async (_, e) =>
        {
            targeted = false;
            paint();
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            Process(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
        };
        return grid;
    }

    private static FrameworkElement RecentRow(RecentJob job)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        var back = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 8, 0), Height = 42 };
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var thumb = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center };
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var t1 = Themed.Text(job.Title, 12);
        t1.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        var t2 = Themed.Text($"{job.Recipe} · {Format.Size(job.Before)} → {Format.Size(job.After)}", 11, "BentoSecondary");
        texts.Children.Add(t1);
        texts.Children.Add(t2);
        var when = Themed.Text(Relative(job.Date), 11, "BentoSecondary");
        when.Opacity = 0.75;
        when.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(when, 2);
        g.Children.Add(thumb);
        g.Children.Add(texts);
        g.Children.Add(when);
        back.Child = g;
        b.Content = back;
        var hovering = false;
        void paint()
        {
            back.Background = hovering ? Palette.Get(b, "BentoFill") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            thumb.Background = job.Thumbnail is ImageSource src ? new ImageBrush { ImageSource = src, Stretch = Stretch.UniformToFill } : Palette.Get(b, "BentoFill");
        }
        Themed.On(b, paint);
        b.PointerEntered += (_, _) => { hovering = true; paint(); };
        b.PointerExited += (_, _) => { hovering = false; paint(); };
        b.Click += (_, _) => AppModel.Shared.Reveal(job.Outputs);
        ToolTipService.SetToolTip(b, "在资源管理器中显示");
        AutomationProperties.SetName(b, $"{job.Title}，{job.Recipe}，{Format.Size(job.Before)} 到 {Format.Size(job.After)}，{Relative(job.Date)}");
        return b;
    }

    private static FrameworkElement MenuRow(string title, string? shortcut, Action action)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        var back = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 0, 8, 0), Height = 30 };
        var g = new Grid();
        var t = Themed.Text(title, 13);
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        if (shortcut is not null)
        {
            var k = Themed.Text(shortcut, 11, "BentoSecondary");
            k.HorizontalAlignment = HorizontalAlignment.Right;
            k.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(k);
        }
        back.Child = g;
        b.Content = back;
        var hovering = false;
        void paint() => back.Background = hovering ? Palette.Get(b, "BentoFill") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Themed.On(b, paint);
        b.PointerEntered += (_, _) => { hovering = true; paint(); };
        b.PointerExited += (_, _) => { hovering = false; paint(); };
        b.Click += (_, _) => action();
        AutomationProperties.SetName(b, title);
        return b;
    }

    public static string Relative(DateTime d)
    {
        var s = (DateTime.Now - d).TotalSeconds;
        if (s < 60) return "刚刚";
        if (s < 3600) return $"{(int)(s / 60)} 分钟前";
        if (d.Date == DateTime.Today) return $"{(int)(s / 3600)} 小时前";
        if (d.Date == DateTime.Today.AddDays(-1)) return "昨天";
        return $"{d.Month} 月 {d.Day} 日";
    }

    /// 剪贴板里的文件或图片按选中的配方直接处理。
    public static async void PasteClipboard()
    {
        if (await AppModel.Shared.ClipboardFilesAsync() is not { } paths)
        {
            Hud.Show("剪贴板里没有图片");
            return;
        }
        Process(paths);
    }

    /// 选中配方那一类的文件用它，其他类型用各自当前的配方。
    private static void Process(IReadOnlyList<string> paths)
    {
        var model = AppModel.Shared;
        if (!registered)
        {
            registered = true;
            model.OtherBusy.Add(() => runs.Any(r => !r.IsFinished));
        }
        var recipe = model.Store.RecipeById(recipeId);
        var byType = new Dictionary<MediaType, List<string>>();
        foreach (var p in paths)
        {
            MediaType? t = Directory.Exists(p) ? MediaType.Image : MediaTypes.Of(p);
            if (t is { } type) (byType.TryGetValue(type, out var l) ? l : byType[type] = []).Add(p);
        }
        if (byType.Count == 0)
        {
            Hud.Show("这些文件处理不了");
            return;
        }
        foreach (var (type, list) in byType)
        {
            var r = recipe?.Type == type ? recipe : model.Workspace(type).Draft;
            var run = new QuickRun(r, list, type);
            run.OnFinished = () =>
            {
                model.EndActivityIfIdle();
                Hud.Show(run.Phase.Kind == QuickRunPhaseKind.Failed
                    ? $"{run.Title} 没有处理成功：{run.Phase.Message}"
                    : $"{run.Title} {Format.Size(run.Before)} → {Format.Size(run.After)}");
            };
            runs.RemoveAll(x => x.IsFinished);
            runs.Add(run);
            model.BeginActivity();
            run.Start();
        }
        Hud.Show($"正在按「{(recipe?.Name ?? "当前配方")}」处理…");
    }

    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
