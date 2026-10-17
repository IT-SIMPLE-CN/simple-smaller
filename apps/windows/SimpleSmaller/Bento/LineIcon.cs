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

    // ---------- 卡片头与按钮（V2.1 · 视频 / 序列帧 / 空状态 / 处理中） ----------
    /// 两道滑杆（「画面与画质」「格式与质量」）。
    public static readonly string Sliders = "M4 8h9M17 8h3M4 16h3M11 16h9" + LineIcon.Circle(15, 8, 2) + LineIcon.Circle(9, 16, 2);
    /// 胶片（「格式」「抽帧」）。
    public static readonly string Film = LineIcon.Rect(3, 4, 18, 16, 2) + "M7 4v16M17 4v16M3 9h4M3 15h4M17 9h4M17 15h4";
    /// 喇叭（「声音与附带」）。
    public const string Speaker = "M4 9v6h4l5 4V5L8 9zM16 9a4 4 0 0 1 0 6";
    /// 文件夹（「输出」）。
    public const string Folder = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    /// 图层（「这一批文件」）。
    public const string Layers = "M12 3l9 5-9 5-9-5zM3 13l9 5 9-5";
    /// 加号。
    public const string Plus = "M12 5v14M5 12h14";
    /// 剪贴板（粘贴）。
    public static readonly string Clipboard = LineIcon.Rect(6, 5, 12, 16, 2) + "M9 3h6v4H9z";
    /// 向右箭头（开始块）。
    public const string ArrowRight = "M5 12h14M13 6l6 6-6 6";
    /// 播放三角（填充）。
    public const string PlayFill = "M7 4l13 8-13 8z";
    /// 暂停两竖（填充）。
    public static readonly string PauseFill = LineIcon.Rect(6, 4, 4, 16, 1) + LineIcon.Rect(14, 4, 4, 16, 1);
    /// 停止方块（填充，开始块处理中）。
    public static readonly string StopFill = LineIcon.Rect(6, 6, 12, 12, 2);
    /// 对勾（开始块已完成）。
    public const string Check = "M5 12.5l4.5 4.5L19 7.5";
}
