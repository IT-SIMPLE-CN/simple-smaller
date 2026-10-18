using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Preview;

namespace SimpleSmaller.Views.Common;

/// 文件列表（设计稿 02 / 05）。图片、视频、PDF 共用，按类型换列。
/// 不画行线，用交替底色区分行；多选；右键菜单；双击在资源管理器中显示；Delete 移除；空格打开；处理完的行可以直接拖出。
/// 选中一张已完成的图时，下面出前后对比。
internal sealed class FileTable : Reactive
{
    private readonly Workspace ws;
    private readonly ListView list = new()
    {
        SelectionMode = ListViewSelectionMode.Extended,
        CanDragItems = true,
        IsItemClickEnabled = false,
    };
    private readonly Grid header = new() { Height = 28, Padding = new Thickness(12, 0, 12, 0) };
    private readonly Grid root = new();
    private readonly Border compareHost = new() { Height = 280, Visibility = Visibility.Collapsed };
    private readonly Dictionary<Guid, FileRow> rows = new();
    private Guid? compared;
    private bool syncing;
    private string built = "";

    public FileTable(Workspace ws)
    {
        this.ws = ws;
        AutomationProperties.SetName(list, "文件");
        var container = new Style(typeof(ListViewItem));
        container.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        container.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        container.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
        container.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        list.ItemContainerStyle = container;
        list.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            ws.SetSelection(list.SelectedItems.OfType<FileRow>().Select(r => r.Item.Id));
        };
        list.DoubleTapped += (_, _) => AppModel.Shared.Reveal(ws.SelectedItems.SelectMany(i => i.RevealPaths).ToList());
        list.DragItemsStarting += (_, e) =>
        {
            var paths = e.Items.OfType<FileRow>().Select(r => r.Item.PrimaryPath).ToList();
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
            {
                var d = request.GetDeferral();
                try
                {
                    var items = new List<Windows.Storage.IStorageItem>();
                    foreach (var p in paths)
                    {
                        try { items.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p)); } catch { }
                    }
                    request.SetData(items);
                }
                finally
                {
                    d.Complete();
                }
            });
        };
        FileInteractions.HandleKeys(list, ws);

        BuildHeader();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(header);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        compareHost.Margin = new Thickness(0, 8, 0, 0);
        compareHost.CornerRadius = new CornerRadius(12);
        // 太窄时左右滚，列不叠在一起。
        var hscroll = new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        hscroll.SizeChanged += (_, e) => { Resize(e.NewSize.Width); root.Width = Math.Max(root.MinWidth, e.NewSize.Width); root.Height = e.NewSize.Height; };
        var outer = new Grid();
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.Children.Add(hscroll);
        Grid.SetRow(compareHost, 1);
        outer.Children.Add(compareHost);
        Content = outer;
        Watch(ws, nameof(Workspace.Selection), nameof(Workspace.Running));
        WatchList(ws.Files);
    }

    /// 列宽按设计稿 V2.1（列表视图 / 处理中）：尺寸 104（视频 150）、原大小 80、状态 92；名称列吃掉其余宽度。
    /// 状态列开始处理后才出现，做完写变化（−98%），输出体积看完成页。放不下时各列往最小宽收，再不够就左右滚。
    private static double[] Ideal(MediaType type) => [type == MediaType.Video ? 150 : 104, 80, 92];
    private static double[] Least(MediaType type) => [type == MediaType.Video ? 120 : 84, 58, 64];
    private const double NameMin = 90;

    public static double[] Widths(MediaType type, double available)
    {
        var ideal = Ideal(type);
        var least = Least(type);
        var room = available - 24 - NameMin;
        var f = Math.Clamp((room - least.Sum()) / Math.Max(1, ideal.Sum() - least.Sum()), 0, 1);
        return ideal.Select((v, i) => Math.Floor(least[i] + (v - least[i]) * f)).ToArray();
    }

    public static void ApplyColumns(Grid g, double[] widths)
    {
        if (g.ColumnDefinitions.Count == 0)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = NameMin });
            foreach (var _ in widths) g.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (var i = 0; i < widths.Length; i++)
        {
            g.ColumnDefinitions[i + 1].Width = new GridLength(widths[i]);
        }
    }

    private double[] widths = [];
    private TextBlock? stateHeader;

    /// 状态列开始处理后才有（还没开始时整列不占位，和设计稿一致）。
    private bool Started => ws.Running || ws.Files.Any(f => f.State.Kind != FileStateKind.Waiting);

    private double lastWidth = 600;

    private void Resize(double width)
    {
        lastWidth = width;
        var w = Widths(ws.Type, width);
        if (!Started) w[^1] = 0;
        root.MinWidth = 24 + NameMin + w.Sum();
        if (w.SequenceEqual(widths)) return;
        widths = w;
        ApplyColumns(header, w);
        foreach (var r in rows.Values) r.Columns(w);
    }

    private void BuildHeader()
    {
        var titles = new[] { "名称", ws.Type == MediaType.Pdf ? "页数" : ws.Type == MediaType.Video ? "时长 · 尺寸" : "尺寸", "原大小", "状态" };
        widths = Widths(ws.Type, 600);
        ApplyColumns(header, widths);
        var i = 0;
        foreach (var _ in titles)
        {
            var t = Themed.Text(titles[i], 11, "BentoSecondary");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            if (i > 0) t.HorizontalAlignment = HorizontalAlignment.Right;
            if (i == 3) stateHeader = t;
            if (i == 0) t.Margin = new Thickness(38, 0, 0, 0);
            Grid.SetColumn(t, i++);
            header.Children.Add(t);
        }
        AutomationProperties.SetAccessibilityView(header, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    protected override void Refresh()
    {
        if (stateHeader is not null) stateHeader.Visibility = Started ? Visibility.Visible : Visibility.Collapsed;
        Resize(lastWidth);
        var key = string.Join(",", ws.Files.Select(f => f.Id));
        if (key != built)
        {
            built = key;
            syncing = true;
            list.Items.Clear();
            var live = new HashSet<Guid>();
            var index = 0;
            foreach (var f in ws.Files)
            {
                live.Add(f.Id);
                if (!rows.TryGetValue(f.Id, out var row))
                {
                    row = new FileRow(f, ws);
                    row.Columns(widths);
                    rows[f.Id] = row;
                }
                row.Index = index++;
                list.Items.Add(row);
            }
            foreach (var gone in rows.Keys.Where(k => !live.Contains(k)).ToList())
            {
                rows.Remove(gone);
            }
            syncing = false;
        }
        // 选中跟着模型（缩略图里选的，切到列表还是选中）。
        syncing = true;
        foreach (var row in rows.Values)
        {
            var want = ws.Selection.Contains(row.Item.Id);
            var has = list.SelectedItems.Contains(row);
            if (want && !has) list.SelectedItems.Add(row);
            if (!want && has) list.SelectedItems.Remove(row);
        }
        syncing = false;
        list.CanDragItems = !ws.Running;

        // 前后对比：选中一张已完成的图。
        var item = ws.Type == MediaType.Image && ws.Selection.Count == 1 ? ws.SelectedItems.FirstOrDefault() : null;
        if (item is not null && (item.State.Kind != FileStateKind.Done || item.Outputs.Length == 0)) item = null;
        if (item?.Id != compared)
        {
            compared = item?.Id;
            if (item is null)
            {
                compareHost.Child = null;
                compareHost.Visibility = Visibility.Collapsed;
            }
            else
            {
                compareHost.Child = new CompareView(item);
                compareHost.Visibility = Visibility.Visible;
                Motion.Appear(compareHost);
            }
        }
    }

    /// 列里写短的原因，鼠标停上去给一句怎么办。
    public static string Explain(string? error) => error switch
    {
        null => "",
        "文件不完整" => "文件不完整，可能没下载完。重新下载后点「重试失败的」",
        "文件损坏" => "文件损坏，读不出来，换一份原文件再试",
        "有打开密码" => "右键 →「输入密码…」，密码只在这次处理里用，不保存",
        _ => error,
    };
}

/// 文件表的一行。
internal sealed class FileRow : Reactive
{
    public FileItem Item { get; }
    private readonly Workspace ws;
    private readonly Grid grid = new() { Padding = new Thickness(6, 0, 12, 0), MinHeight = 40, CornerRadius = new CornerRadius(10) };
    private readonly TextBlock name = Themed.Text("", 13);
    private readonly TextBlock detail = Themed.Text("", 12, "BentoSecondary");
    private readonly TextBlock before = Themed.Text("", 12, "BentoSecondary");
    private readonly TextBlock state = Themed.Text("", 12);
    private int index;

    public FileRow(FileItem item, Workspace ws)
    {
        Item = item;
        this.ws = ws;
        var nameRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 8, 0) };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.Children.Add(new Thumb(item) { Width = 26, Height = 26, VerticalAlignment = VerticalAlignment.Center });
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.Text = item.Name;
        Grid.SetColumn(name, 1);
        nameRow.Children.Add(name);
        ToolTipService.SetToolTip(nameRow, item.FilePath);
        var cells = new FrameworkElement[] { nameRow, detail, before, state };
        detail.HorizontalAlignment = before.HorizontalAlignment = state.HorizontalAlignment = HorizontalAlignment.Right;
        for (var c = 0; c < cells.Length; c++)
        {
            cells[c].VerticalAlignment = VerticalAlignment.Center;
            if (cells[c] is TextBlock t)
            {
                t.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            Grid.SetColumn(cells[c], c);
            grid.Children.Add(cells[c]);
        }
        Content = grid;
        ContextFlyout = FileInteractions.Menu(ws, () => FileInteractions.Targets(ws, Item));
        Watch(item);
        Watch(ws, nameof(Workspace.Running), nameof(Workspace.Selection));
    }

    public void Columns(double[] widths) => FileTable.ApplyColumns(grid, widths);

    public int Index
    {
        get => index;
        set { index = value; Paint(); }
    }

    private bool Selected => ws.Selection.Contains(Item.Id);

    private void Paint()
    {
        // 交替底色；选中的行品牌红底白字（设计稿 V2.1 列表视图）。
        grid.Background = Selected ? Palette.Brand
            : index % 2 == 0 ? Palette.Get(this, "BentoFill") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    protected override void Refresh()
    {
        Paint();
        var f = Item;
        var on = Selected;
        detail.Text = f.MetaError ?? ws.Type switch
        {
            MediaType.Image => f.Dimensions,
            MediaType.Video => f.Duration > 0 ? $"{Format.Duration(f.Duration)} · {f.Width} × {f.Height}" : "—",
            _ => f.Type == MediaType.Image ? f.Dimensions : (f.Pages > 0 ? $"{f.Pages} 页" : "—"),
        };
        Themed.Fg(name, on ? "White" : "BentoPrimary");
        Themed.Fg(detail, on ? "White" : f.MetaError is null ? "BentoPrimary" : "Warning");
        ToolTipService.SetToolTip(detail, string.IsNullOrEmpty(FileTable.Explain(f.MetaError)) ? null : FileTable.Explain(f.MetaError));
        before.Text = Format.Size(f.Bytes);
        Themed.Fg(before, on ? "White" : "BentoPrimary");
        var (text, key, tip) = StateText(f, ws.Running);
        state.Text = text;
        Themed.Fg(state, on ? "White" : key);
        ToolTipService.SetToolTip(state, tip);
        AutomationProperties.SetName(this, $"{f.Name}，{detail.Text}，{before.Text}{(text.Length > 0 ? $"，{text}" : "")}");
    }

    /// 状态列：处理中排队的行才写「等待」；还没点开始时留空。
    public static (string Text, string Key, string? Tip) StateText(FileItem item, bool queued)
    {
        var s = item.State;
        return s.Kind switch
        {
            FileStateKind.Waiting when item.MetaError == "有打开密码" => ("需要密码", "Warning", "右键 →「输入密码…」，密码只在这次处理里用，不保存"),
            FileStateKind.Waiting => (queued ? "等待" : "", "BentoSecondary", null),
            // 图片一张很快，没有中间进度，只标「处理中」；视频与 PDF 带百分比。
            FileStateKind.Running => (item.Type == MediaType.Image || s.Fraction <= 0 ? "处理中" : $"处理中 {(int)(s.Fraction * 100)}%", "Brand", null),
            // 做完写变化（−98%），一张出多个文件时写个数；变大保留原图走 Kept。
            FileStateKind.Done when item.Outputs.Length > 1 => ($"{item.Outputs.Length} 个文件", "Saving", null),
            FileStateKind.Done when item.Bytes > 0 && item.AfterBytes > 0 => (Format.Change(item.Bytes, item.AfterBytes), item.AfterBytes < item.Bytes ? "Saving" : "Warning", $"输出 {Format.Size(item.AfterBytes)}"),
            FileStateKind.Done => ("完成", "Saving", null),
            FileStateKind.Kept => (item.KeptText, "Warning", "不用处理：压完没有变小，或已经符合要求（比如本来就没有声音），原文件保持不动"),
            FileStateKind.Failed => ("失败", "BentoFailed", FileTable.Explain(s.Message)),
            _ => ("已停止", "BentoSecondary", null),
        };
    }
}
