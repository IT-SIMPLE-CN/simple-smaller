using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace SimpleSmaller.Views.Cards;

// MARK: - 左大卡：拖放 / 文件

/// 没有文件时的左大卡：整张卡就是拖放区，也可以选取或粘贴（设计稿 02）。
internal sealed class DropCard : UserControl
{
    private readonly Workspace ws;
    private readonly Rectangle dash = new() { RadiusX = 18, RadiusY = 18, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 } };
    private readonly Border circle = new() { Width = 76, Height = 76, CornerRadius = new CornerRadius(38) };
    private readonly FontIcon arrow = new() { Glyph = "", FontSize = 30 };
    private bool targeted;

    public DropCard(Workspace ws)
    {
        this.ws = ws;
        circle.Child = arrow;
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var center = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        circle.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(circle);
        var title = Themed.Text(Title, 18, "BentoPrimary", Themed.Semibold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        center.Children.Add(title);
        if (Formats is { } f)
        {
            var formats = Themed.Text(f, 12, "BentoSecondary");
            formats.HorizontalAlignment = HorizontalAlignment.Center;
            center.Children.Add(formats);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var pick = new CapsuleButton("选取文件…", 40, "BentoInk", "BentoOnInk");
        pick.Click += async (_, _) => await FilePicker.PickAsync(ws.Type);
        pick.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control });
        pick.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        buttons.Children.Add(pick);
        if (ws.Type == MediaType.Image)
        {
            var paste = new CapsuleButton("粘贴 Ctrl+Shift+V", 40);
            AutomationProperties.SetName(paste, "粘贴");
            paste.Click += async (_, _) => await AppModel.Shared.PasteFromClipboardAsync();
            paste.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift });
            paste.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            buttons.Children.Add(paste);
        }
        center.Children.Add(buttons);

        var area = new Grid();
        area.Children.Add(dash);
        area.Children.Add(center);
        if (ws.Type == MediaType.Image)
        {
            var note = Themed.Text("也可以在资源管理器里按住 Alt 点文件，不用开这个窗口", 12, "BentoSecondary");
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.VerticalAlignment = VerticalAlignment.Bottom;
            note.Margin = new Thickness(12, 0, 12, 18);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextAlignment = TextAlignment.Center;
            area.Children.Add(note);
        }
        Content = Card.Create(area);

        AllowDrop = true;
        DragEnter += (_, e) => Target(e.DataView.Contains(StandardDataFormats.StorageItems));
        DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "加进来";
            }
        };
        DragLeave += (_, _) => Target(false);
        Drop += async (_, e) =>
        {
            e.Handled = true;
            Target(false);
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            AppModel.Shared.Add(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)), ws.Type);
        };
        Themed.OnTheme(this, Paint);
    }

    private void Target(bool on)
    {
        if (on == targeted)
        {
            return;
        }
        targeted = on;
        Paint();
        if (on)
        {
            Motion.SpringScale(circle, 1.08f, 0.45f);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Motion.SpringScale(circle, 1f, 0.6f));
        }
    }

    private void Paint()
    {
        circle.Background = targeted ? Palette.Brand : Palette.Get(this, "BentoFill");
        arrow.Foreground = targeted ? new SolidColorBrush(Microsoft.UI.Colors.White) : Palette.Get(this, "BentoPrimary");
        dash.Stroke = targeted ? Palette.Brand : Palette.Get(this, "BentoLine");
        dash.Fill = targeted ? new SolidColorBrush(Windows.UI.Color.FromArgb(13, 0xEF, 0x39, 0x22)) : null;
    }

    private string Title => ws.Type switch
    {
        MediaType.Image => "把图片或文件夹拖到这里",
        MediaType.Video => "把视频或文件夹拖到这里",
        _ => ws.PdfSegment == PdfSegment.FromImages ? "把图片拖到这里" : "把 PDF 或文件夹拖到这里",
    };

    private string? Formats => ws.Type switch
    {
        MediaType.Image => "JPG · PNG · HEIC · WebP · AVIF · TIFF · GIF",
        MediaType.Video => "MP4 · MOV · M4V · MKV · WebM · AVI · GIF 动图",
        _ => ws.PdfSegment == PdfSegment.FromImages ? "JPG · PNG · HEIC · WebP · AVIF" : null,
    };
}

/// 这一批文件：缩略图 / 列表两种看法，底部一条「拖进更多」（设计稿 03）。
internal sealed class FilesCard : Reactive
{
    /// 缩略图 / 列表（这次运行里记住，几类文件共用）。
    private static string view = "grid";

    private readonly Workspace ws;
    private readonly Header header = new() { Glyph = "\uE81E" };
    private readonly Segments<string> viewSwitch;
    private readonly ContentPresenter body = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Border more = new() { Height = 40, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1.2) };
    private readonly HashSet<FileItem> watched = new();
    private string? shownView;

    public FilesCard(Workspace ws)
    {
        this.ws = ws;
        viewSwitch = new Segments<string>(Segments<string>.Kind.Icon, new[] { ("grid", "缩略图", (string?)""), ("list", "列表", (string?)"") }, view, "显示方式");
        viewSwitch.Changed += v => { view = v; ShowView(animated: true); };
        var add = new CircleIconButton("", "添加文件（Ctrl+O）");
        add.Click += async (_, _) => await FilePicker.PickAsync(ws.Type);
        add.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control });
        add.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        trailing.Children.Add(viewSwitch);
        trailing.Children.Add(add);
        if (ws.Type == MediaType.Image)
        {
            var paste = new CircleIconButton("", "粘贴剪贴板（Ctrl+Shift+V）");
            paste.Click += async (_, _) => await AppModel.Shared.PasteFromClipboardAsync();
            paste.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.V, Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift });
            paste.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            trailing.Children.Add(paste);
        }
        header.Trailing = trailing;

        var moreRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var plus = new FontIcon { Glyph = "", FontSize = 11 };
        Themed.Fg(plus, "BentoSecondary");
        moreRow.Children.Add(plus);
        moreRow.Children.Add(Themed.Text(ws.Type == MediaType.Image ? "拖进更多图片或文件夹" : "拖进更多文件", 12, "BentoSecondary"));
        more.Child = moreRow;
        AutomationProperties.SetAccessibilityView(more, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Themed.OnTheme(more, () => more.BorderBrush = Palette.Get(more, "BentoLine"));

        var grid = new Grid { RowSpacing = 14 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(header);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);
        Grid.SetRow(more, 2);
        grid.Children.Add(more);
        Content = Card.Create(grid);
        Watch(ws, nameof(Workspace.Running), nameof(Workspace.Draft));
        WatchList(ws.Files);
        Unloaded += (_, _) =>
        {
            foreach (var f in watched) f.PropertyChanged -= ItemChanged;
            watched.Clear();
        };
        ShowView(animated: false);
    }

    private void ShowView(bool animated)
    {
        if (shownView == view)
        {
            return;
        }
        shownView = view;
        FrameworkElement content = view == "grid" ? new ThumbGrid(ws) : new FileTable(ws);
        body.Content = content;
        if (animated)
        {
            Motion.Appear(content);
        }
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.Bytes))
        {
            Schedule();
        }
    }

    protected override void Refresh()
    {
        // 总大小跟着每个文件读出的体积变。
        foreach (var f in ws.Files.Where(f => watched.Add(f)))
        {
            f.PropertyChanged += ItemChanged;
        }
        foreach (var gone in watched.Where(f => !ws.Files.Contains(f)).ToList())
        {
            gone.PropertyChanged -= ItemChanged;
            watched.Remove(gone);
        }
        header.Title = Title;
        header.Subtitle = Subtitle;
        more.Visibility = ws.Running ? Visibility.Collapsed : Visibility.Visible;
        viewSwitch.Selected = view;
    }

    private string Title
    {
        get
        {
            var n = ws.Files.Count;
            if (ws.Running) return $"{n} 个文件 · 处理中";
            if (ws.IsCropping)
            {
                var label = ws.CropPreset?.RatioLabel is { Length: > 0 } r ? r : "自由比例";
                return $"{n} 个文件 · 按 {label} 裁";
            }
            return n > 1 ? $"{n} 个文件 · 统一设置" : "1 个文件";
        }
    }

    private string Subtitle
    {
        get
        {
            if (ws.Running) return "完成一个写出一个 · 按 Ctrl+. 停止";
            if (ws.IsCropping) return "框是每张会裁出来的样子 · 点一张可以调框";
            return ws.Files.Count > 1 ? $"改动对这一批全部生效 · {Format.Size(ws.TotalBytes)}" : Format.Size(ws.TotalBytes);
        }
    }
}

/// 缩略图网格：方块自适应列数（最小 130、最大 220），底部胶囊写文件名与大小；处理中、完成在角上标状态。
/// 选中后空格用默认应用打开，处理完的可以直接拖出。
internal sealed class ThumbGrid : Reactive
{
    private readonly Workspace ws;
    private readonly TilePanel panel = new() { Margin = new Thickness(2) };
    private readonly Dictionary<Guid, ThumbTile> tiles = new();
    private string built = "";

    public ThumbGrid(Workspace ws)
    {
        this.ws = ws;
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
        AutomationProperties.SetName(scroll, "缩略图");
        FileInteractions.HandleKeys(scroll, ws);
        Content = scroll;
        WatchList(ws.Files);
    }

    protected override void Refresh()
    {
        var key = string.Join(",", ws.Files.Select(f => f.Id));
        if (key == built)
        {
            return;
        }
        built = key;
        panel.Children.Clear();
        var live = new HashSet<Guid>();
        foreach (var f in ws.Files)
        {
            live.Add(f.Id);
            if (!tiles.TryGetValue(f.Id, out var tile))
            {
                tile = new ThumbTile(f, ws);
                tiles[f.Id] = tile;
            }
            panel.Children.Add(tile);
        }
        foreach (var gone in tiles.Keys.Where(k => !live.Contains(k)).ToList())
        {
            tiles.Remove(gone);
        }
    }
}

/// 方块网格的排布（LazyVGrid adaptive 130–220、间距 10）。
internal sealed class TilePanel : Panel
{
    private const double Min = 130, Max = 220, Gap = 10;

    private (int Cols, double Tile) Grid(double width)
    {
        if (double.IsInfinity(width) || width <= 0) return (1, Min);
        var cols = Math.Max(1, (int)Math.Floor((width + Gap) / (Min + Gap)));
        var tile = Math.Min(Max, (width - Gap * (cols - 1)) / cols);
        return (cols, Math.Floor(tile));
    }

    protected override Size MeasureOverride(Size available)
    {
        var (cols, tile) = Grid(available.Width);
        foreach (var c in Children) c.Measure(new Size(tile, tile));
        var rows = (int)Math.Ceiling(Children.Count / (double)cols);
        var w = double.IsInfinity(available.Width) ? cols * tile + (cols - 1) * Gap : available.Width;
        return new Size(w, rows == 0 ? 0 : rows * tile + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, tile) = Grid(final.Width);
        for (var i = 0; i < Children.Count; i++)
        {
            var r = i / cols;
            var c = i % cols;
            Children[i].Arrange(new Rect(c * (tile + Gap), r * (tile + Gap), tile, tile));
        }
        return final;
    }
}

/// 一张缩略图：选了比例时画出会裁出来的框，调过的标「调过」；点一张进大图调框。
internal sealed class ThumbTile : Reactive
{
    private readonly FileItem item;
    private readonly Workspace ws;
    private readonly Button button = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Grid face = new();
    private readonly CropOverlay overlay = new();
    private readonly Border adjustedTag;
    private readonly ContentPresenter badgeHost = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6) };
    private readonly TextBlock name = Themed.Text("", 10);
    private readonly TextBlock size = Themed.Text("", 10, "BentoSecondary");
    private readonly Border namePill = new() { Height = 22, CornerRadius = new CornerRadius(11), Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border ring = new() { CornerRadius = new CornerRadius(14), IsHitTestVisible = false };
    private string badgeKey = "";

    public ThumbTile(FileItem item, Workspace ws)
    {
        this.item = item;
        this.ws = ws;
        button.Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(button, true);
        face.Children.Add(new Thumb(item, 0));
        face.Children.Add(overlay);
        adjustedTag = Themed.Capsule(Themed.Text("调过", 10, "BentoOnInk", Themed.Semibold), "BentoInk", 20, 7);
        adjustedTag.HorizontalAlignment = HorizontalAlignment.Left;
        adjustedTag.VerticalAlignment = VerticalAlignment.Top;
        adjustedTag.Margin = new Thickness(6);
        face.Children.Add(adjustedTag);
        face.Children.Add(badgeHost);
        var nameRow = new Grid { ColumnSpacing = 6 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Text = item.Name;
        nameRow.Children.Add(name);
        Grid.SetColumn(size, 1);
        nameRow.Children.Add(size);
        namePill.Child = nameRow;
        face.Children.Add(namePill);
        face.Children.Add(ring);
        RoundClip.Apply(face, 14);
        button.Content = face;
        Content = button;

        button.Click += (_, _) => Tap();
        button.AddHandler(DoubleTappedEvent, new DoubleTappedEventHandler((_, _) => AppModel.Shared.Reveal(item.RevealPaths)), true);
        button.ContextFlyout = FileInteractions.Menu(ws, () => FileInteractions.Targets(ws, item));
        FileInteractions.EnableDragOut(button, () => ws.Running ? [] : [item.PrimaryPath]);
        Themed.OnTheme(this, Paint);
        Watch(item);
        Watch(ws, nameof(Workspace.Selection), nameof(Workspace.Draft), nameof(Workspace.CropBoxes), nameof(Workspace.Running));
    }

    /// 这张在裁切里是第几张（读不出尺寸的图不参与裁切）。
    private int? CropIndex
    {
        get
        {
            if (!ws.IsCropping) return null;
            var items = ws.CropItems;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Id == item.Id) return i;
            }
            return null;
        }
    }

    private void Tap()
    {
        ws.SetSelection([item.Id]);
        // 选了比例：点一张进大图调框。
        if (CropIndex is { } i && !ws.Running)
        {
            ws.CropIndex = i;
            ws.CropEditing = true;
        }
    }

    private void Paint()
    {
        var c = ((SolidColorBrush)Palette.Get(this, "BentoCard")).Color;
        namePill.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(235, c.R, c.G, c.B));
        var selected = ws.Selection.Contains(item.Id);
        ring.BorderThickness = new Thickness(selected ? 3 : 1);
        ring.BorderBrush = selected ? Palette.Brand : new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(15, 255, 255, 255) : Windows.UI.Color.FromArgb(15, 0, 0, 0));
    }

    protected override void Refresh()
    {
        Paint();
        var selected = ws.Selection.Contains(item.Id);
        var sizeText = item.State.Kind == FileStateKind.Done && item.AfterBytes > 0 ? Format.Size(item.AfterBytes) : Format.Size(item.Bytes);
        size.Text = sizeText;
        AutomationProperties.SetName(button, $"{item.Name}，{sizeText}");
        AutomationProperties.SetItemStatus(button, selected ? "已选" : "");
        ToolTipService.SetToolTip(button, item.MetaError is { } e ? Common.FileTable.Explain(e) : item.FilePath);

        var crop = CropIndex;
        if (crop is { } i && item.Width > 0 && item.Height > 0)
        {
            var wasHidden = overlay.Visibility != Visibility.Visible;
            overlay.Set(ws.EffectiveCropBox(i), item.Width, item.Height);
            overlay.Visibility = Visibility.Visible;
            if (wasHidden && Motion.Enabled)
            {
                Motion.Appear(overlay);
            }
        }
        else
        {
            overlay.Visibility = Visibility.Collapsed;
        }
        adjustedTag.Visibility = crop is not null && ws.CropBoxes.ContainsKey(item.FilePath) ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadge();
    }

    private void UpdateBadge()
    {
        var s = item.State;
        var key = $"{s.Kind}|{(int)(s.Fraction * 100)}|{s.Message}|{item.MetaError}";
        if (key == badgeKey)
        {
            return;
        }
        badgeKey = key;
        UIElement? badge = s.Kind switch
        {
            FileStateKind.Running => Pill(s.Fraction < 0.02 ? "处理中" : $"{(int)(s.Fraction * 100)}%", "Brand"),
            FileStateKind.Done => Check(),
            FileStateKind.Kept => Pill(item.KeptText, "Warning"),
            FileStateKind.Failed => Pill(s.Message ?? "失败", "BentoFailed"),
            _ => item.MetaError is { } e ? Pill(e, "Warning") : null,
        };
        badgeHost.Content = badge;
        if (badge is FrameworkElement fe)
        {
            Motion.Appear(fe);
        }
    }

    private static Border Pill(string text, string fill)
    {
        var t = Themed.Text(text, 10, "White", Themed.Semibold);
        t.MaxLines = 1;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        var b = Themed.Capsule(t, fill, 20, 7);
        b.MaxWidth = 140;
        return b;
    }

    private static Border Check()
    {
        var icon = new FontIcon { Glyph = "", FontSize = 10, FontWeight = Themed.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var b = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Child = icon };
        Themed.Bg(b, "Saving");
        return b;
    }
}

/// 缩略图上的裁切框：框外压暗，框描白线。缩略图按「填满方块」显示，框按同样的缩放和偏移换算过来。
internal sealed class CropOverlay : Grid
{
    private readonly Path dim = new() { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)) };
    private readonly Rectangle frame = new() { Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(242, 255, 255, 255)), StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private PixelRect box;
    private double imageW, imageH;

    public CropOverlay()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Children.Add(dim);
        Children.Add(frame);
        SizeChanged += (_, _) => Layout();
    }

    public void Set(PixelRect box, double imageW, double imageH)
    {
        if (this.box == box && this.imageW == imageW && this.imageH == imageH)
        {
            return;
        }
        this.box = box;
        this.imageW = imageW;
        this.imageH = imageH;
        Layout();
    }

    private void Layout()
    {
        double tw = ActualWidth, th = ActualHeight;
        if (tw <= 0 || imageW <= 0 || imageH <= 0)
        {
            return;
        }
        var s = Math.Max(tw / imageW, th / imageH);
        var dx = (tw - imageW * s) / 2;
        var dy = (th - imageH * s) / 2;
        var r = new Rect(box.X * s + dx, box.Y * s + dy, box.Width * s, box.Height * s);
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, tw, th) });
        group.Children.Add(new RectangleGeometry { Rect = r });
        dim.Data = group;
        frame.Margin = new Thickness(r.X, r.Y, 0, 0);
        frame.Width = r.Width;
        frame.Height = r.Height;
    }
}
