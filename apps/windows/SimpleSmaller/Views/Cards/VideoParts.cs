using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Cards;

/// 视频卡片用的小件（对应 mac 的 BentoRow、BentoToggleRow、LabeledPills、SaveRecipeAccessory、bentoLocked）。
/// 图片 agent 也在写同类通用组件，合并时再统一；这里全部挂在一个静态类下，不占通用名字。
internal static class VideoParts
{
    /// 代码搭的元素在出现与深浅色切换时上色。
    public static T Paint<T>(T e, Action<T> paint) where T : FrameworkElement
    {
        e.Loaded += (_, _) => paint(e);
        e.ActualThemeChanged += (_, _) => paint(e);
        return e;
    }

    public static TextBlock Caption(string text, double size = 11) =>
        Paint(new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 },
              t => t.Foreground = Palette.Get(t, "BentoSecondary"));

    /// 灰底圆角行：左名称，右值或控件。
    public static Border Row(string label, UIElement trailing, double height = 44)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        grid.Children.Add(name);
        if (trailing is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn((FrameworkElement)trailing, 1);
        grid.Children.Add(trailing);
        var b = new Border
        {
            Height = height,
            CornerRadius = (CornerRadius)Application.Current.Resources["BentoRowRadius"],
            Padding = new Thickness(16, 0, 12, 0),
            Child = grid,
        };
        return Paint(b, x => { x.Background = Palette.Get(x, "BentoFill"); name.Foreground = Palette.Get(x, "BentoPrimary"); });
    }

    /// 开关行：系统开关，开着是品牌红。
    public static (Border Row, ToggleSwitch Switch) ToggleRow(string label, double height = 44)
    {
        // 开关颜色（品牌红底、白钮）在 Themes/Bento.xaml 全局设好。
        var ts = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, Margin = new Thickness(0, 0, -10, 0) };
        AutomationProperties.SetName(ts, label);
        return (Row(label, ts, height), ts);
    }

    /// 一组小标题 + 胶囊。
    public static (StackPanel Panel, PillChoice<T> Pills, TextBlock Trailing) LabeledPills<T>(string title, IEnumerable<(T, string)> options, T selected) where T : notnull
    {
        var head = new Grid();
        var t = Caption(title);
        var trailing = Caption("");
        trailing.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(t);
        head.Children.Add(trailing);
        var pills = new PillChoice<T>(options, selected, 32, title);
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(head);
        p.Children.Add(pills);
        return (p, pills, trailing);
    }

    /// 卡片里的灰底面板。
    public static Border Panel(UIElement child, Thickness padding) =>
        Paint(new Border { CornerRadius = (CornerRadius)Application.Current.Resources["BentoPanelRadius"], Padding = padding, Child = child },
              b => b.Background = Palette.Get(b, "BentoFill"));

    /// 处理中（或不适用时）锁住：不能点，变淡。
    public static void Lock(Control c, bool locked)
    {
        c.IsEnabled = !locked;
        c.Opacity = locked ? 0.45 : 1;
    }

    /// 把一组元素包进可整体禁用的容器。
    public static ContentControl Lockable(UIElement child) => new()
    {
        Content = child,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        IsTabStop = false,
    };

    /// 「已修改」+「存为新配方」。
    public static StackPanel RecipeAccessory(Workspace ws)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var tag = new TextBlock { Text = "已修改", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var tagBack = new Border { Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(13), Child = tag };
        Paint(tagBack, b => { b.Background = Palette.Get(b, "BentoAlertFill"); tag.Foreground = Palette.Get(b, "BentoAlertText"); });
        var save = new CapsuleButton("存为新配方", 26);
        save.Click += (_, _) =>
        {
            var r = ws.Draft with { Id = "", Name = ws.Base.BuiltIn ? $"{ws.Base.Name} 改" : ws.Base.Name, BuiltIn = false };
            App.OpenRecipeEditor(r, saved => ws.Select(saved));
        };
        row.Children.Add(tagBack);
        row.Children.Add(save);
        return row;
    }

    public static void Show(UIElement e, bool visible) => e.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// 卡片头。
    public static Header Header(string glyph, string title, string? subtitle = null) => new() { Glyph = glyph, Title = title, Subtitle = subtitle };

    public static SolidColorBrush Color(byte a, byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(a, r, g, b));
}
