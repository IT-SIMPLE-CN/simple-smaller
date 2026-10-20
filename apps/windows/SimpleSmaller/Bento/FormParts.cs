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

/// 设置里的白卡（设计稿 V2.1 · 10）：圆形图标 + 标题（右边可放按钮），下面是内容，最底下一句说明。
/// 尺寸照设计稿：圆角 20，内边距 14 / 16 / 16，行间距 6；图标圆 30、线性图标 15。没有图标路径时只有标题（「自动」页）。
public static class FormCard
{
    public static Border Create(string? iconPath, string title, string? note, params UIElement[] content) =>
        Create(iconPath, title, null, note, content);

    public static Border Create(string? iconPath, string title, UIElement? trailing, string? note, params UIElement[] content)
    {
        var stack = new StackPanel { Spacing = 6 };
        var head = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 4), MinHeight = 30 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border? circle = null;
        if (iconPath is not null)
        {
            circle = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = new LineIcon(iconPath, 15, 1.8) };
            AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            head.Children.Add(circle);
        }
        var t = FormTheme.Text(title, 14, semibold: true);
        t.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(t, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        Grid.SetColumn(t, iconPath is null ? 0 : 1);
        head.Children.Add(t);
        if (trailing is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(fe, 2);
            head.Children.Add(fe);
        }
        stack.Children.Add(head);
        foreach (var c in content) stack.Children.Add(c);
        if (note is not null)
        {
            var n = FormTheme.Text(note, 11, "BentoSecondary");
            n.TextWrapping = TextWrapping.Wrap;
            n.TextTrimming = TextTrimming.None;
            n.LineHeight = 16.5;
            n.Margin = new Thickness(4, 4, 4, 0);
            stack.Children.Add(n);
        }
        var card = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 16), Child = stack };
        FormTheme.On(card, () =>
        {
            card.Background = Palette.Get(card, "BentoCard");
            if (circle is not null) circle.Background = Palette.Get(card, "BentoFill");
        });
        return card;
    }
}

/// 灰底圆角行（设计稿：40 高、圆角 13、左 14 右 10）：左边文字，右边控件。
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
        var row = new Border { Height = height, CornerRadius = new CornerRadius(13), Padding = new Thickness(14, 0, 10, 0), Child = grid };
        FormTheme.On(row, () => row.Background = Palette.Get(row, "BentoFill"));
        return row;
    }

    /// 开关行：开着是品牌红（便当盒开关 36 × 22）。
    public static Border Toggle(string label, bool on, Action<bool> changed, double height = 40)
    {
        var sw = new BentoSwitch(label, on);
        sw.Changed += changed;
        var row = Create(label, sw, height);
        // 旁白只读开关本身（名字就是这行的文字），文字不再单独读一遍。
        if (row.Child is Grid g && g.Children[0] is TextBlock t)
            AutomationProperties.SetAccessibilityView(t, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return row;
    }

    /// 右侧的灰字说明。
    public static TextBlock Value(string text) => FormTheme.Text(text, 12, "BentoSecondary");

    /// 右侧的等宽灰字（「{name}-small」）。
    public static TextBlock Mono(string text)
    {
        var t = FormTheme.Text(text, 12, "BentoSecondary");
        t.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        return t;
    }

    /// 白底等宽小胶囊（快捷键，设计稿 28 高）。
    public static Border Key(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"), VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = 28, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(14), Child = t };
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
    private readonly LineIcon check = new(SubIcons.Check, 11, 3, "BentoOnInk") { VerticalAlignment = VerticalAlignment.Center };
    private readonly bool showCheck;
    private readonly bool bordered;
    private bool on;

    /// bordered：未选时带细边（设计稿的尺寸、文件名开关）；格式这类大胶囊不带边。
    public Chip(string title, bool on, double height = 32, bool showCheck = true, double fontSize = 12, bool bordered = true)
    {
        this.showCheck = showCheck;
        this.bordered = bordered;
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
        back.BorderBrush = on || !bordered ? new SolidColorBrush(Microsoft.UI.Colors.Transparent) : Palette.Get(this, "BentoLine");
        text.Foreground = Palette.Get(this, on ? "BentoOnInk" : "BentoPrimary");
        text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }
}

/// 品牌红胶囊主按钮（「存储」「新建常用」「在资源管理器中显示」）。不可用时变淡。
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
