using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.System;

namespace SimpleSmaller.Views.Preview;

/// PDF 的文件列表（设计稿 V2.1 · 08）：表头「名称 · 页数 · 原大小」，每个文件一张 52 高的灰底圆角行，
/// 白底小方块里红色文件图标；选中的一行墨黑底白字。处理中、完成、失败等状态写在名称下面一行。
/// 点选（Ctrl 多选、Shift 连选、↑↓ 换行）；右键：在资源管理器中显示 / 输入密码… / 从列表移除；双击在资源管理器中显示；Delete 移除。
internal sealed class PdfFileList : Reactive
{
    /// 列：图标 36 · 名称 · 页数 54 · 原大小 60，间距 10；行左右内边距 8 / 14。
    public static Grid Columns()
    {
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(8, 0, 14, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        return g;
    }

    private readonly Workspace ws;
    private readonly StackPanel stack = new() { Spacing = 6 };
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly Dictionary<Guid, PdfFileRow> rows = new();
    private int? anchor;

    public PdfFileList(Workspace ws)
    {
        this.ws = ws;
        var head = Columns();
        string[] titles = ["", "名称", "页数", "原大小"];
        for (var i = 1; i < titles.Length; i++)
        {
            var t = Themed.Text(titles[i], 11, "BentoSecondary");
            if (i > 1) t.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(t, i);
            head.Children.Add(t);
        }
        AutomationProperties.SetAccessibilityView(head, AccessibilityView.Raw);
        var root = new Grid { RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(head);
        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
        AutomationProperties.SetName(stack, "文件列表");
        Watch(ws, nameof(Workspace.Selection), nameof(Workspace.Running));
        WatchList(ws.Files);
    }

    /// 列表内容的高度（表头 + 全部行），给外面决定留多少给页面预览。
    public double ContentHeight => 14 + 12 + (rows.Count == 0 ? 0 : rows.Count * 52 + (rows.Count - 1) * 6);

    private IReadOnlyList<FileItem> Selected => ws.SelectedItems;

    protected override void Refresh()
    {
        var ids = ws.Files.Select(f => f.Id).ToHashSet();
        foreach (var gone in rows.Keys.Where(id => !ids.Contains(id)).ToList())
        {
            stack.Children.Remove(rows[gone]);
            rows.Remove(gone);
        }
        for (var i = 0; i < ws.Files.Count; i++)
        {
            var f = ws.Files[i];
            if (!rows.TryGetValue(f.Id, out var row))
            {
                row = new PdfFileRow(f, ws, this);
                rows[f.Id] = row;
                stack.Children.Insert(Math.Min(i, stack.Children.Count), row);
            }
            else if (stack.Children.IndexOf(row) != i)
            {
                stack.Children.Remove(row);
                stack.Children.Insert(Math.Min(i, stack.Children.Count), row);
            }
            row.Selected = ws.Selection.Contains(f.Id);
        }
    }

    /// 点一行：单击只选它，Ctrl 加减，Shift 从上次点的那行连到这行。
    public void Click(FileItem item, VirtualKeyModifiers mods)
    {
        var i = ws.Files.IndexOf(item);
        if (mods.HasFlag(VirtualKeyModifiers.Control))
        {
            var set = ws.Selection.ToHashSet();
            if (!set.Remove(item.Id)) set.Add(item.Id);
            ws.SetSelection(set);
            anchor = i;
        }
        else if (mods.HasFlag(VirtualKeyModifiers.Shift) && anchor is { } a && a < ws.Files.Count)
        {
            ws.SetSelection(ws.Files.Skip(Math.Min(a, i)).Take(Math.Abs(a - i) + 1).Select(f => f.Id));
        }
        else
        {
            ws.SetSelection([item.Id]);
            anchor = i;
        }
    }

    /// 行上的按键：↑↓ 换选中行，Delete 移除选中的。
    public void Key(FileItem item, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete && !ws.Running)
        {
            AppModel.Shared.Remove(ws.Selection.Count > 0 ? ws.Selection : new HashSet<Guid> { item.Id }, ws);
            e.Handled = true;
        }
        else if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            var i = ws.Files.IndexOf(item) + (e.Key == VirtualKey.Up ? -1 : 1);
            if (i < 0 || i >= ws.Files.Count) return;
            var next = ws.Files[i];
            ws.SetSelection([next.Id]);
            anchor = i;
            if (rows.TryGetValue(next.Id, out var r)) r.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    public void Reveal() => AppModel.Shared.Reveal(Selected.SelectMany(f => f.RevealPaths).ToList());

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

/// 列表里的一行：图标 · 名称（下面一行状态）· 页数 · 原大小。
internal sealed class PdfFileRow : Reactive
{
    private readonly FileItem item;
    private readonly Workspace ws;
    private readonly Border back = new() { Height = 52, CornerRadius = new CornerRadius(16) };
    private readonly Border iconBack = new() { Width = 36, Height = 36, CornerRadius = new CornerRadius(10) };
    private readonly LineIcon icon = new(Icons.Pdf, 16, 1.8, "Brand");
    private readonly TextBlock name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock state = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock pages = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock size = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private bool selected;
    private bool hovering;
    private string stateKey = "BentoSecondary";

    public PdfFileRow(FileItem item, Workspace ws, PdfFileList owner)
    {
        this.item = item;
        this.ws = ws;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        FocusVisualMargin = new Thickness(-2);
        iconBack.Child = icon;
        AutomationProperties.SetAccessibilityView(iconBack, AccessibilityView.Raw);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(name);
        texts.Children.Add(state);
        ToolTipService.SetToolTip(name, item.FilePath);
        var line = PdfFileList.Columns();
        line.Children.Add(iconBack);
        Grid.SetColumn(texts, 1);
        line.Children.Add(texts);
        Grid.SetColumn(pages, 2);
        line.Children.Add(pages);
        Grid.SetColumn(size, 3);
        line.Children.Add(size);
        back.Child = line;
        Content = back;
        ContextFlyout = owner.Menu(item);

        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            owner.Click(item, e.KeyModifiers);
            Focus(FocusState.Pointer);
        };
        Tapped += async (_, _) =>
        {
            // 加密的 PDF：点状态那行也能输密码。
            if (Locked && !ws.Running) await PasswordDialog.ShowAsync(item, this);
        };
        DoubleTapped += (_, _) => owner.Reveal();
        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space)
            {
                if (Locked && !ws.Running) _ = PasswordDialog.ShowAsync(item, this);
                else owner.Reveal();
                e.Handled = true;
                return;
            }
            owner.Key(item, e);
        };
        Watch(item);
        Watch(ws, nameof(Workspace.Running));
    }

    private bool Locked => item.State.Kind == FileStateKind.Waiting && item.MetaError == "有打开密码";

    public bool Selected
    {
        set
        {
            if (selected == value) return;
            selected = value;
            AutomationProperties.SetItemStatus(this, value ? "已选" : "");
            Paint();
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer(PdfFileRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override IList<AutomationPeer>? GetChildrenCore() => null;
        protected override bool IsKeyboardFocusableCore() => true;
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, selected ? "BentoInk" : (hovering ? "BentoLine" : "BentoFill"));
        // 选中时图标底是白色 12%（深色模式墨色反过来，用黑色 12%）。
        iconBack.Background = selected
            ? new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(31, 0, 0, 0) : Windows.UI.Color.FromArgb(31, 255, 255, 255))
            : Palette.Get(this, "BentoCard");
        icon.ColorKey = selected ? "BentoOnInk" : "Brand";
        name.Foreground = Palette.Get(this, selected ? "BentoOnInk" : "BentoPrimary");
        var secondary = Palette.Get(this, selected ? "BentoOnInk" : "BentoSecondary");
        pages.Foreground = size.Foreground = secondary;
        pages.Opacity = size.Opacity = selected ? 0.7 : 1;
        state.Foreground = selected && stateKey == "BentoSecondary" ? secondary : Themed.Resolve(this, stateKey);
    }

    protected override void Refresh()
    {
        name.Text = item.Name;
        pages.Text = item.MetaError is null ? (item.Pages > 0 ? $"{item.Pages} 页" : "—") : "—";
        size.Text = Format.Size(item.Bytes);
        var (text, key, tip) = State();
        state.Text = text;
        state.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        stateKey = key;
        ToolTipService.SetToolTip(state, tip);
        Paint();
        AutomationProperties.SetName(this, $"{item.Name}，{(item.Pages > 0 ? $"{item.Pages} 页" : item.MetaError ?? "")}，{size.Text}{(text.Length > 0 ? "，" + text : "")}{(Locked ? "，按回车输入密码" : "")}");
    }

    private (string Text, string Key, string? Tip) State() => item.State.Kind switch
    {
        FileStateKind.Waiting when item.MetaError == "有打开密码" => ("需要密码 · 点这里输入", "Warning", "密码只在这次处理里用，不保存"),
        FileStateKind.Waiting when item.MetaError is { } err => (err, "Warning", PdfFileList.Explain(err)),
        FileStateKind.Waiting => (ws.Running ? "等待" : "", "BentoSecondary", null),
        FileStateKind.Running => (item.State.Fraction <= 0 ? "处理中" : $"处理中 {(int)(item.State.Fraction * 100)}%", "Brand", null),
        FileStateKind.Done => (DoneText, "Saving", null),
        FileStateKind.Kept => (item.KeptText, "Warning", "不用处理：压完没有变小，原文件保持不动"),
        FileStateKind.Failed => ($"失败 · {item.State.Message}", "BentoFailed", PdfFileList.Explain(item.State.Message)),
        _ => ("已停止", "BentoSecondary", null),
    };

    private string DoneText
    {
        get
        {
            if (item.Outputs.Length > 1) return $"完成 · {item.Outputs.Length} 个文件";
            if (item.AfterBytes > 0 && item.Bytes > 0) return $"完成 · {Format.Size(item.AfterBytes)} · {Format.Change(item.Bytes, item.AfterBytes)}";
            return "完成";
        }
    }
}
