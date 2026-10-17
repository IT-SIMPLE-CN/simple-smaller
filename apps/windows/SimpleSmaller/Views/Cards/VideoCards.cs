using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;
using static SimpleSmaller.Views.Cards.VideoParts;

namespace SimpleSmaller.Views.Cards;

// 视频（设计稿 V2.1 · 06 剪辑 / 07 序列帧）：压缩、剪辑共用「画面与画质 · 格式 · 声音与开始」；序列帧是「抽帧 · 每一帧 · 输出与开始」。

/// 视频卡片的共同底子：改配方的快捷写法。
internal abstract class VideoCardBase : Reactive
{
    protected readonly Workspace ws;

    protected VideoCardBase(Workspace ws)
    {
        this.ws = ws;
        Watch(ws);
        WatchList(ws.Files);
    }

    protected VideoBody V => ws.Draft.Video;

    protected void Set(Func<VideoBody, VideoBody> edit)
    {
        var next = edit(ws.Draft.Video);
        if (next != ws.Draft.Video) ws.Draft = ws.Draft with { Video = next };
    }

    /// 文件元信息读完（时长、尺寸）也要刷新。
    protected void WatchItems()
    {
        foreach (var f in ws.Files) f.PropertyChanged -= ItemChanged;
        foreach (var f in ws.Files) f.PropertyChanged += ItemChanged;
    }

    private void ItemChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItem.Duration) or nameof(FileItem.Width) or nameof(FileItem.Fps) or nameof(FileItem.Hdr)) Schedule();
    }

    protected static Grid Rows(params (UIElement E, bool Star)[] rows)
    {
        var g = new Grid();
        for (var i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = rows[i].Star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetRow((FrameworkElement)rows[i].E, i);
            g.Children.Add(rows[i].E);
        }
        return g;
    }
}

/// 画面与画质（设计稿 V2.1 · 06）：左边 64 宽的小标题，一行一项，整组在卡里竖向居中：
/// 画面比例（小分段，右边「预览里的框可拖」）、分辨率、画质（右边写输出画面与帧率）。GIF 时换成宽与帧率。
internal sealed class VideoPictureCard : VideoCardBase
{
    /// 设计稿的比例顺序：竖屏在前、原比例在最后。
    private static readonly string[] RatioOrder = ["v916", "v34", "v11", "v169"];
    private static readonly int[] Edges = [0, 1080, 720, 480];

    private readonly Header header = Header(Icons.Sliders, "画面与画质");
    private readonly StackPanel accessory;
    private readonly Border modifiedTag;
    private readonly TextBlock muteNote = new() { Text = "只去掉声音，画面原样复制，不重新编码，几乎不花时间。", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel body = new() { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl ratioSlot = new() { IsTabStop = false };
    private Segments<string>? ratio;
    private string ratioKey = "";
    private readonly TextBlock ratioTrailing = Caption("预览里的框可拖");
    private readonly Grid ratioRow;
    private readonly Grid gifWidthRow, gifFpsRow;
    private readonly PillChoice<int> gifWidth;
    private readonly PillChoice<double> gifFps;
    private readonly ContentControl edgeSlot = new() { IsTabStop = false };
    private PillChoice<int>? shortEdge;
    private string edgeKey = "";
    private readonly Grid edgeRow, rateRow;
    private readonly PillChoice<VideoRateChoice> rate;
    private readonly Border sizeBox;
    private readonly TextBox mb = new();
    private readonly TextBlock outputLine = Caption("", 12);
    private readonly ContentControl lockable;

    public VideoPictureCard(Workspace ws) : base(ws)
    {
        (accessory, modifiedTag) = RecipeAccessory(ws);
        header.Trailing = accessory;
        Paint(muteNote, t => t.Foreground = Palette.Get(t, "BentoSecondary"));

        ratioRow = LabeledRow("画面比例", ratioSlot, ratioTrailing);

        gifWidth = new PillChoice<int>(new[] { 320, 480, 640, 800 }.Select(x => (x, x.ToString(CultureInfo.InvariantCulture))), V.GifWidth, 36, "宽");
        gifWidth.Changed += x => Set(v => v with { GifWidth = x });
        gifFps = new PillChoice<double>(new[] { 10.0, 15, 24 }.Select(x => (x, $"{(int)x} fps")), V.GifFps, 36, "帧率");
        gifFps.Changed += x => Set(v => v with { GifFps = x });
        gifWidthRow = LabeledRow("宽", gifWidth);
        gifFpsRow = LabeledRow("帧率", gifFps);

        edgeRow = LabeledRow("分辨率", edgeSlot);
        rate = new PillChoice<VideoRateChoice>(new[] { VideoRateChoice.High, VideoRateChoice.Medium, VideoRateChoice.Low, VideoRateChoice.Size }.Select(x => (x, VideoCards.RateTitle(x))), V.Rate, 36, "画质");
        rate.Changed += x => Set(v => v with { Rate = x });

        // 「按大小」时跟一个「不超过 [25] MB」：灰底胶囊里的数字框。
        mb.Width = 44;
        mb.MinWidth = 0;
        mb.MinHeight = 0;
        mb.Padding = new Thickness(0);
        mb.BorderThickness = new Thickness(0);
        mb.TextAlignment = TextAlignment.Right;
        mb.FontSize = 13;
        mb.VerticalAlignment = VerticalAlignment.Center;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            mb.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(mb, "体积上限");
        mb.TextChanged += (_, _) =>
        {
            if (double.TryParse(mb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && x > 0)
                Set(v => v with { TargetMB = Math.Min(4000, Math.Max(1, Math.Round(x, 1))) });
        };
        mb.LostFocus += (_, _) => mb.Text = Mb(V.TargetMB);
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var pre = Caption("不超过", 13);
        var post = Caption("MB", 13);
        pre.VerticalAlignment = post.VerticalAlignment = VerticalAlignment.Center;
        sizeRow.Children.Add(pre);
        sizeRow.Children.Add(mb);
        sizeRow.Children.Add(post);
        sizeBox = Paint(new Border { Height = 36, CornerRadius = new CornerRadius(18), Padding = new Thickness(14, 0, 14, 0), Child = sizeRow, VerticalAlignment = VerticalAlignment.Center },
                        b => b.Background = Palette.Get(b, "BentoFill"));
        var rateGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        rateGroup.Children.Add(rate);
        rateGroup.Children.Add(sizeBox);
        rateRow = LabeledRow("画质", rateGroup, outputLine);

        body.Children.Add(ratioRow);
        body.Children.Add(gifWidthRow);
        body.Children.Add(gifFpsRow);
        body.Children.Add(edgeRow);
        body.Children.Add(rateRow);

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(header);
        top.Children.Add(muteNote);
        lockable = Lockable(Rows((top, false), (body, true)));
        Content = Card.Create(lockable);
    }

    private static string Mb(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);

    /// 比例选项：设计稿的四个 + 原比例；配方里用了别的比例（如 4:5）时也列出来，免得没有选中项。
    private void BuildRatio()
    {
        var presets = RatioOrder.Select(id => CropPreset.VideoPresets.First(p => p.Id == id)).ToList();
        if (V.Crop is { } c && !presets.Any(p => p.Id == c.Id)) presets.Add(c);
        var key = string.Join(",", presets.Select(p => p.Id));
        if (key == ratioKey && ratio != null) return;
        ratioKey = key;
        var options = presets.Select(p => (p.Id, p.Name, (string?)null)).Append(("", "原比例", (string?)null));
        ratio = new Segments<string>(Segments<string>.Kind.Mini, options, V.Crop?.Id ?? "", "画面比例");
        ratio.Changed += id =>
        {
            Set(v => v with { Crop = CropPreset.VideoPresets.FirstOrDefault(p => p.Id == id) ?? (v.Crop?.Id == id ? v.Crop : null) });
            ws.ResetVideoCrop();
        };
        ratioSlot.Content = ratio;
    }

    /// 分辨率选项：原分辨率、1080p、720p、480p；配方里是别的档时也列出来。
    private void BuildEdges()
    {
        var edges = Edges.ToList();
        if (V.ShortEdge is { } e && !edges.Contains(e)) edges.Add(e);
        var key = string.Join(",", edges);
        if (key == edgeKey && shortEdge != null) return;
        edgeKey = key;
        shortEdge = new PillChoice<int>(edges.Select(x => (x, x == 0 ? "原分辨率" : $"{x}p")), V.ShortEdge ?? 0, 36, "分辨率");
        shortEdge.Changed += x => Set(v => v with { ShortEdge = x == 0 ? null : x });
        edgeSlot.Content = shortEdge;
    }

    protected override void Refresh()
    {
        WatchItems();
        var v = V;
        BuildRatio();
        BuildEdges();
        header.Subtitle = v.MuteOnly ? "只去掉声音" : "跟着配方走";
        Show(modifiedTag, ws.Modified && ws.UsesRecipe);
        Show(muteNote, v.MuteOnly);
        Show(body, !v.MuteOnly);
        ratio!.Selected = v.Crop?.Id ?? "";
        Show(ratioTrailing, ws.VideoSegment == VideoSegment.Edit);
        var gif = v.Codec == VideoCodec.Gif;
        Show(gifWidthRow, gif);
        Show(gifFpsRow, gif);
        Show(edgeRow, !gif);
        Show(rateRow, !gif);
        gifWidth.Selected = v.GifWidth;
        gifFps.Selected = v.GifFps;
        shortEdge!.Selected = v.ShortEdge ?? 0;
        rate.Selected = v.Rate;
        Show(sizeBox, v.Rate == VideoRateChoice.Size);
        if (mb.FocusState == FocusState.Unfocused) mb.Text = Mb(v.TargetMB);
        // 画质一行的右边：当前这段的实际输出画面与帧率（不写码率，码率由编码器按画质定）。
        if (ws.CurrentVideo is { } item && !gif && !v.MuteOnly && item.Width > 0)
        {
            var size = ws.VideoOutputSize(item);
            outputLine.Text = $"{(int)size.Width} × {(int)size.Height} · {(int)Math.Round(item.Fps)} fps";
            Show(outputLine, true);
        }
        else
        {
            Show(outputLine, false);
        }
        Lock(lockable, ws.Running);
    }
}

/// 格式：H.264 / HEVC / GIF；这批里有 HDR 时底部多一行「HDR 转 SDR」。编码方式自动选，不摆出来。
internal sealed class VideoFormatCard : VideoCardBase
{
    private readonly Header header;
    private readonly PillChoice<VideoCodec> codec;
    private readonly Border hdrRow;
    private readonly ToggleSwitch hdr;
    private readonly ContentControl lockable;

    public VideoFormatCard(Workspace ws) : base(ws)
    {
        header = Header(Icons.Film, "格式");
        codec = new PillChoice<VideoCodec>(new[] { (VideoCodec.H264, "MP4 H.264"), (VideoCodec.Hevc, "MP4 HEVC"), (VideoCodec.Gif, "GIF") }, V.Codec, 36, "格式");
        codec.Changed += c => Set(v => v with { Codec = c });
        codec.Margin = new Thickness(0, 4, 0, 0);
        (hdrRow, hdr) = ToggleRow("HDR 转 SDR");
        ToolTipService.SetToolTip(hdrRow, "HDR 视频在多数手机网页与聊天软件里会发灰，转成普通画面更稳");
        hdr.Toggled += (_, _) => Set(v => v with { HdrToSdr = hdr.IsOn });
        var top = new StackPanel { Spacing = 10 };
        top.Children.Add(header);
        top.Children.Add(codec);
        lockable = Lockable(Rows((top, false), (new Border(), true), (hdrRow, false)));
        Content = Card.Create(lockable);
    }

    protected override void Refresh()
    {
        WatchItems();
        header.Subtitle = ws.VideoSegment == VideoSegment.Compress ? "与「剪辑」共用" : "与「压缩」共用";
        codec.Selected = V.Codec;
        Show(hdrRow, ws.VideoItems.Any(f => f.Hdr) && V.Codec != VideoCodec.Gif);
        if (hdr.IsOn != V.HdrToSdr) hdr.IsOn = V.HdrToSdr;
        Lock(lockable, ws.Running || V.MuteOnly);
    }
}

/// 声音与附带 + 开始。
internal sealed class VideoAudioStartCard : VideoCardBase
{
    private readonly TextBlock gifNote = Caption("GIF 没有声音。", 12);
    private readonly Border audioRow, coverRow;
    private readonly ToggleSwitch audio, cover;
    private readonly ContentControl options;

    public VideoAudioStartCard(Workspace ws) : base(ws)
    {
        (audioRow, audio) = ToggleRow("保留声音");
        (coverRow, cover) = ToggleRow("同时导出封面");
        ToolTipService.SetToolTip(coverRow, "在「剪辑」的时间轴上单击选封面；没选时取开头往后 1 秒");
        audio.Toggled += (_, _) => Set(v => v with { KeepAudio = audio.IsOn });
        cover.Toggled += (_, _) => Set(v => v with { ExportCover = cover.IsOn });
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(gifNote);
        group.Children.Add(audioRow);
        group.Children.Add(coverRow);
        options = Lockable(group);
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(Header(Icons.Speaker, "声音与附带"));
        head.Children.Add(options);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 12, 0, 0) };
        Content = Card.Create(Rows((head, false), (start, true)));
    }

    protected override void Refresh()
    {
        var gif = V.Codec == VideoCodec.Gif;
        Show(gifNote, gif);
        Show(audioRow, !gif && !V.MuteOnly);
        Show(coverRow, !gif && !V.MuteOnly);
        if (audio.IsOn != V.KeepAudio) audio.IsOn = V.KeepAudio;
        if (cover.IsOn != V.ExportCover) cover.IsOn = V.ExportCover;
        options.IsEnabled = !ws.Running;
    }
}

// MARK: - 序列帧

/// 抽帧（设计稿 V2.1 · 07）：每秒 / 间隔 / 总数 / 全部；下面 − 大号数值 + 与右边的张数，在卡里竖向居中。
internal sealed class FramesCard : VideoCardBase
{
    private readonly Header header = Header(Icons.Film, "抽帧");
    private readonly StackPanel accessory;
    private readonly Border modifiedTag;
    private readonly PillChoice<FrameMode> mode;
    private readonly TextBlock allText = new() { FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel stepper = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock number = new() { FontSize = 44, CharacterSpacing = -30, VerticalAlignment = VerticalAlignment.Bottom, LineHeight = 44, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock unit = Caption("", 15);
    private readonly TextBlock total = new() { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock detail = Caption("");
    private readonly TextBlock warning = new() { Text = "张数较多，网页动画建议 60–120 张", FontSize = 11, Foreground = Palette.Warning };
    private readonly ContentControl lockable;

    public FramesCard(Workspace ws) : base(ws)
    {
        (accessory, modifiedTag) = RecipeAccessory(ws);
        header.Trailing = accessory;
        mode = new PillChoice<FrameMode>(new[] { (FrameMode.PerSecond, "每秒"), (FrameMode.Every, "间隔"), (FrameMode.Total, "总数"), (FrameMode.All, "全部") }, V.FrameMode, 36, "抽帧方式");
        mode.Changed += SetMode;
        mode.Margin = new Thickness(0, 4, 0, 0);

        var minus = StepButton("−", "减少");
        var plus = StepButton("+", "增加");
        minus.Click += (_, _) => Step(-1);
        plus.Click += (_, _) => Step(1);
        var value = new StackPanel { Orientation = Orientation.Horizontal, MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(6, 0, 0, 2);
        value.Children.Add(number);
        value.Children.Add(unit);
        var valueBox = new Grid { MinWidth = 120 };
        valueBox.Children.Add(value);
        stepper.Children.Add(minus);
        stepper.Children.Add(valueBox);
        stepper.Children.Add(plus);

        var right = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        detail.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(total);
        right.Children.Add(detail);
        var row = new Grid { ColumnSpacing = 16, VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(allText);
        row.Children.Add(stepper);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        Paint(this, _ => { number.Foreground = total.Foreground = allText.Foreground = Palette.Get(this, "BentoPrimary"); });

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(header);
        top.Children.Add(mode);
        lockable = Lockable(Rows((top, false), (row, true), (warning, false)));
        Content = Card.Create(lockable);
    }

    /// 灰底圆钮，中间是「−」「+」文字（设计稿 40 × 40，18 号字）。
    private static Button StepButton(string text, string label)
    {
        var t = new TextBlock { Text = text, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Child = t };
        Paint(back, b => { b.Background = Palette.Get(b, "BentoFill"); t.Foreground = Palette.Get(b, "BentoPrimary"); });
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, label);
        ToolTipService.SetToolTip(b, label);
        return b;
    }

    private int SourceFps => (int)Math.Round(ws.CurrentVideo?.Fps is > 0 and var f ? f : 30);
    private int Total => ws.VideoItems.Sum(ws.FrameCount);

    protected override void Refresh()
    {
        WatchItems();
        var v = V;
        Show(modifiedTag, ws.Modified && ws.UsesRecipe);
        header.Subtitle = ws.CurrentVideo is { } item
            ? $"{item.Name} · {Format.Duration(item.Duration)} · {SourceFps} fps · {(int)Math.Round(item.Duration * item.Fps)} 帧"
            : "";
        mode.Selected = v.FrameMode;
        var all = v.FrameMode == FrameMode.All;
        Show(allText, all);
        Show(stepper, !all);
        allText.Text = $"原帧率 {SourceFps} fps";
        var n = v.FrameValue;
        number.Text = Math.Round(n) == n ? ((int)n).ToString(CultureInfo.InvariantCulture) : n.ToString("0.0", CultureInfo.InvariantCulture);
        unit.Text = v.FrameMode switch { FrameMode.PerSecond => "帧 / 秒", FrameMode.Every => "秒一张", FrameMode.Total => "张", _ => "" };
        var t = Total;
        total.Text = $"{t} 张";
        detail.Text = $"{v.FrameFormat.Title()} · {(v.FrameWidth is { } w ? $"宽 {w}" : "原尺寸")}";
        Show(warning, t > 200);
        AutomationProperties.SetName(number, $"{number.Text} {unit.Text}");
        Lock(lockable, ws.Running);
    }

    private void SetMode(FrameMode m)
    {
        Set(v => m switch
        {
            FrameMode.PerSecond => v with { FrameMode = m, FrameValue = Math.Min(15, SourceFps) },
            FrameMode.Every => v with { FrameMode = m, FrameValue = 1 },
            FrameMode.Total => v with { FrameMode = m, FrameValue = 60 },
            _ => v with { FrameMode = m },
        });
    }

    private void Step(int dir)
    {
        var n = V.FrameValue;
        switch (V.FrameMode)
        {
            case FrameMode.PerSecond:
                // 1 → 5 → 10 …，不超过原帧率。
                n = dir > 0 ? (n < 5 ? 5 : n + 5) : (n <= 5 ? 1 : n - 5);
                n = Math.Min(SourceFps, Math.Max(1, n));
                break;
            case FrameMode.Every:
                n = Math.Max(1, n + dir);
                break;
            case FrameMode.Total:
                var maxFrames = (double)Math.Max(12, ws.CurrentVideo is { } c ? (int)Math.Round(c.Duration * c.Fps) : 300);
                n = Math.Min(maxFrames, Math.Max(12, n + dir * 12));
                break;
            default:
                return;
        }
        Set(v => v with { FrameValue = n });
    }
}

/// 每一帧（设计稿 V2.1 · 07）：三行贴底——格式「WebP · 质量 80 ›」、宽「1920 px ›」、文件名；比例裁切沿用「剪辑」。
/// 点格式、宽这两行弹出菜单选。
internal sealed class FrameEachCard : VideoCardBase
{
    private static readonly ImageFormatChoice[] Formats = [ImageFormatChoice.Webp, ImageFormatChoice.Jpeg, ImageFormatChoice.Png, ImageFormatChoice.Avif];
    private static readonly int[] Qualities = [60, 70, 80, 90, 100];
    private static readonly int[] Widths = [3840, 2560, 1920, 1280, 960, 640, 0];
    private readonly MenuFlyout formatMenu = new() { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
    private readonly MenuFlyout widthMenu = new() { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
    private readonly Button formatRow, widthRow;
    private readonly TextBlock formatValue, widthValue;
    private readonly List<(ImageFormatChoice F, RadioMenuFlyoutItem Item)> formatItems = new();
    private readonly List<(int Q, RadioMenuFlyoutItem Item)> qualityItems = new();
    private readonly List<(int W, RadioMenuFlyoutItem Item)> widthItems = new();
    private readonly MenuFlyoutSeparator qualitySeparator = new();
    private readonly ContentControl lockable;

    public FrameEachCard(Workspace ws) : base(ws)
    {
        foreach (var f in Formats)
        {
            var item = new RadioMenuFlyoutItem { Text = f.Title(), GroupName = "格式" };
            item.Click += (_, _) => Set(v => v with { FrameFormat = f });
            formatItems.Add((f, item));
            formatMenu.Items.Add(item);
        }
        formatMenu.Items.Add(qualitySeparator);
        foreach (var q in Qualities)
        {
            var item = new RadioMenuFlyoutItem { Text = $"质量 {q}", GroupName = "质量" };
            item.Click += (_, _) => Set(v => v with { FrameQuality = q });
            qualityItems.Add((q, item));
            formatMenu.Items.Add(item);
        }
        foreach (var w in Widths)
        {
            var item = new RadioMenuFlyoutItem { Text = w == 0 ? "原尺寸" : $"{w} px", GroupName = "宽" };
            item.Click += (_, _) => Set(v => v with { FrameWidth = w == 0 ? null : w });
            widthItems.Add((w, item));
            widthMenu.Items.Add(item);
        }
        (formatRow, formatValue) = MenuRow("格式", formatMenu);
        (widthRow, widthValue) = MenuRow("宽", widthMenu);
        var name = Caption("{name}_{0001}", 12);
        name.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        var rows = new StackPanel { Spacing = 8 };
        rows.Children.Add(formatRow);
        rows.Children.Add(widthRow);
        rows.Children.Add(Row("文件名", name));
        lockable = Lockable(Rows((Header(Icons.Image, "每一帧", "比例裁切沿用「剪辑」里的设置"), false), (new Border(), true), (rows, false)));
        Content = Card.Create(lockable);
    }

    protected override void Refresh()
    {
        var v = V;
        var png = v.FrameFormat == ImageFormatChoice.Png;
        formatValue.Text = png ? $"{v.FrameFormat.Title()} ›" : $"{v.FrameFormat.Title()} · 质量 {v.FrameQuality} ›";
        widthValue.Text = v.FrameWidth is { } w ? $"{w} px ›" : "原尺寸 ›";
        AutomationProperties.SetName(formatRow, $"格式，{formatValue.Text.TrimEnd('›', ' ')}");
        AutomationProperties.SetName(widthRow, $"宽，{widthValue.Text.TrimEnd('›', ' ')}");
        foreach (var (f, item) in formatItems) item.IsChecked = f == v.FrameFormat;
        foreach (var (q, item) in qualityItems)
        {
            item.IsChecked = q == v.FrameQuality;
            item.Visibility = png ? Visibility.Collapsed : Visibility.Visible;
        }
        qualitySeparator.Visibility = png ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (x, item) in widthItems) item.IsChecked = x == (v.FrameWidth ?? 0);
        Lock(lockable, ws.Running);
    }
}

/// 输出（存到旁边新文件夹、zip、拼图总览）与开始。
internal sealed class FramesOutputStartCard : VideoCardBase
{
    private readonly ToggleSwitch zip, sheet;
    private readonly ContentControl options;

    public FramesOutputStartCard(Workspace ws) : base(ws)
    {
        var (zipRow, z) = ToggleRow("打包成 zip");
        var (sheetRow, s) = ToggleRow("另存一张拼图总览");
        zip = z;
        sheet = s;
        zip.Toggled += (_, _) => Set(v => v with { FrameZip = zip.IsOn });
        sheet.Toggled += (_, _) => Set(v => v with { FrameContactSheet = sheet.IsOn });
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(zipRow);
        group.Children.Add(sheetRow);
        options = Lockable(group);
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(Header(Icons.Folder, "输出", "存到旁边新文件夹"));
        head.Children.Add(options);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 12, 0, 0) };
        Content = Card.Create(Rows((head, false), (start, true)));
    }

    protected override void Refresh()
    {
        if (zip.IsOn != V.FrameZip) zip.IsOn = V.FrameZip;
        if (sheet.IsOn != V.FrameContactSheet) sheet.IsOn = V.FrameContactSheet;
        options.IsEnabled = !ws.Running;
    }
}
