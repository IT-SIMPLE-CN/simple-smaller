using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;

namespace SimpleSmaller.Views.Common;

/// 文件格子 / 文件行的通用操作（缩略图、文件表、完成页共用，行为一致）：
/// 单击选中，双击在资源管理器中显示，拖出，右键菜单，空格用系统默认应用打开（代替 mac 的快速查看）。
internal static class FileInteractions
{
    /// 右键菜单：在资源管理器中显示、从列表移除。items 为空时按 ws 的选中项。
    public static MenuFlyout Menu(Workspace ws, Func<IReadOnlyList<FileItem>> items, bool removable = true)
    {
        var menu = new MenuFlyout();
        var reveal = new MenuFlyoutItem { Text = "在资源管理器中显示", Icon = new FontIcon { Glyph = "" } };
        reveal.Click += (_, _) => AppModel.Shared.Reveal(items().SelectMany(i => i.RevealPaths).ToList());
        menu.Items.Add(reveal);
        if (removable)
        {
            var remove = new MenuFlyoutItem { Text = "从列表移除", Icon = new FontIcon { Glyph = "" } };
            remove.Click += (_, _) => AppModel.Shared.Remove(items().Select(i => i.Id).ToHashSet(), ws);
            menu.Items.Add(remove);
            menu.Opening += (_, _) => remove.IsEnabled = !ws.Running;
        }
        return menu;
    }

    /// 右键点在没选中的那个上：只对它；点在选中的里面：对全部选中的。
    public static IReadOnlyList<FileItem> Targets(Workspace ws, FileItem item) =>
        ws.Selection.Contains(item.Id) ? ws.SelectedItems : [item];

    /// 处理完的可以直接拖出（拖进微信、浏览器、邮件）：处理完拖输出，没处理拖原文件。
    public static void EnableDragOut(UIElement element, Func<IReadOnlyList<string>> paths)
    {
        element.CanDrag = true;
        element.DragStarting += async (_, e) =>
        {
            var list = paths();
            if (list.Count == 0)
            {
                e.Cancel = true;
                return;
            }
            var deferral = e.GetDeferral();
            try
            {
                await Fill(e.Data, list);
            }
            finally
            {
                deferral.Complete();
            }
        };
    }

    public static async Task Fill(DataPackage data, IReadOnlyList<string> paths)
    {
        var items = new List<IStorageItem>();
        foreach (var p in paths)
        {
            try { items.Add(await StorageFile.GetFileFromPathAsync(p)); } catch { }
        }
        data.RequestedOperation = DataPackageOperation.Copy;
        data.SetStorageItems(items);
    }

    /// 空格：用系统默认应用打开选中的那一个（处理完看输出，没处理看原文件）。
    public static void Open(FileItem? item)
    {
        if (item is null)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.PrimaryPath) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            AppModel.Shared.Flash($"打不开：{e.Message}");
        }
    }

    /// 文件网格 / 列表上的按键：Delete 移除选中的，空格打开。
    public static void HandleKeys(UIElement element, Workspace ws)
    {
        element.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.OriginalSource is TextBox)
            {
                return;
            }
            switch (e.Key)
            {
                case VirtualKey.Delete:
                    if (ws.Selection.Count > 0)
                    {
                        AppModel.Shared.Remove(ws.Selection, ws);
                        e.Handled = true;
                    }
                    break;
                case VirtualKey.Space:
                    if (ws.SelectedItems.FirstOrDefault() is { } item)
                    {
                        Open(item);
                        e.Handled = true;
                    }
                    break;
            }
        }), true);
    }
}

/// 选取文件（Ctrl+O）。系统的文件选择器不能同时选文件和文件夹：这里选文件，文件夹直接拖进来。
internal static class FilePicker
{
    public static async Task PickAsync(MediaType preferred)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, CommitButtonText = "添加" };
        var types = preferred switch
        {
            MediaType.Image => new[] { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".avif", ".tif", ".tiff", ".gif", ".bmp" },
            MediaType.Video => new[] { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mts", ".m2ts", ".gif", ".webp", ".png" },
            _ => new[] { ".pdf", ".jpg", ".jpeg", ".png", ".heic", ".webp", ".avif" },
        };
        foreach (var t in types)
        {
            picker.FileTypeFilter.Add(t);
        }
        picker.SuggestedStartLocation = preferred == MediaType.Video ? PickerLocationId.VideosLibrary : PickerLocationId.PicturesLibrary;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
        {
            AppModel.Shared.Add(files.Select(f => f.Path), preferred);
        }
    }
}
