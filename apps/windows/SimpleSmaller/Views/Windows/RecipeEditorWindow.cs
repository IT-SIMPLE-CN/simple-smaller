using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Storage.Pickers;
using Windows.System;

namespace SimpleSmaller.Views.Sub;

/// 新建 / 编辑配方（设计稿 BentoRecipe）：760 宽白底，红色圆形图标 + 大标题，三个圆角 20 的灰底分组
/// （左列 96 宽的小标签），底部从文件导入 / 取消 / 存储。配方会出现在顶部配方条、资源管理器圆环和右键菜单里。
internal sealed class RecipeEditorWindow : SubWindow
{
    private readonly Recipe initial;
    private readonly Action<Recipe> onSave;
    private Recipe r;
    private readonly StackPanel groups = new() { Spacing = 12, Padding = new Thickness(28, 8, 28, 8) };
    private readonly PlainField nameBox = new("名称") { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly PlainField templateBox = new("文件名规则") { FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock namePlaceholder = new() { Text = "比如：官网三档 srcset", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private FrameworkElement? nameField, templateField;
    private readonly TextBlock example = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock note = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly BrandButton save = new("存储", 44);
    private bool importFailed;

    private bool IsNew => initial.Id.Length == 0 || initial.BuiltIn;

    public RecipeEditorWindow(Recipe initial, Action<Recipe> onSave) : base(initial.Id.Length == 0 || initial.BuiltIn ? "新建配方" : "编辑配方", 760, 720, "BentoCard", showTitle: false)
    {
        this.initial = initial;
        this.onSave = onSave;
        r = initial;
        OverlayTitle();
        nameBox.Text = r.Name;
        nameBox.TextChanged += (_, _) => { r = r with { Name = nameBox.Text }; UpdateSave(); namePlaceholder.Visibility = nameBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
        namePlaceholder.Visibility = r.Name.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        FormTheme.On(namePlaceholder, () => namePlaceholder.Foreground = Palette.Get(namePlaceholder, "BentoSecondary"));
        templateBox.Text = r.Naming.Template;
        templateBox.TextChanged += (_, _) => { r = r with { Naming = r.Naming with { Template = templateBox.Text } }; UpdateExample(); };

        var page = new Grid();
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(Head());
        var scroll = new ScrollViewer { Content = groups, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        page.Children.Add(scroll);
        var foot = Foot();
        Grid.SetRow(foot, 2);
        page.Children.Add(foot);
        Body.Children.Add(page);

        var enter = new KeyboardAccelerator { Key = VirtualKey.Enter };
        enter.Invoked += (_, e) => { e.Handled = true; if (save.IsEnabled) Save(); };
        Root.KeyboardAccelerators.Add(enter);
        Rebuild();
        UpdateSave();
        Root.Loaded += (_, _) => nameBox.Focus(FocusState.Programmatic);
    }

    private UIElement Head()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Padding = new Thickness(28, 24, 28, 10) };
        var circle = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Palette.Brand, VerticalAlignment = VerticalAlignment.Top };
        circle.Child = new LineIcon(SubIcons.Sliders, 20, 2, "OnBrand");
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var texts = new StackPanel { Spacing = 4 };
        var t = FormTheme.Text(IsNew ? "新建配方" : "编辑配方", 22);
        t.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        AutomationProperties.SetHeadingLevel(t, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        texts.Children.Add(t);
        texts.Children.Add(FormTheme.Text("配方会出现在顶部配方条、资源管理器圆环和右键菜单里。", 12, "BentoSecondary"));
        row.Children.Add(circle);
        row.Children.Add(texts);
        return row;
    }

    private FrameworkElement Foot()
    {
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(28, 14, 28, 22) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var import = new TextCapsule("从文件导入…", 40, 13, 16);
        import.Click += async (_, _) => await ImportAsync();
        note.Text = "可导出为 .smaller 发给同事";
        FormTheme.On(note, PaintNote);
        var cancel = new TextCapsule("取消", 44, 13, 20);
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) => Save();
        g.Children.Add(import);
        Grid.SetColumn(note, 1);
        g.Children.Add(note);
        Grid.SetColumn(cancel, 2);
        g.Children.Add(cancel);
        Grid.SetColumn(save, 3);
        g.Children.Add(save);
        return g;
    }

    private void PaintNote() => note.Foreground = importFailed ? Palette.Warning : Palette.Get(note, "BentoSecondary");

    private void UpdateSave() => save.IsEnabled = r.Name.Trim().Length > 0;

    // ---------- 分组 ----------

    /// 改了会影响别的字段显隐的选项后整体重排（两个输入框保留，打字不丢焦点）。
    private void Rebuild()
    {
        // 重排后焦点回到同名的控件上（键盘、旁白操作时不跳到别处）。
        var focusedName = Root.XamlRoot is { } xr && FocusManager.GetFocusedElement(xr) is FrameworkElement f && groups.Children.Count > 0
            ? AutomationProperties.GetName(f) : null;
        if (!string.IsNullOrEmpty(focusedName))
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (Find(groups, focusedName) is Control c) c.Focus(FocusState.Keyboard);
            });
        }
        if (nameField is not null) Detach(nameField);
        if (templateField is not null) Detach(templateField);
        Detach(example);
        groups.Children.Clear();
        var type = new Segments<MediaType>(Segments<MediaType>.Kind.Mini, MediaTypes.All.Select(t => (t, t.Title(), (string?)null)), r.Type, "用于") { IsHitTestVisible = IsNew, Opacity = IsNew ? 1 : 0.5 };
        type.Changed += t => { r = r with { Type = t }; Rebuild(); };
        nameField ??= Field(nameBox, 38, namePlaceholder);
        groups.Children.Add(Group(Line("名称", nameField), Line("用于", type)));
        groups.Children.Add(r.Type switch
        {
            MediaType.Image => ImageSection(),
            MediaType.Video => VideoSection(),
            _ => PdfSection(),
        });
        groups.Children.Add(NamingSection());
        UpdateExample();
    }

    private static DependencyObject? Find(DependencyObject root, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control c && AutomationProperties.GetName(c) == name) return c;
            if (Find(child, name) is { } hit) return hit;
        }
        return null;
    }

    private static void Detach(FrameworkElement e)
    {
        switch (e.Parent)
        {
            case Panel p: p.Children.Remove(e); break;
            case Border b: b.Child = null; break;
        }
    }

    /// 灰底分组（设计稿：圆角 20 的浅灰块，内边距 10 / 16，左列 96 宽的小标签）。
    private static Border Group(params UIElement[] lines)
    {
        var s = new StackPanel { Spacing = 2 };
        foreach (var l in lines) s.Children.Add(l);
        var b = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 10, 16, 10), Child = s };
        FormTheme.On(b, () => b.Background = Palette.Get(b, "BentoFill"));
        return b;
    }

    private static Grid Line(string label, UIElement content)
    {
        var g = new Grid { ColumnSpacing = 12, MinHeight = 40 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var t = FormTheme.Text(label, 12, "BentoSecondary");
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        if (content is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            fe.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetColumn(fe, 1);
            g.Children.Add(fe);
        }
        return g;
    }

    /// 白底圆角 12 的输入框（设计稿：无边框，左右 12）。
    private static FrameworkElement Field(TextBox box, double height, UIElement? placeholder = null)
    {
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        var inner = new Grid();
        if (placeholder is not null) inner.Children.Add(placeholder);
        inner.Children.Add(box);
        var b = new Border { Height = height, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 0, 12, 0), Child = inner };
        FormTheme.On(b, () => b.Background = Palette.Get(b, "BentoCard"));
        b.Tapped += (_, _) => box.Focus(FocusState.Pointer);
        return b;
    }

    private static WrapPanel Flow(params UIElement[] items)
    {
        var w = new WrapPanel { Spacing = 6 };
        foreach (var i in items) w.Children.Add(i);
        return w;
    }

    private Chip Toggle(string title, bool on, Action<bool> set)
    {
        var c = new Chip(title, on);
        c.Click += (_, _) => { set(!c.IsOn); c.IsOn = !c.IsOn; Rebuild(); };
        return c;
    }

    private WrapPanel WhitePills<T>(T selected, IEnumerable<(T Value, string Title)> options, Action<T> set) where T : notnull
    {
        var w = new WrapPanel { Spacing = 6 };
        foreach (var (v, title) in options)
        {
            var c = new Chip(title, EqualityComparer<T>.Default.Equals(v, selected), 36, showCheck: false, fontSize: 13, bordered: false);
            c.Click += (_, _) => { set(v); Rebuild(); };
            w.Children.Add(c);
        }
        return w;
    }

    /// 白底细边胶囊「原比例 ›」，点开是单选菜单（设计稿的比例、存到）。
    private MenuCapsule Pick<T>(string label, T selected, IEnumerable<(T Value, string Title)> options, Action<T> set)
    {
        var box = new MenuCapsule(label, "", 32, "BentoCard", bordered: true, arrowSuffix: true) { HorizontalAlignment = HorizontalAlignment.Left };
        box.Options(options, selected, v => { set(v); Rebuild(); });
        return box;
    }

    private static TextBox Number(double value, string label, Action<double> set)
    {
        var box = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 64, TextAlignment = TextAlignment.Right, InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
        AutomationProperties.SetName(box, label);
        box.TextChanged += (_, _) =>
        {
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0) set(v);
        };
        return box;
    }

    private UIElement ImageSection()
    {
        var img = r.Image;
        var lines = new List<UIElement>
        {
            Line("格式", WhitePills(img.Format, Enum.GetValues<ImageFormatChoice>().Select(f => (f, f.Title())), f => r = r with { Image = r.Image with { Format = f } })),
        };
        if (!img.Lossless && img.Format != ImageFormatChoice.Png)
        {
            var row = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var slider = new BrandSlider(30, 100) { Value = img.Quality, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, "质量");
            var value = new TextBlock { Text = $"{img.Quality}", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            FormTheme.On(value, () => value.Foreground = Palette.Get(value, "BentoPrimary"));
            slider.ValueChanged += (_, e) =>
            {
                var q = (int)Num.Round(e.NewValue);
                r = r with { Image = r.Image with { Quality = q } };
                value.Text = $"{q}";
            };
            row.Children.Add(slider);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            lines.Add(Line("质量", row));
        }
        // 勾几档就出几个文件（同一张图出多档，适合网站 srcset）。
        var chosen = (img.Size is { } s0 ? new List<int> { s0 } : []).Concat(img.ExtraSizes).OrderDescending().ToList();
        var chips = new List<UIElement>
        {
            Toggle("原尺寸", img.Size == null, on =>
                r = r with { Image = on ? r.Image with { Size = null, ExtraSizes = [] } : r.Image with { Size = 2560 } }),
        };
        foreach (var w in new[] { 3840, 2560, 1920, 1280, 640 })
        {
            chips.Add(Toggle($"{w}", chosen.Contains(w), on =>
            {
                var set = chosen.ToHashSet();
                if (on) set.Add(w); else set.Remove(w);
                var list = set.OrderDescending().ToList();
                r = r with { Image = r.Image with { Size = list.Count > 0 ? list[0] : null, ExtraSizes = list.Skip(1).ToList() } };
            }));
        }
        chips.Add(CustomSize(img, chosen));
        lines.Add(Line($"尺寸 · 按{img.SizeAxis.Title()}", Flow(chips.ToArray())));
        var presets = CropPreset.ImagePresets.Where(p => p.Aspect != null).ToList();
        lines.Add(Line("比例", Pick("比例", img.Crop?.Id ?? "",
            new[] { ("", "原比例") }.Concat(presets.Select(p => (p.Id, $"{p.Name} {p.RatioLabel}"))),
            id => r = r with { Image = r.Image with { Crop = CropPreset.ImagePresets.FirstOrDefault(p => p.Id == id) } })));
        return Group(lines.ToArray());
    }

    /// 「自定…」（设计稿尺寸行的最后一个胶囊）：填一个不在常用档里的尺寸，并选按宽 / 高 / 长边。
    private Chip CustomSize(ImageBody img, List<int> chosen)
    {
        int[] common = [3840, 2560, 1920, 1280, 640];
        var custom = chosen.FirstOrDefault(w => !common.Contains(w));
        var chip = new Chip(custom > 0 ? $"{custom}" : "自定…", custom > 0);
        var panel = new StackPanel { Spacing = 10, Width = 220 };
        var axis = new Segments<SizeAxis>(Segments<SizeAxis>.Kind.Mini, Enum.GetValues<SizeAxis>().Select(a => (a, $"按{a.Title()}", (string?)null)), img.SizeAxis, "尺寸按");
        axis.Changed += a => { r = r with { Image = r.Image with { SizeAxis = a } }; Rebuild(); };
        var field = new NumberField("自定尺寸（像素）") { Value = custom > 0 ? custom : 1080, FontSize = 13 };
        var fieldBack = new Border { Height = 32, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0, 10, 0), BorderThickness = new Thickness(1), Child = field };
        FormTheme.On(fieldBack, () => { fieldBack.Background = Palette.Get(fieldBack, "BentoCard"); fieldBack.BorderBrush = Palette.Get(fieldBack, "BentoLine"); });
        var add = new BrandButton("加上这一档", 32, 12, 14) { HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(axis);
        panel.Children.Add(fieldBack);
        panel.Children.Add(add);
        var flyout = new Flyout { Content = panel, Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        field.Commit = v => Math.Clamp(v, 16, 16384);
        add.Click += (_, _) =>
        {
            var w = Math.Clamp(int.TryParse(field.Text.Trim(), out var v) ? v : field.Value, 16, 16384);
            var set = chosen.Where(x => common.Contains(x)).ToHashSet();
            set.Add(w);
            var list = set.OrderDescending().ToList();
            r = r with { Image = r.Image with { Size = list[0], ExtraSizes = list.Skip(1).ToList() } };
            flyout.Hide();
            Rebuild();
        };
        chip.Click += (_, _) => flyout.ShowAt(chip);
        AutomationProperties.SetName(chip, custom > 0 ? $"自定尺寸 {custom}" : "自定尺寸");
        return chip;
    }

    private UIElement VideoSection()
    {
        var v = r.Video;
        var lines = new List<UIElement>
        {
            Line("编码", Pick("编码", v.Codec, [(VideoCodec.H264, "H.264 · MP4"), (VideoCodec.Hevc, "HEVC · MP4"), (VideoCodec.Gif, "GIF")],
                c => r = r with { Video = r.Video with { Codec = c } })),
        };
        if (v.Codec != VideoCodec.Gif)
        {
            lines.Add(Line("分辨率", Pick("分辨率", v.ShortEdge ?? 0, [(1080, "1080p"), (720, "720p"), (540, "540p"), (0, "原分辨率")],
                e => r = r with { Video = r.Video with { ShortEdge = e == 0 ? null : e } })));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var check = new CheckBox { IsChecked = v.Rate == VideoRateChoice.Size, MinWidth = 0, Padding = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetName(check, "大小上限");
            var mb = Number(v.TargetMB, "大小上限（MB）", x => r = r with { Video = r.Video with { TargetMB = x } });
            mb.IsEnabled = v.Rate == VideoRateChoice.Size;
            check.Click += (_, _) =>
            {
                var on = check.IsChecked == true;
                r = r with { Video = r.Video with { Rate = on ? VideoRateChoice.Size : (initial.Video.Rate == VideoRateChoice.Size ? VideoRateChoice.Medium : initial.Video.Rate) } };
                mb.IsEnabled = on;
            };
            var unit = FormTheme.Text("MB（按时长倒推码率）", 13, "BentoSecondary");
            unit.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(check);
            row.Children.Add(mb);
            row.Children.Add(unit);
            lines.Add(Line("大小上限", row));
            lines.Add(Line("声音", Toggle("保留声音", v.KeepAudio, on => r = r with { Video = r.Video with { KeepAudio = on } })));
        }
        return Group(lines.ToArray());
    }

    private UIElement PdfSection()
    {
        var p = r.Pdf;
        var lines = new List<UIElement>
        {
            Line("图片分辨率", Pick("图片分辨率", p.Level,
                [(PdfLevelChoice.Screen, "110 dpi · 屏幕阅读"), (PdfLevelChoice.Email, "150 dpi · 邮件附件"), (PdfLevelChoice.Print, "300 dpi · 打印保真"), (PdfLevelChoice.Size, "指定大小")],
                l => r = r with { Pdf = r.Pdf with { Level = l } })),
        };
        if (p.Level == PdfLevelChoice.Size)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(Number(p.TargetMB, "大小上限（MB）", x => r = r with { Pdf = r.Pdf with { TargetMB = x } }));
            var unit = FormTheme.Text("MB", 13, "BentoSecondary");
            unit.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(unit);
            lines.Add(Line("大小上限", row));
        }
        else if (p.Level == PdfLevelChoice.Email)
        {
            lines.Add(Line("大小上限", FormTheme.Text("10 MB", 13, "BentoSecondary")));
        }
        return Group(lines.ToArray());
    }

    // ---------- 命名 ----------

    private UIElement NamingSection()
    {
        // 规则输入框 + 一排变量胶囊，同一排末尾是示例（设计稿）。
        var tokens = new WrapPanel { Spacing = 4 };
        foreach (var token in new[] { "{name}", "{w}", "{h}", "{ratio}", "{date}", "{n}" })
        {
            var t = new TextBlock { Text = token, FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
            var back = new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = t };
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back };
            Motion.SetPress(b, true);
            FormTheme.On(b, () => { back.Background = Palette.Get(b, "BentoCard"); back.BorderBrush = Palette.Get(b, "BentoLine"); t.Foreground = Palette.Get(b, "BentoPrimary"); });
            ToolTipService.SetToolTip(b, TokenHelp(token));
            AutomationProperties.SetName(b, $"插入 {token}，{TokenHelp(token)}");
            b.Click += (_, _) =>
            {
                templateBox.Text += token;
                templateBox.SelectionStart = templateBox.Text.Length;
            };
            tokens.Children.Add(b);
        }
        var exampleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Height = 24 };
        var exampleLabel = FormTheme.Text("示例：", 11, "BentoSecondary");
        exampleLabel.VerticalAlignment = VerticalAlignment.Center;
        exampleRow.Children.Add(exampleLabel);
        exampleRow.Children.Add(example);
        tokens.Children.Add(exampleRow);
        FormTheme.On(example, () => example.Foreground = Palette.Get(example, "BentoSecondary"));
        templateField ??= Field(templateBox, 34);
        var naming = new StackPanel { Spacing = 6, Padding = new Thickness(0, 4, 0, 4) };
        naming.Children.Add(templateField);
        naming.Children.Add(tokens);
        var lines = new List<UIElement>
        {
            Line("文件名", naming),
            Line("", Flow(
                Toggle("小写", r.Naming.Lowercase, on => r = r with { Naming = r.Naming with { Lowercase = on } }),
                Toggle("中文转拼音", r.Naming.Pinyin, on => r = r with { Naming = r.Naming with { Pinyin = on } }),
                Toggle("空格换连字符", r.Naming.Hyphenate, on => r = r with { Naming = r.Naming with { Hyphenate = on } }))),
        };
        // 设计稿最后一行是「存到 ……  去掉位置与设备信息 ◯」。Windows 版配方不带存放位置（存到在设置里统一设），只留右边的开关。
        var stripLabel = r.Type == MediaType.Pdf ? "去掉作者与修改记录" : "去掉位置与设备信息";
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var stripText = FormTheme.Text(stripLabel, 12);
        stripText.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(stripText, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var sw = new BentoSwitch(stripLabel, r.StripMetadata, 40, 24, 18);
        sw.Changed += on => r = r with { StripMetadata = on };
        strip.Children.Add(stripText);
        strip.Children.Add(sw);
        lines.Add(Line("", strip));
        return Group(lines.ToArray());
    }

    private static string TokenHelp(string t) => t switch
    {
        "{name}" => "原文件名",
        "{w}" => "输出宽",
        "{h}" => "输出高",
        "{ratio}" => "比例，如 3x4",
        "{date}" => "日期，如 20261016",
        _ => "批次内序号",
    };

    /// 用引擎同一套规则算示例（设计稿：新品 发布.jpg → xin-pin-fa-bu-2560.webp）。
    private void UpdateExample()
    {
        uint w, h;
        string ext;
        switch (r.Type)
        {
            case MediaType.Image:
                w = (uint)(r.Image.Size ?? 4000);
                h = (uint)((r.Image.Size ?? 4000) * 0.5625);
                ext = r.Image.Format is ImageFormatChoice.Original or ImageFormatChoice.Jpeg ? "jpg" : r.Image.Format.ToString().ToLowerInvariant();
                break;
            case MediaType.Video:
                w = (uint)(r.Video.ShortEdge is { } e ? e * 16 / 9 : 1920);
                h = (uint)(r.Video.ShortEdge ?? 1080);
                ext = r.Video.Codec == VideoCodec.Gif ? "gif" : "mp4";
                break;
            default:
                w = h = 0;
                ext = "pdf";
                break;
        }
        var source = r.Type == MediaType.Image ? "新品 发布.jpg" : r.Type == MediaType.Video ? "新品 发布.mov" : "新品 发布.pdf";
        try
        {
            example.Text = AppModel.Shared.Engine.PreviewName(r.Naming.Ffi(Format.Today), source, w, h) + "." + ext;
        }
        catch
        {
            example.Text = "—";
        }
        ToolTipService.SetToolTip(example, example.Text);
    }

    // ---------- 导入、存储 ----------

    private async Task ImportAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".smaller");
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        if (await picker.PickSingleFileAsync() is not { } file) return;
        try
        {
            var n = AppModel.Shared.Store.ImportRecipes(file.Path);
            AppModel.Shared.Flash($"导入了 {n} 个配方");
            Close();
        }
        catch
        {
            importFailed = true;
            note.Text = "这个文件读不出配方";
            PaintNote();
        }
    }

    private void Save()
    {
        var output = r with { Name = r.Name.Trim() };
        if (IsNew) output = output with { Id = "" };
        var saved = AppModel.Shared.Store.SaveMine(output);
        onSave(saved);
        Close();
    }
}
