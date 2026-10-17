using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace SimpleSmaller.Views.Preview;

/// 图片「裁切」（设计稿 03）：上面一条写当前比例与这张图，中间大图拖框，底部胶片条。
/// 每张默认居中、按比例取最大；拖框移动、拖四角缩放，动了就按新的，不用确认；
/// Enter 或 → 下一张，← 上一张，Ctrl+Z 撤回这一张，Esc 回到全部缩略图。
internal sealed class CropView : Reactive
{
    private readonly Workspace ws;
    private readonly Grid stage = new() { CornerRadius = new CornerRadius(16) };
    private readonly CropCanvas canvas;
    private readonly FilmStrip strip;
    private readonly ProgressRing loading = new() { IsActive = true, Width = 28, Height = 28 };

    // 浮在画面上方的一条。
    private readonly TextBlock backText = Themed.Text("", 13, "BentoPrimary");
    private readonly Border backPill = new() { Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 0, 16, 0) };
    private readonly Button back = new() { Style = (Style)Application.Current.Resources["BentoBareButton"], VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock ratioText = Themed.Text("", 12, "BentoPrimary", Themed.Bold);
    private readonly TextBlock nameText = Themed.Text("", 12);
    private readonly Border namePill = new() { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock sizeText = Themed.Text("", 11, "BentoPrimary");
    private readonly Border sizePill = new() { Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0) };

    // 浮在大图下方的两个按钮。
    private readonly CropButton prev = new("← 上一张", primary: false);
    private readonly CropButton next = new("下一张 →", primary: true);

    /// 当前这张和前后各一张预先解码成屏幕尺寸，切换时不等。
    private readonly Dictionary<string, ImageSource> images = new(Workspace.PathComparer);
    private readonly HashSet<string> pending = new(Workspace.PathComparer);

    public CropView(Workspace ws)
    {
        this.ws = ws;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AutomationProperties.SetName(this, "裁切");
        canvas = new CropCanvas(ws) { Focusable = this };
        strip = new FilmStrip(i => ws.CropIndex = i,
            f => f.Name + (ws.CropBoxes.ContainsKey(f.FilePath) ? "，调过" : "，居中取最大"),
            f => ws.CropBoxes.ContainsKey(f.FilePath) ? AdjustedBadge() : null);

        RoundClip.Apply(stage, 16);
        stage.Children.Add(canvas);
        stage.Children.Add(Header());
        prev.Click += (_, _) => Go(-1);
        next.Click += (_, _) => Go(1);
        loading.HorizontalAlignment = HorizontalAlignment.Center;
        loading.VerticalAlignment = VerticalAlignment.Center;
        stage.Children.Add(loading);
        Themed.OnTheme(stage, () => stage.Background = Palette.Get(stage, "BentoFill"));

        // 设计稿 V2.2：大图下面一行「← 全部 N 张」+ 提示，右边「← 上一张」「下一张 →」；再下面是胶片条。
        var bar = new Grid { ColumnSpacing = 8 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(BackButton());
        var hint = Themed.Text("拖框就按新的；Ctrl+Z 撤回这一张", 11, "BentoSecondary");
        hint.TextTrimming = TextTrimming.CharacterEllipsis;
        hint.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(hint, 1);
        bar.Children.Add(hint);
        Grid.SetColumn(prev, 2);
        bar.Children.Add(prev);
        Grid.SetColumn(next, 3);
        bar.Children.Add(next);

        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(stage);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);
        Grid.SetRow(strip, 2);
        root.Children.Add(strip);
        Content = root;

        KeyDown += OnKey;
        Loaded += (_, _) => Focus(FocusState.Programmatic);
        Watch(ws, nameof(Workspace.CropIndex), nameof(Workspace.CropBoxes), nameof(Workspace.Draft), nameof(Workspace.Running));
        WatchList(ws.Files);
    }

    private UIElement BackButton()
    {
        var backRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var chevron = new FontIcon { Glyph = "\uE72B", FontSize = 12 };
        Themed.Fg(chevron, "BentoPrimary");
        backRow.Children.Add(chevron);
        backRow.Children.Add(backText);
        backPill.Child = backRow;
        back.Content = backPill;
        Motion.SetPress(back, true);
        ToolTipService.SetToolTip(back, "回到全部缩略图（Esc）");
        back.Click += (_, _) => ws.CropEditing = false;
        var esc = new KeyboardAccelerator { Key = VirtualKey.Escape };
        esc.Invoked += (_, e) => { ws.CropEditing = false; e.Handled = true; };
        back.KeyboardAccelerators.Add(esc);
        back.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        return back;
    }

    private UIElement Header()
    {
        // 设计稿 V2.2：左上一个标签「**3:4** 文件名 · 尺寸」，右上「裁出 …」。
        ratioText.Margin = new Thickness(0, 0, 6, 0);
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameText.MaxLines = 1;
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nameRow.Children.Add(ratioText);
        nameRow.Children.Add(nameText);
        namePill.Child = nameRow;
        sizePill.Child = sizeText;
        Themed.OnTheme(this, () =>
        {
            var c = ((SolidColorBrush)Palette.Get(this, "BentoCard")).Color;
            var glass = new SolidColorBrush(Windows.UI.Color.FromArgb(235, c.R, c.G, c.B));
            namePill.Background = sizePill.Background = glass;
            backPill.Background = Palette.Get(this, "BentoFill");
        });

        var row = new Grid { ColumnSpacing = 8, Margin = new Thickness(14), VerticalAlignment = VerticalAlignment.Top };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(namePill);
        Grid.SetColumn(sizePill, 1);
        sizePill.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(sizePill);
        return row;
    }

    private static UIElement AdjustedBadge()
    {
        var icon = new FontIcon { Glyph = "", FontSize = 8 };
        Themed.Fg(icon, "BentoOnInk");
        var b = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Child = icon };
        Themed.Bg(b, "BentoInk");
        AutomationProperties.SetAccessibilityView(b, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return b;
    }

    private FileItem? CurrentItem
    {
        get
        {
            var items = ws.CropItems;
            var i = ws.CropCurrentIndex;
            return i < items.Count ? items[i] : null;
        }
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter or VirtualKey.Right:
                Go(1);
                e.Handled = true;
                break;
            case VirtualKey.Left:
                Go(-1);
                e.Handled = true;
                break;
            case VirtualKey.Z when ctrl:
                if (CurrentItem is { } item)
                {
                    ws.UndoCropBox(item);
                }
                e.Handled = true;
                break;
        }
    }

    private void Go(int delta)
    {
        var count = ws.CropItems.Count;
        if (count == 0)
        {
            return;
        }
        var target = Math.Min(count - 1, Math.Max(0, ws.CropCurrentIndex + delta));
        if (target != ws.CropCurrentIndex)
        {
            ws.CropIndex = target;
        }
    }

    protected override void Refresh()
    {
        var items = ws.CropItems;
        var index = ws.CropCurrentIndex;
        var item = index < items.Count ? items[index] : null;
        loading.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        canvas.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is null)
        {
            return;
        }
        backText.Text = $"全部 {items.Count} 张";
        AutomationProperties.SetName(back, $"全部 {items.Count} 张");
        ratioText.Text = ws.CropAspect is null ? "自由" : (ws.CropPreset?.RatioLabel ?? "");
        nameText.Text = $"{item.Name} · {item.Dimensions}";
        var box = ws.EffectiveCropBox(index);
        sizeText.Text = $"裁出 {(int)box.Width} × {(int)box.Height}";
        prev.IsEnabled = index > 0;
        next.IsEnabled = index + 1 < items.Count;
        // 单张图片时没有胶片条。
        strip.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        strip.Update(items, index);
        canvas.Show(item, index, images.TryGetValue(item.FilePath, out var src) ? src : item.Thumbnail as ImageSource);
        LoadAround(items, index);
    }

    private async void LoadAround(IReadOnlyList<FileItem> items, int i)
    {
        // 只留附近几张，避免一大批图都压在内存里。
        var keep = new HashSet<string>(Enumerable.Range(i - 2, 5).Where(k => k >= 0 && k < items.Count).Select(k => items[k].FilePath), Workspace.PathComparer);
        foreach (var gone in images.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            images.Remove(gone);
        }
        foreach (var k in new[] { i, i + 1, i - 1 })
        {
            if (k < 0 || k >= items.Count) continue;
            var path = items[k].FilePath;
            if (images.ContainsKey(path) || !pending.Add(path)) continue;
            var source = await ImageLoader.LoadAsync(path, 2400);
            pending.Remove(path);
            if (source is null) continue;
            images[path] = source;
            if (CurrentItem is { } cur && Workspace.PathComparer.Equals(cur.FilePath, path))
            {
                canvas.Show(cur, ws.CropCurrentIndex, source);
            }
        }
    }
}

/// 大图与裁切框。整块自己做命中判断：四角 28×28 缩放，框内移动，指针形状跟着变。
internal sealed class CropCanvas : Grid
{
    private enum Zone { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }

    private readonly Workspace ws;
    private readonly Image image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Path dim = new() { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Path thirds = new() { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(115, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false };
    private readonly Rectangle frame = new() { Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Path corners = new() { Fill = new SolidColorBrush(Microsoft.UI.Colors.White), IsHitTestVisible = false };
    private readonly TextBlock label = new() { FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
    private readonly Border labelBack = new() { Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };

    private FileItem? item;
    private int index;
    private ImageSource? source;

    // 布局：大图放进画布，四周留白（按钮在画布下面的一行）。
    private double scale = 1;
    private Rect display;

    // 拖动。
    private Zone dragZone = Zone.None;
    private Point dragPoint;
    private PixelRect? dragStart;
    private bool moved;

    // 框的过渡动画（撤回、换比例时；拖动中不做）。
    private Rect shownView;
    private Rect animFrom, animTo;
    private DateTime animStart;
    private bool animating;

    public UIElement? Focusable { get; set; }

    public CropCanvas(Workspace ws)
    {
        this.ws = ws;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        labelBack.Child = label;
        Children.Add(image);
        Children.Add(dim);
        Children.Add(thirds);
        Children.Add(frame);
        Children.Add(corners);
        // 框内不标尺寸：裁出尺寸写在右上角的标签里（设计稿）。
        AutomationProperties.SetName(this, "裁切框");
        AutomationProperties.SetHelpText(this, "拖框就按新的取景；回车或右方向键到下一张，Ctrl+Z 撤回这一张");
        SizeChanged += (_, _) => Render(animated: false);
        PointerMoved += OnMoved;
        PointerPressed += OnPressed;
        PointerReleased += (_, e) => End(e);
        PointerCaptureLost += (_, _) => { dragZone = Zone.None; dragStart = null; };
    }

    public void Show(FileItem item, int index, ImageSource? source)
    {
        var same = ReferenceEquals(this.item, item);
        this.item = item;
        this.index = index;
        if (!ReferenceEquals(this.source, source))
        {
            this.source = source;
            image.Source = source;
        }
        Render(animated: same && dragStart is null && Motion.Enabled);
    }

    private PixelRect Box => ws.EffectiveCropBox(index);

    private void Render(bool animated)
    {
        if (item is null || ActualWidth <= 0)
        {
            return;
        }
        double w = Math.Max(1, item.Width), h = Math.Max(1, item.Height);
        double aw = Math.Max(1, ActualWidth - 48), ah = Math.Max(1, ActualHeight - 24 - 24);
        scale = Math.Min(aw / w, ah / h);
        var dw = w * scale;
        var dh = h * scale;
        display = new Rect(Math.Round((ActualWidth - dw) / 2), Math.Round(24 + (ah - dh) / 2), dw, dh);
        image.Margin = new Thickness(display.X, display.Y, 0, 0);
        image.Width = dw;
        image.Height = dh;
        var b = Box;
        var view = ToView(b);
        label.Text = $"{(int)b.Width} × {(int)b.Height}";
        AutomationProperties.SetName(this, $"裁切框，{(int)b.Width} × {(int)b.Height} 像素");
        if (animated && shownView != view && shownView.Width > 0)
        {
            animFrom = Current();
            animTo = view;
            animStart = DateTime.UtcNow;
            if (!animating)
            {
                animating = true;
                CompositionTarget.Rendering += Tick;
            }
        }
        else
        {
            StopAnimation();
            Draw(view);
        }
        shownView = view;
    }

    private Rect ToView(PixelRect r) => new(display.X + r.X * scale, display.Y + r.Y * scale, r.Width * scale, r.Height * scale);

    private Rect drawn;

    private Rect Current() => drawn;

    private void Tick(object? sender, object e)
    {
        var t = Math.Min(1, (DateTime.UtcNow - animStart).TotalMilliseconds / 250);
        // 接近 cubic-bezier(.22,1,.36,1) 的缓出。
        var k = 1 - Math.Pow(1 - t, 4);
        Draw(new Rect(
            animFrom.X + (animTo.X - animFrom.X) * k,
            animFrom.Y + (animTo.Y - animFrom.Y) * k,
            animFrom.Width + (animTo.Width - animFrom.Width) * k,
            animFrom.Height + (animTo.Height - animFrom.Height) * k));
        if (t >= 1)
        {
            StopAnimation();
        }
    }

    private void StopAnimation()
    {
        if (animating)
        {
            animating = false;
            CompositionTarget.Rendering -= Tick;
        }
    }

    /// 框外压暗一半；三分线；框线；四角加粗的角标（画在框内侧，框贴着图片边时也落在图上）；左上角的像素尺寸。
    private void Draw(Rect v)
    {
        drawn = v;
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = display });
        group.Children.Add(new RectangleGeometry { Rect = v });
        dim.Data = group;

        var lines = new GeometryGroup();
        for (var k = 1; k <= 2; k++)
        {
            var x = v.X + v.Width * k / 3;
            var y = v.Y + v.Height * k / 3;
            lines.Children.Add(new LineGeometry { StartPoint = new Point(x, v.Y), EndPoint = new Point(x, v.Y + v.Height) });
            lines.Children.Add(new LineGeometry { StartPoint = new Point(v.X, y), EndPoint = new Point(v.X + v.Width, y) });
        }
        thirds.Data = lines;

        frame.Margin = new Thickness(v.X, v.Y, 0, 0);
        frame.Width = Math.Max(0, v.Width);
        frame.Height = Math.Max(0, v.Height);

        // 设计稿 V2.2：细白边 + 四角 9 px 白色小方块。
        const double h = 4.5;
        var path = new GeometryGroup();
        foreach (var p in new[] { new Point(v.X, v.Y), new Point(v.X + v.Width, v.Y), new Point(v.X, v.Y + v.Height), new Point(v.X + v.Width, v.Y + v.Height) })
        {
            path.Children.Add(new RectangleGeometry { Rect = new Rect(p.X - h, p.Y - h, 2 * h, 2 * h) });
        }
        corners.Data = path;

    }

    // ---------- 拖动 ----------

    private Zone HitTest(Point p)
    {
        var v = drawn;
        if (v.Width <= 0)
        {
            return Zone.None;
        }
        bool near(double ax, double ay) => Math.Abs(p.X - ax) <= 14 && Math.Abs(p.Y - ay) <= 14;
        if (near(v.X, v.Y)) return Zone.TopLeft;
        if (near(v.X + v.Width, v.Y)) return Zone.TopRight;
        if (near(v.X, v.Y + v.Height)) return Zone.BottomLeft;
        if (near(v.X + v.Width, v.Y + v.Height)) return Zone.BottomRight;
        if (v.Contains(p)) return Zone.Move;
        return Zone.None;
    }

    private void SetCursor(Zone z)
    {
        ProtectedCursor = z switch
        {
            Zone.Move => InputSystemCursor.Create(InputSystemCursorShape.SizeAll),
            Zone.TopLeft or Zone.BottomRight => InputSystemCursor.Create(InputSystemCursorShape.SizeNorthwestSoutheast),
            Zone.TopRight or Zone.BottomLeft => InputSystemCursor.Create(InputSystemCursorShape.SizeNortheastSouthwest),
            _ => null,
        };
    }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Focusable is Control c)
        {
            c.Focus(FocusState.Pointer);
        }
        if (ws.Running || item is null)
        {
            return;
        }
        var p = e.GetCurrentPoint(this).Position;
        var z = HitTest(p);
        if (z == Zone.None)
        {
            return;
        }
        StopAnimation();
        Draw(ToView(Box));
        dragZone = z;
        dragPoint = p;
        dragStart = Box;
        moved = false;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        if (dragStart is not { } start || item is null)
        {
            SetCursor(HitTest(p));
            return;
        }
        var dx = (p.X - dragPoint.X) / scale;
        var dy = (p.Y - dragPoint.Y) / scale;
        if (!moved && Math.Abs(p.X - dragPoint.X) < 1 && Math.Abs(p.Y - dragPoint.Y) < 1)
        {
            return;
        }
        double W = item.Width, H = item.Height;
        PixelRect next;
        if (dragZone == Zone.Move)
        {
            next = Workspace.Clamp(start with { X = start.X + dx, Y = start.Y + dy }, W, H, ws.CropAspect);
        }
        else
        {
            next = Resize(start, dragZone, dx, dy, W, H);
        }
        if (!moved)
        {
            // 一次拖动开始：记下原来的框，供 Ctrl+Z 撤回。
            moved = true;
            ws.SetCropBox(item, next);
        }
        else
        {
            ws.CropBoxes = Workspace.With(ws.CropBoxes, item.FilePath, next);
        }
        e.Handled = true;
    }

    /// 拖一个角：对角固定，按比例缩放（自由比例时宽高各自变），不超出画面。
    private PixelRect Resize(PixelRect start, Zone corner, double dx, double dy, double W, double H)
    {
        Point anchor;
        double dirX, dirY;
        switch (corner)
        {
            case Zone.TopLeft: anchor = new Point(start.MaxX, start.MaxY); dirX = -1; dirY = -1; break;
            case Zone.TopRight: anchor = new Point(start.MinX, start.MaxY); dirX = 1; dirY = -1; break;
            case Zone.BottomLeft: anchor = new Point(start.MaxX, start.MinY); dirX = -1; dirY = 1; break;
            default: anchor = new Point(start.MinX, start.MinY); dirX = 1; dirY = 1; break;
        }
        var w = start.Width + dirX * dx;
        var h = start.Height + dirY * dy;
        // 朝拖动方向还剩多少画面。
        var roomX = dirX > 0 ? W - anchor.X : anchor.X;
        var roomY = dirY > 0 ? H - anchor.Y : anchor.Y;
        var minSide = Math.Max(16, 24 / scale);
        if (ws.CropAspect is { } a)
        {
            w = Math.Max(w, h * a);
            w = Math.Min(Math.Min(Math.Max(w, minSide * Math.Max(1, a)), roomX), roomY * a);
            h = w / a;
        }
        else
        {
            w = Math.Min(Math.Max(w, minSide), roomX);
            h = Math.Min(Math.Max(h, minSide), roomY);
        }
        var x = dirX > 0 ? anchor.X : anchor.X - w;
        var y = dirY > 0 ? anchor.Y : anchor.Y - h;
        return new PixelRect(Num.Round(x), Num.Round(y), Num.Round(w), Num.Round(h));
    }

    private void End(PointerRoutedEventArgs e)
    {
        if (dragStart is not null)
        {
            ReleasePointerCapture(e.Pointer);
        }
        dragStart = null;
        dragZone = Zone.None;
    }
}

/// 大图下面的按钮：「← 上一张」灰底、「下一张 →」墨黑（深色下反白）。
internal sealed class CropButton : Button
{
    private readonly bool primary;
    private readonly Border back = new() { Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly TextBlock text;

    public CropButton(string title, bool primary)
    {
        this.primary = primary;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        text = new TextBlock { Text = title, FontSize = 13, FontWeight = primary ? Themed.Semibold : Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        back.Padding = new Thickness(primary ? 18 : 16, 0, primary ? 18 : 16, 0);
        back.Child = text;
        Content = back;
        AutomationProperties.SetName(this, title.Replace("←", "").Replace("→", "").Trim());
        Themed.OnTheme(this, Paint);
        IsEnabledChanged += (_, _) => Paint();
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, primary ? "BentoInk" : "BentoFill");
        text.Foreground = Palette.Get(this, primary ? "BentoOnInk" : "BentoPrimary");
        Opacity = IsEnabled ? 1 : 0.45;
    }
}
