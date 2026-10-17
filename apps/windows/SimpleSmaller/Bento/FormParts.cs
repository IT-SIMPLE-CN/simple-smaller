using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SimpleSmaller.Bento;

/// 代码搭的元素按深浅色上色：加载时和切换深浅色时各调一次。
public static class FormTheme
{
    public static T On<T>(T element, Action paint) where T : FrameworkElement
    {
        element.Loaded += (_, _) => paint();
        element.ActualThemeChanged += (_, _) => paint();
        return element;
    }

    public static TextBlock Text(string text, double size, string colorKey = "BentoPrimary", bool semibold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis };
        if (semibold) t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        return On(t, () => t.Foreground = Palette.Get(t, colorKey));
    }

    /// 品牌红半透明（选中行的底）。
    public static Brush BrandTint(double opacity) =>
        new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(opacity * 255), 0xEF, 0x39, 0x22));
}

/// 设置里的白卡（设计稿 V2.1 · 10）：圆形图标 + 标题，下面是内容，最底下一句说明。
public static class FormCard
{
    public static Border Create(string glyph, string title, string? note, params UIElement[] content)
    {
        var stack = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 4) };
        var icon = new FontIcon { Glyph = glyph, FontSize = 14, FontFamily = (FontFamily)Application.Current.Resources["BentoIcons"] };
        var circle = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = icon };
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var t = FormTheme.Text(title, 14, semibold: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(t, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        head.Children.Add(circle);
        head.Children.Add(t);
        stack.Children.Add(head);
        foreach (var c in content) stack.Children.Add(c);
        if (note is not null)
        {
            var n = FormTheme.Text(note, 11, "BentoSecondary");
            n.TextWrapping = TextWrapping.Wrap;
            n.TextTrimming = TextTrimming.None;
            n.LineHeight = 16;
            n.Margin = new Thickness(4, 2, 4, 0);
            stack.Children.Add(n);
        }
        var card = new Border { CornerRadius = new CornerRadius(22), Padding = new Thickness(16, 14, 16, 16), Child = stack };
        FormTheme.On(card, () =>
        {
            card.Background = Palette.Get(card, "BentoCard");
            circle.Background = Palette.Get(card, "BentoFill");
            icon.Foreground = Palette.Get(card, "BentoPrimary");
        });
        return card;
    }
}

/// 灰底圆角行：左边文字，右边控件。
public static class FormRow
{
    public static Border Create(string label, UIElement? trailing, double height = 40)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = FormTheme.Text(label, 13);
        t.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(t);
        if (trailing is FrameworkElement fe)
        {
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 1);
            grid.Children.Add(fe);
        }
        var row = new Border { Height = height, CornerRadius = new CornerRadius(15), Padding = new Thickness(16, 0, 12, 0), Child = grid };
        FormTheme.On(row, () => row.Background = Palette.Get(row, "BentoFill"));
        return row;
    }

    /// 开关行：开着是品牌红。
    public static Border Toggle(string label, bool on, Action<bool> changed, double height = 40)
    {
        var sw = BrandToggle.Create(label, on, changed);
        var row = Create(label, sw, height);
        // 旁白只读开关本身（名字就是这行的文字），文字不再单独读一遍。
        if (row.Child is Grid g && g.Children[0] is TextBlock t)
            AutomationProperties.SetAccessibilityView(t, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return row;
    }

    /// 右侧的灰字说明。
    public static TextBlock Value(string text) => FormTheme.Text(text, 12, "BentoSecondary");

    /// 白底等宽小胶囊（快捷键）。
    public static Border Key(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(13), Child = t };
        FormTheme.On(b, () => { b.Background = Palette.Get(b, "BentoCard"); t.Foreground = Palette.Get(b, "BentoPrimary"); });
        return b;
    }
}

/// 系统开关，开着是品牌红（mac 的 .tint(.brand)）；不显示「开 / 关」字样。
public static class BrandToggle
{
    public static ToggleSwitch Create(string label, bool on, Action<bool> changed)
    {
        var sw = new ToggleSwitch { IsOn = on, OnContent = null, OffContent = null, MinWidth = 0, Margin = new Thickness(0, 0, -6, 0) };
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed" })
            sw.Resources[key] = key.EndsWith("Pressed") ? Application.Current.Resources["BrandPressed"] : Application.Current.Resources["Brand"];
        foreach (var key in new[] { "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed" })
            sw.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(sw, label);
        sw.Toggled += (_, _) => changed(sw.IsOn);
        return sw;
    }
}

/// 白底胶囊（配方编辑里的多选、单选）：选中墨黑底白字，未选白底细边。
public sealed class Chip : Button
{
    private readonly Border back = new() { Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 0, 12, 0), BorderThickness = new Thickness(1) };
    private readonly TextBlock text = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon check = new() { Glyph = "", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
    private readonly bool showCheck;
    private bool on;

    public Chip(string title, bool on, double height = 32, bool showCheck = true, double fontSize = 12)
    {
        this.showCheck = showCheck;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        back.Height = height;
        back.CornerRadius = new CornerRadius(height / 2);
        back.Padding = new Thickness(height >= 34 ? 16 : 12, 0, height >= 34 ? 16 : 12, 0);
        text.FontSize = fontSize;
        text.Text = title;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(check);
        row.Children.Add(text);
        back.Child = row;
        Content = back;
        AutomationProperties.SetName(this, title);
        IsOn = on;
        FormTheme.On(this, Paint);
    }

    public bool IsOn
    {
        get => on;
        set
        {
            on = value;
            AutomationProperties.SetItemStatus(this, value ? "已选" : "");
            Paint();
        }
    }

    private void Paint()
    {
        check.Visibility = on && showCheck ? Visibility.Visible : Visibility.Collapsed;
        back.Background = Palette.Get(this, on ? "BentoInk" : "BentoCard");
        back.BorderBrush = on ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Palette.Get(this, "BentoLine");
        text.Foreground = check.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoPrimary");
        text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }
}

/// 品牌红胶囊主按钮（「存储」「新建配方」「在资源管理器中显示」）。不可用时变淡。
public sealed class BrandButton : Button
{
    private readonly Border back = new();
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    public BrandButton(string title, double height = 44, double fontSize = 13, double padding = 26)
    {
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        text.Text = title;
        text.FontSize = fontSize;
        text.Foreground = (Brush)Application.Current.Resources["OnBrand"];
        back.Height = height;
        back.CornerRadius = new CornerRadius(height / 2);
        back.Padding = new Thickness(padding, 0, padding, 0);
        back.Background = Palette.Brand;
        back.Child = text;
        Content = back;
        AutomationProperties.SetName(this, title);
        IsEnabledChanged += (_, _) => back.Opacity = IsEnabled ? 1 : 0.4;
    }
}
