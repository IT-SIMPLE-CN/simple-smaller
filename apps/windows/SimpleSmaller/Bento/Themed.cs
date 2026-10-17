using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace SimpleSmaller.Bento;

/// 跟着深浅色重新上色的小工具：代码搭的文字、底色记住色板键，Loaded / ActualThemeChanged 时按键重取。
/// 键是 Palette 的主题键（BentoPrimary…），或 Brand / Saving / Warning / White 这几个两种外观一样的颜色。
public static class Themed
{
    public static Brush Resolve(FrameworkElement e, string key) => key switch
    {
        "Brand" => Palette.Brand,
        "Saving" => Palette.Saving,
        "Warning" => Palette.Warning,
        "White" => new SolidColorBrush(Microsoft.UI.Colors.White),
        "Transparent" => new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        _ => Palette.Get(e, key),
    };

    /// 一段文字：字号、色板键、字重。
    public static TextBlock Text(string text, double size, string key = "BentoPrimary", FontWeight? weight = null)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (weight is { } w)
        {
            t.FontWeight = w;
        }
        Fg(t, key);
        return t;
    }

    private static readonly DependencyProperty FgKeyProperty =
        DependencyProperty.RegisterAttached("FgKey", typeof(string), typeof(Themed), new PropertyMetadata(null));
    private static readonly DependencyProperty BgKeyProperty =
        DependencyProperty.RegisterAttached("BgKey", typeof(string), typeof(Themed), new PropertyMetadata(null));

    /// 文字色（可反复改键，比如次要色 ↔ 提醒色）。
    public static void Fg(TextBlock t, string key)
    {
        var first = t.GetValue(FgKeyProperty) is null;
        t.SetValue(FgKeyProperty, key);
        if (first)
        {
            t.Loaded += (_, _) => PaintFg(t);
            t.ActualThemeChanged += (_, _) => PaintFg(t);
        }
        PaintFg(t);
    }

    public static void Fg(FontIcon t, string key)
    {
        var first = t.GetValue(FgKeyProperty) is null;
        t.SetValue(FgKeyProperty, key);
        if (first)
        {
            t.Loaded += (_, _) => t.Foreground = Resolve(t, (string)t.GetValue(FgKeyProperty));
            t.ActualThemeChanged += (_, _) => t.Foreground = Resolve(t, (string)t.GetValue(FgKeyProperty));
        }
        t.Foreground = Resolve(t, key);
    }

    private static void PaintFg(TextBlock t) => t.Foreground = Resolve(t, (string)t.GetValue(FgKeyProperty));

    /// 底色（Border / Panel）。
    public static void Bg(Border b, string key)
    {
        var first = b.GetValue(BgKeyProperty) is null;
        b.SetValue(BgKeyProperty, key);
        if (first)
        {
            b.Loaded += (_, _) => b.Background = Resolve(b, (string)b.GetValue(BgKeyProperty));
            b.ActualThemeChanged += (_, _) => b.Background = Resolve(b, (string)b.GetValue(BgKeyProperty));
        }
        b.Background = Resolve(b, key);
    }

    /// 任意重新上色的动作。
    public static void OnTheme(FrameworkElement e, Action paint)
    {
        e.Loaded += (_, _) => paint();
        e.ActualThemeChanged += (_, _) => paint();
    }

    /// 灰底胶囊 / 圆角块：Border + 文字。
    public static Border Capsule(TextBlock text, string fillKey, double height, double padX)
    {
        var b = new Border { Height = height, CornerRadius = new CornerRadius(height / 2), Padding = new Thickness(padX, 0, padX, 0), Child = text };
        Bg(b, fillKey);
        return b;
    }

    public static FontWeight Semibold => FontWeights.SemiBold;
    public static FontWeight Bold => FontWeights.Bold;
    public static FontWeight Light => FontWeights.Light;
    public static FontWeight Medium => FontWeights.Medium;
}
