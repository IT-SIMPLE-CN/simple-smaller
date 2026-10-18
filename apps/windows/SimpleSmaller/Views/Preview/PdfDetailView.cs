using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Cards;
using Windows.System;

namespace SimpleSmaller.Views.Preview;

/// PDF 详情区（设计稿 05）：
/// 压缩、拆分、转图片——上面文件列表，下面选中文件的页面条；
/// 合并——全部页面按顺序排开，拖动排序、选中按 Delete 删页；
/// 图片转 PDF——图片按顺序排开，一张一页，拖动排序。
internal static class PdfDetailView
{
    public static UIElement Create(Workspace ws) => ws.PdfSegment switch
    {
        PdfSegment.Merge => new PdfPageGrid(ws),
        PdfSegment.FromImages => new PdfImageOrderGrid(ws),
        _ => new PdfFilesView(ws),
    };
}

/// 压缩、拆分、转图片（设计稿 V2.1 · 08）：卡头「N 个文件」+ 添加，文件列表，下面灰底面板里是选中文件的页面网格（每行 4 页）。
internal sealed class PdfFilesView : Reactive
{
    private readonly Workspace ws;
    private readonly Header head = new();
    private readonly Grid root = new() { RowSpacing = 12 };
    private readonly PdfFileList list;
    private readonly PdfPagePreview preview = new() { Margin = new Thickness(0, 4, 0, 0) };
    private FileItem? shown;

    public PdfFilesView(Workspace ws)
    {
        this.ws = ws;
        HeaderIcon.Set(head, PdfIcons.Layers);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var add = new CircleIconButton("\uE710", "添加文件（Ctrl+O）");
        add.Click += async (_, _) => await PdfFilePicker.PickAsync(ws);
        var ctrlO = new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control };
        ctrlO.Invoked += async (_, e) => { e.Handled = true; await PdfFilePicker.PickAsync(ws); };
        add.KeyboardAccelerators.Add(ctrlO);
        add.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        trailing.Children.Add(add);
        trailing.Children.Add(Common.ClearButton.Create(ws));
        head.Trailing = trailing;
        root.Children.Add(head);
        list = new PdfFileList(ws);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        Grid.SetRow(preview, 2);
        root.Children.Add(preview);
        Content = root;
        // 文件多时列表自己滚，至少给页面预览留 200。
        root.SizeChanged += (_, _) => FitList();
        Watch(ws, nameof(Workspace.Selection));
        WatchList(ws.Files);
        PdfFileWatch.Pages(this, ws, Schedule, nameof(FileItem.Pages), nameof(FileItem.Bytes), nameof(FileItem.UnlockedPath));
    }

    private void FitList()
    {
        var h = root.ActualHeight;
        if (h <= 0) return;
        var room = h - 34 - 24 - (preview.Visibility == Visibility.Visible ? 204 : 0);
        list.MaxHeight = Math.Max(26 + 52, room);
    }

    /// 页面预览显示选中的那个；没选时显示第一个。
    private FileItem? PreviewItem
    {
        get
        {
            var pdfs = ws.Files.Where(f => f.Type == MediaType.Pdf && f.Pages > 0).ToList();
            return pdfs.FirstOrDefault(f => ws.Selection.Contains(f.Id)) ?? pdfs.FirstOrDefault();
        }
    }

    protected override void Refresh()
    {
        head.Title = $"{ws.Files.Count} 个文件";
        head.Subtitle = $"{ws.Files.Sum(f => f.Pages)} 页 · {Format.Size(ws.TotalBytes)}";
        var item = PreviewItem;
        preview.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is not null && (item != shown || preview.Path != item.PreviewPath || preview.Count != item.Pages))
        {
            var changed = item != shown;
            preview.Show(item);
            if (changed && shown is not null) Motion.Appear(preview);
        }
        shown = item;
        FitList();
    }
}

/// 一个文件的页面预览（只看不改）：灰底面板，上面「文件名 · N 页」和说明，下面每行 4 页，16:10 的白底小图，下方页码。
internal sealed class PdfPagePreview : UserControl
{
    private readonly TextBlock name = Themed.Text("", 12, "BentoPrimary", Themed.Semibold);
    private readonly Grid grid = new() { ColumnSpacing = 12, RowSpacing = 14 };
    private readonly List<PdfPreviewTile> tiles = new();

    public string? Path { get; private set; }
    public int Count { get; private set; }

    public PdfPagePreview()
    {
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var g = new Grid { RowSpacing = 12 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(name);
        var note = Themed.Text("页面预览 · 排序和删页在「合并」里", 11, "BentoSecondary");
        Grid.SetColumn(note, 1);
        head.Children.Add(note);
        g.Children.Add(head);
        var scroll = new ScrollViewer
        {
            Content = grid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        g.Children.Add(scroll);
        Content = PdfUi.Panel(g, new Thickness(16, 14, 16, 16));
        Loaded += (_, _) => PdfThumbnails.Shared.Changed += OnThumb;
        Unloaded += (_, _) => PdfThumbnails.Shared.Changed -= OnThumb;
    }

    public void Show(FileItem item)
    {
        Path = item.PreviewPath;
        Count = item.Pages;
        name.Text = $"{item.Name} · {item.Pages} 页";
        AutomationProperties.SetName(this, $"{item.Name} 的页面预览");
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        tiles.Clear();
        for (var i = 0; i < item.Pages; i++)
        {
            if (i % 4 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var t = new PdfPreviewTile(i + 1);
            Grid.SetRow(t, i / 4);
            Grid.SetColumn(t, i % 4);
            grid.Children.Add(t);
            tiles.Add(t);
        }
        PdfThumbnails.Shared.Load(Path);
        OnThumb(Path);
    }

    private void OnThumb(string path)
    {
        if (!string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) return;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].Source is null && PdfThumbnails.Shared.Page(path, i) is { } img) tiles[i].Source = img;
        }
    }
}

/// 页面预览的一格：16:10 白底圆角 6、细描边，页面等比放在里面；下方页码。
internal sealed class PdfPreviewTile : StackPanel
{
    private readonly Border paper = new()
    {
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Microsoft.UI.Colors.White),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 0, 0, 0)),
    };
    private readonly Image image = new() { Stretch = Stretch.Uniform };

    public PdfPreviewTile(int number)
    {
        Spacing = 6;
        paper.Child = image;
        paper.SizeChanged += (_, e) =>
        {
            var h = Math.Round(e.NewSize.Width * 10 / 16);
            if (Math.Abs(paper.Height - h) >= 1 || double.IsNaN(paper.Height)) paper.Height = h;
        };
        Children.Add(paper);
        var n = Themed.Text($"{number}", 11, "BentoSecondary");
        n.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(n);
        AutomationProperties.SetName(this, $"第 {number} 页");
        AutomationProperties.SetAccessibilityView(n, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(paper, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    public ImageSource? Source
    {
        get => image.Source;
        set => image.Source = value;
    }
}
