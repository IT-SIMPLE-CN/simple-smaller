using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.System;

namespace SimpleSmaller.Views.Preview;

/// PDF 的文件列表（设计稿 05，对应 mac FileTableView 的 PDF 列）：名称 · 页数 · 原大小 · 输出 · 变化 · 状态。
/// 右键：在资源管理器中显示 / 输入密码… / 从列表移除；双击在资源管理器中显示；Delete 移除。
/// 加密的 PDF 状态写「需要密码」，点它也能输密码。
internal sealed class PdfFileList : Reactive
{
    /// 列宽按设计稿：页数 104、原大小 80、输出 80、变化 64、状态 92；名称列吃掉其余宽度。
    /// 卡片窄时（与 mac 表格一样）其余列按比例缩到最小宽度，名称列至少 110。
    private static readonly double[] Ideal = [104, 80, 80, 64, 92];
    private static readonly double[] Min = [88, 64, 64, 52, 76];
    private const double NameMin = 110;
    private const double Spacing = 8;
    private readonly List<Grid> lines = new();
    private double[] widths = Ideal;

    private readonly Workspace ws;
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Extended, IsMultiSelectCheckBoxEnabled = false };
    private readonly Dictionary<Guid, ListViewItem> rows = new();
    private bool syncing;

    public PdfFileList(Workspace ws)
    {
        this.ws = ws;
        var head = NewLine();
        string[] titles = ["名称", "页数", "原大小", "输出", "变化", "状态"];
        for (var i = 0; i < titles.Length; i++)
        {
            var t = PdfUi.Text(titles[i], 11, "BentoSecondary");
            Grid.SetColumn(t, i);
            head.Children.Add(t);
        }
        head.Padding = new Thickness(12, 0, 12, 0);
        head.Height = 26;
        AutomationProperties.SetAccessibilityView(head, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(head);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        Content = root;
        AutomationProperties.SetName(list, "文件列表");
        list.SelectionChanged += (_, _) =>
        {
            if (syncing) return;
            ws.SetSelection(list.SelectedItems.OfType<ListViewItem>().Select(i => (Guid)i.Tag));
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Delete && !ws.Running)
            {
                AppModel.Shared.Remove(ws.Selection, ws);
                e.Handled = true;
            }
        };
        list.DoubleTapped += (_, _) => AppModel.Shared.Reveal(Selected.SelectMany(f => f.RevealPaths).ToList());
        Watch(ws, nameof(Workspace.Selection), nameof(Workspace.Running));
        WatchList(ws.Files);
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// 一行的列（表头与每一行共用同一组列宽）。
    public Grid NewLine()
    {
        var g = new Grid { ColumnSpacing = Spacing };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var w in widths)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
        }
        lines.Add(g);
        return g;
    }

    private void Fit(double width)
    {
        var room = width - 24 - Spacing * Ideal.Length - NameMin;
        var ideal = Ideal.Sum();
        var min = Min.Sum();
        var t = ideal <= room ? 1 : Math.Clamp((room - min) / (ideal - min), 0, 1);
        // 连最小宽度都放不下（窗口窄、200% 缩放）：其余列再等比缩一点，给名称列留出地方。
        var squeeze = room < min ? Math.Max(0.7, room / min) : 1;
        widths = Ideal.Select((w, i) => Math.Round((Min[i] + (w - Min[i]) * t) * squeeze)).ToArray();
        foreach (var g in lines)
        {
            for (var i = 0; i < widths.Length; i++) g.ColumnDefinitions[i + 1].Width = new GridLength(widths[i]);
        }
    }

    private IReadOnlyList<FileItem> Selected => ws.SelectedItems;

    protected override void Refresh()
    {
        syncing = true;
        var ids = ws.Files.Select(f => f.Id).ToHashSet();
        foreach (var gone in rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            list.Items.Remove(rows[gone]);
            rows.Remove(gone);
        }
        for (var i = 0; i < ws.Files.Count; i++)
        {
            var f = ws.Files[i];
            if (!rows.TryGetValue(f.Id, out var row))
            {
                row = new ListViewItem
                {
                    Tag = f.Id,
                    Content = new PdfFileRow(f, ws, this),
                    Padding = new Thickness(12, 0, 12, 0),
                    MinHeight = 34,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                rows[f.Id] = row;
                list.Items.Insert(Math.Min(i, list.Items.Count), row);
            }
            else if (list.Items.IndexOf(row) != i)
            {
                list.Items.Remove(row);
                list.Items.Insert(Math.Min(i, list.Items.Count), row);
            }
            ((PdfFileRow)row.Content).Alternate = i % 2 == 1;
            var on = ws.Selection.Contains(f.Id);
            if (row.IsSelected != on) row.IsSelected = on;
        }
        list.IsEnabled = true;
        syncing = false;
    }

    /// 右键菜单：对选中的那些（右键点的不在选中里时只对它）。
    public MenuFlyout Menu(FileItem item)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var items = ws.Selection.Contains(item.Id) ? Selected : [item];
            var ids = items.Select(f => f.Id).ToHashSet();
            var reveal = new MenuFlyoutItem { Text = "在资源管理器中显示" };
            reveal.Click += (_, _) => AppModel.Shared.Reveal(items.SelectMany(f => f.RevealPaths).ToList());
            menu.Items.Add(reveal);
            if (items.FirstOrDefault(f => f.MetaError == "有打开密码") is { } locked)
            {
                var unlock = new MenuFlyoutItem { Text = "输入密码…" };
                unlock.Click += async (_, _) => await PasswordDialog.ShowAsync(locked, this);
                menu.Items.Add(unlock);
            }
            var remove = new MenuFlyoutItem { Text = "从列表移除", IsEnabled = !ws.Running };
            remove.Click += (_, _) => AppModel.Shared.Remove(ids, ws);
            menu.Items.Add(remove);
        };
        return menu;
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

/// 列表里的一行：缩略图 + 名称、页数、原大小、输出、变化、状态。
internal sealed class PdfFileRow : Reactive
{
    private readonly FileItem item;
    private readonly Workspace ws;
    private readonly Grid line;
    private readonly Image thumb = new() { Width = 22, Height = 22, Stretch = Stretch.UniformToFill };
    private readonly TextBlock name = PdfUi.Text("", 13);
    private readonly TextBlock detail = PdfUi.Text("", 13, "BentoSecondary");
    private readonly TextBlock before = PdfUi.Text("", 13, "BentoSecondary");
    private readonly TextBlock after = PdfUi.Text("", 13);
    private readonly TextBlock change = PdfUi.Text("", 13);
    private readonly TextBlock state = PdfUi.Text("", 13);
    private readonly Button stateButton = new() { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Left };
    private bool alternate;

    public PdfFileRow(FileItem item, Workspace ws, PdfFileList owner)
    {
        this.item = item;
        this.ws = ws;
        line = owner.NewLine();
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var thumbBack = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), Child = thumb };
        AutomationProperties.SetAccessibilityView(thumbBack, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(thumb, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        nameCell.Children.Add(thumbBack);
        nameCell.Children.Add(name);
        ToolTipService.SetToolTip(nameCell, item.FilePath);
        stateButton.Content = state;
        stateButton.Click += async (_, _) =>
        {
            if (item.MetaError == "有打开密码" && !ws.Running) await PasswordDialog.ShowAsync(item, this);
        };
        var i = 0;
        foreach (var e in new FrameworkElement[] { nameCell, detail, before, after, change, stateButton })
        {
            Grid.SetColumn(e, i++);
            e.VerticalAlignment = VerticalAlignment.Center;
            line.Children.Add(e);
        }
        line.Height = 34;
        Content = line;
        ContextFlyout = owner.Menu(item);
        Watch(item);
        Watch(ws, nameof(Workspace.Running));
    }

    public bool Alternate
    {
        set { alternate = value; Paint(); }
    }

    private void Paint()
    {
        // 与资源管理器详细信息视图一致：不画行线，交替底色区分行。
        line.Background = alternate ? Palette.Get(this, "BentoFill") : null;
        line.Margin = new Thickness(-12, 0, -12, 0);
        line.Padding = new Thickness(12, 0, 12, 0);
    }

    protected override void Refresh()
    {
        Paint();
        name.Text = item.Name;
        if (item.Thumbnail is ImageSource img) thumb.Source = img;
        thumb.Parent.SetValue(Border.BackgroundProperty, Palette.Get(this, "BentoFill"));
        detail.Text = item.MetaError ?? (item.Type == MediaType.Image ? item.Dimensions : item.Pages > 0 ? $"{item.Pages}" : "—");
        PdfUi.Retint(detail, item.MetaError is null ? "BentoSecondary" : "Warning");
        ToolTipService.SetToolTip(detail, item.MetaError is null ? null : PdfFileList.Explain(item.MetaError));
        before.Text = Format.Size(item.Bytes);
        after.Text = item.State.Kind switch
        {
            FileStateKind.Done => Format.Size(item.AfterBytes),
            FileStateKind.Kept => "—",
            _ => "",
        };
        PdfUi.Retint(after, item.State.Kind == FileStateKind.Kept ? "BentoSecondary" : "BentoPrimary");
        if (item.State.Kind == FileStateKind.Kept)
        {
            change.Text = "已很小";
            PdfUi.Retint(change, "BentoSecondary");
        }
        else if (item.State.Kind == FileStateKind.Done && item.AfterBytes > 0 && item.Bytes > 0)
        {
            change.Text = Format.Change(item.Bytes, item.AfterBytes);
            PdfUi.Retint(change, item.AfterBytes < item.Bytes ? "Saving" : "Warning");
        }
        else
        {
            change.Text = "";
        }
        var (text, key, tip) = State();
        state.Text = text;
        PdfUi.Retint(state, key);
        ToolTipService.SetToolTip(stateButton, tip);
        var locked = item.State.Kind == FileStateKind.Waiting && item.MetaError == "有打开密码";
        stateButton.IsHitTestVisible = locked;
        stateButton.IsTabStop = locked;
        AutomationProperties.SetName(stateButton, locked ? "需要密码，输入密码" : text);
        AutomationProperties.SetAccessibilityView(stateButton, locked ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        if (Parent is ListViewItem row)
        {
            var what = item.MetaError ?? (item.Type == MediaType.Image ? item.Dimensions : $"{item.Pages} 页");
            AutomationProperties.SetName(row, $"{item.Name}，{what}，{before.Text}{(text.Length > 0 ? "，" + text : "")}");
        }
    }

    private (string Text, string Key, string? Tip) State() => item.State.Kind switch
    {
        FileStateKind.Waiting when item.MetaError == "有打开密码" => ("需要密码", "Warning", "点这里或右键 →「输入密码…」，密码只在这次处理里用，不保存"),
        FileStateKind.Waiting => (ws.Running ? "等待" : "", "BentoSecondary", null),
        // PDF 带百分比；图片一张很快，只标「处理中」。
        FileStateKind.Running => (item.Type == MediaType.Image || item.State.Fraction <= 0 ? "处理中" : $"处理中 {(int)(item.State.Fraction * 100)}%", "Brand", null),
        FileStateKind.Done => (item.Outputs.Length > 1 ? $"{item.Outputs.Length} 个文件" : "完成", "Saving", null),
        FileStateKind.Kept => (item.KeptText, "Warning", "不用处理：压完没有变小，或已经符合配方（比如本来就没有声音），原文件保持不动"),
        FileStateKind.Failed => ("失败", "BentoFailed", PdfFileList.Explain(item.State.Message)),
        _ => ("已停止", "BentoSecondary", null),
    };
}
