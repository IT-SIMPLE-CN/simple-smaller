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

    /// 开关行：系统开关，开着是品牌红；关着照设计稿是浅灰底、白圆钮、无描边。
    public static (Border Row, ToggleSwitch Switch) ToggleRow(string label, double height = 44)
    {
        // 开着的颜色（品牌红底、白钮）在 Themes/Bento.xaml 全局设好；关着的颜色只改视频卡里的开关。
        var ts = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, Margin = new Thickness(0, 0, -10, 0) };
        foreach (var (theme, fill) in new[] { ("Light", Windows.UI.Color.FromArgb(255, 0xD9, 0xDC, 0xE0)), ("Dark", Windows.UI.Color.FromArgb(255, 0x45, 0x47, 0x4C)) })
        {
            var d = new ResourceDictionary();
            foreach (var state in new[] { "", "PointerOver", "Pressed" })
            {
                d["ToggleSwitchFillOff" + state] = new SolidColorBrush(fill);
                d["ToggleSwitchStrokeOff" + state] = new SolidColorBrush(fill);
                d["ToggleSwitchKnobFillOff" + state] = new SolidColorBrush(Microsoft.UI.Colors.White);
            }
            ts.Resources.ThemeDictionaries[theme] = d;
        }
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

    /// 卡片头右边：「已修改」（改过才出现）+「另存为配方」（设计稿 V2.1 · 06 / 07）。
    public static (StackPanel Panel, Border Modified) RecipeAccessory(Workspace ws)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var tag = new TextBlock { Text = "已修改", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var tagBack = new Border { Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(13), Child = tag, VerticalAlignment = VerticalAlignment.Center };
        Paint(tagBack, b => { b.Background = Palette.Get(b, "BentoAlertFill"); tag.Foreground = Palette.Get(b, "BentoAlertText"); });
        var save = new CapsuleButton("另存为配方", 34);
        save.Click += (_, _) =>
        {
            var r = ws.Draft with { Id = "", Name = ws.Base.BuiltIn ? $"{ws.Base.Name} 改" : ws.Base.Name, BuiltIn = false };
            App.OpenRecipeEditor(r, saved => ws.Select(saved));
        };
        row.Children.Add(tagBack);
        row.Children.Add(save);
        return (row, tagBack);
    }

    public static void Show(UIElement e, bool visible) => e.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// 卡片头：图标是设计稿的线性图标（Icons 里的路径）。
    public static Header Header(string icon, string title, string? subtitle = null) =>
        new Header { Title = title, Subtitle = subtitle }.WithIcon(icon);

    /// 左边 64 宽的小标题、中间控件、右边说明（设计稿「画面比例 / 分辨率 / 画质」一行）。
    public static Grid LabeledRow(string label, UIElement content, UIElement? trailing = null)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = Caption(label, 12);
        t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(t);
        if (content is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Left;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn((FrameworkElement)content, 1);
        g.Children.Add(content);
        if (trailing is FrameworkElement tr)
        {
            tr.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tr, 2);
            g.Children.Add(tr);
        }
        return g;
    }

    /// 可点的灰底行：左名称，右「值 ›」，点开一个菜单（设计稿「每一帧」卡的格式、宽）。
    public static (Button Button, TextBlock Value) MenuRow(string label, MenuFlyout menu)
    {
        var value = new TextBlock { FontSize = 13, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        Paint(value, t => t.Foreground = Palette.Get(t, "BentoSecondary"));
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch, Flyout = menu };
        Motion.SetPress(b, true);
        b.Content = Row(label, value);
        AutomationProperties.SetName(b, label);
        return (b, value);
    }

    public static SolidColorBrush Color(byte a, byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(a, r, g, b));
}
