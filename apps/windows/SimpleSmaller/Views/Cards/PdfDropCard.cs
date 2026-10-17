using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace SimpleSmaller.Views.Cards;

/// PDF 没有文件时的左大卡：整张卡就是拖放区，也可以选取（设计稿 02，对应 mac DropCard 的 PDF 部分）。
/// 拖进来的文件由主窗口统一接收分类；这里只管拖到卡上时变红。
internal sealed class PdfDropCard : UserControl
{
    private readonly Workspace ws;
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle dash = PdfDashed.Create();
    private readonly Border circle = new() { Width = 76, Height = 76, CornerRadius = new CornerRadius(38) };
    private readonly FontIcon arrow = new() { Glyph = "\uE896", FontSize = 30 };
    private bool targeted;

    public PdfDropCard(Workspace ws)
    {
        this.ws = ws;
        var fromImages = ws.PdfSegment == PdfSegment.FromImages;
        circle.Child = arrow;
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var stack = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        circle.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(circle);
        var title = PdfUi.Text(fromImages ? "把图片拖到这里" : "把 PDF 或文件夹拖到这里", 18, semibold: true);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        stack.Children.Add(title);
        if (fromImages)
        {
            var formats = PdfUi.Text("JPG · PNG · HEIC · WebP · AVIF", 12, "BentoSecondary");
            formats.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(formats);
        }
        var pick = new CapsuleButton("选取文件…", 40, "BentoInk", "BentoOnInk") { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        pick.Click += async (_, _) => await PdfFilePicker.PickAsync(ws);
        stack.Children.Add(pick);
        var inner = new Grid();
        inner.Children.Add(dash);
        inner.Children.Add(stack);
        Content = Card.Create(inner);
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.DataView.Contains(StandardDataFormats.StorageItems)) SetTargeted(true); };
        DragLeave += (_, _) => SetTargeted(false);
        Drop += (_, _) => SetTargeted(false);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    private void SetTargeted(bool value)
    {
        targeted = value;
        Paint();
    }

    private void Paint()
    {
        dash.Stroke = targeted ? Palette.Brand : Palette.Get(this, "BentoLine");
        dash.Fill = targeted ? new SolidColorBrush(Windows.UI.Color.FromArgb(13, 0xEF, 0x39, 0x22)) : null;
        circle.Background = targeted ? Palette.Brand : Palette.Get(this, "BentoFill");
        arrow.Foreground = targeted ? new SolidColorBrush(Microsoft.UI.Colors.White) : Palette.Get(this, "BentoPrimary");
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
