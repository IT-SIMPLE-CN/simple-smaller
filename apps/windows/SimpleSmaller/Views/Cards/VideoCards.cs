using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;
using static SimpleSmaller.Views.Cards.VideoParts;

namespace SimpleSmaller.Views.Cards;

// 视频（设计稿 V2.1 · 06 / 07）：压缩、剪辑共用「画面与画质 · 格式 · 声音与开始」；序列帧是「抽帧 · 每一帧 · 输出与开始」。

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

/// 画面与画质：比例、分辨率、画质（GIF 时换成宽与帧率）。
internal sealed class VideoPictureCard : VideoCardBase
{
    private readonly Header header = Header("", "画面与画质");
    private readonly StackPanel accessory;
    private readonly TextBlock muteNote = new() { Text = "只去掉声音，画面原样复制，不重新编码，几乎不花时间。", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel body = new() { Spacing = 12 };
    private readonly PillChoice<string> ratio;
    private readonly TextBlock ratioTrailing;
    private readonly StackPanel gifRow = new() { Orientation = Orientation.Horizontal, Spacing = 18 };
    private readonly PillChoice<int> gifWidth;
    private readonly PillChoice<double> gifFps;
    private readonly StackPanel rateBlock = new() { Spacing = 12 };
    private readonly PillChoice<int> shortEdge;
    private readonly PillChoice<VideoRateChoice> rate;
    private readonly Border sizeBox;
    private readonly TextBox mb = new();
    private readonly TextBlock outputLine = Caption("");
    private readonly ContentControl lockable;

    public VideoPictureCard(Workspace ws) : base(ws)
    {
        accessory = RecipeAccessory(ws);
        header.Trailing = accessory;
        Paint(muteNote, t => t.Foreground = Palette.Get(t, "BentoSecondary"));

        var r = LabeledPills("画面比例", new[] { ("", "原比例") }.Concat(CropPreset.VideoPresets.Select(p => (p.Id, p.Name))), V.Crop?.Id ?? "");
        ratio = r.Pills;
        ratioTrailing = r.Trailing;
        ratio.Changed += id =>
        {
            Set(v => v with { Crop = CropPreset.VideoPresets.FirstOrDefault(p => p.Id == id) });
            ws.ResetVideoCrop();
        };

        var gw = LabeledPills("宽", new[] { 320, 480, 640, 800 }.Select(x => (x, x.ToString(CultureInfo.InvariantCulture))), V.GifWidth);
        gifWidth = gw.Pills;
        gifWidth.Changed += x => Set(v => v with { GifWidth = x });
        var gf = LabeledPills("帧率", new[] { 10.0, 15, 24 }.Select(x => (x, $"{(int)x} fps")), V.GifFps);
        gifFps = gf.Pills;
        gifFps.Changed += x => Set(v => v with { GifFps = x });
        gifRow.Children.Add(gw.Panel);
        gifRow.Children.Add(gf.Panel);

        var se = LabeledPills("分辨率", new[] { (0, "原分辨率"), (1080, "1080p"), (720, "720p"), (540, "540p") }, V.ShortEdge ?? 0);
        shortEdge = se.Pills;
        shortEdge.Changed += x => Set(v => v with { ShortEdge = x == 0 ? null : x });
        var rt = LabeledPills("画质", new[] { VideoRateChoice.High, VideoRateChoice.Medium, VideoRateChoice.Low, VideoRateChoice.Size }.Select(x => (x, VideoCards.RateTitle(x))), V.Rate);
        rate = rt.Pills;
        rate.Changed += x => Set(v => v with { Rate = x });

        // 「不超过 [25] MB」：灰底胶囊里的数字框。
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
        sizeBox = Paint(new Border { Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 0, 12, 0), Child = sizeRow, VerticalAlignment = VerticalAlignment.Bottom },
                        b => b.Background = Palette.Get(b, "BentoFill"));
        var rateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        rateRow.Children.Add(rt.Panel);
        rateRow.Children.Add(sizeBox);
        rateBlock.Children.Add(se.Panel);
        rateBlock.Children.Add(rateRow);

        body.Children.Add(r.Panel);
        body.Children.Add(gifRow);
        body.Children.Add(rateBlock);
        outputLine.VerticalAlignment = VerticalAlignment.Bottom;

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(header);
        top.Children.Add(muteNote);
        top.Children.Add(body);
        var spacer = new Border();
        lockable = Lockable(Rows((top, false), (spacer, true), (outputLine, false)));
        Content = Card.Create(lockable);
    }

    private static string Mb(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);

    protected override void Refresh()
    {
        WatchItems();
        var v = V;
        header.Subtitle = v.MuteOnly ? "只去掉声音" : "跟着配方走";
        Show(accessory, ws.Modified && ws.UsesRecipe);
        Show(muteNote, v.MuteOnly);
        Show(body, !v.MuteOnly);
        ratio.Selected = v.Crop?.Id ?? "";
        ratioTrailing.Text = ws.VideoSegment == VideoSegment.Edit ? "预览里的框可拖" : "";
        var gif = v.Codec == VideoCodec.Gif;
        Show(gifRow, gif);
        Show(rateBlock, !gif);
        gifWidth.Selected = v.GifWidth;
        gifFps.Selected = v.GifFps;
        shortEdge.Selected = v.ShortEdge ?? 0;
        rate.Selected = v.Rate;
        Show(sizeBox, v.Rate == VideoRateChoice.Size);
        if (mb.FocusState == FocusState.Unfocused) mb.Text = Mb(v.TargetMB);
        // 「输出 1080 × 1920 · 30 fps」：当前这段的实际输出画面。
        if (ws.CurrentVideo is { } item && !gif && !v.MuteOnly)
        {
            var size = ws.VideoOutputSize(item);
            outputLine.Text = $"输出 {(int)size.Width} × {(int)size.Height} · {(int)Math.Round(item.Fps)} fps";
            Show(outputLine, true);
        }
        else
        {
            Show(outputLine, false);
        }
        Lock(lockable, ws.Running);
    }
}

/// 格式：H.264 / HEVC / GIF；这批里有 HDR 时多一个「HDR 转 SDR」。编码方式自动选，不摆出来。
internal sealed class VideoFormatCard : VideoCardBase
{
    private readonly PillChoice<VideoCodec> codec;
    private readonly Border hdrRow;
    private readonly ToggleSwitch hdr;
    private readonly ContentControl lockable;

    public VideoFormatCard(Workspace ws) : base(ws)
    {
        codec = new PillChoice<VideoCodec>(new[] { (VideoCodec.H264, "MP4 H.264"), (VideoCodec.Hevc, "MP4 HEVC"), (VideoCodec.Gif, "GIF") }, V.Codec, 34, "格式");
        codec.Changed += c => Set(v => v with { Codec = c });
        (hdrRow, hdr) = ToggleRow("HDR 转 SDR");
        ToolTipService.SetToolTip(hdrRow, "HDR 视频在多数手机网页与聊天软件里会发灰，转成普通画面更稳");
        hdr.Toggled += (_, _) => Set(v => v with { HdrToSdr = hdr.IsOn });
        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(Header("", "格式"));
        top.Children.Add(codec);
        lockable = Lockable(Rows((top, false), (new Border(), true), (hdrRow, false)));
        Content = Card.Create(lockable);
    }

    protected override void Refresh()
    {
        WatchItems();
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
        (audioRow, audio) = ToggleRow("保留声音", 40);
        (coverRow, cover) = ToggleRow("同时导出封面", 40);
        ToolTipService.SetToolTip(coverRow, "在「剪辑」的时间轴上单击选封面；没选时取开头往后 1 秒");
        audio.Toggled += (_, _) => Set(v => v with { KeepAudio = audio.IsOn });
        cover.Toggled += (_, _) => Set(v => v with { ExportCover = cover.IsOn });
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(gifNote);
        group.Children.Add(audioRow);
        group.Children.Add(coverRow);
        options = Lockable(group);
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(Header("", "声音与附带"));
        head.Children.Add(options);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 10, 0, 0) };
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

/// 抽帧：每秒 / 间隔 / 总数 / 全部，大号数值 + 张数。
internal sealed class FramesCard : VideoCardBase
{
    private readonly Header header = Header("", "抽帧");
    private readonly StackPanel accessory;
    private readonly PillChoice<FrameMode> mode;
    private readonly TextBlock allText = new() { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Light, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel stepper = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock number = new() { FontSize = 44, FontWeight = Microsoft.UI.Text.FontWeights.Light };
    private readonly TextBlock unit = Caption("", 13);
    private readonly TextBlock total = new() { FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock detail = Caption("");
    private readonly TextBlock warning = new() { Text = "张数较多，网页动画建议 60–120 张", FontSize = 11, Foreground = Palette.Warning };
    private readonly ContentControl lockable;

    public FramesCard(Workspace ws) : base(ws)
    {
        accessory = RecipeAccessory(ws);
        header.Trailing = accessory;
        mode = new PillChoice<FrameMode>(new[] { (FrameMode.PerSecond, "每秒"), (FrameMode.Every, "间隔"), (FrameMode.Total, "总数"), (FrameMode.All, "全部") }, V.FrameMode, 36, "抽帧方式");
        mode.Changed += SetMode;

        var minus = new CircleIconButton("", "减少", 40);
        var plus = new CircleIconButton("", "增加", 40);
        minus.Click += (_, _) => Step(-1);
        plus.Click += (_, _) => Step(1);
        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 10);
        value.Children.Add(number);
        value.Children.Add(unit);
        stepper.Children.Add(minus);
        stepper.Children.Add(value);
        stepper.Children.Add(plus);

        var right = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        detail.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(total);
        right.Children.Add(detail);
        var row = new Grid { ColumnSpacing = 12, MinHeight = 56 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(allText);
        row.Children.Add(stepper);
        Grid.SetColumn(right, 1);
        row.Children.Add(right);
        var panel = Panel(row, new Thickness(16, 12, 16, 12));
        Paint(this, _ => { number.Foreground = total.Foreground = allText.Foreground = Palette.Get(this, "BentoPrimary"); });

        var top = new StackPanel { Spacing = 14 };
        top.Children.Add(header);
        top.Children.Add(mode);
        var bottom = new StackPanel { Spacing = 14 };
        bottom.Children.Add(panel);
        bottom.Children.Add(warning);
        lockable = Lockable(Rows((top, false), (new Border(), true), (bottom, false)));
        Content = Card.Create(lockable);
    }

    private int SourceFps => (int)Math.Round(ws.CurrentVideo?.Fps is > 0 and var f ? f : 30);
    private int Total => ws.VideoItems.Sum(ws.FrameCount);

    protected override void Refresh()
    {
        WatchItems();
        var v = V;
        Show(accessory, ws.Modified && ws.UsesRecipe);
        header.Subtitle = ws.CurrentVideo is { } item ? $"{item.Name} · {Format.Duration(item.Duration)} · {SourceFps} fps" : "";
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

/// 每一帧：格式、质量、宽、文件名（比例裁切沿用「剪辑」）。
internal sealed class FrameEachCard : VideoCardBase
{
    private readonly ComboBox format = Combo("格式");
    private readonly ComboBox quality = Combo("质量");
    private readonly ComboBox width = Combo("宽");
    private readonly Border qualityRow;
    private static readonly ImageFormatChoice[] Formats = [ImageFormatChoice.Webp, ImageFormatChoice.Jpeg, ImageFormatChoice.Png, ImageFormatChoice.Avif];
    private static readonly int[] Qualities = [60, 70, 80, 90, 100];
    private static readonly int[] Widths = [3840, 2560, 1920, 1280, 960, 640, 0];
    private bool updating;

    public FrameEachCard(Workspace ws) : base(ws)
    {
        foreach (var f in Formats) format.Items.Add(f.Title());
        foreach (var q in Qualities) quality.Items.Add(q.ToString(CultureInfo.InvariantCulture));
        foreach (var w in Widths) width.Items.Add(w == 0 ? "原尺寸" : $"{w} px");
        format.SelectionChanged += (_, _) => { if (!updating && format.SelectedIndex >= 0) Set(v => v with { FrameFormat = Formats[format.SelectedIndex] }); };
        quality.SelectionChanged += (_, _) => { if (!updating && quality.SelectedIndex >= 0) Set(v => v with { FrameQuality = Qualities[quality.SelectedIndex] }); };
        width.SelectionChanged += (_, _) =>
        {
            if (!updating && width.SelectedIndex >= 0)
            {
                var w = Widths[width.SelectedIndex];
                Set(v => v with { FrameWidth = w == 0 ? null : w });
            }
        };
        var name = Caption("{name}_0001", 12);
        name.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        qualityRow = Row("质量", quality, 40);
        var top = new StackPanel { Spacing = 8 };
        top.Children.Add(Header("", "每一帧", "比例裁切沿用「剪辑」里的设置"));
        top.Children.Add(Row("格式", format, 40));
        top.Children.Add(qualityRow);
        top.Children.Add(Row("宽", width, 40));
        top.Children.Add(Row("文件名", name, 40));
        Content = Card.Create(Rows((top, false), (new Border(), true)));
    }

    private static ComboBox Combo(string label)
    {
        var c = new ComboBox { MinWidth = 0, Height = 30, Padding = new Thickness(10, 0, 4, 0), FontSize = 13 };
        AutomationProperties.SetName(c, label);
        return c;
    }

    protected override void Refresh()
    {
        updating = true;
        var v = V;
        format.SelectedIndex = Array.IndexOf(Formats, v.FrameFormat);
        quality.SelectedIndex = Array.IndexOf(Qualities, v.FrameQuality);
        width.SelectedIndex = Array.IndexOf(Widths, v.FrameWidth ?? 0);
        updating = false;
        Show(qualityRow, v.FrameFormat != ImageFormatChoice.Png);
        Lock(this, ws.Running);
    }
}

/// 输出（存到旁边的新文件夹、zip、拼图总览）与开始。
internal sealed class FramesOutputStartCard : VideoCardBase
{
    private readonly ToggleSwitch zip, sheet;
    private readonly ContentControl options;

    public FramesOutputStartCard(Workspace ws) : base(ws)
    {
        var (zipRow, z) = ToggleRow("打包成 zip", 40);
        var (sheetRow, s) = ToggleRow("另存一张拼图总览", 40);
        zip = z;
        sheet = s;
        zip.Toggled += (_, _) => Set(v => v with { FrameZip = zip.IsOn });
        sheet.Toggled += (_, _) => Set(v => v with { FrameContactSheet = sheet.IsOn });
        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(zipRow);
        group.Children.Add(sheetRow);
        options = Lockable(group);
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(Header("", "输出", "存到旁边的新文件夹"));
        head.Children.Add(options);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 10, 0, 0) };
        Content = Card.Create(Rows((head, false), (start, true)));
    }

    protected override void Refresh()
    {
        if (zip.IsOn != V.FrameZip) zip.IsOn = V.FrameZip;
        if (sheet.IsOn != V.FrameContactSheet) sheet.IsOn = V.FrameContactSheet;
        options.IsEnabled = !ws.Running;
    }
}
