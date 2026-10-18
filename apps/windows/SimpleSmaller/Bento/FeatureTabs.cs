using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SimpleSmaller.Bento;

/// 各子功能的图标（路径照搬设计稿 V2.2 首页 H1、功能页顶部标签、设置 › 常用）。
public static class FeatureIcons
{
    public const string Compress = "M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7";
    public static readonly string Crop = "M6 2v14a2 2 0 0 0 2 2h14M18 22V8a2 2 0 0 0-2-2H2";
    public static readonly string Edit = LineIcon.Circle(6, 6, 3) + LineIcon.Circle(6, 18, 3) + "M20 4L8.1 15.9M14.5 14.5L20 20M8.1 8.1L12 12";
    public static readonly string Frames = LineIcon.Rect(3, 5, 18, 14, 2) + "M9 5v14M15 5v14";
    public const string Merge = "M6 4v5a6 6 0 0 0 6 6a6 6 0 0 0 6-6V4M12 15v5";
    public const string Split = "M12 4v5M6 20v-5a6 6 0 0 1 6-6a6 6 0 0 1 6 6v5";
    public static readonly string ToImages = LineIcon.Rect(3, 5, 18, 14, 2) + "M3 16l5-5 4 4 3-3 6 6";
    public const string FromImages = "M7 3h7l5 5v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM14 3v5h5";

    // 设置 › 常用 左栏的类型图标。
    public static readonly string TypeImage = LineIcon.Rect(3, 5, 18, 14, 3) + "M3 16l5-5 4 4 3-3 6 6" + LineIcon.Circle(16, 9, 1.5);
    public static readonly string TypeVideo = LineIcon.Rect(3, 5, 18, 14, 3) + "M10 9.5v5l4.5-2.5z";
    public const string TypePdf = "M7 3h7l5 5v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM14 3v5h5M9 13h6M9 17h4";

    /// 按子功能名取图标（压缩 / 裁切 / 剪辑 / 序列帧 / 合并 / 拆分 / 转图片 / 图片转 PDF）。
    public static string For(string name) => name switch
    {
        "裁切" => Crop,
        "剪辑" => Edit,
        "序列帧" => Frames,
        "合并" => Merge,
        "拆分" => Split,
        "转图片" => ToImages,
        "图片转 PDF" => FromImages,
        _ => Compress,
    };
}

/// 标题栏按宽度切换顶部标签的样子用。
public interface IFeatureTabs
{
    bool Compact { get; set; }
    double FullWidth { get; }
    double CompactWidth { get; }
}

/// 功能页顶部的大标签（设计稿 V2.2 G1 / G2）：白底胶囊（内边距 4），每项高 40、左边 28 的圆形图标底；
/// 选中项墨黑底白字、图标底品牌红；没选中图标底灰。墨黑底块在项之间弹簧滑动。
/// 放不下时（Compact）只显示图标，文字进悬停提示与旁白名。
public sealed class FeatureTabs<T> : Grid, IFeatureTabs where T : notnull
{
    private sealed record Item(T Value, Button Button, Border Pad, Border IconBack, LineIcon Icon, TextBlock Text);

    private readonly Border track = new();
    private readonly Border thumb = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(20) };
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4) };
    private readonly List<Item> items = [];
    private T selected;
    private bool compact;
    private bool placed;
    private double fullWidth;

    public event Action<T>? Changed;

    public FeatureTabs(IEnumerable<(T Value, string Title, string Icon)> options, T selected, string label)
    {
        this.selected = selected;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Capsule.Apply(track);
        Children.Add(track);
        Children.Add(thumb);
        Children.Add(row);
        AutomationProperties.SetName(this, label);
        AutomationProperties.SetLocalizedControlType(this, "选项卡列表");
        foreach (var (value, title, icon) in options)
        {
            var line = new LineIcon(icon, 14, 2);
            var iconBack = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = line };
            var text = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(iconBack);
            stack.Children.Add(text);
            var pad = new Border { Height = 40, Padding = new Thickness(6, 0, 16, 0), Child = stack };
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = pad };
            Motion.SetPress(b, true);
            AutomationProperties.SetName(b, title);
            AutomationProperties.SetLocalizedControlType(b, "选项卡");
            b.Click += (_, _) => Select(value, notify: true);
            items.Add(new Item(value, b, pad, iconBack, line, text));
            row.Children.Add(b);
        }
        row.SizeChanged += (_, _) => MoveThumb(animated: placed);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); MoveThumb(animated: false); };
    }

    public T Selected { get => selected; set => Select(value, notify: false); }

    /// 只显示图标（窗口窄、标题栏放不下文字时）。
    public bool Compact
    {
        get => compact;
        set
        {
            if (compact == value) return;
            compact = value;
            foreach (var i in items)
            {
                i.Text.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
                i.Pad.Padding = value ? new Thickness(6, 0, 6, 0) : new Thickness(6, 0, 16, 0);
                ToolTipService.SetToolTip(i.Button, value ? i.Text.Text : null);
            }
        }
    }

    /// 带文字时的整体宽度（判断标题栏放不放得下用）。
    public double FullWidth
    {
        get
        {
            if (fullWidth > 0) return fullWidth;
            double w = 8 + 4 * (items.Count - 1);
            foreach (var i in items)
            {
                i.Text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // 选中项是粗体，按粗体量宽一点的那个。
                w += 6 + 28 + 8 + i.Text.DesiredSize.Width * 1.06 + 16;
            }
            if (items.All(i => i.Text.DesiredSize.Width > 0)) fullWidth = w;
            return w;
        }
    }

    /// 只显示图标时的宽度：每项 40（图标底 28 + 两边 6）。
    public double CompactWidth => 8 + 4 * (items.Count - 1) + 40 * items.Count;

    private void Select(T value, bool notify)
    {
        if (EqualityComparer<T>.Default.Equals(value, selected) && placed) return;
        selected = value;
        Paint();
        MoveThumb(animated: true);
        if (notify) Changed?.Invoke(value);
    }

    private void MoveThumb(bool animated)
    {
        var item = items.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, selected));
        if (item is null || item.Button.ActualWidth == 0) return;
        var origin = item.Button.TransformToVisual(this).TransformPoint(new Point(0, 0));
        thumb.Width = item.Button.ActualWidth;
        thumb.Height = item.Button.ActualHeight;
        var to = new Vector3((float)origin.X, (float)origin.Y, 0);
        if (animated && Motion.Enabled) Motion.SpringOffset(thumb, to);
        else Motion.SetTranslation(thumb, to);
        placed = true;
    }

    private void Paint()
    {
        track.Background = Palette.Get(this, "BentoCard");
        thumb.Background = Palette.Get(this, "BentoInk");
        foreach (var i in items)
        {
            var on = EqualityComparer<T>.Default.Equals(i.Value, selected);
            AutomationProperties.SetItemStatus(i.Button, on ? "已选" : "");
            i.Text.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoPrimary");
            i.Text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            i.IconBack.Background = on ? Palette.Brand : Palette.Get(this, "BentoFill");
            i.Icon.ColorKey = on ? "OnBrand" : "BentoPrimary";
        }
    }
}
