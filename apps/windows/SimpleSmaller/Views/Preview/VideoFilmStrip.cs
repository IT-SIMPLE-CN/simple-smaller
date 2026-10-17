using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Preview;

/// 底部胶片条（剪辑与序列帧的多个视频）：当前这段外圈品牌红、内圈卡片色，右下角写截取后的时长；
/// 切换时自动滚到当前这段。对应 mac FilmStrip（图片裁切那边由图片页自己做）。
internal sealed class VideoFilmStrip : UserControl
{
    private static readonly Dictionary<string, ImageSource> Thumbs = new(StringComparer.OrdinalIgnoreCase);

    private readonly Workspace ws;
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(2, 0, 2, 0) };
    private readonly ScrollViewer scroll;
    private readonly List<(FileItem Item, Button Button, Border Outer, Border Inner, Border Thumb, TextBlock Badge)> cells = new();
    private string built = "";

    public VideoFilmStrip(Workspace ws)
    {
        this.ws = ws;
        Height = 64;
        scroll = new ScrollViewer
        {
            Content = row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        AutomationProperties.SetName(scroll, "视频");
        Content = scroll;
        ActualThemeChanged += (_, _) => Update();
    }

    /// 文件或当前段变了：必要时重建格子，再更新选中与时长。
    public void Update()
    {
        var items = ws.VideoItems;
        var key = string.Join("|", items.Select(i => i.Id));
        if (key != built)
        {
            built = key;
            row.Children.Clear();
            cells.Clear();
            for (var i = 0; i < items.Count; i++) AddCell(items[i], i);
        }
        var current = ws.CurrentVideo;
        var card = Palette.Get(this, "BentoCard");
        var fill = Palette.Get(this, "BentoFill");
        foreach (var (item, button, outer, inner, thumb, badge) in cells)
        {
            var on = item.Id == current?.Id;
            outer.BorderBrush = on ? Palette.Brand : null;
            inner.BorderBrush = on ? card : null;
            thumb.Background = fill;
            var r = ws.TrimRange(item);
            badge.Text = Format.Duration(r.End - r.Start);
            AutomationProperties.SetName(button, $"{item.Name}，{badge.Text}");
            AutomationProperties.SetItemStatus(button, on ? "已选" : "");
            if (on && item.Id != centered)
            {
                centered = item.Id;
                var b = button;
                // 等排好版再滚到中间；不可见时不滚（WinUI 对没排版的元素滚动会直接崩）。
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Center(b));
            }
        }
    }

    private Guid? centered;

    private void Center(Button b)
    {
        if (Visibility != Visibility.Visible || !b.IsLoaded || scroll.ViewportWidth <= 0) return;
        var x = b.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        scroll.ChangeView(Math.Max(0, x - (scroll.ViewportWidth - b.ActualWidth) / 2), null, null, !Motion.Enabled);
    }

    private void AddCell(FileItem item, int index)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var thumb = new Border { Width = 72, Height = 48, CornerRadius = new CornerRadius(9), Child = image };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(image, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var badge = new TextBlock { FontSize = 9, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        var badgeBack = new Border
        {
            Child = badge,
            Padding = new Thickness(4, 0, 4, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(3),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(badgeBack, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var stack = new Grid();
        stack.Children.Add(thumb);
        stack.Children.Add(badgeBack);
        // 外圈品牌红 2 px，内圈卡片色 2 px，再留 4 px 到缩略图。
        var inner = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Child = stack };
        var outer = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Child = inner };
        var button = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = outer };
        AutomationProperties.SetName(button, item.Name);
        ToolTipService.SetToolTip(button, item.Name);
        Motion.SetPress(button, true);
        button.Click += (_, _) =>
        {
            var i = ws.VideoItems.ToList().FindIndex(x => x.Id == item.Id);
            if (i >= 0) ws.VideoIndex = i;
        };
        row.Children.Add(button);
        cells.Add((item, button, outer, inner, thumb, badge));
        _ = LoadThumb(item, image);
    }

    private static async Task LoadThumb(FileItem item, Image image)
    {
        if (Thumbs.TryGetValue(item.FilePath, out var cached))
        {
            image.Source = cached;
            return;
        }
        var t = Math.Min(1.0, item.Duration * 0.1);
        var bytes = await Task.Run(() => VideoFrames.GrabAsync(item.FilePath, t, 160));
        if (bytes == null) return;
        if (await VideoFrames.ToImageAsync(bytes) is { } img)
        {
            Thumbs[item.FilePath] = img;
            image.Source = img;
        }
    }
}
