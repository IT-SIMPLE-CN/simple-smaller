using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.Foundation;
using Windows.System;

namespace SimpleSmaller.Views.Preview;

/// 视频「剪辑」（设计稿 V2.1 · 06）：上面是画面与比例框，下面是播放、截取与时间轴。
/// 拖时间轴两端截取，单击时间轴设为封面；比例框可拖动取景。多段视频时底部可切换。
internal sealed class VideoEditView : Reactive
{
    private readonly Workspace ws;
    private readonly Grid root = new() { RowSpacing = 12 };
    private readonly VideoStage stage;
    private readonly TrimBar trim;
    private readonly VideoFilmStrip strip;
    private readonly ProgressRing waiting = new() { IsActive = true, Width = 32, Height = 32 };
    private VideoPlayback? playback;
    private string? loadedPath;

    public VideoEditView(Workspace ws)
    {
        this.ws = ws;
        stage = new VideoStage(ws);
        trim = new TrimBar(ws);
        strip = new VideoFilmStrip(ws);
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(stage);
        Grid.SetRow(trim, 1);
        root.Children.Add(trim);
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
            stage.Playback = trim.Playback = playback;
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
            stage.Playback = trim.Playback = null;
            // 先放开画面元素（SetMediaPlayer(null)），再隔一轮释放 MediaPlayer：画面元素还在卸载流程里时释放会让 XAML 崩（E_ABORT）。
            stage.Detach();
            playback = null;
            p.Pause();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, p.Dispose);
        });
        // 空格播放 / 暂停（焦点在预览里时）。
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Space && playback is { } p)
            {
                p.Toggle();
                e.Handled = true;
            }
        };
        PointerPressed += (_, _) => Focus(FocusState.Pointer);
    }

    private void PlaybackChanged() => trim.UpdatePlayhead();

    protected override void Refresh()
    {
        // 元信息读完才进 VideoItems：给每个文件挂一次。
        foreach (var f in ws.Files)
        {
            f.PropertyChanged -= ItemChanged;
            f.PropertyChanged += ItemChanged;
        }
        var item = ws.CurrentVideo;
        Show(waiting, item == null);
        Show(stage, item != null);
        Show(trim, item != null);
        Show(strip, item != null && ws.VideoItems.Count > 1);
        if (item == null) return;
        if (playback is { } p)
        {
            if (loadedPath != item.FilePath)
            {
                loadedPath = item.FilePath;
                if (!MediaTypes.IsAnimated(item.FilePath)) p.Load(item.FilePath, ws.TrimRange(item));
                else p.Pause();
            }
            else
            {
                p.Range = ws.TrimRange(item);
            }
        }
        stage.Update(item);
        trim.Update(item);
        strip.Update();
    }

    private void ItemChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItem.Duration) or nameof(FileItem.Width)) Schedule();
    }

    private static void Show(UIElement e, bool on) => e.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
}

/// 画面：深色底（#151618）上等比放视频，比例框白色 2px、框外压暗 55%，左上角标签「9:16 · 1080 × 1920 · 框可拖」。
internal sealed class VideoStage : UserControl
{
    private static readonly SolidColorBrush White = new(Microsoft.UI.Colors.White);
    private readonly Workspace ws;
    private readonly Canvas canvas = new();
    private readonly Border host;
    /// 框外压暗：上、下、左、右四块。
    private readonly Border[] dims = Enumerable.Range(0, 4).Select(_ => new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)), IsHitTestVisible = false }).ToArray();
    private readonly Border box = new() { BorderBrush = White, BorderThickness = new Thickness(2), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock labelText = new() { FontSize = 11, Foreground = White };
    private readonly Border label;
    private MediaPlayerElement? player;
    private Image? animated;
    private FileItem? item;
    private VideoPlayback? playback;
    private PixelRect? dragStart;
    private Point dragOrigin;
    private (double Scale, Rect Rect) layout;

    public VideoStage(Workspace ws)
    {
        this.ws = ws;
        label = new Border
        {
            Child = labelText,
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(36, 255, 255, 255)),
            IsHitTestVisible = false,
        };
        labelText.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetName(box, "取景框，可拖动");
        foreach (var d in dims) canvas.Children.Add(d);
        canvas.Children.Add(box);
        canvas.Children.Add(label);
        // 深色底只铺在自己的区域。
        host = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x15, 0x16, 0x18)),
            Child = canvas,
            RequestedTheme = ElementTheme.Dark,
        };
        Content = host;
        SizeChanged += (_, _) => Layout();
        box.AddHandler(PointerPressedEvent, new PointerEventHandler(BoxPressed), true);
        box.PointerMoved += BoxMoved;
        box.PointerReleased += BoxReleased;
        box.PointerCaptureLost += (_, _) => dragStart = null;
        box.ManipulationMode = ManipulationModes.None;
    }

    public VideoPlayback? Playback
    {
        get => playback;
        set
        {
            playback = value;
            if (player != null && value != null) player.SetMediaPlayer(value.Player);
        }
    }

    /// 播放器要关掉时先把画面元素放开。
    public void Detach()
    {
        if (player != null)
        {
            player.SetMediaPlayer(null);
            canvas.Children.Remove(player);
            player = null;
        }
    }

    public void Update(FileItem item)
    {
        var changed = this.item?.FilePath != item.FilePath;
        this.item = item;
        var isAnimated = MediaTypes.IsAnimated(item.FilePath);
        if (isAnimated)
        {
            Detach();
            if (animated == null)
            {
                animated = new Image { Stretch = Stretch.Fill };
                AutomationProperties.SetName(animated, "视频画面");
                canvas.Children.Insert(0, animated);
            }
            if (changed || animated.Source == null) animated.Source = new BitmapImage(new Uri(item.FilePath));
        }
        else
        {
            if (animated != null)
            {
                canvas.Children.Remove(animated);
                animated = null;
            }
            if (player == null && playback != null)
            {
                player = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Fill, IsHitTestVisible = false };
                AutomationProperties.SetName(player, "视频画面");
                player.SetMediaPlayer(playback.Player);
                canvas.Children.Insert(0, player);
            }
        }
        var size = ws.VideoSegment == VideoSegment.Live ? ws.LiveOutputSize(item) : ws.VideoOutputSize(item);
        var crop = ws.Draft.Video.Crop;
        labelText.Text = $"{crop?.Name ?? "原比例"} · {(int)size.Width} × {(int)size.Height}{(crop != null ? " · 框可拖" : "")}";
        Layout();
    }

    private void Layout()
    {
        if (item == null || ActualWidth <= 0) return;
        double w = ActualWidth, h = ActualHeight;
        // 画面铺满深色底的宽或高（设计稿不留边）。
        var aw = Math.Max(1, w);
        var ah = Math.Max(1, h);
        var scale = Math.Min(aw / Math.Max(1, item.Width), ah / Math.Max(1, item.Height));
        var dw = item.Width * scale;
        var dh = item.Height * scale;
        var rect = new Rect(Math.Round((w - dw) / 2), Math.Round((h - dh) / 2), dw, dh);
        layout = (scale, rect);
        foreach (var e in new FrameworkElement?[] { player, animated })
        {
            if (e == null) continue;
            e.Width = dw;
            e.Height = dh;
            Canvas.SetLeft(e, rect.X);
            Canvas.SetTop(e, rect.Y);
        }
        // 标签贴深色底的左上角（14, 14），不跟画面走。
        Canvas.SetLeft(label, 14);
        Canvas.SetTop(label, 14);
        if (ws.VideoCropBox(item) is { } b)
        {
            var v = ToView(b);
            Place(dims[0], rect.X, rect.Y, rect.Width, v.Y - rect.Y);
            Place(dims[1], rect.X, v.Y + v.Height, rect.Width, rect.Y + rect.Height - v.Y - v.Height);
            Place(dims[2], rect.X, v.Y, v.X - rect.X, v.Height);
            Place(dims[3], v.X + v.Width, v.Y, rect.X + rect.Width - v.X - v.Width, v.Height);
            box.Visibility = Visibility.Visible;
            box.Width = v.Width;
            box.Height = v.Height;
            Canvas.SetLeft(box, v.X);
            Canvas.SetTop(box, v.Y);
        }
        else
        {
            foreach (var d in dims) d.Visibility = Visibility.Collapsed;
            box.Visibility = Visibility.Collapsed;
        }
    }

    private static void Place(Border r, double x, double y, double w, double h)
    {
        r.Visibility = Visibility.Visible;
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w);
        r.Height = Math.Max(0, h);
    }

    private Rect ToView(PixelRect r) =>
        new(layout.Rect.X + r.X * layout.Scale, layout.Rect.Y + r.Y * layout.Scale, r.Width * layout.Scale, r.Height * layout.Scale);

    private void BoxPressed(object sender, PointerRoutedEventArgs e)
    {
        if (item == null || ws.VideoCropBox(item) is not { } b || ws.Running) return;
        dragStart = b;
        dragOrigin = e.GetCurrentPoint(canvas).Position;
        box.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void BoxMoved(object sender, PointerRoutedEventArgs e)
    {
        if (dragStart is not { } start || item == null) return;
        var p = e.GetCurrentPoint(canvas).Position;
        var dx = (p.X - dragOrigin.X) / layout.Scale;
        var dy = (p.Y - dragOrigin.Y) / layout.Scale;
        var x = Math.Min(Math.Max(0, start.X + dx), item.Width - start.Width);
        var y = Math.Min(Math.Max(0, start.Y + dy), item.Height - start.Height);
        // 编码要求偶数坐标。
        x = Math.Round(x / 2) * 2;
        y = Math.Round(y / 2) * 2;
        ws.SetVideoCrop(item.FilePath, start with { X = x, Y = y });
        e.Handled = true;
    }

    private void BoxReleased(object sender, PointerRoutedEventArgs e)
    {
        BoxMoved(sender, e);
        dragStart = null;
        box.ReleasePointerCapture(e.Pointer);
    }
}

/// 播放、截取与时间轴（设计稿 V2.1 · 06）：黑色圆形播放钮、时间、浅红「截取 00:18 – 01:03 · 45 秒」、右边提示；
/// 下面 52 高的缩略图条，截取范围外蒙白，范围是品牌红框（上下 3、两端 12），白色播放头。
internal sealed class TrimBar : UserControl
{
    private const int Count = 14;
    private const double HandleWidth = 12;
    private const double BarHeight = 52;
    private readonly Workspace ws;
    private readonly Button play = new() { Style = (Style)Application.Current.Resources["BentoBareButton"] };
    private readonly Border playBack = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly TextBlock time = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock range = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border rangeBack = new() { Height = 28, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock hint = new() { Text = "拖两端截取 · 单击设为封面", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid timeline = new() { Height = BarHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Grid thumbs = new() { ColumnSpacing = 2 };
    private readonly Border[] cells = new Border[Count];
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle dimLeft = new(), dimRight = new();
    private readonly Border frame = new() { BorderBrush = Palette.Brand, BorderThickness = new Thickness(HandleWidth, 3, HandleWidth, 3), CornerRadius = new CornerRadius(10) };
    private readonly Border cover;
    private readonly Border playhead = new() { Width = 2, Height = BarHeight + 4, Background = new SolidColorBrush(Microsoft.UI.Colors.White) };
    private readonly Border playheadShade = new() { Width = 4, Height = BarHeight + 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(70, 0, 0, 0)) };
    private Microsoft.UI.Xaml.Shapes.Path playPath = new();
    private bool playing;
    private FileItem? item;
    private string? thumbsFor;
    private CancellationTokenSource? thumbsCts;
    private enum Handle { None, Start, End }
    private Handle dragging = Handle.None;
    private bool pressed;
    private Point pressAt;

    public TrimBar(Workspace ws)
    {
        this.ws = ws;
        Padding = new Thickness(6, 4, 6, 2);
        play.Content = playBack;
        SetPlayIcon(false);
        Motion.SetPress(play, true);
        AutomationProperties.SetName(play, "播放");
        ToolTipService.SetToolTip(play, "播放 / 暂停（空格）");
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
        var thumbsClip = new Border { CornerRadius = new CornerRadius(12), Child = thumbs };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(thumbsClip, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        timeline.Children.Add(thumbsClip);

        frame.Height = BarHeight;
        var coverIcon = new FontIcon { Glyph = "", FontSize = 8, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        cover = new Border { Width = 16, Height = 14, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(153, 0, 0, 0)), Child = coverIcon };
        dimLeft.Height = dimRight.Height = BarHeight;
        overlay.Children.Add(frame);
        overlay.Children.Add(cover);
        overlay.Children.Add(playheadShade);
        overlay.Children.Add(playhead);
        // 两端蒙白要跟着缩略图条的圆角裁掉（Border 按圆角裁子元素）。
        var dims = new Canvas { IsHitTestVisible = false };
        dims.Children.Add(dimLeft);
        dims.Children.Add(dimRight);
        timeline.Children.Add(new Border { CornerRadius = new CornerRadius(12), Child = dims, IsHitTestVisible = false });
        timeline.Children.Add(overlay);
        AutomationProperties.SetName(timeline, "时间轴");
        timeline.SizeChanged += (_, _) => LayoutOverlay();
        timeline.AddHandler(PointerPressedEvent, new PointerEventHandler(Pressed), true);
        timeline.PointerMoved += Moved;
        timeline.PointerReleased += Released;
        timeline.PointerCaptureLost += (_, _) => { pressed = false; dragging = Handle.None; };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(top);
        stack.Children.Add(timeline);
        Content = stack;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    public VideoPlayback? Playback { get; set; }

    /// 白色实心三角 / 两竖（14）。
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
        var veil = new SolidColorBrush(Windows.UI.Color.FromArgb(184, card.R, card.G, card.B));
        dimLeft.Fill = dimRight.Fill = veil;
        var fill = Palette.Get(this, "BentoFill");
        foreach (var c in cells) c.Background = fill;
    }

    /// 「2.5 秒」「12 秒」「1 分 5 秒」。
    public static string Seconds(double s)
    {
        if (s < 10) return $"{Math.Round(s, 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} 秒";
        var t = (int)Math.Round(s);
        return t >= 60 ? $"{t / 60} 分 {t % 60} 秒" : $"{t} 秒";
    }

    public void Update(FileItem item)
    {
        this.item = item;
        var r = ws.TrimRange(item);
        var end = r.End < 1 ? r.End : Math.Round(r.End);
        range.Text = $"截取 {Format.Duration(r.Start)} – {Format.Duration(end)} · {Seconds(r.End - r.Start)}";
        AutomationProperties.SetItemStatus(timeline, $"截取 {Format.Duration(r.Start)} 到 {Format.Duration(r.End)}");
        if (thumbsFor != item.FilePath)
        {
            thumbsFor = item.FilePath;
            LoadThumbs(item);
        }
        LayoutOverlay();
        UpdatePlayhead();
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
        var r = ws.TrimRange(item);
        var x0 = r.Start / d * w;
        var x1 = r.End / d * w;
        // 蒙白：左边从 0 到截取起点、右边从终点到头。
        dimLeft.Width = Math.Max(0, x0);
        dimRight.Width = Math.Max(0, w - x1);
        Canvas.SetLeft(dimRight, x1);
        frame.Width = Math.Max(HandleWidth * 2, x1 - x0);
        Canvas.SetLeft(frame, Math.Min(x0, w - frame.Width));
        if (ws.Draft.Video.ExportCover && ws.CoverAt.TryGetValue(item.FilePath, out var c))
        {
            cover.Visibility = Visibility.Visible;
            Canvas.SetLeft(cover, c / d * w - 8);
            Canvas.SetTop(cover, BarHeight - 18);
        }
        else
        {
            cover.Visibility = Visibility.Collapsed;
        }
        UpdatePlayhead();
    }

    public void UpdatePlayhead()
    {
        var p = Playback;
        var t = p?.Time ?? 0;
        time.Text = Format.Timecode(t);
        SetPlayIcon(p?.Playing == true);
        AutomationProperties.SetName(play, p?.Playing == true ? "暂停" : "播放");
        if (item == null) return;
        var w = timeline.ActualWidth;
        var d = Math.Max(0.001, item.Duration);
        var x = Math.Min(Math.Max(t, 0), d) / d * w;
        Canvas.SetLeft(playhead, x - 1);
        Canvas.SetTop(playhead, -2);
        Canvas.SetLeft(playheadShade, x - 2);
        Canvas.SetTop(playheadShade, -2);
    }

    private double TimeAt(double x) => item == null ? 0 : Math.Min(Math.Max(0, x / Math.Max(1, timeline.ActualWidth) * item.Duration), item.Duration);

    /// 按在两端把手附近：拖动截取；其余位置：拖动预览，松手时（没怎么动）设为封面。

    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (item == null || ws.Running) return;
        pressed = true;
        pressAt = e.GetCurrentPoint(timeline).Position;
        var w = timeline.ActualWidth;
        var d = item.Duration;
        var r = ws.TrimRange(item);
        var x0 = r.Start / d * w;
        var x1 = r.End / d * w;
        dragging = Math.Abs(pressAt.X - x0) <= 14 ? Handle.Start : Math.Abs(pressAt.X - x1) <= 14 ? Handle.End : Handle.None;
        timeline.CapturePointer(e.Pointer);
        Drag(pressAt.X);
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
        var t = TimeAt(x);
        var r = ws.TrimRange(item);
        var minLength = Math.Min(0.5, item.Duration);
        switch (dragging)
        {
            case Handle.Start:
            {
                var s = Math.Min(t, r.End - minLength);
                var next = new TimeRange(s, r.End);
                ws.SetTrim(item.FilePath, next);
                if (Playback is { } p) { p.Range = next; p.Seek(s); }
                break;
            }
            case Handle.End:
            {
                var end = Math.Max(t, r.Start + minLength);
                var next = new TimeRange(r.Start, end);
                ws.SetTrim(item.FilePath, next);
                if (Playback is { } p) { p.Range = next; p.Seek(end); }
                break;
            }
            default:
                if (Playback is { } q)
                {
                    if (q.Playing) q.Pause();
                    q.Seek(t);
                }
                break;
        }
    }

    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (!pressed || item == null) return;
        pressed = false;
        var pt = e.GetCurrentPoint(timeline).Position;
        // 拖动中合并掉的最后一段移动：按松手位置补一次。
        if (dragging != Handle.None) Drag(pt.X);
        timeline.ReleasePointerCapture(e.Pointer);
        if (dragging == Handle.None && Math.Abs(pt.X - pressAt.X) < 3)
        {
            ws.SetCoverAt(item.FilePath, TimeAt(pt.X));
            if (!ws.Draft.Video.ExportCover) ws.Draft = ws.Draft with { Video = ws.Draft.Video with { ExportCover = true } };
        }
        // 截取全片时不记，免得「已修改」误报。
        if (ws.Trim.TryGetValue(item.FilePath, out var r) && r.Start <= 0.01 && r.End >= item.Duration - 0.01) ws.SetTrim(item.FilePath, null);
        dragging = Handle.None;
        e.Handled = true;
    }
}
