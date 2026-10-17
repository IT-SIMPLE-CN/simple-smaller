using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace SimpleSmaller.Views.Result;

/// 名称 · 原大小 · 输出 · 变化 · 状态 五列。
internal static class ResultColumns
{
    public static Grid Create(UIElement name, UIElement before, UIElement after, UIElement change, UIElement state)
    {
        var g = new Grid { ColumnSpacing = 12, Padding = new Thickness(6, 0, 14, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        UIElement[] cells = [name, before, after, change, state];
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Center;
                if (i > 0) fe.HorizontalAlignment = HorizontalAlignment.Right;
            }
            Grid.SetColumn((FrameworkElement)cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    /// 失败原因的说明（悬停提示）。
    public static string Explain(string? error) => error switch
    {
        null => "",
        "文件不完整" => "文件不完整，可能没下载完。重新下载后点「重试失败的」",
        "文件损坏" => "文件损坏，读不出来，换一份原文件再试",
        "有打开密码" => "右键 →「输入密码…」，密码只在这次处理里用，不保存",
        _ => error,
    };
}

/// 完成页逐个文件的一行：单击选中、双击或空格 / 回车用默认应用打开、可以直接拖出输出文件、右键在资源管理器中显示。
internal sealed class ResultRow : Reactive
{
    private readonly FileItem item;
    private readonly Workspace ws;
    private readonly bool striped;
    private readonly Border back = new() { Height = 34, CornerRadius = new CornerRadius(11) };
    private readonly Border thumb = new() { Width = 24, Height = 24, CornerRadius = new CornerRadius(6) };
    private readonly TextBlock name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock before = new() { FontSize = 12 };
    private readonly TextBlock after = new() { FontSize = 12 };
    private readonly TextBlock change = new() { FontSize = 12 };
    private readonly StackPanel stateCell = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private LineIcon? stateIcon;
    private readonly TextBlock stateText = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };

    public ResultRow(FileItem item, Workspace ws, bool striped)
    {
        this.item = item;
        this.ws = ws;
        this.striped = striped;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        var nameCell = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        nameCell.Children.Add(thumb);
        nameCell.Children.Add(name);
        stateText.MaxWidth = 68;
        stateCell.Children.Add(stateText);
        // PDF 没有缩略图：白底红色文件图标（与 PDF 页文件列表一致）。
        if (item.Type == MediaType.Pdf) thumb.Child = new LineIcon(Icons.Pdf, 14, 1.8, "Brand") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        back.Child = ResultColumns.Create(nameCell, before, after, change, stateCell);
        Content = back;

        name.Text = item.Name;
        before.Text = Format.Size(item.Bytes);
        after.Text = item.State == FileState.Done ? Format.Size(item.AfterBytes) : "—";
        change.Text = item.State == FileState.Done ? Format.Change(item.Bytes, item.AfterBytes) : "—";
        (var iconData, stateText.Text) = item.State.Kind switch
        {
            FileStateKind.Done => (ResultIcons.Check, "完成"),
            FileStateKind.Kept => (ResultIcons.Alert, item.KeptText),
            FileStateKind.Failed => (ResultIcons.Cross, item.State.Message ?? "失败"),
            FileStateKind.Cancelled => ("", "已停止"),
            _ => ("", "—"),
        };
        if (iconData.Length > 0)
        {
            stateIcon = new LineIcon(iconData, 12, 2.6) { VerticalAlignment = VerticalAlignment.Center };
            stateCell.Children.Insert(0, stateIcon);
        }
        ToolTipService.SetToolTip(this, Help);
        AutomationProperties.SetName(this, $"{item.Name}，原大小 {before.Text}，输出 {after.Text}，变化 {change.Text}，{stateText.Text}");

        Watch(item, nameof(FileItem.Thumbnail));
        Watch(ws, nameof(Workspace.Selection));

        PointerPressed += (_, e) =>
        {
            var ctrl = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            Select(ctrl);
            Focus(FocusState.Pointer);
        };
        DoubleTapped += (_, _) => Open();
        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter)
            {
                e.Handled = true;
                Open();
            }
        };
        GotFocus += (_, _) => { if (!ws.Selection.Contains(item.Id)) Select(false); };

        var menu = new MenuFlyout();
        var reveal = new MenuFlyoutItem { Text = "在资源管理器中显示" };
        reveal.Click += (_, _) => AppModel.Shared.Reveal(item.RevealPaths);
        var open = new MenuFlyoutItem { Text = "打开" };
        open.Click += (_, _) => Open();
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        ContextFlyout = menu;

        // 拖出：有输出拖输出，没有就拖原文件。
        CanDrag = true;
        DragStarting += async (_, e) =>
        {
            var d = e.GetDeferral();
            try
            {
                var files = new List<IStorageItem>();
                foreach (var p in item.RevealPaths.Where(File.Exists))
                    files.Add(await StorageFile.GetFileFromPathAsync(p));
                if (files.Count == 0)
                {
                    e.Cancel = true;
                    return;
                }
                e.Data.SetStorageItems(files);
                e.Data.RequestedOperation = DataPackageOperation.Copy;
                e.AllowedOperations = DataPackageOperation.Copy;
            }
            catch
            {
                e.Cancel = true;
            }
            finally
            {
                d.Complete();
            }
        };
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer(ResultRow owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override IList<AutomationPeer>? GetChildrenCore() => null;
        protected override bool IsKeyboardFocusableCore() => true;
    }

    private string Help => item.State.Kind switch
    {
        FileStateKind.Failed => ResultColumns.Explain(item.State.Message),
        FileStateKind.Kept => "压完会比原来大，保留原文件",
        _ => item.Outputs.FirstOrDefault()?.Path ?? item.FilePath,
    };

    private void Select(bool toggle)
    {
        if (toggle)
        {
            var set = ws.Selection.ToHashSet();
            if (!set.Remove(item.Id)) set.Add(item.Id);
            ws.SetSelection(set);
        }
        else
        {
            ws.SetSelection([item.Id]);
        }
    }

    /// 用系统默认应用打开（mac 的「快速查看」）。
    private void Open()
    {
        var path = item.PrimaryPath;
        if (!File.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    protected override void Refresh()
    {
        var selected = ws.Selection.Contains(item.Id);
        back.Background = selected ? FormTheme.BrandTint(0.14) : (striped ? Palette.Get(this, "BentoCard") : new SolidColorBrush(Microsoft.UI.Colors.Transparent));
        AutomationProperties.SetItemStatus(this, selected ? "已选" : "");
        var secondary = Palette.Get(this, "BentoSecondary");
        name.Foreground = after.Foreground = Palette.Get(this, "BentoPrimary");
        before.Foreground = secondary;
        change.Foreground = item.State.Kind switch
        {
            FileStateKind.Done => Palette.Saving,
            FileStateKind.Kept => Palette.Warning,
            _ => secondary,
        };
        (var iconKey, stateText.Foreground) = item.State.Kind switch
        {
            FileStateKind.Done => ("Saving", secondary),
            FileStateKind.Kept => ("Warning", Palette.Warning),
            FileStateKind.Failed => ("BentoFailed", Palette.Get(this, "BentoFailed")),
            _ => ("BentoSecondary", secondary),
        };
        if (stateIcon is not null) stateIcon.ColorKey = iconKey;
        if (item.Thumbnail is ImageSource src)
        {
            thumb.Background = new ImageBrush { ImageSource = src, Stretch = Stretch.UniformToFill };
        }
        else
        {
            thumb.Background = Palette.Get(this, "BentoFill");
        }
    }
}
