using Windows.UI.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Guide;

/// 资源管理器圆环教学弹窗（设计稿 V2.2 · D，Windows 版）：3 步——在什么场景用（资源管理器画面 + 真实圆环）、
/// 三步用法、快捷键与右键菜单。宽 724、圆角 30，页面压暗 rgba(17,18,20,.36)；
/// 底部左边分页点，右边上一步 / 下一步，最后一步「知道了，去试试」；右上 × 关闭，Esc 也关。
/// 点首页「资源管理器圆环」卡片打开（每次点都打开），设置 › 入口 的「再看一遍」也能打开；不自动弹，不做「不再显示」勾选。
internal sealed class RingGuide : Grid
{
    private const double DialogWidth = 724, DialogHeight = 568;

    private readonly Grid dialog = new() { Width = DialogWidth, Height = DialogHeight };
    private readonly Border panel = new() { CornerRadius = new CornerRadius(30) };
    private readonly ColorShadow shadow = new(Windows.UI.Color.FromArgb(0x4D, 0, 0, 0), 80, 30, 30);
    private readonly Grid body = new() { Padding = new Thickness(26, 12, 26, 0) };
    private readonly StackPanel dots = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly List<Border> dotBars = [];
    private readonly List<Button> dotButtons = [];
    private int step;
    private bool closing;
    private Button? primary;

    /// 关掉时回调（×、知道了、Esc 都算看过）。
    public event Action? Closed;

    /// 唤出圆环的键（设置里可以改成 Ctrl + Alt）。
    private static string[] TriggerKeys => AppSettings.Shared.RingCtrlAlt ? ["Ctrl", "Alt"] : ["Alt"];
    private static string TriggerText => string.Join(" + ", TriggerKeys);

    public RingGuide()
    {
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x5C, 0x11, 0x12, 0x14));
        dialog.HorizontalAlignment = HorizontalAlignment.Center;
        dialog.VerticalAlignment = VerticalAlignment.Center;
        dialog.Children.Add(shadow.Host);
        dialog.Children.Add(panel);
        Children.Add(dialog);
        AutomationProperties.SetName(dialog, "资源管理器圆环怎么用");
        AutomationProperties.SetLandmarkType(dialog, AutomationLandmarkType.Custom);
        AutomationProperties.SetLocalizedLandmarkType(dialog, "对话框");
        TabFocusNavigation = KeyboardNavigationMode.Cycle;

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(Header());
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        var footer = Footer();
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        panel.Child = layout;

        SizeChanged += (_, e) => dialog.Height = Math.Min(DialogHeight, Math.Max(420, e.NewSize.Height - 48));
        ActualThemeChanged += (_, _) => { Paint(); Fill(appear: false); };
        Loaded += (_, _) =>
        {
            Paint();
            Fill(appear: false);
            Motion.Appear(dialog);
            var v = Motion.VisualOf(this);
            var fade = v.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(1, 1, Motion.Ease(v.Compositor));
            fade.Duration = TimeSpan.FromMilliseconds(220);
            v.StartAnimation("Opacity", fade);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    private readonly List<Action> painters = [];

    private void Paint()
    {
        panel.Background = Palette.Get(this, "BentoCard");
        foreach (var p in painters) p();
        PaintDots(animated: false);
    }

    // MARK: 头

    private UIElement Header()
    {
        var grid = new Grid { Padding = new Thickness(26, 22, 26, 6), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Palette.Brand, Child = new LogoMark(26, "OnBrand") };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        grid.Children.Add(icon);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { Text = "资源管理器圆环怎么用", FontSize = 15, FontWeight = FontWeights.Bold, LineHeight = 15 * 1.3, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var sub = new TextBlock { Text = "点首页「资源管理器圆环」卡片打开 · 设置 › 入口 也能打开", FontSize = 11, LineHeight = 11 * 1.3, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        texts.Children.Add(title);
        texts.Children.Add(sub);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var x = new TextBlock { Text = "×", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var circle = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = x };
        var close = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = circle, VerticalAlignment = VerticalAlignment.Center };
        Motion.SetPress(close, true);
        AutomationProperties.SetName(close, "关闭");
        ToolTipService.SetToolTip(close, "关闭");
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        painters.Add(() =>
        {
            title.Foreground = Palette.Get(this, "BentoPrimary");
            sub.Foreground = Palette.Get(this, "BentoSecondary");
            circle.Background = Palette.Get(this, "BentoFill");
            x.Foreground = Palette.Get(this, "BentoPrimary");
        });
        return grid;
    }

    // MARK: 底

    private FrameworkElement Footer()
    {
        var grid = new Grid { Padding = new Thickness(26, 16, 26, 22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AutomationProperties.SetName(dots, "步骤");
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var bar = new Border { Height = 8, Width = 8, CornerRadius = new CornerRadius(4) };
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = bar, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(b, $"第 {i + 1} 步");
            b.Click += (_, _) => Go(index);
            dots.Children.Add(b);
            dotBars.Add(bar);
            dotButtons.Add(b);
        }
        grid.Children.Add(dots);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void PaintDots(bool animated)
    {
        for (var i = 0; i < dotBars.Count; i++)
        {
            var on = i == step;
            var bar = dotBars[i];
            bar.Background = on ? Palette.Brand : Palette.Get(this, "BentoLine");
            AutomationProperties.SetItemStatus(dotButtons[i], on ? "已选" : "");
            var to = on ? 24.0 : 8.0;
            if (!animated || !Motion.Enabled || bar.Width == to)
            {
                bar.Width = to;
                continue;
            }
            // 设计稿：宽度 .3 秒带一点回弹。
            var anim = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(300), EnableDependentAnimation = true, EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(anim, bar);
            Storyboard.SetTargetProperty(anim, "Width");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
    }

    private Button FooterButton(string text, string fill, string fg, double padX, bool semibold)
    {
        var label = new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (semibold) label.FontWeight = FontWeights.SemiBold;
        label.Foreground = fg == "OnBrand" ? (Brush)Application.Current.Resources["OnBrand"] : Palette.Get(this, fg);
        var back = new Border { Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(padX, 0, padX, 0), Child = label, Background = fill == "Brand" ? Palette.Brand : Palette.Get(this, fill) };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, text);
        return b;
    }

    // MARK: 翻页

    private void Go(int to)
    {
        if (to < 0 || to > 2 || to == step) return;
        step = to;
        Fill(appear: true);
        PaintDots(animated: true);
    }

    private void Fill(bool appear)
    {
        var content = step switch
        {
            0 => Scene(),
            1 => ThreeSteps(),
            _ => Shortcuts(),
        };
        body.Children.Clear();
        body.Children.Add(content);
        if (appear && content is FrameworkElement fe) fe.Loaded += (_, _) => Motion.Appear(fe);

        actions.Children.Clear();
        if (step > 0)
        {
            var prev = FooterButton("上一步", "BentoFill", "BentoPrimary", 20, false);
            prev.Click += (_, _) => Go(step - 1);
            actions.Children.Add(prev);
        }
        primary = step < 2
            ? FooterButton("下一步", "BentoInk", "BentoOnInk", 26, true)
            : FooterButton("知道了，去试试", "Brand", "OnBrand", 26, true);
        primary.Click += (_, _) => { if (step < 2) Go(step + 1); else Close(); };
        actions.Children.Add(primary);
        PaintDots(animated: false);
        DispatcherQueue.TryEnqueue(() => primary?.Focus(FocusState.Programmatic));
    }

    private TextBlock Text(string text, double size, string key, FontWeight? weight = null, double lineHeight = 0)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = Palette.Get(this, key), TextWrapping = TextWrapping.Wrap };
        if (weight is { } w) t.FontWeight = w;
        if (lineHeight > 0)
        {
            t.LineHeight = size * lineHeight;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }
        return t;
    }

    private Border Chip(string text)
    {
        var t = Text(text, 12, "BentoPrimary");
        t.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Height = 24, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(12), Background = Palette.Get(this, "BentoFill"), Child = t };
    }

    // MARK: 第 1 步：在资源管理器里，选中就能压

    private UIElement Scene()
    {
        var col = new StackPanel { Spacing = 14 };
        col.Children.Add(ExplorerScene.Create(TriggerKeys));
        var texts = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var title = Text("在资源管理器里，选中就能压", 22, "BentoPrimary", FontWeights.Bold);
        title.TextWrapping = TextWrapping.NoWrap;
        title.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        head.Children.Add(title);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in new[] { "发群之前", "传官网之前", "交给客户之前", "整理素材时" }) chips.Children.Add(Chip(c));
        head.Children.Add(chips);
        texts.Children.Add(head);
        texts.Children.Add(Text($"不用打开应用、不用拖来拖去：选中文件按住 {TriggerText} 点一下，选一项就开始，结果存在原文件旁边。", 13, "BentoSoft", null, 1.7));
        col.Children.Add(texts);
        return col;
    }

    // MARK: 第 2 步：三步用法

    private UIElement ThreeSteps()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var col = new StackPanel { Spacing = 18 };
        var title = Text("三步用法", 22, "BentoPrimary", FontWeights.Bold);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        col.Children.Add(title);
        var row = new Grid { ColumnSpacing = 16 };
        (string Title, string Text, Action<Sketch> Art)[] items =
        [
            ($"按住 {TriggerText} 点文件", "多选、整个文件夹都可以；选了几个，圆环上沿就标几个。", s => KeyArt(s, TriggerText)),
            ("内圈选做什么", "压缩、换格式、改尺寸、按用途裁切。带 ● 的是默认项，点内圈直接用它。", InnerArt),
            ("外圈选常用", "点一下就开始；也可以按住不放，划到外圈再松手。", OuterArt),
        ];
        for (var i = 0; i < items.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var item = new StackPanel { Spacing = 10 };
            var sketch = new Sketch(196, 150, dark);
            items[i].Art(sketch);
            item.Children.Add(new Border { Width = 196, Height = 150, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(16), Child = sketch.View(196) });
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var num = new TextBlock { Text = $"{i + 1}", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Palette.Get(this, "BentoOnInk"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Palette.Get(this, "BentoInk"), Child = num };
            AutomationProperties.SetAccessibilityView(badge, AccessibilityView.Raw);
            head.Children.Add(badge);
            var t = Text(items[i].Title, 15, "BentoPrimary", FontWeights.SemiBold);
            t.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(t, $"第 {i + 1} 步，{items[i].Title}");
            head.Children.Add(t);
            item.Children.Add(head);
            item.Children.Add(Text(items[i].Text, 12, "BentoSoft", null, 1.6));
            Grid.SetColumn(item, i);
            row.Children.Add(item);
        }
        col.Children.Add(row);
        col.Children.Add(new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(14),
            Background = Palette.Get(this, "BentoFill"),
            Child = Text("点圆环中心、点圆外或按 Esc 收起。点的是文件夹，会按所选常用处理里面所有图片和视频，结果放在旁边的新文件夹。", 12, "BentoSoft"),
        });
        return col;
    }

    /// 第 1 格插图：键帽 + 一张图 + 指针（设计稿 ⌥，Windows 写 Alt）。
    private static void KeyArt(Sketch s, string key)
    {
        s.Rect(0, 0, 196, 150, 16, "stage");
        s.Rect(98, 38, 70, 50, 6, "#BFA48C");
        s.Rect(106, 94, 54, 12, 4, "brand");
        s.Rect(22, 55, 54, 46, 8, "key");
        s.Rect(22, 52, 54, 46, 8, "card");
        s.Text(49, 75, key, key.Length > 3 ? 13 : 20, "primary", FontWeights.SemiBold);
        s.Text(86, 74, "+", 18, "primary", FontWeights.SemiBold);
        CoachSteps.Pointer(s, "M128 60l0 24l6 -6l5 11l4 -2l-5 -11l8 0z", 1.6);
        s.Circle(128, 60, 12, null, "brand", 2, opacity: 0.6);
    }

    private static string Hair(Sketch s) => s.Dark ? "#1FFFFFFF" : "#0F000000";

    /// 第 2 格插图：内圈八格，WebP 选中（照搬设计稿路径）。
    private static void InnerArt(Sketch s)
    {
        s.Rect(0, 0, 196, 150, 16, "stage");
        (string D, string Fill, double X, double Y, string Label)[] sectors =
        [
            ("M75.2 17.2 A62.1 62.1 0 0 1 120.8 17.2 L107.9 49.9 A27.0 27.0 0 0 0 88.1 49.9 Z", "card", 98, 30.5, "压缩"),
            ("M122.8 18.1 A62.1 62.1 0 0 1 154.9 50.2 L122.8 64.2 A27.0 27.0 0 0 0 108.8 50.2 Z", "brand", 129.5, 43.5, "WebP"),
            ("M155.8 52.2 A62.1 62.1 0 0 1 155.8 97.8 L123.1 84.9 A27.0 27.0 0 0 0 123.1 65.1 Z", "card", 142.6, 75, "AVIF"),
            ("M154.9 99.8 A62.1 62.1 0 0 1 122.8 131.9 L108.8 99.8 A27.0 27.0 0 0 0 122.8 85.8 Z", "card", 129.5, 106.5, "JPG"),
            ("M120.8 132.8 A62.1 62.1 0 0 1 75.2 132.8 L88.1 100.1 A27.0 27.0 0 0 0 107.9 100.1 Z", "card", 98, 119.5, "PNG"),
            ("M73.2 131.9 A62.1 62.1 0 0 1 41.1 99.8 L73.2 85.8 A27.0 27.0 0 0 0 87.2 99.8 Z", "card", 66.5, 106.5, "尺寸"),
            ("M40.2 97.8 A62.1 62.1 0 0 1 40.2 52.2 L72.9 65.1 A27.0 27.0 0 0 0 72.9 84.9 Z", "card", 53.5, 75, "用途"),
            ("M41.1 50.2 A62.1 62.1 0 0 1 73.2 18.1 L87.2 50.2 A27.0 27.0 0 0 0 73.2 64.2 Z", "card", 66.5, 43.5, "自定义"),
        ];
        foreach (var (d, fill, x, y, label) in sectors)
        {
            s.Path(d, fill, Hair(s), 1, round: false);
            s.Text(x, y, label, 11.5, fill == "brand" ? "white" : "primary", FontWeights.SemiBold);
        }
        s.Circle(98, 75, 24.3, null, "brand", 2.2);
    }

    /// 第 3 格插图：小圆环 + 外圈三格（中间一格墨黑）、指针、完成的对勾。
    private static void OuterArt(Sketch s)
    {
        s.Rect(0, 0, 196, 150, 16, "stage");
        string[] inner =
        [
            "M81.1 32.2 A46.0 46.0 0 0 1 114.9 32.2 L105.3 56.4 A20.0 20.0 0 0 0 90.7 56.4 Z",
            "M116.3 32.8 A46.0 46.0 0 0 1 140.2 56.7 L116.3 67 A20.0 20.0 0 0 0 106 56.7 Z",
            "M140.8 58.1 A46.0 46.0 0 0 1 140.8 91.9 L116.6 82.3 A20.0 20.0 0 0 0 116.6 67.7 Z",
            "M140.2 93.3 A46.0 46.0 0 0 1 116.3 117.2 L106 93.3 A20.0 20.0 0 0 0 116.3 83 Z",
            "M114.9 117.8 A46.0 46.0 0 0 1 81.1 117.8 L90.7 93.6 A20.0 20.0 0 0 0 105.3 93.6 Z",
            "M79.7 117.2 A46.0 46.0 0 0 1 55.8 93.3 L79.7 83 A20.0 20.0 0 0 0 90 93.3 Z",
            "M55.2 91.9 A46.0 46.0 0 0 1 55.2 58.1 L79.4 67.7 A20.0 20.0 0 0 0 79.4 82.3 Z",
            "M55.8 56.7 A46.0 46.0 0 0 1 79.7 32.8 L90 56.7 A20.0 20.0 0 0 0 79.7 67 Z",
        ];
        for (var i = 0; i < inner.Length; i++) s.Path(inner[i], i == 1 ? "brand" : "card", Hair(s), 1, round: false);
        s.Path("M104.7 5.3 A70.0 70.0 0 0 1 133.5 14.7 L123.4 31.9 A50.0 50.0 0 0 0 102.8 25.2 Z", "card", Hair(s), 1, round: false);
        s.Path("M135.6 16 A70.0 70.0 0 0 1 157 37.4 L140.2 48.1 A50.0 50.0 0 0 0 124.9 32.8 Z", "ink", Hair(s), 1, round: false);
        s.Path("M158.3 39.5 A70.0 70.0 0 0 1 167.7 68.3 L147.8 70.2 A50.0 50.0 0 0 0 141.1 49.6 Z", "card", Hair(s), 1, round: false);
        s.Circle(98, 75, 18, null, "brand", 1.6);
        CoachSteps.Pointer(s, "M122 34l0 21.6l5.4 -5.4l4.5 9.9l3.6 -1.8l-4.5 -9.9l7.2 0z", 1.4);
        s.Circle(160, 118, 14, "brand");
        s.Path("M153 118l5 5 9-10", null, "white", 2.6);
    }

    // MARK: 第 3 步：快捷键

    private UIElement Shortcuts()
    {
        var col = new StackPanel { Spacing = 14 };
        var title = Text("快捷键", 22, "BentoPrimary", FontWeights.Bold);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        col.Children.Add(title);
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new StackPanel { Spacing = 6 };
        rows.Children.Add(KeyRow([.. TriggerKeys.SelectMany(k => new[] { k, "+" }), "点击"], "唤出圆环"));
        rows.Children.Add(KeyRow(["点内圈"], "用带 ● 的默认项"));
        rows.Children.Add(KeyRow(["按住", "→", "划到外圈松手"], "直接执行这一项"));
        rows.Children.Add(KeyRow(["Esc"], "收起，什么都不做"));
        rows.Children.Add(KeyRow(["Ctrl", "Shift", "V"], "在主窗口里处理剪贴板图片"));
        grid.Children.Add(rows);

        var menu = MenuCard();
        Grid.SetColumn(menu, 1);
        grid.Children.Add(menu);
        col.Children.Add(grid);
        return col;
    }

    /// 一行快捷键：左边键帽（「+」「→」是连接符），右边说明。
    private Border KeyRow(string[] keys, string label)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in keys)
        {
            if (k is "+" or "→")
            {
                var joint = Text(k, 13, "BentoSecondary");
                joint.VerticalAlignment = VerticalAlignment.Center;
                caps.Children.Add(joint);
            }
            else
            {
                caps.Children.Add(KeyCap(k));
            }
        }
        grid.Children.Add(caps);
        var t = Text(label, 13, "BentoPrimary");
        t.TextWrapping = TextWrapping.NoWrap;
        t.HorizontalAlignment = HorizontalAlignment.Right;
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        grid.Children.Add(t);
        var row = new Border { Height = 46, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(14), Background = Palette.Get(this, "BentoFill"), Child = grid };
        AutomationProperties.SetName(row, $"{string.Join(" ", keys)}：{label}");
        return row;
    }

    /// 键帽：白底、圆角 8、最小 30 × 30，底边 2px 阴影 #C9CCD1、内描边 #E2E4E8（设计稿 box-shadow）。
    private Border KeyCap(string label)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var t = Text(label, 13, "BentoPrimary", FontWeights.SemiBold);
        t.TextWrapping = TextWrapping.NoWrap;
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.VerticalAlignment = VerticalAlignment.Center;
        var face = new Border
        {
            MinWidth = 30,
            Height = 30,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(8),
            Background = Palette.Get(this, "BentoCard"),
            BorderBrush = Sketch.BrushOf("keyEdge", dark),
            BorderThickness = new Thickness(1),
            Child = t,
        };
        var cap = new Border { CornerRadius = new CornerRadius(8), Background = Sketch.BrushOf("key", dark), Padding = new Thickness(0, 0, 0, 2), Child = face, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        AutomationProperties.SetAccessibilityView(cap, AccessibilityView.Raw);
        return cap;
    }

    /// 右边那张卡（Windows 版）：mac 是「辅助功能」权限，Windows 不需要权限，换成「右键菜单也能用」，中性浅灰底。
    private Border MenuCard()
    {
        var col = new StackPanel { Spacing = 10 };
        col.Children.Add(Text("右键菜单也能用", 14, "BentoPrimary", FontWeights.SemiBold));
        col.Children.Add(Text($"不按键也行：在资源管理器里右键选中的文件，点「显示更多选项」，再点「{ExplorerMenu.Title}」，文件直接加进主窗口。", 12, "BentoSoft", null, 1.6));

        var inner = new StackPanel { Spacing = 8 };
        inner.Children.Add(Text("资源管理器 › 右键 › 显示更多选项", 10, "BentoSecondary"));
        var item = new Grid { ColumnSpacing = 8 };
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Palette.Brand, Child = new LogoMark(14, "OnBrand") };
        AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
        item.Children.Add(icon);
        var name = Text(ExplorerMenu.Title, 12, "BentoPrimary", FontWeights.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        item.Children.Add(name);
        var on = ExplorerMenu.Enabled;
        var state = Text(on ? "已开启" : "已关闭", 11, "BentoSecondary");
        state.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(state, 2);
        item.Children.Add(state);
        inner.Children.Add(item);
        col.Children.Add(new Border { Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(14), Background = Palette.Get(this, "BentoCard"), Child = inner });
        if (!on) col.Children.Add(Text("在 设置 › 入口 里打开「资源管理器右键」就有这一项。", 11, "BentoSecondary"));
        return new Border { Padding = new Thickness(16), CornerRadius = new CornerRadius(18), Background = Palette.Get(this, "BentoFill"), Child = col };
    }

    // MARK: 关掉

    public void Close()
    {
        if (closing) return;
        closing = true;
        IsHitTestVisible = false;
        var v = Motion.VisualOf(this);
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, Motion.Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(180);
        v.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (Parent is Panel p) p.Children.Remove(this);
        });
        Closed?.Invoke();
    }
}
