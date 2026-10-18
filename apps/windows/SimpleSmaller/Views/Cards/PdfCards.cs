using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;

namespace SimpleSmaller.Views.Cards;

// PDF（设计稿 V2.1 · 08）：上方一张宽卡放这个分段的主设置，左下「选项」，右下「开始」。
// 分段一换 WorkView 就整组重建卡片，所以每张卡按建好时的分段搭一次，之后只刷新数值。

/// 上方宽卡：按分段换内容。
internal sealed class PdfMainCard : Reactive
{
    private readonly Workspace ws;
    private readonly PdfSegment segment;
    private readonly Border card;
    private readonly List<Action> updates = new();

    public PdfMainCard(Workspace ws)
    {
        this.ws = ws;
        segment = ws.PdfSegment;
        var body = new Grid { RowSpacing = segment == PdfSegment.Compress ? 14 : 12 };
        card = Card.Create(body);
        Content = card;
        switch (segment)
        {
            case PdfSegment.Compress: BuildCompress(body); break;
            case PdfSegment.Merge: BuildMerge(body); break;
            case PdfSegment.Split: BuildSplit(body); break;
            case PdfSegment.ToImages: BuildToImages(body); break;
            default: BuildFromImages(body); break;
        }
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Base), nameof(Workspace.Running), nameof(Workspace.PageOrder));
        WatchList(ws.Files);
        PdfFileWatch.Pages(this, ws, Schedule);
    }

    private PdfBody P => ws.Draft.Pdf;

    private void SetPdf(Func<PdfBody, PdfBody> edit) => ws.Draft = ws.Draft with { Pdf = edit(ws.Draft.Pdf) };

    protected override void Refresh()
    {
        Lock.Apply(card, ws.Running);
        foreach (var u in updates) u();
    }

    private static void Row(Grid g, UIElement e, bool fill = false)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow((FrameworkElement)e, g.RowDefinitions.Count - 1);
        g.Children.Add(e);
    }

    private Header Head(string glyph, string title, string? subtitle = null, bool save = false)
    {
        var h = new Header { Glyph = glyph, Title = title, Subtitle = subtitle };
        if (save)
        {
            var s = new SaveRecipeAccessory(ws);
            h.Trailing = s;
            updates.Add(s.Update);
        }
        return h;
    }

    // MARK: 压缩：四档大格子

    private void BuildCompress(Grid body)
    {
        var head = Head("\uE73F", "压缩档", "跟着常用走", save: true);
        HeaderIcon.Set(head, PdfIcons.Sliders);
        Row(body, head);
        var grid = new Grid { RowSpacing = 10, ColumnSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        AutomationProperties.SetName(grid, "压缩档");

        void tile(int row, int col, string title, string detail, PdfLevelChoice level, FrameworkElement? accessory = null)
        {
            var t = new PdfLevelTile(title, detail, accessory);
            t.Click += (_, _) => SetPdf(p => p with { Level = level });
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
            updates.Add(() => t.IsOn = P.Level == level);
        }

        tile(0, 0, "屏幕阅读", "图片 110 dpi", PdfLevelChoice.Screen);
        tile(0, 1, "邮件附件", "图片 150 dpi，目标 10 MB 内", PdfLevelChoice.Email);
        tile(1, 0, "打印保真", "图片 300 dpi，只优化结构", PdfLevelChoice.Print);

        // 指定大小：右下角一直有「__ MB」白底输入框（设计稿），改了数就选中这一档。
        var mb = new TextBox
        {
            Width = 28,
            MinWidth = 0,
            Height = 30,
            MinHeight = 0,
            Padding = new Thickness(0, 5, 0, 0),
            FontSize = 13,
            TextAlignment = TextAlignment.Right,
            BorderThickness = new Thickness(0),
            Background = null,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrushFocused" })
        {
            mb.Resources[k] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        AutomationProperties.SetName(mb, "目标大小");
        void commit()
        {
            if (double.TryParse(mb.Text, out var v))
            {
                SetPdf(p => p with { Level = PdfLevelChoice.Size, TargetMB = Math.Min(2000, Math.Max(0.5, Math.Round(v, 1))) });
            }
            mb.Text = Format(P.TargetMB);
        }
        mb.LostFocus += (_, _) => commit();
        mb.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { commit(); e.Handled = true; } };
        var unit = PdfUi.Text("MB", 13);
        var field = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Padding = new Thickness(10, 0, 10, 0), Height = 30 };
        field.Children.Add(mb);
        field.Children.Add(unit);
        var fieldBack = new Border { Child = field, CornerRadius = new CornerRadius(10) };
        fieldBack.Loaded += (_, _) => fieldBack.Background = Palette.Get(fieldBack, "BentoCard");
        fieldBack.ActualThemeChanged += (_, _) => fieldBack.Background = Palette.Get(fieldBack, "BentoCard");
        // 点输入框不触发格子本身的点击。
        fieldBack.PointerPressed += (_, e) => e.Handled = true;
        fieldBack.Tapped += (_, e) => e.Handled = true;
        tile(1, 1, "指定大小", "逐档降图片质量", PdfLevelChoice.Size, fieldBack);
        updates.Add(() =>
        {
            if (mb.FocusState == FocusState.Unfocused) mb.Text = Format(P.TargetMB);
            // 输入框始终是白卡底上的深色字（不跟墨黑格子反色）。
            unit.Foreground = mb.Foreground = Palette.Get(fieldBack, "BentoPrimary");
        });
        Row(body, grid, fill: true);
    }

    private static string Format(double v) => v.ToString(v % 1 == 0 ? "0" : "0.#");

    // MARK: 合并 / 拆分

    private void BuildMerge(Grid body)
    {
        Row(body, Head("\uE8C8", "页面顺序", "在左边拖动排序；选中按 Delete 删页"));
        Row(body, new Border(), fill: true);
        var number = BigNumber();
        var line = new Grid { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var unit = PdfUi.Text("页", 13, "BentoSecondary");
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 10);
        var note = PdfUi.Text("合成 1 个文件", 16);
        note.HorizontalAlignment = HorizontalAlignment.Right;
        line.Children.Add(number);
        Grid.SetColumn(unit, 1);
        line.Children.Add(unit);
        Grid.SetColumn(note, 2);
        line.Children.Add(note);
        var panel = PdfUi.Panel(line, new Thickness(16, 10, 16, 10));
        updates.Add(() =>
        {
            number.Text = $"{ws.PageOrder.Count}";
            AutomationProperties.SetName(panel, $"{ws.PageOrder.Count} 页，合成 1 个文件");
        });
        Row(body, panel);
    }

    private static TextBlock BigNumber()
    {
        var t = PdfUi.Text("", 44);
        t.FontWeight = Microsoft.UI.Text.FontWeights.Light;
        t.TextTrimming = TextTrimming.None;
        return t;
    }

    private void BuildSplit(Grid body)
    {
        Row(body, Head("\uE8C6", "拆分方式", "每个文件分别拆"));
        Row(body, new Border(), fill: true);
        var row = new Grid { ColumnSpacing = 12 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        }
        var each = PdfUi.Text("每", 13, "BentoSecondary");
        var minus = new PdfRepeatCircle("\uE738", "减少");
        var number = BigNumber();
        number.MinWidth = 60;
        number.TextAlignment = TextAlignment.Center;
        var plus = new PdfRepeatCircle("\uE710", "增加");
        var unit = PdfUi.Text("页一个文件", 13, "BentoSecondary");
        var total = PdfUi.Text("", 20);
        total.HorizontalAlignment = HorizontalAlignment.Right;
        minus.Step += () => SetPdf(p => p with { SplitEvery = Math.Max(1, p.SplitEvery - 1) });
        plus.Step += () => SetPdf(p => p with { SplitEvery = Math.Min(500, p.SplitEvery + 1) });
        var i = 0;
        foreach (var e in new FrameworkElement[] { each, minus, number, plus, unit, total })
        {
            Grid.SetColumn(e, i++);
            e.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(e);
        }
        var panel = PdfUi.Panel(row, new Thickness(16, 12, 16, 12));
        updates.Add(() =>
        {
            number.Text = $"{P.SplitEvery}";
            var count = ws.Files.Where(f => f.Type == MediaType.Pdf)
                                .Sum(f => (int)Math.Ceiling(f.Pages / (double)Math.Max(1, P.SplitEvery)));
            total.Text = $"共拆出 {count} 个";
            AutomationProperties.SetName(number, $"每 {P.SplitEvery} 页一个文件，共拆出 {count} 个");
        });
        Row(body, panel);
    }

    // MARK: 转图片 / 图片转 PDF

    private void BuildToImages(Grid body)
    {
        var head = Head("\uE8B9", "转图片", null, save: true);
        updates.Add(() => head.Subtitle = P.LongImage ? "所有页面从上到下拼成一张长图" : "每页存成一张图片");
        Row(body, head);
        var mode = new Segments<bool>(Segments<bool>.Kind.Mini, [(false, "每页一张", null), (true, "拼成长图", null)], P.LongImage, "方式");
        mode.Changed += v => SetPdf(p => p with { LongImage = v });
        updates.Add(() => mode.Selected = P.LongImage);
        Row(body, mode);
        var format = new PillChoice<ImageFormatChoice>(new[] { ImageFormatChoice.Jpeg, ImageFormatChoice.Png, ImageFormatChoice.Webp }.Select(f => (f, f.Title())), P.ImageFormat, 32, "格式");
        format.Changed += v => SetPdf(p => p with { ImageFormat = v });
        var width = new PillChoice<int>(new[] { 750, 1080, 1242, 1600, 2400 }.Select(w => (w, $"{w}")), P.ImageWidth, 32, "宽");
        width.Changed += v => SetPdf(p => p with { ImageWidth = v });
        updates.Add(() => { format.Selected = P.ImageFormat; width.Selected = P.ImageWidth; });
        var pills = new Grid { ColumnSpacing = 18 };
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var f = PdfUi.LabeledPills("格式", format);
        var w = PdfUi.LabeledPills("宽", width);
        Grid.SetColumn(w, 1);
        pills.Children.Add(f);
        pills.Children.Add(w);
        Row(body, pills);
        var note = PdfUi.Text("页数很多时会按高度上限分成几张。", 11, "BentoSecondary");
        note.VerticalAlignment = VerticalAlignment.Bottom;
        updates.Add(() => note.Visibility = P.LongImage ? Visibility.Visible : Visibility.Collapsed);
        Row(body, note, fill: true);
    }

    private void BuildFromImages(Grid body)
    {
        Row(body, Head("\uE7C3", "页面", "图片按左边的顺序，一张一页"));
        var mode = new Segments<PdfPageModeChoice>(Segments<PdfPageModeChoice>.Kind.Mini,
            [(PdfPageModeChoice.A4, "A4 纸", null), (PdfPageModeChoice.Fit, "按图片大小", null)], P.PageMode, "页面大小");
        mode.Changed += v => SetPdf(p => p with { PageMode = v });
        Row(body, mode);
        var note = PdfUi.Text("", 12, "BentoSecondary");
        note.TextWrapping = TextWrapping.Wrap;
        note.VerticalAlignment = VerticalAlignment.Bottom;
        updates.Add(() =>
        {
            mode.Selected = P.PageMode;
            note.Text = P.PageMode == PdfPageModeChoice.A4 ? "每张图等比放进 A4 页面，四周留白，适合打印。" : "每页和图片一样大，没有白边，适合屏幕看。";
        });
        Row(body, note, fill: true);
    }
}

/// 压缩档大格子：名称 + 说明（不写预估体积：点「开始」才压）。选中墨黑底，右上角品牌红对勾。
internal sealed class PdfLevelTile : Button
{
    private readonly Border back = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 14) };
    private readonly TextBlock title;
    private readonly TextBlock detail;
    private readonly Border check = new() { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), VerticalAlignment = VerticalAlignment.Top };
    private readonly LineIcon checkIcon = new(PdfIcons.Check, 13, 2.6, "OnBrand");
    private readonly string name;
    private bool on;
    private bool hovering;

    public PdfLevelTile(string title, string detail, FrameworkElement? accessory)
    {
        name = $"{title}，{detail}";
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Motion.SetPress(this, true);
        this.title = new TextBlock { Text = title, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        this.title.VerticalAlignment = VerticalAlignment.Top;
        this.detail = new TextBlock { Text = detail, FontSize = 11 };
        this.detail.TextWrapping = TextWrapping.Wrap;
        this.detail.MaxLines = 2;
        this.detail.VerticalAlignment = VerticalAlignment.Bottom;
        check.Child = checkIcon;
        check.Background = Palette.Brand;
        AutomationProperties.SetAccessibilityView(check, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var g = new Grid { ColumnSpacing = 4 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 6 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(this.title);
        Grid.SetColumn(check, 1);
        g.Children.Add(check);
        Grid.SetRow(this.detail, 1);
        g.Children.Add(this.detail);
        if (accessory is not null)
        {
            accessory.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(accessory, 1);
            Grid.SetColumn(accessory, 1);
            g.Children.Add(accessory);
        }
        back.Child = g;
        Content = back;
        AutomationProperties.SetName(this, name);
        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    public bool IsOn
    {
        get => on;
        set
        {
            on = value;
            AutomationProperties.SetItemStatus(this, value ? "已选" : "");
            Paint();
        }
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, on ? "BentoInk" : (hovering ? "BentoLine" : "BentoFill"));
        title.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoPrimary");
        detail.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoSecondary");
        detail.Opacity = on ? 0.7 : 1;
        check.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// 左下：选项（去掉作者、转灰度、打开密码）；转图片时是质量。
internal sealed class PdfOptionsCard : Reactive
{
    private readonly Workspace ws;
    private readonly Border card;
    private readonly List<Action> updates = new();

    public PdfOptionsCard(Workspace ws)
    {
        this.ws = ws;
        var body = new Grid { RowSpacing = 8 };
        card = Card.Create(body);
        Content = card;
        if (ws.PdfSegment == PdfSegment.ToImages) BuildQuality(body); else BuildOptions(body);
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Running), nameof(Workspace.OutputPasswordOn), nameof(Workspace.OutputPassword));
    }

    protected override void Refresh()
    {
        Lock.Apply(card, ws.Running);
        foreach (var u in updates) u();
    }

    private static void Row(Grid g, UIElement e, bool fill = false)
    {
        g.RowDefinitions.Add(new RowDefinition { Height = fill ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetRow((FrameworkElement)e, g.RowDefinitions.Count - 1);
        g.Children.Add(e);
    }

    /// 开关行（通用的 BentoRow + BentoToggle，44 高）：加进 stack，返回开关。
    private static ToggleSwitch ToggleRow(StackPanel stack, string label, Func<bool> get, Action<bool> set)
    {
        var t = BentoToggle.Create(label);
        t.IsOn = get();
        t.Toggled += (_, _) => { if (t.IsOn != get()) set(t.IsOn); };
        stack.Children.Add(BentoRow.Create(label, t));
        return t;
    }

    private void BuildQuality(Grid body)
    {
        Row(body, new Header { Glyph = "\uE9E9", Title = "质量", Subtitle = null });
        Row(body, new Border(), fill: true);
        var png = PdfUi.Text("PNG 无损，不用调质量。", 12, "BentoSecondary");
        var value = PdfUi.Text("", 22);
        value.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        var slider = new Slider { Minimum = 30, Maximum = 100, StepFrequency = 1, SmallChange = 1, LargeChange = 5, IsThumbToolTipEnabled = false };
        AutomationProperties.SetName(slider, "质量");
        // 品牌红轨道（设计稿：粗轨道、白色圆钮）。
        foreach (var k in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" })
        {
            slider.Resources[k] = Palette.Brand;
        }
        var white = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
        foreach (var k in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed", "SliderOuterThumbBackground" })
        {
            slider.Resources[k] = white;
        }
        // 没选到的一段用描边灰（深浅色各取各的）。
        void track()
        {
            foreach (var k in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" })
            {
                slider.Resources[k] = Palette.Get(slider, "BentoLine");
            }
        }
        track();
        slider.ActualThemeChanged += (_, _) => track();
        slider.Resources["SliderTrackThemeHeight"] = 8.0;
        slider.Resources["SliderHorizontalThumbWidth"] = 24.0;
        slider.Resources["SliderHorizontalThumbHeight"] = 24.0;
        slider.ValueChanged += (_, e) =>
        {
            var q = (int)Math.Round(e.NewValue);
            if (q != ws.Draft.Pdf.ImageQuality) ws.Draft = ws.Draft with { Pdf = ws.Draft.Pdf with { ImageQuality = q } };
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(png);
        stack.Children.Add(value);
        stack.Children.Add(slider);
        updates.Add(() =>
        {
            var p = ws.Draft.Pdf;
            var isPng = p.ImageFormat == ImageFormatChoice.Png;
            png.Visibility = isPng ? Visibility.Visible : Visibility.Collapsed;
            value.Visibility = slider.Visibility = isPng ? Visibility.Collapsed : Visibility.Visible;
            value.Text = $"{p.ImageQuality} · {QualityWord.Of(p.ImageQuality)}";
            if ((int)Math.Round(slider.Value) != p.ImageQuality) slider.Value = p.ImageQuality;
        });
        Row(body, stack);
    }

    private void BuildOptions(Grid body)
    {
        var head = new Header { Title = "选项", Subtitle = null };
        HeaderIcon.Set(head, PdfIcons.Pages);
        Row(body, head);
        Row(body, new Border(), fill: true);
        var stack = new StackPanel { Spacing = 8 };
        if (ws.PdfSegment is PdfSegment.Compress or PdfSegment.Merge)
        {
            var strip = ToggleRow(stack, "去掉作者与修改记录", () => ws.Draft.StripMetadata, v => ws.Draft = ws.Draft with { StripMetadata = v });
            updates.Add(() => strip.IsOn = ws.Draft.StripMetadata);
        }
        if (ws.PdfSegment == PdfSegment.Compress)
        {
            var gray = ToggleRow(stack, "转灰度", () => ws.Draft.Pdf.Grayscale, v => ws.Draft = ws.Draft with { Pdf = ws.Draft.Pdf with { Grayscale = v } });
            updates.Add(() => gray.IsOn = ws.Draft.Pdf.Grayscale);
        }
        AddPasswordRows(stack);
        Row(body, stack);
    }

    /// 输出加打开密码：发给客户的报价、合同常用。密码不写进配方、不保存。
    private void AddPasswordRows(StackPanel stack)
    {
        var box = new PasswordBox { PlaceholderText = "设置密码", MaxLength = 127, Height = 44, MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, "设置密码");
        box.Resources["TextControlBorderBrushFocused"] = Palette.Brand;
        box.Resources["TextControlElevationBorderFocusedBrush"] = Palette.Brand;
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1.2);
        box.CornerRadius = new CornerRadius(15);
        box.Padding = new Thickness(16, 10, 12, 0);
        var hint = PdfUi.Text("", 11);
        var toggle = ToggleRow(stack, "输出加打开密码", () => ws.OutputPasswordOn, v =>
        {
            ws.OutputPasswordOn = v;
            if (v)
            {
                DispatcherQueue.TryEnqueue(() => box.Focus(FocusState.Programmatic));
            }
            else
            {
                ws.OutputPassword = "";
            }
        });
        box.PasswordChanged += (_, _) => { if (box.Password != ws.OutputPassword) ws.OutputPassword = box.Password; };
        updates.Add(() =>
        {
            toggle.IsOn = ws.OutputPasswordOn;
            box.Visibility = hint.Visibility = ws.OutputPasswordOn ? Visibility.Visible : Visibility.Collapsed;
            if (box.Password != ws.OutputPassword) box.Password = ws.OutputPassword;
            var empty = string.IsNullOrWhiteSpace(ws.OutputPassword);
            hint.Text = empty ? "先设好密码才能开始。" : "只在这次用，不保存。记得另外告诉对方。";
            PdfUi.Retint(hint, empty ? "Warning" : "BentoSecondary");
        });
        stack.Children.Add(box);
        stack.Children.Add(hint);
    }
}

/// PDF 压缩的右下格：这一批多少页 + 开始。
internal sealed class PdfStartCard : Reactive
{
    private readonly Workspace ws;
    private readonly TextBlock count = PdfUi.Text("", 26);

    public PdfStartCard(Workspace ws)
    {
        this.ws = ws;
        var body = new Grid { RowSpacing = 10 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new StackPanel { Spacing = 4, Padding = new Thickness(2, 2, 2, 0) };
        head.Children.Add(PdfUi.Text("这一批", 11, "BentoSecondary"));
        head.Children.Add(count);
        body.Children.Add(head);
        // 设计稿 #3E4248：比次要灰深一档。
        var note = PdfUi.Text("压完变大的文件会保留原文件", 11, "BentoPrimary");
        note.Opacity = 0.8;
        note.LineHeight = 16.5;
        var box = new Border { Child = note, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10) };
        box.Loaded += (_, _) => box.Background = Palette.Get(box, "BentoFill");
        box.ActualThemeChanged += (_, _) => box.Background = Palette.Get(box, "BentoFill");
        Grid.SetRow(box, 1);
        body.Children.Add(box);
        var start = new StartBlock(ws);
        Grid.SetRow(start, 2);
        body.Children.Add(start);
        Content = Card.Create(body);
        Watch(ws, nameof(Workspace.Running));
        WatchList(ws.Files);
        PdfFileWatch.Pages(this, ws, Schedule);
    }

    protected override void Refresh() => count.Text = $"{ws.Files.Count} 个 · {ws.Files.Sum(f => f.Pages)} 页";
}

/// 文件的页数是后台读出来的：订阅每个文件的 Pages 变化（随文件进出增减订阅，卸载时全部退订）。
internal static class PdfFileWatch
{
    public static void Pages(FrameworkElement owner, Workspace ws, Action changed, params string[] props)
    {
        var names = props.Length == 0 ? new[] { nameof(FileItem.Pages) } : props;
        var watched = new HashSet<FileItem>();
        void onItem(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null || names.Contains(e.PropertyName)) changed();
        }
        void sync()
        {
            foreach (var f in watched.Where(f => !ws.Files.Contains(f)).ToList()) { f.PropertyChanged -= onItem; watched.Remove(f); }
            foreach (var f in ws.Files.Where(watched.Add)) f.PropertyChanged += onItem;
        }
        void onList(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => sync();
        owner.Loaded += (_, _) => { ws.Files.CollectionChanged += onList; sync(); };
        owner.Unloaded += (_, _) =>
        {
            ws.Files.CollectionChanged -= onList;
            foreach (var f in watched) f.PropertyChanged -= onItem;
            watched.Clear();
        };
    }
}
