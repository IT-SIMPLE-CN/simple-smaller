using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

/// 压缩、拆分、转图片：文件列表 + 页面条。
internal sealed class PdfFilesView : Reactive
{
    private readonly Workspace ws;
    private readonly Header head = new() { Glyph = "\uE8A5" };
    private readonly Grid root = new() { RowSpacing = 12 };
    private readonly PdfPageStrip strip = new();
    private FileItem? shown;

    public PdfFilesView(Workspace ws)
    {
        this.ws = ws;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var add = new CircleIconButton("\uE710", "添加文件（Ctrl+O）");
        add.Click += async (_, _) => await PdfFilePicker.PickAsync(ws);
        var ctrlO = new KeyboardAccelerator { Key = VirtualKey.O, Modifiers = VirtualKeyModifiers.Control };
        ctrlO.Invoked += async (_, e) => { e.Handled = true; await PdfFilePicker.PickAsync(ws); };
        add.KeyboardAccelerators.Add(ctrlO);
        add.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        head.Trailing = add;
        head.Margin = new Thickness(6, 4, 6, 0);
        root.Children.Add(head);
        var list = new PdfFileList(ws);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        Grid.SetRow(strip, 2);
        root.Children.Add(strip);
        Content = root;
        Watch(ws, nameof(Workspace.Selection));
        WatchList(ws.Files);
        PdfFileWatch.Pages(this, ws, Schedule, nameof(FileItem.Pages), nameof(FileItem.Bytes), nameof(FileItem.UnlockedPath));
    }

    /// 页面条显示选中的那个；没选时显示第一个。
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
        strip.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        if (item is not null && (item != shown || strip.Path != item.PreviewPath || strip.Count != item.Pages))
        {
            var changed = item != shown;
            strip.Show(item);
            if (changed && shown is not null) Motion.Appear(strip);
        }
        shown = item;
    }
}

/// 一个文件的页面条（只看不改）。
internal sealed class PdfPageStrip : UserControl
{
    private readonly TextBlock name = PdfUi.Text("", 12, semibold: true);
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly List<PdfPageThumb> thumbs = new();

    public string? Path { get; private set; }
    public int Count { get; private set; }

    public PdfPageStrip()
    {
        Height = 168;
        var g = new Grid { RowSpacing = 8 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(name);
        var note = PdfUi.Text("页面预览 · 排序和删页在「合并」里", 11, "BentoSecondary");
        Grid.SetColumn(note, 1);
        head.Children.Add(note);
        g.Children.Add(head);
        var scroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        // 鼠标滚轮竖着滚也能左右翻。
        scroll.PointerWheelChanged += (_, e) =>
        {
            var d = e.GetCurrentPoint(scroll).Properties.MouseWheelDelta;
            scroll.ChangeView(scroll.HorizontalOffset - d, null, null, true);
            e.Handled = true;
        };
        Grid.SetRow(scroll, 1);
        g.Children.Add(scroll);
        Content = PdfUi.Panel(g, new Thickness(16, 12, 16, 12));
        Loaded += (_, _) => PdfThumbnails.Shared.Changed += OnThumb;
        Unloaded += (_, _) => PdfThumbnails.Shared.Changed -= OnThumb;
    }

    public void Show(FileItem item)
    {
        Path = item.PreviewPath;
        Count = item.Pages;
        name.Text = $"{item.Name} · {item.Pages} 页";
        AutomationProperties.SetName(this, $"{item.Name} 的页面预览");
        row.Children.Clear();
        thumbs.Clear();
        for (var i = 0; i < item.Pages; i++)
        {
            var t = new PdfPageThumb(96);
            t.Set(i + 1, false, null, $"第 {i + 1} 页");
            row.Children.Add(t);
            thumbs.Add(t);
        }
        PdfThumbnails.Shared.Load(Path);
        OnThumb(Path);
    }

    private void OnThumb(string path)
    {
        if (!string.Equals(path, Path, StringComparison.OrdinalIgnoreCase)) return;
        for (var i = 0; i < thumbs.Count; i++)
        {
            if (thumbs[i].Source is null && PdfThumbnails.Shared.Page(path, i) is { } img)
            {
                thumbs[i].Source = img;
                thumbs[i].SetAspect(PdfThumbnails.Shared.Aspect(path, i));
            }
        }
    }
}
