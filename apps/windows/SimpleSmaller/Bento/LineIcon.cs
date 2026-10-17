using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Bento;

/// 设计稿里的线性图标（24 × 24 网格、圆头圆角描边），照搬设计稿 SVG 的路径，比系统图标字体更贴近设计。
/// 颜色按色板键跟随深浅色；装饰性图标，旁白不读。
public sealed class LineIcon : UserControl
{
    private readonly Path path = new()
    {
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };
    private string colorKey;

    public LineIcon(string data, double size, double stroke = 1.8, string colorKey = "BentoPrimary")
    {
        this.colorKey = colorKey;
        Width = Height = size;
        IsTabStop = false;
        path.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
        path.StrokeThickness = stroke;
        // 固定 24 × 24 的画布，图标在里面的位置与设计稿一致。
        var canvas = new Grid { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    /// 换颜色：色板键（BentoPrimary…）或 OnBrand（白）/ Brand。
    public string ColorKey
    {
        get => colorKey;
        set { colorKey = value; Paint(); }
    }

    private void Paint()
    {
        path.Stroke = colorKey switch
        {
            "OnBrand" => (Brush)Application.Current.Resources["OnBrand"],
            "Brand" => Palette.Brand,
            _ => Palette.Get(this, colorKey),
        };
    }

    // ---------- 把 SVG 的基本形状换成路径数据 ----------

    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string Rect(double x, double y, double w, double h, double rx = 0)
    {
        if (rx <= 0) return $"M{N(x)} {N(y)}h{N(w)}v{N(h)}h{N(-w)}z";
        return $"M{N(x + rx)} {N(y)}h{N(w - 2 * rx)}a{N(rx)} {N(rx)} 0 0 1 {N(rx)} {N(rx)}v{N(h - 2 * rx)}a{N(rx)} {N(rx)} 0 0 1 {N(-rx)} {N(rx)}h{N(-(w - 2 * rx))}a{N(rx)} {N(rx)} 0 0 1 {N(-rx)} {N(-rx)}v{N(-(h - 2 * rx))}a{N(rx)} {N(rx)} 0 0 1 {N(rx)} {N(-rx)}z";
    }

    public static string Circle(double cx, double cy, double r) =>
        $"M{N(cx - r)} {N(cy)}a{N(r)} {N(r)} 0 1 0 {N(2 * r)} 0a{N(r)} {N(r)} 0 1 0 {N(-2 * r)} 0z";
}

/// 设计稿用到的图标（路径照搬 V2.1 / V2.2 画布的 SVG）。
public static class Icons
{
    /// 斜向箭头 ↗（首页卡片、「关于」）。
    public const string ArrowUpRight = "M7 17L17 7M8 7h9v9";
    /// 先下后右的箭头 ↳（首页大标题）。
    public const string TurnDownRight = "M5 6v7a3 3 0 0 0 3 3h11M15 12l4 4-4 4";
    /// 两道横线（首页右上的菜单）。
    public const string Menu = "M5 9h14M9 15h10";
    public static readonly string Image = LineIcon.Rect(3, 5, 18, 14, 2) + "M3 16l5-5 4 4 3-3 6 6";
    public static readonly string Video = LineIcon.Rect(3, 6, 13, 12, 2) + "M16 10l5-3v10l-5-3";
    public const string Pdf = "M6 3h8l4 4v14H6zM14 3v4h4";
    public static readonly string Ring = LineIcon.Circle(12, 12, 8) + LineIcon.Circle(12, 12, 3) + "M12 4v5M20 12h-5";
}
