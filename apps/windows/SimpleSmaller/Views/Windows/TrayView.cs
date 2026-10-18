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

/// 托盘小窗（设计稿 BentoTray，mac 的菜单栏小窗）：352 宽白底，内边距 12、块间距 10。
/// 斜纹拖入区（拖入或 Ctrl+Shift+V 直接按选中的配方处理）→ 横排配方胶囊 → 最近 → 打开主窗口 / 设置…。
/// 点通知区域图标打开，点别处收起；粘贴、退出在通知区域图标的右键菜单里。
/// 窗口外框是 Windows 11 浮窗的系统圆角与投影（设计稿的 26 圆角是 mac 菜单栏弹窗的样子）。
internal static class TrayView
{
    private const double Width = 352;
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
        window = new Window { Title = "简单小一点 · 托盘" };
        root = new Grid();
        FormTheme.On(root, () => root.Background = Palette.Get(root, "BentoCard"));
        body = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Spacing = 10, Padding = new Thickness(12) };
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
        body.Children.Add(RecipeStrip());

        var recent = FormTheme.Text("最近", 11, "BentoSecondary", semibold: true);
        recent.Margin = new Thickness(4, 4, 4, 0);
        AutomationProperties.SetHeadingLevel(recent, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        body.Children.Add(recent);
        if (model.Recent.Count == 0)
        {
            var empty = FormTheme.Text("还没有处理过文件", 12, "BentoSecondary");
            empty.Margin = new Thickness(4, 0, 4, 2);
            body.Children.Add(empty);
        }
        else
        {
            var list = new StackPanel { Spacing = 6 };
            foreach (var job in model.Recent.Take(5)) list.Children.Add(RecentRow(job));
            body.Children.Add(list);
        }

        var buttons = new Grid { ColumnSpacing = 6, Padding = new Thickness(0, 2, 0, 0) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var open = new TextCapsule("打开主窗口", 40, 13, 18, "BentoInk", "BentoOnInk", semibold: true) { HorizontalAlignment = HorizontalAlignment.Stretch };
        open.Click += (_, _) => { Hide(); App.ShowMain(); };
        var settings = new TextCapsule("设置…", 40, 13, 18);
        settings.Click += (_, _) => { Hide(); App.OpenSettings(); };
        Grid.SetColumn(settings, 1);
        buttons.Children.Add(open);
        buttons.Children.Add(settings);
        body.Children.Add(buttons);
    }

    /// 「配方」+ 横排胶囊（选中墨黑底白字，其余灰底），放不下时横向滚动，右边渐隐。
    private static FrameworkElement RecipeStrip()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var label = FormTheme.Text("常用", 12, semibold: true);
        label.Margin = new Thickness(4, 0, 4, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        row.Children.Add(label);
        var chips = new List<(Button Button, Border Back, TextBlock Text, string Id)>();
        void paint()
        {
            foreach (var (b, back, text, id) in chips)
            {
                var on = id == recipeId;
                back.Background = Palette.Get(b, on ? "BentoInk" : "BentoFill");
                text.Foreground = Palette.Get(b, on ? "BentoOnInk" : "BentoPrimary");
                text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                AutomationProperties.SetItemStatus(b, on ? "已选" : "");
            }
        }
        foreach (var r in AppModel.Shared.Store.All)
        {
            var text = new TextBlock { Text = r.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), Child = text };
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back };
            Motion.SetPress(b, true);
            AutomationProperties.SetName(b, $"常用 {r.Name}");
            var id = r.Id;
            b.Click += (_, _) => { recipeId = id; paint(); };
            chips.Add((b, back, text, id));
            row.Children.Add(b);
        }
        var scroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        // 右边 15% 渐隐（设计稿 mask-image: linear-gradient(90deg, #000 85%, transparent)）。
        var fade = new Border { Width = 48, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
        var host = new Grid();
        host.Children.Add(scroll);
        host.Children.Add(fade);
        FormTheme.On(host, () =>
        {
            paint();
            var card = ((SolidColorBrush)Palette.Get(host, "BentoCard")).Color;
            fade.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = Windows.UI.Color.FromArgb(0, card.R, card.G, card.B), Offset = 0 },
                    new GradientStop { Color = card, Offset = 1 },
                },
            };
        });
        // 滚轮横着滚。
        scroll.PointerWheelChanged += (_, e) =>
        {
            var d = e.GetCurrentPoint(scroll).Properties.MouseWheelDelta;
            scroll.ChangeView(scroll.HorizontalOffset - d, null, null, true);
            e.Handled = true;
        };
        return host;
    }

    private static void Hide() => window?.AppWindow.Hide();

    /// 拖入区（设计稿：132 高、圆角 18、2px 浅灰内描边、135° 斜纹底；红色圆底加号、「拖到这里」、一句说明）。
    private static FrameworkElement DropArea()
    {
        var grid = new Grid { Height = 132, AllowDrop = true };
        var back = new Border { CornerRadius = new CornerRadius(18) };
        // 斜纹用一组平行四边形画（GradientSpreadMethod.Repeat 在 WinUI 里画不对），按圆角裁剪。
        var stripes = new Microsoft.UI.Xaml.Shapes.Path { IsHitTestVisible = false };
        var stripeHost = new Grid { Children = { stripes } };
        Views.Common.RoundClip.Apply(stripeHost, 18);
        stripeHost.SizeChanged += (_, e) => stripes.Data = Stripes(e.NewSize.Width, e.NewSize.Height);
        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plus = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Background = Palette.Brand, HorizontalAlignment = HorizontalAlignment.Center, Child = new LineIcon(SubIcons.Plus, 18, 2.4, "OnBrand") };
        var title = FormTheme.Text("拖到这里", 15, semibold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var note = FormTheme.Text("或 Ctrl+Shift+V 处理剪贴板里的图片", 11, "BentoSecondary");
        note.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(plus);
        stack.Children.Add(title);
        stack.Children.Add(note);
        var frame = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), IsHitTestVisible = false };
        grid.Children.Add(back);
        grid.Children.Add(stripeHost);
        grid.Children.Add(frame);
        grid.Children.Add(stack);
        AutomationProperties.SetName(grid, "拖入文件直接处理");
        var targeted = false;
        void paint()
        {
            var dark = grid.ActualTheme == ElementTheme.Dark;
            // 斜纹：两种很浅的灰，各 12px，135°。
            var a = dark ? Windows.UI.Color.FromArgb(255, 0x2A, 0x2B, 0x2F) : Windows.UI.Color.FromArgb(255, 0xFA, 0xFA, 0xFB);
            var b = dark ? Windows.UI.Color.FromArgb(255, 0x26, 0x27, 0x2A) : Windows.UI.Color.FromArgb(255, 0xF4, 0xF5, 0xF7);
            stripes.Fill = new SolidColorBrush(b);
            stripes.Visibility = targeted ? Visibility.Collapsed : Visibility.Visible;
            back.Background = targeted ? FormTheme.BrandTint(0.06) : new SolidColorBrush(a);
            frame.BorderBrush = targeted ? Palette.Brand : new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 0x3A, 0x3B, 0x40) : Windows.UI.Color.FromArgb(255, 0xE2, 0xE4, 0xE8));
        }
        FormTheme.On(grid, paint);
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

    /// 135° 斜纹：每条 12px 宽、间隔 12px（沿 45° 方向量）。
    private static Geometry Stripes(double w, double h)
    {
        var step = 12 * Math.Sqrt(2);
        var sb = new System.Text.StringBuilder();
        string n(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        for (var c = step; c < w + h; c += step * 2)
            sb.Append($"M{n(c)} 0L{n(c + step)} 0L{n(c + step - h)} {n(h)}L{n(c - h)} {n(h)}Z");
        return (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry), sb.ToString());
    }

    /// 最近一条（设计稿：52 高灰底、圆角 14；36 见方圆角 10 的缩略图；名称 13、配方与前后大小 11；右边时间）。
    private static FrameworkElement RecentRow(RecentJob job)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        Motion.SetPress(b, true);
        var back = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 0, 12, 0), Height = 52 };
        var g = new Grid { ColumnSpacing = 10 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var thumb = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var t1 = FormTheme.Text(job.Title, 13);
        var t2 = FormTheme.Text($"{job.Recipe} · {Format.Size(job.Before)} → {Format.Size(job.After)}", 11, "BentoSecondary");
        texts.Children.Add(t1);
        texts.Children.Add(t2);
        var when = FormTheme.Text(Relative(job.Date), 11, "BentoSecondary");
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
            back.Background = Palette.Get(b, hovering ? "BentoLine" : "BentoFill");
            thumb.Background = job.Thumbnail is ImageSource src ? new ImageBrush { ImageSource = src, Stretch = Stretch.UniformToFill } : Palette.Get(b, "BentoCard");
            thumb.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x0F, 0, 0, 0));
        }
        FormTheme.On(b, paint);
        // 缩略图在后台生成，好了再换上。
        void changed(object? _, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(RecentJob.Thumbnail)) paint(); }
        b.Loaded += (_, _) => job.PropertyChanged += changed;
        b.Unloaded += (_, _) => job.PropertyChanged -= changed;
        b.PointerEntered += (_, _) => { hovering = true; paint(); };
        b.PointerExited += (_, _) => { hovering = false; paint(); };
        b.Click += (_, _) => AppModel.Shared.Reveal(job.Outputs);
        ToolTipService.SetToolTip(b, "在资源管理器中显示");
        AutomationProperties.SetName(b, $"{job.Title}，{job.Recipe}，{Format.Size(job.Before)} 到 {Format.Size(job.After)}，{Relative(job.Date)}");
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
        Hud.Show($"正在按「{(recipe?.Name ?? "当前常用")}」处理…");
    }

    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
