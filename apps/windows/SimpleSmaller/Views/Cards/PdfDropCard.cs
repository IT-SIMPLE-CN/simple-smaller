using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace SimpleSmaller.Views.Cards;

/// PDF 没有文件时的左大卡：与图片、视频共用设计稿 V3 · 11 的拖放区（DropCard）。
/// 拖进来的文件由主窗口统一接收分类；选取按分段选 PDF 或图片。
internal sealed class PdfDropCard : UserControl
{
    public PdfDropCard(Workspace ws)
    {
        Content = new DropCard(ws);
    }
}

/// 选取文件：PDF 分段选 PDF；「图片转 PDF」选图片。
internal static class PdfFilePicker
{
    public static readonly string[] ImageTypes = [".jpg", ".jpeg", ".png", ".heic", ".heif", ".webp", ".avif", ".tif", ".tiff", ".gif", ".bmp"];

    public static async Task PickAsync(Workspace ws)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (var t in ws.PdfSegment == PdfSegment.FromImages ? ImageTypes : [".pdf"])
        {
            picker.FileTypeFilter.Add(t);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        IReadOnlyList<Windows.Storage.StorageFile>? files;
        try
        {
            files = await picker.PickMultipleFilesAsync();
        }
        catch (Exception e)
        {
            // 选择器进程被关掉、系统策略拦截等：提示一句，不让应用崩。
            Log.Error($"选取文件：{e.Message}");
            AppModel.Shared.Flash("打不开选取文件的窗口，可以直接把文件拖进来");
            return;
        }
        if (files is { Count: > 0 })
        {
            AppModel.Shared.Add(files.Select(f => f.Path), MediaType.Pdf);
        }
    }
}
