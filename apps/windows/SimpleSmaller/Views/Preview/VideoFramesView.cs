using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.Foundation;

namespace SimpleSmaller.Views.Preview;

/// 视频「序列帧」（设计稿 V2.1 · 07）：上面灰底面板是范围与要导出的帧（竖线），下面灰底面板预览前 16 帧；张数写在右边的「抽帧」卡里。
internal sealed class VideoFramesView : Reactive
{
    private readonly Workspace ws;
    private readonly FrameRangeBar rangeBar;
    private readonly FramePreviewGrid preview;
    private readonly VideoFilmStrip strip;
    private readonly ProgressRing waiting = new() { IsActive = true, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public VideoFramesView(Workspace ws)
    {
        this.ws = ws;
        rangeBar = new FrameRangeBar(ws);
        preview = new FramePreviewGrid(ws);
        strip = new VideoFilmStrip(ws);
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(rangeBar);
        Grid.SetRow(preview, 1);
        root.Children.Add(preview);
        Grid.SetRow(strip, 2);
        root.Children.Add(strip);
        root.Children.Add(waiting);
        Grid.SetRowSpan(waiting, 3);
        Content = root;
        Watch(ws);
        WatchList(ws.Files);
    }

    protected override void Refresh()
    {
        foreach (var f in ws.Files)
        {
            f.PropertyChanged -= ItemChanged;
            f.PropertyChanged += ItemChanged;
        }
        var item = ws.CurrentVideo;
        waiting.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
        rangeBar.Visibility = preview.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        strip.Visibility = item != null && ws.VideoItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (item == null) return;
        rangeBar.Update(item);
        preview.Update(item);
        strip.Update();
    }

    private void ItemChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FileItem.Duration) or nameof(FileItem.Width)) Schedule();
    }
}

/// 范围条：浅红标签「范围：全片 00:00 – 00:10」+ 右边提示；缩略图底上白色竖线是每一帧（最多 60 条），品牌红框两端可拖，只导出一段。
internal sealed class FrameRangeBar : UserControl
{
    private const int Count = 14;
    private const double BarHeight = 48;
    private readonly Workspace ws;
    private readonly TextBlock title = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border titleBack = new() { Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(13) };
    private readonly TextBlock hint = new() { Text = "竖线是要导出的帧，拖两端只导出一段", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border panel = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 12, 14, 12) };
    private readonly Grid bar = new() { Height = BarHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly Grid thumbs = new() { ColumnSpacing = 1 };
    private readonly Border[] cells = new Border[Count];
    private readonly Canvas overlay = new() { IsHitTestVisible = false };
    private readonly Rectangle dimLeft = new() { Height = BarHeight }, dimRight = new() { Height = BarHeight };
    private readonly Microsoft.UI.Xaml.Shapes.Path ticks = new() { StrokeThickness = 1 };
    private readonly Border frame = new() { BorderThickness = new Thickness(12, 3, 12, 3), CornerRadius = new CornerRadius(12), Height = BarHeight };
    private FileItem? item;
    private string? thumbsFor;
    private CancellationTokenSource? cts;
    private bool? draggingStart;

    public FrameRangeBar(Workspace ws)
    {
        this.ws = ws;
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBack.Child = title;
        head.Children.Add(titleBack);
        Grid.SetColumn(hint, 1);
        head.Children.Add(hint);
        for (var k = 0; k < Count; k++)
        {
            thumbs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cells[k] = new Border { Child = VideoFrames.DecorImage() };
            Grid.SetColumn(cells[k], k);
            thumbs.Children.Add(cells[k]);
        }
        var thumbsClip = new Border { CornerRadius = new CornerRadius(12), Child = thumbs };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(thumbsClip, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        bar.Children.Add(thumbsClip);
        ticks.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 255, 255, 255));
        frame.BorderBrush = Palette.Brand;
        // 蒙白与竖线跟着缩略图条的圆角裁掉；红框在最上面。
        var clipped = new Canvas { IsHitTestVisible = false };
        clipped.Children.Add(dimLeft);
        clipped.Children.Add(dimRight);
        clipped.Children.Add(ticks);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(12), Child = clipped, IsHitTestVisible = false });
        overlay.Children.Add(frame);
        bar.Children.Add(overlay);
        AutomationProperties.SetName(bar, "导出范围");
        bar.SizeChanged += (_, _) => LayoutOverlay();
        bar.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (item == null || ws.Running) return;
            bar.CapturePointer(e.Pointer);
            var x = e.GetCurrentPoint(bar).Position.X;
            var (x0, x1) = Ends();
            draggingStart = Math.Abs(x - x0) <= Math.Abs(x - x1);
            e.Handled = true;
        }), true);
        bar.PointerMoved += (_, e) =>
        {
            if (draggingStart is not { } start || item == null) return;
            DragTo(start, e.GetCurrentPoint(bar).Position.X);
            e.Handled = true;
        };
        bar.PointerReleased += (_, e) =>
        {
            if (draggingStart is not { } start || item == null) return;
            DragTo(start, e.GetCurrentPoint(bar).Position.X);
            draggingStart = null;
            bar.ReleasePointerCapture(e.Pointer);
            if (ws.Trim.TryGetValue(item.FilePath, out var r) && r.Start <= 0.01 && r.End >= item.Duration - 0.01) ws.SetTrim(item.FilePath, null);
        };
        bar.PointerCaptureLost += (_, _) => draggingStart = null;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(head);
        stack.Children.Add(bar);
        panel.Child = stack;
        Content = panel;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    private void DragTo(bool start, double x)
    {
        if (item == null) return;
        var d = Math.Max(0.001, item.Duration);
        var t = Math.Min(Math.Max(0, x / Math.Max(1, bar.ActualWidth) * d), d);
        var r = ws.TrimRange(item);
        var minLength = Math.Min(0.5, d);
        ws.SetTrim(item.FilePath, start ? new TimeRange(Math.Min(t, r.End - minLength), r.End) : new TimeRange(r.Start, Math.Max(t, r.Start + minLength)));
    }

    private void Paint()
    {
        panel.Background = Palette.Get(this, "BentoFill");
        titleBack.Background = Palette.Get(this, "BentoAlertFill");
        title.Foreground = Palette.Get(this, "BentoAlertText");
        hint.Foreground = Palette.Get(this, "BentoSecondary");
        var card = ((SolidColorBrush)Palette.Get(this, "BentoCard")).Color;
        dimLeft.Fill = dimRight.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(178, card.R, card.G, card.B));
        var fill = Palette.Get(this, "BentoFill");
        foreach (var c in cells) c.Background = fill;
    }

    private (double, double) Ends()
    {
        if (item == null) return (0, 0);
        var w = bar.ActualWidth;
        var d = Math.Max(0.001, item.Duration);
        var r = ws.TrimRange(item);
        return (r.Start / d * w, r.End / d * w);
    }

    public void Update(FileItem item)
    {
        this.item = item;
        var r = ws.TrimRange(item);
        var full = r.Start <= 0.01 && r.End >= item.Duration - 0.01;
        string End(double e) => Format.Duration(e < 1 ? e : Math.Round(e));
        title.Text = full ? $"范围：全片 {Format.Duration(0)} – {End(item.Duration)}" : $"范围：{Format.Duration(r.Start)} – {End(r.End)}";
        AutomationProperties.SetItemStatus(bar, $"{Format.Duration(r.Start)} 到 {Format.Duration(r.End)}，{ws.FrameCount(item)} 张");
        if (thumbsFor != item.FilePath)
        {
            thumbsFor = item.FilePath;
            LoadThumbs(item);
        }
        LayoutOverlay();
    }

    private async void LoadThumbs(FileItem item)
    {
        cts?.Cancel();
        var c = cts = new CancellationTokenSource();
        foreach (var cell in cells) ((Image)cell.Child).Source = null;
        var got = new List<(int, byte[])>();
        try
        {
            await Task.Run(() => VideoFrames.StripAsync(item.FilePath, item.Duration, Count, (int)(BarHeight * 2), (k, b) => { lock (got) got.Add((k, b)); }, c.Token));
        }
        catch { }
        if (c.IsCancellationRequested) return;
        foreach (var (k, b) in got)
        {
            if (await VideoFrames.ToImageAsync(b) is { } img && !c.IsCancellationRequested)
                ((Image)cells[k].Child).Source = img;
        }
    }

    private void LayoutOverlay()
    {
        if (item == null || bar.ActualWidth <= 0) return;
        var w = bar.ActualWidth;
        var d = Math.Max(0.001, item.Duration);
        var (x0, x1) = Ends();
        dimLeft.Width = Math.Max(0, x0);
        dimRight.Width = Math.Max(0, w - x1);
        Canvas.SetLeft(dimRight, x1);
        frame.Width = Math.Max(8, x1 - x0);
        Canvas.SetLeft(frame, x0);
        // 要导出的帧：张数多时按 60 条均匀示意。
        var n = ws.FrameCount(item);
        var count = Math.Min(n, 60);
        var g = new GeometryGroup();
        for (var k = 0; k < count; k++)
        {
            var t = ws.FrameTime(item, count == n ? k : k * n / Math.Max(1, count));
            var x = t / d * w;
            g.Children.Add(new LineGeometry { StartPoint = new Point(x, 0), EndPoint = new Point(x, BarHeight) });
        }
        ticks.Data = g;
    }
}

/// 预览前 16 帧（灰底面板、4 列），文件名按实际输出写。
internal sealed class FramePreviewGrid : UserControl
{
    private readonly Workspace ws;
    private readonly TextBlock title = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock folder = new() { FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, Consolas"), HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly AdaptiveGrid grid = new();
    private readonly Border panel = new() { CornerRadius = new CornerRadius(16) };
    private readonly List<(Border Box, Image Image, TextBlock Name)> tiles = new();
    private string key = "";
    private CancellationTokenSource? cts;

    public FramePreviewGrid(Workspace ws)
    {
        this.ws = ws;
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(title);
        Grid.SetColumn(folder, 1);
        head.Children.Add(folder);
        var root = new Grid { RowSpacing = 10, Padding = new Thickness(14, 12, 14, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(head);
        var scroll = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        panel.Child = root;
        Content = panel;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    private void Paint()
    {
        panel.Background = Palette.Get(this, "BentoFill");
        title.Foreground = Palette.Get(this, "BentoPrimary");
        folder.Foreground = Palette.Get(this, "BentoSecondary");
        foreach (var (box, _, name) in tiles)
        {
            box.Background = Palette.Get(this, "BentoLine");
            name.Foreground = Palette.Get(this, "BentoSecondary");
        }
    }

    private string Ext => ws.Draft.Video.FrameFormat switch
    {
        ImageFormatChoice.Jpeg or ImageFormatChoice.Original => "jpg",
        ImageFormatChoice.Png => "png",
        ImageFormatChoice.Webp => "webp",
        _ => "avif",
    };

    public void Update(FileItem item)
    {
        var n = Math.Min(16, ws.FrameCount(item));
        title.Text = $"预览前 {n} 帧";
        folder.Text = $"{System.IO.Path.GetFileNameWithoutExtension(item.FilePath)}_frames/";
        var crop = ws.VideoCropBox(item);
        grid.Aspect = crop is { Height: > 0 } c ? c.Width / c.Height : (double)item.Width / Math.Max(1, item.Height);
        while (tiles.Count < n) AddTile();
        while (tiles.Count > n)
        {
            grid.Children.RemoveAt(grid.Children.Count - 1);
            tiles.RemoveAt(tiles.Count - 1);
        }
        for (var k = 0; k < n; k++) tiles[k].Name.Text = string.Format(CultureInfo.InvariantCulture, "_{0:0000}.{1}", k + 1, Ext);
        Paint();
        var v = ws.Draft.Video;
        var r = ws.TrimRange(item);
        var next = $"{item.FilePath}|{v.FrameMode}|{v.FrameValue}|{r.Start}|{r.End}|{crop}|{n}";
        if (next == key) return;
        key = next;
        _ = Load(item, n, crop);
    }

    private void AddTile()
    {
        var image = VideoFrames.DecorImage();
        var box = new Border { CornerRadius = new CornerRadius(8), Child = image };
        var name = new TextBlock { FontSize = 10, FontFamily = new FontFamily("Cascadia Mono, Consolas"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var cell = new StackPanel { Spacing = 5 };
        cell.Children.Add(box);
        cell.Children.Add(name);
        AutomationProperties.SetAccessibilityView(box, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        grid.Children.Add(cell);
        tiles.Add((box, image, name));
    }

    private async Task Load(FileItem item, int n, PixelRect? crop)
    {
        cts?.Cancel();
        var c = cts = new CancellationTokenSource();
        try { await Task.Delay(150, c.Token); } catch { return; } // 连点 +/− 时停手再取
        var times = Enumerable.Range(0, n).Select(k => ws.FrameTime(item, k)).ToList();
        var path = item.FilePath;
        var jobs = times.Select(t => Task.Run(() => VideoFrames.GrabAsync(path, t, 480, crop, c.Token))).ToList();
        for (var k = 0; k < n; k++)
        {
            byte[]? b;
            try { b = await jobs[k]; } catch { b = null; }
            if (c.IsCancellationRequested) return;
            if (k >= tiles.Count) return;
            tiles[k].Image.Source = b == null ? null : await VideoFrames.ToImageAsync(b);
        }
    }
}

/// 帧网格：固定 4 列铺满（设计稿 V2.1 · 07，列距 10、行距 12）；格子高度按画面比例。
internal sealed class AdaptiveGrid : Panel
{
    private const double Gap = 10, RowGap = 12;
    private double aspect = 16.0 / 9;

    public double Aspect
    {
        get => aspect;
        set
        {
            if (Math.Abs(value - aspect) < 0.0001) return;
            aspect = value;
            InvalidateMeasure();
        }
    }

    private (int Cols, double Cell) Columns(double width)
    {
        if (double.IsInfinity(width) || width <= 0) width = 600;
        const int cols = 4;
        var cell = Math.Max(1, (width - (cols - 1) * Gap) / cols);
        return (cols, cell);
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, cell) = Columns(available.Width);
        double y = 0, rowH = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = (FrameworkElement)Children[i];
            if (child is StackPanel sp && sp.Children[0] is Border b)
            {
                b.Width = cell;
                b.Height = cell / Math.Max(0.05, aspect);
            }
            child.Measure(new Size(cell, double.PositiveInfinity));
            rowH = Math.Max(rowH, child.DesiredSize.Height);
            if (i % cols == cols - 1 || i == Children.Count - 1)
            {
                y += rowH + RowGap;
                rowH = 0;
            }
        }
        return new Size(double.IsInfinity(available.Width) ? cols * (cell + Gap) : available.Width, Math.Max(0, y - RowGap));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, cell) = Columns(final.Width);
        double y = 0, rowH = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var col = i % cols;
            child.Arrange(new Rect(col * (cell + Gap), y, cell, child.DesiredSize.Height));
            rowH = Math.Max(rowH, child.DesiredSize.Height);
            if (col == cols - 1)
            {
                y += rowH + RowGap;
                rowH = 0;
            }
        }
        return final;
    }
}
