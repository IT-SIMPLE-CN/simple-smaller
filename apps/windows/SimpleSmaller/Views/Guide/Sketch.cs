using Windows.UI.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Views.Guide;

/// 教学插图的小画板：照搬设计稿 SVG 的坐标（rect / circle / path / text），用代码画
/// （WinUI 的 SvgImageSource 不画 text）。颜色写色板名，浅色 / 深色各取一套（数值同 Themes/Bento.xaml）；
/// 也可以直接写 #RRGGBB / #AARRGGBB。主题变了由调用方整张重画。
internal sealed class Sketch
{
    private static readonly Dictionary<string, (uint Light, uint Dark)> Tokens = new()
    {
        ["card"] = (0xFFFFFF, 0x242528),
        ["fill"] = (0xF2F3F5, 0x303135),
        ["ink"] = (0x111214, 0xF2F3F5),
        ["onInk"] = (0xFFFFFF, 0x111214),
        ["primary"] = (0x111214, 0xF2F3F5),
        ["secondary"] = (0x63676E, 0x9DA1A8),
        ["soft"] = (0x3E4248, 0xC4C7CC),
        ["line"] = (0xD9DCE0, 0x45474C),
        ["alertFill"] = (0xFDE8E4, 0x3D201C),
        ["alertText"] = (0xA8240F, 0xFF9B8A),
        // 键帽底边、胶片格（设计稿 #C9CCD1）。
        ["key"] = (0xC9CCD1, 0x45474C),
        // 键帽内描边（#E2E4E8）。
        ["keyEdge"] = (0xE2E4E8, 0x45474C),
        // 圆环教学插图的底（#E4E6E9）。
        ["stage"] = (0xE4E6E9, 0x303135),
        // 墨黑底上的次要字（#B9BCC2）。
        ["inkSub"] = (0xB9BCC2, 0x63676E),
        ["brand"] = (0xEF3922, 0xEF3922),
        ["white"] = (0xFFFFFF, 0xFFFFFF),
    };

    public readonly Canvas Canvas;
    private readonly bool dark;

    public Sketch(double width, double height, bool dark)
    {
        this.dark = dark;
        Canvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false };
    }

    public static Color ColorOf(string token, bool dark)
    {
        if (token.StartsWith('#'))
        {
            var hex = token[1..];
            var v = Convert.ToUInt32(hex, 16);
            return hex.Length == 8
                ? Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        var (l, d) = Tokens[token];
        var c = dark ? d : l;
        return Color.FromArgb(255, (byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    public static Brush BrushOf(string token, bool dark) => new SolidColorBrush(ColorOf(token, dark));

    public Brush B(string token) => BrushOf(token, dark);

    public bool Dark => dark;

    /// 左上到右下的渐变（设计稿 linearGradient x1=0 y1=0 x2=1 y2=1）。
    public static Brush Gradient(string from, string to) => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = ColorOf(from, false), Offset = 0 },
            new GradientStop { Color = ColorOf(to, false), Offset = 1 },
        },
    };

    public Rectangle Rect(double x, double y, double w, double h, double rx, string? fill, string? stroke = null, double strokeWidth = 0, double dash = 0) =>
        Rect(x, y, w, h, rx, fill is null ? null : B(fill), stroke, strokeWidth, dash);

    public Rectangle Rect(double x, double y, double w, double h, double rx, Brush? fill, string? stroke = null, double strokeWidth = 0, double dash = 0)
    {
        // SVG 的描边骑在边线上；Rectangle 的描边画在里面：外扩半个线宽对齐。
        var half = stroke is null ? 0 : strokeWidth / 2;
        var r = new Rectangle
        {
            Width = w + 2 * half,
            Height = h + 2 * half,
            RadiusX = rx + half,
            RadiusY = rx + half,
            Fill = fill,
        };
        if (stroke is not null)
        {
            r.Stroke = B(stroke);
            r.StrokeThickness = strokeWidth;
            if (dash > 0) r.StrokeDashArray = new DoubleCollection { dash / strokeWidth, dash / strokeWidth };
        }
        Place(r, x - half, y - half);
        return r;
    }

    public Ellipse Circle(double cx, double cy, double r, string? fill, string? stroke = null, double strokeWidth = 0, double opacity = 1)
    {
        var half = stroke is null ? 0 : strokeWidth / 2;
        var e = new Ellipse { Width = 2 * (r + half), Height = 2 * (r + half), Fill = fill is null ? null : B(fill), Opacity = opacity };
        if (stroke is not null)
        {
            e.Stroke = B(stroke);
            e.StrokeThickness = strokeWidth;
        }
        Place(e, cx - r - half, cy - r - half);
        return e;
    }

    public Path Path(string data, string? fill, string? stroke = null, double strokeWidth = 0, bool round = true)
    {
        var p = new Path
        {
            Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
            Fill = fill is null ? null : B(fill),
        };
        if (stroke is not null)
        {
            p.Stroke = B(stroke);
            p.StrokeThickness = strokeWidth;
            if (round)
            {
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
            }
        }
        Canvas.Children.Add(p);
        return p;
    }

    /// 文字：anchor 'm' 居中、's' 从 x 起、'e' 到 x 止；纵向以 y 为中线（dominant-baseline: central）。
    public TextBlock Text(double x, double y, string text, double size, string fill, FontWeight? weight = null, char anchor = 'm')
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = B(fill),
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = anchor switch { 's' => HorizontalAlignment.Left, 'e' => HorizontalAlignment.Right, _ => HorizontalAlignment.Center },
        };
        const double box = 400;
        var host = new Grid { Width = box, Height = size * 2 };
        host.Children.Add(t);
        var left = anchor switch { 's' => x, 'e' => x - box, _ => x - box / 2 };
        Place(host, left, y - size);
        return t;
    }

    public void Add(UIElement e, double x, double y) => Place(e, x, y);

    private void Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        Canvas.Children.Add(e);
    }

    /// 按宽度等比缩放的插图（装饰，旁白不读）。
    public FrameworkElement View(double width)
    {
        var box = new Viewbox { Width = width, Height = width * Canvas.Height / Canvas.Width, Stretch = Stretch.Uniform, Child = Canvas, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(box, AccessibilityView.Raw);
        return new Silent(box);
    }

    /// 旁白不往里读的容器（插图里的文字是画出来的）。
    internal sealed class Silent : Grid
    {
        private readonly bool named;

        /// name 为空：装饰图，旁白跳过；有 name：旁白当一张图读这句话。
        public Silent(UIElement child, string? name = null)
        {
            Children.Add(child);
            IsHitTestVisible = false;
            named = name is not null;
            if (name is not null) AutomationProperties.SetName(this, name);
            else AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this, named);

        private sealed class Peer(FrameworkElement owner, bool named) : FrameworkElementAutomationPeer(owner)
        {
            protected override IList<AutomationPeer> GetChildrenCore() => new List<AutomationPeer>();
            protected override AutomationControlType GetAutomationControlTypeCore() => named ? AutomationControlType.Image : AutomationControlType.Custom;
        }
    }
}
