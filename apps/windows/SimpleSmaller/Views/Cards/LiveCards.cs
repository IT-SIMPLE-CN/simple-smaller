using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;
using SimpleSmaller.Views.Preview;
using static SimpleSmaller.Views.Cards.VideoParts;

namespace SimpleSmaller.Views.Cards;

// 视频「实况」：做成哪种 · 封面 · 声音与开始（结构照「序列帧」：主参数卡、细节卡、开始卡；对应 mac LiveCards）。

/// 做成哪种（苹果实况 / 安卓动态照片 / 两种都要）、画面比例、分辨率。
internal sealed class LiveKindCard : VideoCardBase
{
    private readonly Header header = Header(FeatureIcons.Live, "做成", "跟着常用走");
    private readonly Border modifiedTag;
    private readonly PillChoice<LiveKind> kind;
    private readonly TextBlock note = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly PillChoice<string> ratio;
    private readonly PillChoice<int> edge;
    private readonly TextBlock outputLine = Caption("");
    private readonly ContentControl lockable;

    public LiveKindCard(Workspace ws) : base(ws)
    {
        var (accessory, tag) = RecipeAccessory(ws);
        modifiedTag = tag;
        header.Trailing = accessory;
        kind = new PillChoice<LiveKind>([(LiveKind.Apple, "苹果实况"), (LiveKind.Android, "安卓动态照片"), (LiveKind.Both, "两种都要")], V.LiveKind, 34, "做成哪种");
        kind.Changed += k => Set(v => v.WithLiveKind(k));
        Paint(note, t => t.Foreground = Palette.Get(t, "BentoSecondary"));

        var (ratioPanel, ratioPills, ratioTrailing) = LabeledPills("画面比例",
            new[] { ("", "原比例") }.Concat(CropPreset.VideoPresets.Select(p => (p.Id, p.Name))), V.Crop?.Id ?? "");
        ratio = ratioPills;
        ratioTrailing.Text = "预览里的框可拖";
        ratio.Changed += id =>
        {
            Set(v => v with { Crop = CropPreset.VideoPresets.FirstOrDefault(p => p.Id == id) });
            ws.ResetVideoCrop();
        };
        var (edgePanel, edgePills, _) = LabeledPills("分辨率", new[] { (0, "原分辨率"), (1920, "1080p"), (1280, "720p") }, V.LiveLongEdge);
        edge = edgePills;
        edge.Changed += x => Set(v => v with { LiveLongEdge = x });

        var top = new StackPanel { Spacing = 12 };
        top.Children.Add(header);
        top.Children.Add(kind);
        top.Children.Add(note);
        top.Children.Add(ratioPanel);
        top.Children.Add(edgePanel);
        lockable = Lockable(Rows((top, false), (new Border(), true), (outputLine, false)));
        Content = Card.Create(CardBody.Fit(lockable));
    }

    protected override void Refresh()
    {
        WatchItems();
        var v = V;
        Show(modifiedTag, ws.Modified && ws.UsesRecipe);
        kind.Selected = v.LiveKind;
        note.Text = v.LiveKind switch
        {
            LiveKind.Apple => "一张照片配一段视频，同名放在一起。一起传到 iPhone 的「照片」里，按住就会动。",
            LiveKind.Android => "一张会动的照片，小米、三星、Google 等安卓手机的相册都认。",
            _ => "苹果实况和安卓动态照片各出一份。",
        };
        ratio.Selected = v.Crop?.Id ?? "";
        edge.Selected = v.LiveLongEdge;
        // 「输出 1080 × 1920 · 30 fps · 3 秒」：当前这段的实际输出。
        if (ws.CurrentVideo is { } item)
        {
            var size = ws.LiveOutputSize(item);
            var w = ws.LiveWindow(item);
            outputLine.Text = $"输出 {(int)size.Width} × {(int)size.Height} · {(int)Math.Round(Math.Min(30, item.Fps))} fps · {TrimBar.Seconds(w.End - w.Start)}";
            Show(outputLine, true);
        }
        else
        {
            Show(outputLine, false);
        }
        Lock(lockable, ws.Running);
    }
}

/// 封面：照片用的那一帧，大图看清楚；‹ › 逐帧微调。
internal sealed class LiveCoverCard : VideoCardBase
{
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Border imageClip;
    private readonly Border panel;
    private readonly TextBlock time = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel steps = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly ContentControl lockable;
    private string loadedKey = "";
    private CancellationTokenSource? loading;

    public LiveCoverCard(Workspace ws) : base(ws)
    {
        imageClip = new Border { CornerRadius = new CornerRadius(8), Child = image, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        panel = Panel(imageClip, new Thickness(8));
        var prev = StepButton("M15 6l-6 6 6 6", "上一帧");
        var next = StepButton("M9 6l6 6-6 6", "下一帧");
        prev.Click += (_, _) => Step(-1);
        next.Click += (_, _) => Step(1);
        Paint(time, t => t.Foreground = Palette.Get(t, "BentoPrimary"));
        steps.Children.Add(prev);
        steps.Children.Add(time);
        steps.Children.Add(next);
        var g = new Grid { RowSpacing = 10 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(Header(Icons.Image, "封面", "照片用这一帧，在时间轴上单击换"));
        Grid.SetRow(panel, 1);
        g.Children.Add(panel);
        Grid.SetRow(steps, 2);
        g.Children.Add(steps);
        lockable = Lockable(g);
        Content = Card.Create(CardBody.Fit(lockable));
        Unloaded += (_, _) => loading?.Cancel();
    }

    /// 灰底圆钮，里面是设计稿式的线性箭头（32）。
    private static Button StepButton(string icon, string label)
    {
        var back = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = new LineIcon(icon, 14, 2) };
        Paint(back, b => b.Background = Palette.Get(b, "BentoFill"));
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, label);
        ToolTipService.SetToolTip(b, label);
        return b;
    }

    protected override void Refresh()
    {
        WatchItems();
        var item = ws.CurrentVideo;
        Show(panel, item != null);
        Show(steps, item != null);
        Lock(lockable, ws.Running);
        if (item == null) return;
        var t = ws.LiveCoverTime(item);
        time.Text = Format.Timecode(t);
        AutomationProperties.SetName(panel, $"封面预览，{Format.Timecode(t)}");
        var crop = ws.VideoCropBox(item);
        var key = $"{item.FilePath}|{t:0.000}|{crop}";
        if (key == loadedKey) return;
        loadedKey = key;
        Load(item, t, crop);
    }

    private async void Load(FileItem item, double t, PixelRect? crop)
    {
        loading?.Cancel();
        var cts = loading = new CancellationTokenSource();
        try
        {
            await Task.Delay(80, cts.Token); // 连点 ‹ › 时停手再取
            var bytes = await Task.Run(() => VideoFrames.GrabAsync(item.FilePath, t, 900, crop, cts.Token));
            if (bytes == null || cts.IsCancellationRequested) return;
            if (await VideoFrames.ToImageAsync(bytes) is { } img && !cts.IsCancellationRequested)
                image.Source = img;
        }
        catch (OperationCanceledException) { }
    }

    private void Step(int dir)
    {
        if (ws.CurrentVideo is not { } item) return;
        var frame = 1 / Math.Max(1, Math.Min(30, item.Fps));
        var w = ws.LiveWindow(item);
        ws.SetLiveCover(item.FilePath, Math.Min(Math.Max(ws.LiveCoverTime(item) + dir * frame, w.Start), w.End));
    }
}

/// 声音与存放 + 开始。
internal sealed class LiveStartCard : VideoCardBase
{
    private readonly Header header = Header(Icons.Speaker, "声音与存放");
    private readonly ToggleSwitch audio;
    private readonly ContentControl options;

    public LiveStartCard(Workspace ws) : base(ws)
    {
        var (row, a) = ToggleRow("保留声音", 40);
        audio = a;
        audio.Toggled += (_, _) => Set(v => v with { KeepAudio = audio.IsOn });
        options = Lockable(row);
        var head = new StackPanel { Spacing = 8 };
        head.Children.Add(header);
        head.Children.Add(options);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 10, 0, 0) };
        Content = Card.Create(Rows((head, false), (start, true)));
        Watch(AppModel.Shared.Settings, nameof(AppSettings.OutputLocation), nameof(AppSettings.OutputFolder));
    }

    protected override void Refresh()
    {
        var s = AppModel.Shared.Settings;
        header.Subtitle = s.OutputLocation == OutputLocation.Beside ? "存在原视频旁边" : $"存在「{System.IO.Path.GetFileName(s.OutputFolder.TrimEnd('\\', '/'))}」";
        if (audio.IsOn != V.KeepAudio) audio.IsOn = V.KeepAudio;
        options.IsEnabled = !ws.Running;
    }
}
