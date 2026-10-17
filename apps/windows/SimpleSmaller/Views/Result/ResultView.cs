using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Result;

/// 完成页（设计稿 V2.1 · 04）：一张大卡。半圆刻度盘看这一批小了多少，右边「原来 → 现在」；
/// 下方逐个文件，有问题的排最前，收起时也看得到。出路：在资源管理器中显示、重试失败的、改设置再做一次、再来一批。
internal sealed class ResultView : Reactive
{
    private readonly Workspace ws;
    private readonly AppModel model = AppModel.Shared;
    private readonly Grid layout = new() { ColumnSpacing = 36 };
    private readonly Grid left = new();
    private readonly ContentPresenter panelHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Gauge gauge;
    private readonly Canvas gaugeLayer = new();
    private readonly Border gaugeFrame = new() { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel header = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock subtitle = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly Border badge = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly LineIcon badgeCheck = new(ResultIcons.Check, 20, 2.4, "OnBrand");
    private readonly LineIcon badgeCross = new(ResultIcons.Cross, 20, 2.4, "OnBrand");
    private readonly ResultList list;
    private bool open;

    public ResultView(Workspace ws)
    {
        this.ws = ws;
        gauge = new Gauge(Saved);
        gaugeFrame.Child = gauge;

        var badgeIcons = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        badgeIcons.Children.Add(badgeCheck);
        badgeIcons.Children.Add(badgeCross);
        badge.Child = badgeIcons;
        AutomationProperties.SetAccessibilityView(badge, AccessibilityView.Raw);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        header.Children.Add(badge);
        header.Children.Add(texts);
        AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level1);

        list = new ResultList(ws, Rows, ListSummary, () => SetOpen(!open)) { VerticalAlignment = VerticalAlignment.Bottom };

        // 刻度盘比左栏宽（伸进左边距和栏间距），放在不裁剪的画布上。
        gaugeLayer.Children.Add(gaugeFrame);
        left.Children.Add(gaugeLayer);
        left.Children.Add(header);
        left.Children.Add(list);
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(336) });
        layout.Children.Add(left);
        Grid.SetColumn(panelHost, 1);
        layout.Children.Add(panelHost);

        var card = Card.Create(layout, new Thickness(28, 26, 28, 24));
        card.CornerRadius = new CornerRadius(26);
        Content = card;
        AutomationProperties.SetName(this, "完成");

        layout.SizeChanged += (_, _) => Size(animated: false);
        Watch(ws, nameof(Workspace.ShowResult), nameof(Workspace.FinishedCount));
        Watch(model.Settings, nameof(AppSettings.OutputLocation), nameof(AppSettings.OutputFolder));
    }

    // ---------- 数据 ----------

    private List<FileItem> Ok => ws.Files.Where(f => f.State.IsDoneOrKept).ToList();
    private List<FileItem> Failed => ws.Files.Where(f => f.State.IsFailed).ToList();
    private List<FileItem> Kept => ws.Files.Where(f => f.State == FileState.Kept).ToList();
    /// 只算处理成功的；保留原图的按原大小计。
    private ulong Before => Ok.Aggregate(0UL, (s, f) => s + f.Bytes);
    private ulong After => Ok.Aggregate(0UL, (s, f) => s + (f.State == FileState.Done ? f.AfterBytes : f.Bytes));
    private double Saved => Before > 0 ? Math.Max(0, 1 - (double)After / Before) : 0;

    /// 有问题的排最前：失败 → 保留原图 → 完成。
    private List<FileItem> Rows
    {
        get
        {
            var failed = Failed;
            var kept = Kept;
            return failed.Concat(kept).Concat(ws.Files.Where(f => !failed.Contains(f) && !kept.Contains(f))).ToList();
        }
    }

    private string Title
    {
        get
        {
            if (Failed.Count == ws.Files.Count) return "没有处理成功";
            return ws.Type switch
            {
                MediaType.Image => ws.IsCropping ? "裁切完成" : "压缩完成",
                MediaType.Video => ws.VideoSegment switch
                {
                    VideoSegment.Frames => "序列帧完成",
                    VideoSegment.Edit => "剪辑完成",
                    _ => "压缩完成",
                },
                _ => ws.PdfSegment == PdfSegment.Compress ? "压缩完成" : "处理完成",
            };
        }
    }

    private string Subtitle
    {
        get
        {
            var noun = ws.Type == MediaType.Image ? "图片" : ws.Type == MediaType.Video ? "视频" : "文件";
            var parts = new List<string> { $"{ws.Files.Count} 个{noun}" };
            // 设计稿：12 个图片 · WebP · 网站用 · …（图片写输出格式）。
            if (ws.Type == MediaType.Image && !ws.IsCropping && ws.Draft.Image.Format != ImageFormatChoice.Original) parts.Add(ws.Draft.Image.Format.Title());
            if (ws.UsesRecipe) parts.Add(ws.Draft.Name);
            var s = model.Settings;
            parts.Add(s.OutputLocation == OutputLocation.Beside ? "存在原文件旁边" : $"存在「{System.IO.Path.GetFileName(s.OutputFolder.TrimEnd('\\', '/'))}」");
            parts.Add("行可以直接拖出，按空格用默认应用打开");
            return string.Join(" · ", parts);
        }
    }

    private string ListSummary
    {
        get
        {
            var parts = new List<string> { $"{ws.Files.Count} 个", $"{ws.Files.Count(f => f.State == FileState.Done)} 个完成" };
            if (Failed.Count > 0) parts.Add($"{Failed.Count} 个失败");
            if (Kept.Count > 0) parts.Add($"{Kept.Count} 个{(ws.Type == MediaType.Image ? "保留原图" : "保留原文件")}");
            return string.Join(" · ", parts);
        }
    }

    private double ListHeight
    {
        get
        {
            var n = Rows.Count;
            return 56 + 26 + Math.Min(3, n) * 36 + (n > 3 ? 34 : 6) + 10;
        }
    }

    // ---------- 刷新 ----------

    protected override void Refresh()
    {
        var allFailed = Failed.Count == ws.Files.Count;
        title.Text = Title;
        subtitle.Text = Subtitle;
        AutomationProperties.SetName(header, $"{Title}，{Subtitle}");
        badgeCheck.Visibility = allFailed ? Visibility.Collapsed : Visibility.Visible;
        badgeCross.Visibility = allFailed ? Visibility.Visible : Visibility.Collapsed;
        badge.Background = allFailed ? Palette.Get(this, "BentoFailed") : Palette.Brand;
        title.Foreground = Palette.Get(this, "BentoPrimary");
        subtitle.Foreground = Palette.Get(this, "BentoSecondary");
        panelHost.Content = BuildPanel();
        list.Update(Rows, ListSummary, open);
        Size(animated: false);
    }

    private void SetOpen(bool value)
    {
        open = value;
        list.Update(Rows, ListSummary, open);
        gaugeFrame.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (!open) Motion.Appear(gaugeFrame);
        Size(animated: true);
    }

    /// 右栏宽 min(336, 36%)，与左栏隔 36。刻度盘按设计稿 840 × 432：圆心对着列表中线，
    /// 底边压在收起的列表下面 22（列表盖住弧带的根部），上面不碰到标题。
    private void Size(bool animated)
    {
        var w = layout.ActualWidth;
        var h = layout.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var panelW = Math.Min(336, w * 0.36);
        layout.ColumnDefinitions[1].Width = new GridLength(panelW);
        var leftW = w - panelW - 36;
        var listTop = h - ListHeight;
        var s = Math.Max(0.3, Math.Min(Math.Min(leftW / 780, (listTop - 60) / 314), 1.3));
        gaugeFrame.Width = 840 * s;
        gaugeFrame.Height = 432 * s;
        Canvas.SetLeft(gaugeFrame, leftW / 2 - 6 * s - 412 * s);
        Canvas.SetTop(gaugeFrame, listTop + 22 * s - 432 * s);
        var target = open ? Math.Max(ListHeight, h - 66) : ListHeight;
        if (!animated || !Motion.Enabled || double.IsNaN(list.Height))
        {
            list.Height = target;
            return;
        }
        var anim = new DoubleAnimation
        {
            From = list.ActualHeight,
            To = target,
            Duration = TimeSpan.FromMilliseconds(380),
            EnableDependentAnimation = true,
            EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 },
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, list);
        Storyboard.SetTargetProperty(anim, "Height");
        sb.Children.Add(anim);
        sb.Completed += (_, _) => list.Height = target;
        sb.Begin();
    }

    // ---------- 右栏 ----------

    private UIElement BuildPanel()
    {
        var before = Before;
        var after = After;
        var failed = Failed;
        var kept = Kept;
        var col = new Grid { RowSpacing = 10 };
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // 原来 → 现在。
        var stats = new Grid();
        for (var i = 0; i < 6; i++)
            stats.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var secondary = Palette.Get(this, "BentoSecondary");
        var primary = Palette.Get(this, "BentoPrimary");
        AddRow(stats, 0, new TextBlock { Text = "原来", FontSize = 12, Foreground = secondary });
        AddRow(stats, 1, new TextBlock { Text = Format.Size(before), FontSize = 28, CharacterSpacing = -20, Foreground = secondary, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) });
        AddRow(stats, 2, Arrow());
        AddRow(stats, 3, new TextBlock { Text = "现在", FontSize = 12, Foreground = secondary });
        var big = new TextBlock { FontSize = 68, CharacterSpacing = -40, Foreground = primary };
        var (num, unit) = SplitSize(Format.Size(after));
        big.Inlines.Add(new Run { Text = num });
        big.Inlines.Add(new Run { Text = " " + unit, FontSize = 26, CharacterSpacing = 0 });
        AddRow(stats, 4, new Viewbox { Child = big, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) });
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 16, 0, 0) };
        chips.Children.Add(Capsule("省下 ", Palette.Get(this, "BentoAlertFill"), Palette.Get(this, "BentoAlertText"), Format.Size(before > after ? before - after : 0)));
        string? times = null;
        if (after > 0 && before > after)
        {
            times = $"小了 {((double)before / after).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} 倍";
            chips.Children.Add(Capsule(times, Palette.Get(this, "BentoCard"), primary));
        }
        AddRow(stats, 5, chips);
        var statsPanel = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(24, 22, 24, 22), Background = Palette.Get(this, "BentoFill"), Child = stats };
        AutomationProperties.SetName(statsPanel, $"原来 {Format.Size(before)}，现在 {Format.Size(after)}，省下 {Format.Size(before > after ? before - after : 0)}{(times is null ? "" : "，" + times)}");
        AutomationProperties.SetAccessibilityView(stats, AccessibilityView.Raw);
        col.Children.Add(statsPanel);

        var next = 1;
        void add(UIElement e)
        {
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)e, next++);
            col.Children.Add(e);
        }

        if (failed.Count > 0) add(FailBlock(failed));
        if (kept.Count > 0)
        {
            var text = kept.Count == 1
                ? $"{kept[0].Name} 压完会变大，{kept[0].KeptText.Replace("保留", "保留了")}。"
                : $"{kept.Count} 个文件压完会变大，保留了原文件。";
            add(new TextBlock { Text = text, FontSize = 11, Foreground = Palette.Warning, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 6, 0) });
        }
        var reveal = new BrandButton("在资源管理器中显示", 56, 15) { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = Ok.Count > 0 };
        reveal.Click += (_, _) => model.Reveal(ws.Files.SelectMany(f => f.Outputs.Select(o => o.Path)).ToList());
        add(reveal);
        var pair = new Grid { ColumnSpacing = 8 };
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        var again = Wide("改设置再做一次", "回到设置，这一批文件还在；改了设置后可以再开始");
        again.Click += (_, _) => ws.ShowResult = false;
        var fresh = Wide("再来一批", "清空这一批，拖新的文件进来");
        fresh.Click += (_, _) => model.Clear(ws);
        Grid.SetColumn(fresh, 1);
        pair.Children.Add(again);
        pair.Children.Add(fresh);
        add(pair);
        return col;
    }

    private static void AddRow(Grid g, int row, FrameworkElement e)
    {
        Grid.SetRow(e, row);
        g.Children.Add(e);
    }

    /// 虚线 → 红箭头 → 红线（「原来 → 现在」之间）。
    private FrameworkElement Arrow()
    {
        var g = new Grid { Width = 30, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 10) };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var dash = new Line { X1 = 15, X2 = 15, Y1 = 0, StrokeThickness = 2, StrokeDashArray = [2, 2], Stroke = Palette.Get(this, "BentoLine") };
        var host = new Grid();
        host.Children.Add(dash);
        host.SizeChanged += (_, e) => dash.Y2 = e.NewSize.Height;
        var circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Palette.Get(this, "BentoCard") };
        circle.Child = new LineIcon(ResultIcons.ArrowDown, 15, 2.4, "Brand");
        var red = new Rectangle { Width = 2, Fill = Palette.Brand, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetRow(circle, 1);
        Grid.SetRow(red, 2);
        g.Children.Add(host);
        g.Children.Add(circle);
        g.Children.Add(red);
        AutomationProperties.SetAccessibilityView(g, AccessibilityView.Raw);
        return g;
    }

    /// 胶囊：文字 + 可选的加粗数值（「省下 **53.8 MB**」）。
    private static Border Capsule(string text, Brush fill, Brush fg, string? bold = null)
    {
        var t = new TextBlock { FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines.Add(new Run { Text = text });
        if (bold is not null) t.Inlines.Add(new Run { Text = bold, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        return new Border { Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), Background = fill, Child = t };
    }

    /// 「6.9 MB」拆成数字和单位（单位用小字）。
    private static (string, string) SplitSize(string text)
    {
        var i = text.LastIndexOf(' ');
        return i > 0 ? (text[..i], text[(i + 1)..]) : (text, "");
    }

    private FrameworkElement FailBlock(List<FileItem> failed)
    {
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(14, 12, 10, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var x = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = Palette.Get(this, "BentoFailed"), VerticalAlignment = VerticalAlignment.Center };
        x.Child = new LineIcon(ResultIcons.Cross, 14, 2.6, "OnBrand");
        AutomationProperties.SetAccessibilityView(x, AccessibilityView.Raw);
        var alert = Palette.Get(this, "BentoAlertText");
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = $"{failed.Count} 个失败", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = alert });
        var note = failed.Count == 1 ? $"{failed[0].Name} {failed[0].State.Message}" : "鼠标停在行上看原因";
        texts.Children.Add(new TextBlock { Text = note, FontSize = 11, Foreground = alert, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var retry = new CapsuleButton("重试失败的 Ctrl+Shift+R", 30, "BentoCard", "BentoAlertText") { VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(retry, "重试失败的");
        ToolTipService.SetToolTip(retry, "按当前设置再处理一遍失败的文件（Ctrl+Shift+R）");
        retry.Click += (_, _) => model.RetryFailed(ws);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(retry, 2);
        g.Children.Add(x);
        g.Children.Add(texts);
        g.Children.Add(retry);
        return new Border { CornerRadius = new CornerRadius(18), Background = Palette.Get(this, "BentoAlertFill"), Child = g };
    }

    private Button Wide(string text, string help)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        Motion.SetPress(b, true);
        b.Content = new Border
        {
            Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = Palette.Get(this, "BentoFill"),
            Child = new TextBlock { Text = text, FontSize = 13, Foreground = Palette.Get(this, "BentoPrimary"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        AutomationProperties.SetName(b, text);
        ToolTipService.SetToolTip(b, help);
        return b;
    }
}

/// 逐个文件：标题行（点按展开 / 收起）、表头、前 3 行或全部、「还有 N 个 · 展开全部」。
internal sealed class ResultList : UserControl
{
    private readonly Workspace ws;
    private readonly Action toggle;
    private readonly Border panel = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(0, 0, 0, 10) };
    private readonly Button head = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock summary = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock headTitle = new() { Text = "逐个文件", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border pill = new() { Height = 34, Padding = new Thickness(14, 0, 12, 0), CornerRadius = new CornerRadius(17) };
    private readonly TextBlock pillText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly LineIcon chevron = new(ResultIcons.ChevronUp, 14, 2.2);
    private readonly Grid columns;
    private readonly StackPanel rows = new() { Spacing = 2, Padding = new Thickness(10, 0, 10, 0) };
    private readonly ScrollViewer scroll = new();
    private readonly Button more = new() { HorizontalAlignment = HorizontalAlignment.Center, Height = 30 };
    private readonly TextBlock moreText = new() { FontSize = 12 };
    private string built = "";

    public ResultList(Workspace ws, List<FileItem> initial, string summaryText, Action toggle)
    {
        this.ws = ws;
        this.toggle = toggle;
        head.Style = (Style)Application.Current.Resources["BentoBareButton"];
        more.Style = (Style)Application.Current.Resources["BentoBareButton"];
        var headRow = new Grid { Height = 56, Padding = new Thickness(20, 0, 12, 0), ColumnSpacing = 12, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pillRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        pillRow.Children.Add(pillText);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        chevron.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        pillRow.Children.Add(chevron);
        pill.Child = pillRow;
        pill.VerticalAlignment = VerticalAlignment.Center;
        headRow.Children.Add(headTitle);
        Grid.SetColumn(summary, 1);
        headRow.Children.Add(summary);
        Grid.SetColumn(pill, 2);
        headRow.Children.Add(pill);
        head.Content = headRow;
        head.Click += (_, _) => toggle();
        AutomationProperties.SetHeadingLevel(head, AutomationHeadingLevel.Level2);

        columns = ResultColumns.Create(Caption("名称", new Thickness(34, 0, 0, 0)), Caption("原大小"), Caption("输出"), Caption("变化"), Caption("状态"));
        columns.Height = 22;
        columns.Margin = new Thickness(10, 0, 10, 0);

        scroll.Content = rows;
        moreText.HorizontalAlignment = HorizontalAlignment.Center;
        more.Content = moreText;
        more.Click += (_, _) => toggle();

        var g = new Grid { RowSpacing = 2 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(head);
        Grid.SetRow(columns, 1);
        g.Children.Add(columns);
        Grid.SetRow(scroll, 2);
        g.Children.Add(scroll);
        Grid.SetRow(more, 3);
        g.Children.Add(more);
        panel.Child = g;
        Content = panel;
        FormTheme.On(this, Paint);
        Update(initial, summaryText, false);
    }

    private TextBlock Caption(string text, Thickness? margin = null)
    {
        var t = FormTheme.Text(text, 11, "BentoSecondary");
        if (margin is { } m) t.Margin = m;
        return t;
    }

    private void Paint()
    {
        panel.Background = Palette.Get(this, "BentoFill");
        headTitle.Foreground = pillText.Foreground = Palette.Get(this, "BentoPrimary");
        summary.Foreground = moreText.Foreground = Palette.Get(this, "BentoSecondary");
        pill.Background = Palette.Get(this, "BentoCard");
    }

    public void Update(List<FileItem> all, string summaryText, bool open)
    {
        summary.Text = summaryText;
        pill.Visibility = all.Count > 3 || open ? Visibility.Visible : Visibility.Collapsed;
        pillText.Text = open ? "收起" : "展开";
        chevron.RenderTransform = new RotateTransform { Angle = open ? 180 : 0 };
        AutomationProperties.SetName(head, $"逐个文件，{summaryText}{(pill.Visibility == Visibility.Visible ? (open ? "，收起" : "，展开") : "")}");
        var shown = open ? all : all.Take(3).ToList();
        var key = $"{open}|{string.Join(",", shown.Select(f => f.Id))}|{string.Join(",", shown.Select(f => f.State.Kind))}";
        if (key != built)
        {
            built = key;
            rows.Children.Clear();
            for (var i = 0; i < shown.Count; i++) rows.Children.Add(new ResultRow(shown[i], ws, i % 2 == 0));
        }
        scroll.VerticalScrollMode = open ? ScrollMode.Enabled : ScrollMode.Disabled;
        scroll.VerticalScrollBarVisibility = open ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        if (!open) scroll.ChangeView(null, 0, null, true);
        more.Visibility = !open && all.Count > 3 ? Visibility.Visible : Visibility.Collapsed;
        moreText.Text = $"还有 {all.Count - 3} 个 · 展开全部";
        AutomationProperties.SetName(more, moreText.Text);
    }
}

/// 完成页用到的设计稿图标（V2.1 · 04 的 SVG 路径）。
internal static class ResultIcons
{
    public const string Check = "M5 12l5 5 9-10";
    public const string Cross = "M7 7l10 10M17 7L7 17";
    public const string Alert = "M12 7v6M12 17h.01";
    public const string ArrowDown = "M12 5v14M6 13l6 6 6-6";
    public const string ChevronUp = "M6 15l6-6 6 6";
}
