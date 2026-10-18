using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.Foundation;
using Windows.System;

namespace SimpleSmaller.Views.Preview;

/// 视频「实况」（对应 mac LiveEditView）：上面是画面与比例框（同剪辑），下面是时间轴：红框框住会动的那一段（最长 3 秒），
/// 拖红框挪位置、拖两端改长短；单击时间轴选封面（照片用的那一帧）。多段视频时底部可切换。
internal sealed class LiveEditView : Reactive
{
    private readonly Workspace ws;
    private readonly Grid root = new() { RowSpacing = 12 };
    private readonly VideoStage stage;
    private readonly LiveTimeline timeline;
    private readonly VideoFilmStrip strip;
    private readonly ProgressRing waiting = new() { IsActive = true, Width = 32, Height = 32 };
    private VideoPlayback? playback;
    private string? loadedPath;

    public LiveEditView(Workspace ws)
    {
        this.ws = ws;
        stage = new VideoStage(ws);
        timeline = new LiveTimeline(ws);
        strip = new VideoFilmStrip(ws) { Badge = item => { var w = ws.LiveWindow(item); return TrimBar.Seconds(w.End - w.Start); } };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(stage);
        Grid.SetRow(timeline, 1);
        root.Children.Add(timeline);
        Grid.SetRow(strip, 2);
        root.Children.Add(strip);
        root.Children.Add(waiting);
        Grid.SetRowSpan(waiting, 3);
        waiting.HorizontalAlignment = HorizontalAlignment.Center;
        waiting.VerticalAlignment = VerticalAlignment.Center;
        Content = root;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        Watch(ws);
        WatchList(ws.Files);
        Loaded += (_, _) =>
        {
            playback ??= new VideoPlayback(DispatcherQueue);
            playback.Changed -= PlaybackChanged;
            playback.Changed += PlaybackChanged;
            stage.Playback = timeline.Playback = playback;
            loadedPath = null;
            Refresh();
            Focus(FocusState.Programmatic);
        };
        // 窗口变宽变窄时网格会把预览挪个位置：先触发新的 Loaded、后触发旧的 Unloaded。等这一轮走完再看，
        // 还在界面上就不拆，免得播放器被关掉、画面变黑。
        Unloaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsLoaded || playback is not { } p) return;
            p.Changed -= PlaybackChanged;
            stage.Playback = timeline.Playback = null;
            // 先放开画面元素（SetMediaPlayer(null)），再隔一轮释放 MediaPlayer：画面元素还在卸载流程里时释放会让 XAML 崩（E_ABORT）。
            stage.Detach();
            playback = null;
            p.Pause();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, p.Dispose);
        });
        // 空格播放这一段；← → 把红框挪 0.5 秒（焦点在预览里时）。
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Space && playback is { } p)
            {
                p.Toggle();
                e.Handled = true;
            }
            else if (e.Key is VirtualKey.Left or VirtualKey.Right && ws.CurrentVideo is { } item && !ws.Running)
            {
                timeline.Nudge(item, e.Key == VirtualKey.Right ? 0.5 : -0.5);
                e.Handled = true;
            }
        };
        PointerPressed += (_, _) => Focus(FocusState.Pointer);
    }

    private void PlaybackChanged() => timeline.UpdatePlayhead();

    protected override void Refresh()
    {
        foreach (var f in ws.Files)
        {
            f.PropertyChanged -= ItemChanged;
            f.PropertyChanged += ItemChanged;
        }
        var item = ws.CurrentVideo;
        Show(waiting, item == null);
        Show(stage, item != null);
        Show(timeline, item != null);
        Show(strip, item != null && ws.VideoItems.Count > 1);
        if (item == null) return;
        if (playback is { } p)
        {
            var window = ws.LiveWindow(item);
            if (loadedPath != item.FilePath)
            {
                loadedPath = item.FilePath;
                if (!MediaTypes.IsAnimated(item.FilePath))
                {
                    p.Load(item.FilePath, window);
                    p.Seek(ws.LiveCoverTime(item));
                }
                else
                {
                    p.Pause();
                }
            }
            else
            {
                p.Range = window;
            }
        }
        stage.Update(item);
        timeline.Update(item);
        strip.Update();
    }

    private void ItemChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItem.Duration) or nameof(FileItem.Width)) Schedule();
    }

    private static void Show(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
}

/// 时间轴（样子同剪辑的截取条）：上面播放钮、时间、浅红「会动的部分 2.5 秒 · 封面 00:05.2」、右边提示；
/// 下面缩略图条上一个品牌红框（会动的那一段，框外蒙白），框里一条白线是封面，线顶上一个红色小照片标。
internal sealed class LiveTimeline : UserControl
{
    private const int Count = 14;
    private const double HandleWidth = 10;
    private const double BarHeight = 52;
    /// 最短 0.5 秒。
    private const double MinLength = 0.5;
    private readonly Workspace ws;
    private readonly Button play = new() { Style = (Style)Application.Current.Resources["BentoBareButton"] };
    private readonly Border playBack = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly TextBlock time = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock range = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border rangeBack = new() { Height = 28, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock hint = new() { Text = "拖红框选一段（最长 3 秒）· 单击选封面", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right, MaxLines = 1 };
    private readonly Grid timeline = new() { Height = BarHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Margin = new Thickness(0, 18, 0, 0) };
    private readonly Grid thumbs = new() { ColumnSpacing = 2 };
    private readonly Border[] cells = new Border[Count];
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle dimLeft = new(), dimRight = new();
    private readonly Grid frame = new() { Height = BarHeight };
    private readonly Border frameBorder = new() { BorderBrush = Palette.Brand, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6) };
    private readonly Border coverLine = new() { Width = 2, Height = BarHeight - 6, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
    private readonly Border coverTag = new() { Width = 18, Height = 14, CornerRadius = new CornerRadius(3), Background = Palette.Brand };
    private readonly Border playhead = new() { Width = 2, Height = BarHeight, Background = Palette.Brand };
    private Microsoft.UI.Xaml.Shapes.Path playPath = new();
    private bool playing;
    private FileItem? item;
    private string? thumbsFor;
    private CancellationTokenSource? thumbsCts;
    private enum DragKind { None, Start, End, Move, Scrub }
    private DragKind drag = DragKind.None;
    private bool pressed;
    private Point pressAt;
    private TimeRange pressWindow;

    public LiveTimeline(Workspace ws)
    {
        this.ws = ws;
        Padding = new Thickness(6, 4, 6, 2);
        play.Content = playBack;
        SetPlayIcon(false);
        Motion.SetPress(play, true);
        AutomationProperties.SetName(play, "播放");
        ToolTipService.SetToolTip(play, "播放这一段（空格）");
        play.Click += (_, _) => Playback?.Toggle();
        rangeBack.Child = range;
        var top = new Grid { ColumnSpacing = 12 };
        foreach (var w in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star) })
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        top.Children.Add(play);
        Grid.SetColumn(time, 1);
        top.Children.Add(time);
        Grid.SetColumn(rangeBack, 2);
        top.Children.Add(rangeBack);
        Grid.SetColumn(hint, 3);
        top.Children.Add(hint);

        for (var k = 0; k < Count; k++)
        {
            thumbs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cells[k] = new Border { Child = VideoFrames.DecorImage() };
            Grid.SetColumn(cells[k], k);
            thumbs.Children.Add(cells[k]);
        }
        var thumbsClip = new Border { CornerRadius = new CornerRadius(8), Child = thumbs };
        AutomationProperties.SetAccessibilityView(thumbsClip, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        timeline.Children.Add(thumbsClip);

        // 红框：品牌红描边，两端宽把手中间一道白色握痕（同剪辑的截取框）。
        frame.Children.Add(frameBorder);
        frame.Children.Add(Grip(HorizontalAlignment.Left));
        frame.Children.Add(Grip(HorizontalAlignment.Right));
        coverTag.Child = new LineIcon(Icons.Image, 10, 2.4, "OnBrand") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dimLeft.Height = dimRight.Height = BarHeight;
        overlay.Children.Add(frame);
        overlay.Children.Add(coverLine);
        overlay.Children.Add(coverTag);
        overlay.Children.Add(playhead);
        // 两端蒙白跟着缩略图条的圆角裁掉。
        var dims = new Canvas { IsHitTestVisible = false };
        dims.Children.Add(dimLeft);
        dims.Children.Add(dimRight);
        timeline.Children.Add(new Border { CornerRadius = new CornerRadius(8), Child = dims, IsHitTestVisible = false });
        timeline.Children.Add(overlay);
        AutomationProperties.SetName(timeline, "实况时间轴");
        timeline.SizeChanged += (_, _) => LayoutOverlay();
        timeline.AddHandler(PointerPressedEvent, new PointerEventHandler(Pressed), true);
        timeline.PointerMoved += Moved;
        timeline.PointerReleased += Released;
        timeline.PointerCaptureLost += (_, _) => { pressed = false; drag = DragKind.None; };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(top);
        stack.Children.Add(timeline);
        Content = stack;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    public VideoPlayback? Playback { get; set; }

    private static Border Grip(HorizontalAlignment side)
    {
        var mark = new Border { Width = 2, Height = 14, CornerRadius = new CornerRadius(1), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(230, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border { Width = HandleWidth, CornerRadius = new CornerRadius(4), Background = Palette.Brand, HorizontalAlignment = side, Child = mark };
    }

    private void SetPlayIcon(bool isPlaying)
    {
        if (playBack.Child != null && isPlaying == playing) return;
        playing = isPlaying;
        playBack.Child = Glyph24.Create(isPlaying ? Icons.PauseFill : Icons.PlayFill, 14, out var path);
        playPath = path;
        playPath.Fill = Palette.Get(this, "BentoOnInk");
    }

    private void Paint()
    {
        playBack.Background = Palette.Get(this, "BentoInk");
        playPath.Fill = Palette.Get(this, "BentoOnInk");
        time.Foreground = Palette.Get(this, "BentoPrimary");
        hint.Foreground = Palette.Get(this, "BentoSecondary");
        rangeBack.Background = Palette.Get(this, "BentoAlertFill");
        range.Foreground = Palette.Get(this, "BentoAlertText");
        var card = ((SolidColorBrush)Palette.Get(this, "BentoCard")).Color;
        var veil = new SolidColorBrush(Windows.UI.Color.FromArgb(178, card.R, card.G, card.B));
        dimLeft.Fill = dimRight.Fill = veil;
        var fill = Palette.Get(this, "BentoFill");
        foreach (var c in cells) c.Background = fill;
    }

    public void Update(FileItem item)
    {
        this.item = item;
        var w = ws.LiveWindow(item);
        var cover = ws.LiveCoverTime(item);
        range.Text = $"会动的部分 {TrimBar.Seconds(w.End - w.Start)} · 封面 {Format.Timecode(cover)}";
        AutomationProperties.SetItemStatus(timeline, $"会动的部分 {Format.Timecode(w.Start)} 到 {Format.Timecode(w.End)}，封面 {Format.Timecode(cover)}");
        if (thumbsFor != item.FilePath)
        {
            thumbsFor = item.FilePath;
            LoadThumbs(item);
        }
        LayoutOverlay();
    }

    private async void LoadThumbs(FileItem item)
    {
        thumbsCts?.Cancel();
        var cts = thumbsCts = new CancellationTokenSource();
        foreach (var c in cells) ((Image)c.Child).Source = null;
        var got = new List<(int, byte[])>();
        try
        {
            await Task.Run(() => VideoFrames.StripAsync(item.FilePath, item.Duration, Count, (int)(BarHeight * 2), (k, b) => { lock (got) got.Add((k, b)); }, cts.Token));
        }
        catch { }
        if (cts.IsCancellationRequested) return;
        foreach (var (k, b) in got)
        {
            if (await VideoFrames.ToImageAsync(b) is { } img && !cts.IsCancellationRequested)
                ((Image)cells[k].Child).Source = img;
        }
    }

    private void LayoutOverlay()
    {
        if (item == null) return;
        var w = timeline.ActualWidth;
        if (w <= 0) return;
        var d = Math.Max(0.001, item.Duration);
        var r = ws.LiveWindow(item);
        var x0 = r.Start / d * w;
        var x1 = r.End / d * w;
        dimLeft.Width = Math.Max(0, x0);
        dimRight.Width = Math.Max(0, w - x1);
        Canvas.SetLeft(dimRight, x1);
        frame.Width = Math.Max(HandleWidth * 2 + 4, x1 - x0);
        Canvas.SetLeft(frame, Math.Min(x0, w - frame.Width));
        var cx = ws.LiveCoverTime(item) / d * w;
        Canvas.SetLeft(coverLine, cx - 1);
        Canvas.SetTop(coverLine, 3);
        Canvas.SetLeft(coverTag, cx - 9);
        Canvas.SetTop(coverTag, -17);
        UpdatePlayhead();
    }

    /// 播放头（播放时才显示，免得和封面线混在一起）。
    public void UpdatePlayhead()
    {
        var p = Playback;
        var t = p?.Time ?? 0;
        time.Text = Format.Timecode(t);
        SetPlayIcon(p?.Playing == true);
        AutomationProperties.SetName(play, p?.Playing == true ? "暂停" : "播放");
        playhead.Visibility = p?.Playing == true ? Visibility.Visible : Visibility.Collapsed;
        if (item == null) return;
        var w = timeline.ActualWidth;
        var d = Math.Max(0.001, item.Duration);
        Canvas.SetLeft(playhead, Math.Min(Math.Max(t, 0), d) / d * w - 1);
    }

    /// 键盘挪红框（← →）。
    public void Nudge(FileItem item, double seconds)
    {
        ws.MoveLiveWindow(item, ws.LiveWindow(item).Start + seconds);
        SyncPlayback(item, seek: true);
    }

    private double TimeAt(double x) => item == null ? 0 : Math.Min(Math.Max(0, x / Math.Max(1, timeline.ActualWidth) * item.Duration), item.Duration);

    private void SyncPlayback(FileItem item, bool seek)
    {
        if (Playback is not { } p) return;
        p.Range = ws.LiveWindow(item);
        if (seek) p.Seek(ws.LiveCoverTime(item));
    }

    /// 按在两端把手附近：改长短；按在红框里：拖动整段；松手时没怎么动：选封面（框外点一下，框跟过去）。
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (item == null || ws.Running) return;
        pressed = true;
        pressAt = e.GetCurrentPoint(timeline).Position;
        pressWindow = ws.LiveWindow(item);
        var w = timeline.ActualWidth;
        var d = Math.Max(0.001, item.Duration);
        var x0 = pressWindow.Start / d * w;
        var x1 = pressWindow.End / d * w;
        drag = Math.Abs(pressAt.X - x0) <= 12 ? DragKind.Start
            : Math.Abs(pressAt.X - x1) <= 12 ? DragKind.End
            : pressAt.X > x0 && pressAt.X < x1 ? DragKind.Move
            : DragKind.Scrub;
        timeline.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed) return;
        Drag(e.GetCurrentPoint(timeline).Position.X);
        e.Handled = true;
    }

    private void Drag(double x)
    {
        if (item == null) return;
        var d = Math.Max(0.001, item.Duration);
        var t = TimeAt(x);
        var window = ws.LiveWindow(item);
        var maxLength = Math.Min(Workspace.LiveMaxSeconds, d);
        var minLength = Math.Min(MinLength, d);
        switch (drag)
        {
            case DragKind.Start:
            {
                var s = Math.Min(Math.Max(t, window.End - maxLength), window.End - minLength);
                var next = new TimeRange(s, window.End);
                ws.SetLiveRange(item.FilePath, next);
                if (Playback is { } p) { p.Range = next; p.Seek(s); }
                break;
            }
            case DragKind.End:
            {
                var end = Math.Max(Math.Min(t, window.Start + maxLength), window.Start + minLength);
                var next = new TimeRange(window.Start, end);
                ws.SetLiveRange(item.FilePath, next);
                if (Playback is { } p) { p.Range = next; p.Seek(end); }
                break;
            }
            case DragKind.Move:
            {
                var dx = x - pressAt.X;
                if (Math.Abs(dx) < 3) return;
                ws.MoveLiveWindow(item, pressWindow.Start + dx / Math.Max(1, timeline.ActualWidth) * d);
                SyncPlayback(item, seek: true);
                break;
            }
        }
    }

    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed || item == null) return;
        pressed = false;
        var pt = e.GetCurrentPoint(timeline).Position;
        if (drag != DragKind.None && drag != DragKind.Scrub) Drag(pt.X);
        timeline.ReleasePointerCapture(e.Pointer);
        var still = Math.Abs(pt.X - pressAt.X) < 3;
        if (still && drag is DragKind.Move or DragKind.Scrub)
        {
            // 框外点：先把框挪过去（以这一刻为中心），再把封面放在这里。
            var t = TimeAt(pt.X);
            var current = ws.LiveWindow(item);
            if (t < current.Start || t > current.End) ws.MoveLiveWindow(item, t - (current.End - current.Start) / 2);
            ws.SetLiveCover(item.FilePath, t);
            Playback?.Pause();
            SyncPlayback(item, seek: true);
        }
        // 封面挪出了框：拉回框里。
        if (ws.LiveCover.TryGetValue(item.FilePath, out var c))
        {
            var r = ws.LiveWindow(item);
            var clamped = Math.Min(Math.Max(c, r.Start), r.End);
            if (clamped != c) ws.SetLiveCover(item.FilePath, clamped);
        }
        drag = DragKind.None;
        e.Handled = true;
    }
}
