using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Foundation;
using Windows.System;
using uniffi.smaller_core;

namespace SimpleSmaller.Views.Preview;

/// 页面缩略图：白纸、细边、下方页码；选中时品牌红描边，多文件合并时左上角标「文件·页」。
internal sealed class PdfPageThumb : Grid
{
    private readonly double height;
    private readonly Border paper = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness = new Thickness(0.5), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(31, 0, 0, 0)) };
    private readonly Image image = new() { Stretch = Stretch.Uniform };
    private readonly Border ring = new() { BorderThickness = new Thickness(2.5), CornerRadius = new CornerRadius(4), Padding = new Thickness(0.5) };
    private readonly Border badge = new() { CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly TextBlock badgeText = new() { FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
    private readonly TextBlock number = new() { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
    private bool selected;

    public PdfPageThumb(double height)
    {
        this.height = height;
        RowSpacing = 4;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        paper.Child = image;
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        // 「文件·页」角标贴在纸的左上角。
        var sheet = new Grid();
        sheet.Children.Add(paper);
        sheet.Children.Add(badge);
        badge.Margin = new Thickness(3);
        ring.Child = sheet;
        ring.HorizontalAlignment = HorizontalAlignment.Center;
        badge.Child = badgeText;
        // 缩略图区固定高（横版页面按格宽缩小，上下居中），页码行对齐。
        var top = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Height = height + 6 };
        ring.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(ring);
        Children.Add(top);
        SetRow(number, 1);
        Children.Add(number);
        AutomationProperties.SetAccessibilityView(top, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(number, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
        SetAspect(1.414);
    }

    /// 页面高宽比（图片转 PDF 时是图片的）。
    public void SetAspect(double aspect)
    {
        this.aspect = Math.Max(0.05, aspect);
        InvalidateMeasure();
    }

    private double aspect = 1.414;

    /// 按页面比例放进「格宽 × 固定高」：竖版顶满高，横版顶满宽（与 mac scaledToFit 一样）。
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size available)
    {
        var w = height / aspect;
        var maxW = double.IsInfinity(available.Width) ? w : Math.Max(20, available.Width - 6);
        if (w > maxW) w = maxW;
        paper.Width = w;
        paper.Height = w * aspect;
        return base.MeasureOverride(available);
    }

    public ImageSource? Source
    {
        get => image.Source;
        set { if (image.Source != value) image.Source = value; }
    }

    public void Set(int number, bool selected, string? badge, string name)
    {
        this.number.Text = $"{number}";
        this.selected = selected;
        badgeText.Text = badge ?? "";
        this.badge.Visibility = badge is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetItemStatus(this, selected ? "已选" : "");
        Paint();
    }

    private void Paint()
    {
        ring.BorderBrush = selected ? Palette.Brand : null;
        number.Foreground = selected ? Palette.Brand : Palette.Get(this, "BentoSecondary");
    }

    protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ThumbPeer(this);

    /// 旁白读成图片：「第 n 页」，选中带「已选」。
    private sealed class ThumbPeer(PdfPageThumb owner) : Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override Microsoft.UI.Xaml.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.Image;
        protected override string GetClassNameCore() => "PdfPageThumb";
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}

/// 可拖动排序的缩略图网格（合并的页面、图片转 PDF 的图片共用）：
/// 点选（Ctrl 多选、Shift 连选），拖动排序（拖到哪一格就挪到哪一格，其余格滑开），Delete 删掉选中的。
internal abstract class PdfReorderGrid<TKey> : Reactive where TKey : notnull
{
    protected readonly Workspace ws;
    private readonly PdfTileGrid grid = new();
    private readonly TextBlock title = PdfUi.Text("", 12, semibold: true);
    private readonly Dictionary<TKey, PdfPageThumb> tiles = new();
    private readonly Dictionary<PdfPageThumb, TKey> keys = new();
    private PdfPageThumb? pressed;
    private Point pressPoint;
    private bool dragging;
    private VirtualKeyModifiers pressMods;
    private int? anchor;

    protected PdfReorderGrid(Workspace ws)
    {
        this.ws = ws;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid { Padding = new Thickness(8, 0, 8, 0) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(title);
        var hint = PdfUi.Text("拖动可排序 · 选中后按 Delete 删除", 11, "BentoSecondary");
        Grid.SetColumn(hint, 1);
        head.Children.Add(hint);
        root.Children.Add(head);
        var holder = new Border { Padding = new Thickness(16), Child = grid, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var scroll = new ScrollViewer { Content = holder, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        // 点空白处取消选择。
        holder.PointerPressed += (_, e) =>
        {
            Focus(FocusState.Pointer);
            if (!e.Handled) SetSelected([]);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Delete or VirtualKey.Back && !ws.Running)
            {
                Delete();
                e.Handled = true;
            }
        };
        Unloaded += (_, _) => PdfThumbnails.Shared.Changed -= OnThumb;
        Loaded += (_, _) => PdfThumbnails.Shared.Changed += OnThumb;
    }

    // ---------- 子类提供 ----------

    protected abstract IReadOnlyList<TKey> Keys { get; }
    protected abstract string Title { get; }
    protected abstract bool IsSelected(TKey key);
    protected abstract void SetSelected(IEnumerable<TKey> keys);
    protected abstract void Move(int from, int to);
    protected abstract void Delete();
    protected abstract void UpdateTile(PdfPageThumb tile, TKey key, int index);
    protected abstract string Tooltip(TKey key);
    protected virtual void OnThumb(string path) { }

    protected IEnumerable<(TKey Key, PdfPageThumb Tile)> Tiles => tiles.Select(kv => (kv.Key, kv.Value));

    protected override void Refresh()
    {
        title.Text = Title;
        var order = Keys;
        var live = new HashSet<TKey>(order);
        foreach (var gone in tiles.Keys.Where(k => !live.Contains(k)).ToList())
        {
            grid.Children.Remove(tiles[gone]);
            keys.Remove(tiles[gone]);
            tiles.Remove(gone);
        }
        for (var i = 0; i < order.Count; i++)
        {
            var key = order[i];
            if (!tiles.TryGetValue(key, out var tile))
            {
                tile = Create(key);
                tiles[key] = tile;
                keys[tile] = key;
                grid.Children.Add(tile);
            }
            grid.SetSlot(tile, i);
            UpdateTile(tile, key, i);
            ToolTipService.SetToolTip(tile, Tooltip(key));
        }
    }

    private PdfPageThumb Create(TKey key)
    {
        var t = new PdfPageThumb(132);
        t.PointerPressed += (_, e) => Press(t, e);
        t.PointerMoved += (_, e) => Drag(t, e);
        t.PointerReleased += (_, e) => Release(t, e);
        t.PointerCaptureLost += (_, _) => EndDrag();
        return t;
    }

    private void Press(PdfPageThumb t, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        Focus(FocusState.Pointer);
        if (ws.Running || !e.GetCurrentPoint(t).Properties.IsLeftButtonPressed) return;
        pressed = t;
        dragging = false;
        pressPoint = e.GetCurrentPoint(grid).Position;
        pressMods = e.KeyModifiers;
        t.CapturePointer(e.Pointer);
    }

    private void Drag(PdfPageThumb t, PointerRoutedEventArgs e)
    {
        if (pressed != t) return;
        var p = e.GetCurrentPoint(grid).Position;
        if (!dragging)
        {
            if (Math.Abs(p.X - pressPoint.X) + Math.Abs(p.Y - pressPoint.Y) < 6) return;
            dragging = true;
            t.Opacity = 0.4;
            SetSelected([keys[t]]);
        }
        var to = grid.IndexAt(p);
        var from = grid.SlotOf(t);
        if (to >= 0 && to != from)
        {
            Move(from, to);
            Refresh();
        }
    }

    private void Release(PdfPageThumb t, PointerRoutedEventArgs e)
    {
        if (pressed != t) return;
        var wasDrag = dragging;
        t.ReleasePointerCapture(e.Pointer);
        EndDrag();
        if (!wasDrag) Tap(grid.SlotOf(t));
    }

    private void EndDrag()
    {
        if (pressed is { } t) t.Opacity = 1;
        pressed = null;
        dragging = false;
    }

    private void Tap(int i)
    {
        var order = Keys;
        if (i < 0 || i >= order.Count) return;
        var key = order[i];
        if (pressMods.HasFlag(VirtualKeyModifiers.Control))
        {
            var set = order.Where(IsSelected).ToList();
            if (!set.Remove(key)) set.Add(key);
            SetSelected(set);
            anchor = i;
        }
        else if (pressMods.HasFlag(VirtualKeyModifiers.Shift) && anchor is { } a && a < order.Count)
        {
            SetSelected(order.Skip(Math.Min(a, i)).Take(Math.Abs(a - i) + 1));
        }
        else
        {
            SetSelected([key]);
            anchor = i;
        }
        Refresh();
    }
}

/// 合并：全部页面按顺序排开。选中只在这张卡里记（按页面），删页、排序直接改工作区的页序。
internal sealed class PdfPageGrid : PdfReorderGrid<PdfPageRef>
{
    private HashSet<PdfPageRef> selected = new();

    public PdfPageGrid(Workspace ws) : base(ws)
    {
        Watch(ws, nameof(Workspace.PageOrder), nameof(Workspace.Running));
        WatchList(ws.Files);
    }

    private List<FileItem> Pdfs => ws.Files.Where(f => f.Type == MediaType.Pdf).ToList();

    protected override IReadOnlyList<PdfPageRef> Keys => ws.PageOrder;
    protected override string Title => $"合并后 {ws.PageOrder.Count} 页 · 来自 {Pdfs.Count} 个文件";
    protected override bool IsSelected(PdfPageRef key) => selected.Contains(key);
    protected override void SetSelected(IEnumerable<PdfPageRef> keys) => selected = keys.ToHashSet();

    protected override void Refresh()
    {
        foreach (var f in Pdfs) PdfThumbnails.Shared.Load(f.PreviewPath);
        base.Refresh();
    }

    protected override void Move(int from, int to)
    {
        var order = ws.PageOrder.ToList();
        var page = order[from];
        order.RemoveAt(from);
        order.Insert(to, page);
        ws.PageOrder = order;
    }

    protected override void Delete()
    {
        if (selected.Count == 0) return;
        ws.PageOrder = ws.PageOrder.Where(r => !selected.Contains(r)).ToList();
        selected.Clear();
    }

    private FileItem? File(PdfPageRef r)
    {
        var pdfs = Pdfs;
        return r.File < pdfs.Count ? pdfs[(int)r.File] : null;
    }

    protected override void UpdateTile(PdfPageThumb tile, PdfPageRef key, int index)
    {
        var item = File(key);
        var multi = Pdfs.Count > 1;
        tile.Set(index + 1, selected.Contains(key), multi ? $"{key.File + 1}·{key.Page + 1}" : null, $"第 {index + 1} 页");
        if (item is not null)
        {
            tile.Source = PdfThumbnails.Shared.Page(item.PreviewPath, (int)key.Page);
            tile.SetAspect(PdfThumbnails.Shared.Aspect(item.PreviewPath, (int)key.Page));
        }
    }

    protected override string Tooltip(PdfPageRef key) => File(key) is { } f ? $"{f.Name} 第 {key.Page + 1} 页" : "";

    protected override void OnThumb(string path)
    {
        foreach (var (key, tile) in Tiles)
        {
            if (File(key) is { } f && string.Equals(f.PreviewPath, path, StringComparison.OrdinalIgnoreCase))
            {
                tile.Source = PdfThumbnails.Shared.Page(path, (int)key.Page);
                tile.SetAspect(PdfThumbnails.Shared.Aspect(path, (int)key.Page));
            }
        }
    }
}

/// 图片转 PDF：图片按顺序排开，一张一页；拖动排序（改的是文件列表的顺序），Delete 移出。
internal sealed class PdfImageOrderGrid : PdfReorderGrid<Guid>
{
    public PdfImageOrderGrid(Workspace ws) : base(ws)
    {
        Watch(ws, nameof(Workspace.Selection), nameof(Workspace.Running));
        WatchList(ws.Files);
        Cards.PdfFileWatch.Pages(this, ws, Schedule, nameof(FileItem.Width), nameof(FileItem.Height), nameof(FileItem.Thumbnail));
    }

    private List<FileItem> Images => ws.Files.Where(f => f.Type == MediaType.Image).ToList();

    protected override IReadOnlyList<Guid> Keys => Images.Select(f => f.Id).ToList();
    protected override string Title => $"{Images.Count} 张图片 · 一张一页";
    protected override bool IsSelected(Guid key) => ws.Selection.Contains(key);
    protected override void SetSelected(IEnumerable<Guid> keys) => ws.SetSelection(keys);

    protected override void Move(int from, int to)
    {
        var images = Images;
        var a = ws.Files.IndexOf(images[from]);
        var b = ws.Files.IndexOf(images[to]);
        if (a >= 0 && b >= 0) ws.Files.Move(a, b);
    }

    protected override void Delete()
    {
        if (ws.Selection.Count > 0) AppModel.Shared.Remove(ws.Selection, ws);
    }

    protected override void UpdateTile(PdfPageThumb tile, Guid key, int index)
    {
        var item = ws.Files.First(f => f.Id == key);
        tile.Set(index + 1, ws.Selection.Contains(key), null, $"第 {index + 1} 页，{item.Name}");
        if (item.Width > 0 && item.Height > 0) tile.SetAspect(item.Height / (double)item.Width);
        tile.Source ??= item.Thumbnail as ImageSource ?? PdfThumbnails.ImageThumb(item.FilePath);
    }

    protected override string Tooltip(Guid key) => ws.Files.FirstOrDefault(f => f.Id == key)?.Name ?? "";
}
