using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Cards;

/// 画面（设计稿 V2.2）：比例和尺寸放在一起定。图片不再分「压缩 / 裁切」——
/// 选「原比例」就是只压缩，下面是尺寸；选了比例就是同时裁切，下面是输出宽（高按比例）。
internal sealed class FrameCard : Reactive
{
    private readonly Workspace ws;
    private readonly Header header = new() { Glyph = "", Title = "画面" };
    private readonly SaveRecipeAccessory accessory;
    private readonly List<(CropPreset? Preset, RatioTile Tile)> tiles = new();
    private readonly Border panel = new() { Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(18) };
    private readonly Border card;
    private bool? showingCrop;

    // 原比例：尺寸。
    private readonly Grid plain = new() { ColumnSpacing = 12 };
    private readonly TextBlock axisLabel = Themed.Text("", 12, "BentoSecondary");
    private readonly NumberField sizeField = new("像素") { FontSize = 36, FontWeight = Themed.Light };
    private readonly TextBlock px1 = Themed.Text("px", 13, "BentoSecondary");
    private readonly TextBlock extra = Themed.Text("", 11, "BentoSecondary");
    private readonly Segments<SizeAxis> axis;
    private readonly TextBlock originalBig = Themed.Text("原尺寸", 28, "BentoSecondary", Themed.Light);
    private readonly TextBlock originalNote = Themed.Text("每张保持原来的宽高，只压体积", 12, "BentoSecondary");
    private readonly Segments<bool> custom;
    private readonly StackPanel original = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };

    // 选了比例：输出宽。
    private readonly Grid crop = new() { ColumnSpacing = 10 };
    private readonly NumberField widthField = new("输出宽度") { FontSize = 32, FontWeight = Themed.Light, Width = 92, TextAlignment = TextAlignment.Center };
    private readonly TextBlock heightText = Themed.Text("", 12, "BentoSecondary");
    private readonly TextBlock shortText = Themed.Text("", 11, "BentoSecondary");
    private readonly FontIcon shortIcon = new() { Glyph = "", FontSize = 11 };

    public FrameCard(Workspace ws)
    {
        this.ws = ws;
        accessory = new SaveRecipeAccessory(ws);
        header.Trailing = accessory;

        // 比例格子：9 格一排。
        var grid = new Grid { ColumnSpacing = 6 };
        AutomationProperties.SetName(grid, "比例");
        var all = new List<CropPreset?> { null };
        all.AddRange(CropPreset.ImagePresets);
        for (var i = 0; i < all.Count; i++)
        {
            var p = all[i];
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tile = p is null
                ? new RatioTile("原比例", "只压缩", "原比例，只压缩")
                : new RatioTile(p.RatioLabel.Length == 0 ? "—" : p.RatioLabel, p.RatioLabel.Length == 0 ? "自由" : ShortName(p),
                                p.RatioLabel.Length == 0 ? "自由" : $"{ShortName(p)} {p.RatioLabel}");
            if (p is not null)
            {
                ToolTipService.SetToolTip(tile, p.RatioLabel.Length == 0 ? p.Name : $"{p.Name} {p.RatioLabel}");
            }
            tile.Click += (_, _) => Choose(p);
            Grid.SetColumn(tile, i);
            grid.Children.Add(tile);
            tiles.Add((p, tile));
        }

        // 原尺寸 / 自定义。
        axis = new Segments<SizeAxis>(Segments<SizeAxis>.Kind.Mini, new[] { SizeAxis.Width, SizeAxis.Height, SizeAxis.LongEdge }.Select(a => (a, a.Title(), (string?)null)), ws.Draft.Image.SizeAxis, "按宽、高或长边");
        axis.Changed += a => ws.Draft = ws.Draft with { Image = ws.Draft.Image with { SizeAxis = a } };
        custom = new Segments<bool>(Segments<bool>.Kind.Mini, new[] { (false, "原尺寸", (string?)null), (true, "自定义", (string?)null) }, ws.Draft.Image.Size != null, "尺寸方式");
        custom.Changed += on => ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Size = on ? (ws.Base.Image.Size ?? 2560) : null } };
        sizeField.Commit = v =>
        {
            var s = Math.Max(16, v);
            ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Size = s } };
            return s;
        };
        sizeField.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(extra, "这个配方每张图还会多出这几档小图，给网站按屏幕大小加载用；在配方里改");
        var cols = new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto };
        foreach (var c in cols)
        {
            plain.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
        }
        Put(plain, axisLabel, 0);
        Put(plain, sizeField, 1);
        Put(plain, px1, 2);
        Put(plain, extra, 3);
        Put(plain, axis, 5);
        Put(plain, custom, 6);
        // 原尺寸时：大字 + 一句说明占前几列。
        originalBig.Opacity = 0.6;
        original.Children.Add(originalBig);
        original.Children.Add(originalNote);
        originalNote.TextTrimming = TextTrimming.CharacterEllipsis;
        Put(plain, original, 0);
        Grid.SetColumnSpan(original, 5);

        // 输出宽 − 数字 + px，右边高与提醒。
        var minus = new CircleIconButton("", "减小宽度", 30, "BentoCard");
        var plus = new CircleIconButton("", "增大宽度", 30, "BentoCard");
        Repeat.Attach(minus, () => SetWidth(ws.CropOutputWidth - 10));
        Repeat.Attach(plus, () => SetWidth(ws.CropOutputWidth + 10));
        widthField.Commit = v => SetWidth(v);
        widthField.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < 6; i++)
        {
            crop.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        }
        Put(crop, Themed.Text("输出宽", 12, "BentoSecondary"), 0);
        Put(crop, minus, 1);
        Put(crop, widthField, 2);
        Put(crop, plus, 3);
        Put(crop, Themed.Text("px", 13, "BentoSecondary"), 4);
        var right = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        heightText.HorizontalAlignment = HorizontalAlignment.Right;
        var shortRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAccessibilityView(shortIcon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        shortRow.Children.Add(shortIcon);
        shortRow.Children.Add(shortText);
        shortText.TextTrimming = TextTrimming.CharacterEllipsis;
        right.Children.Add(heightText);
        right.Children.Add(shortRow);
        Put(crop, right, 5);

        Themed.Bg(panel, "BentoFill");
        var body = new Grid { RowSpacing = 12 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(header);
        var ratioLabel = Themed.Text("比例", 11, "BentoSecondary");
        Grid.SetRow(ratioLabel, 1);
        body.Children.Add(ratioLabel);
        Grid.SetRow(grid, 2);
        body.Children.Add(grid);
        Grid.SetRow(panel, 4);
        body.Children.Add(panel);
        card = Card.Create(CardBody.Fit(body));
        Content = card;
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Base), nameof(Workspace.Running), nameof(Workspace.CropBoxes), nameof(Workspace.CropIndex));
        WatchList(ws.Files);
    }

    private static void Put(Grid g, FrameworkElement e, int col)
    {
        e.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(e, col);
        g.Children.Add(e);
    }

    /// 比例格子里写短名（一行放得下）；完整名字在悬停和旁白里。
    public static string ShortName(CropPreset p) => p.Id switch
    {
        "dy" => "抖音",
        "gzh" => "公众号",
        "gzh2" => "次图",
        "shop" => "电商",
        "ig" => "IG",
        "banner" => "横幅",
        _ => p.Name,
    };

    private void Choose(CropPreset? preset)
    {
        if (ws.Draft.Image.Crop?.Id == preset?.Id)
        {
            return;
        }
        ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Crop = preset, CropWidth = null } };
        ws.ResetCrop();
        if (preset is null)
        {
            ws.CropEditing = false;
        }
    }

    private int SetWidth(int w)
    {
        var v = Math.Min(20000, Math.Max(16, w));
        ws.Draft = ws.Draft with { Image = ws.Draft.Image with { CropWidth = v } };
        return v;
    }

    protected override void Refresh()
    {
        var image = ws.Draft.Image;
        header.Subtitle = ws.IsCropping ? "每张默认居中取最大；在左边点一张可以调框" : "比例和尺寸一起定；选「原比例」就是只压缩";
        accessory.Update();
        foreach (var (p, tile) in tiles)
        {
            tile.IsOn = image.Crop?.Id == p?.Id;
        }

        var cropping = ws.IsCropping;
        if (showingCrop != cropping)
        {
            var first = showingCrop is null;
            showingCrop = cropping;
            panel.Child = cropping ? crop : plain;
            if (!first)
            {
                Motion.Appear(panel);
            }
        }
        if (cropping)
        {
            widthField.Value = ws.CropOutputWidth;
            heightText.Text = $"高 {OutputHeight} px · 按比例";
            var shortCount = Enumerable.Range(0, ws.CropItems.Count).Count(i => (int)ws.EffectiveCropBox(i).Width < ws.CropOutputWidth);
            if (shortCount > 0)
            {
                shortText.Text = $"{shortCount} 张裁出来不够宽，按原宽输出";
                Themed.Fg(shortText, "Warning");
                Themed.Fg(shortIcon, "Warning");
                shortIcon.Visibility = Visibility.Visible;
            }
            else
            {
                shortText.Text = "原图不够大时提醒，不放大";
                Themed.Fg(shortText, "BentoSecondary");
                shortIcon.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            var hasSize = image.Size is not null;
            foreach (var e in new FrameworkElement[] { axisLabel, sizeField, px1, axis })
            {
                e.Visibility = hasSize ? Visibility.Visible : Visibility.Collapsed;
            }
            extra.Visibility = hasSize && image.ExtraSizes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            original.Visibility = hasSize ? Visibility.Collapsed : Visibility.Visible;
            if (image.Size is { } s)
            {
                axisLabel.Text = image.SizeAxis.Title();
                sizeField.Value = s;
                sizeField.Width = Math.Max(2, s.ToString().Length) * 22 + 4;
                extra.Text = "另出 " + string.Join(" / ", image.ExtraSizes);
                axis.Selected = image.SizeAxis;
            }
            custom.Selected = hasSize;
        }
        Lock.Apply(card, ws.Running);
    }

    /// 高按比例；自由比例按当前这张框的宽高比。
    private int OutputHeight
    {
        get
        {
            double w = ws.CropOutputWidth;
            if (ws.CropAspect is { } a) return (int)Num.Round(w / a);
            var b = ws.EffectiveCropBox(ws.CropCurrentIndex);
            return b.Width > 0 ? (int)Num.Round(w * b.Height / b.Width) : 0;
        }
    }
}

/// 比例格子：上面比例，下面平台；选中墨黑。
internal sealed class RatioTile : Button
{
    private readonly Border back = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 8, 10, 8) };
    private readonly TextBlock ratio;
    private readonly TextBlock name;
    private bool on;

    public RatioTile(string ratioText, string nameText, string label)
    {
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Motion.SetPress(this, true);
        ratio = new TextBlock { Text = ratioText, FontSize = 15, FontWeight = Themed.Semibold, MaxLines = 1 };
        name = new TextBlock { Text = nameText, FontSize = 10, MaxLines = 1, TextTrimming = TextTrimming.Clip };
        var stack = new StackPanel { Spacing = 1 };
        // 放不下时整体缩小（最多到 80% 左右），不截断比例。
        stack.Children.Add(new Viewbox { Child = ratio, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Height = 20 });
        stack.Children.Add(name);
        back.Child = stack;
        Content = back;
        AutomationProperties.SetName(this, label);
        Themed.OnTheme(this, Paint);
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
        back.Background = Palette.Get(this, on ? "BentoInk" : "BentoFill");
        ratio.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoPrimary");
        name.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoSecondary");
        name.Opacity = on ? 0.75 : 1;
    }
}

/// 按住连续触发（mac 的 buttonRepeatBehavior）：按下先触发一次，停 0.4 秒后每 0.08 秒一次。
internal static class Repeat
{
    public static void Attach(Button b, Action action)
    {
        var timer = new DispatcherTimer();
        var fast = false;
        timer.Tick += (_, _) =>
        {
            action();
            if (!fast)
            {
                fast = true;
                timer.Interval = TimeSpan.FromMilliseconds(80);
            }
        };
        void stop() => timer.Stop();
        var byPointer = false;
        b.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) =>
        {
            byPointer = true;
            action();
            fast = false;
            timer.Interval = TimeSpan.FromMilliseconds(400);
            timer.Start();
        }), true);
        b.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => stop()), true);
        b.PointerCaptureLost += (_, _) => stop();
        b.PointerExited += (_, _) => stop();
        b.PointerCanceled += (_, _) => stop();
        // 键盘、旁白点按：触发一次（指针点按已经在按下时触发过了）。
        b.Click += (_, _) =>
        {
            if (!byPointer)
            {
                action();
            }
            byPointer = false;
        };
    }
}
