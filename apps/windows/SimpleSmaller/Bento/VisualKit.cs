using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Bento;

/// 设计稿里的彩色投影（如开始块 `0 10px 24px rgba(239,57,34,.3)`）：ThemeShadow 只有灰色，这里用合成层的 DropShadow。
/// 把 Host 放在要投影的元素下面（同一格、尺寸相同）。
public sealed class ColorShadow
{
    private readonly SpriteVisual sprite;

    public ColorShadow(Windows.UI.Color color, float blur, float offsetY, float cornerRadius)
    {
        var host = new Border { IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(host, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Host = host;
        var c = ElementCompositionPreview.GetElementVisual(host).Compositor;
        sprite = c.CreateSpriteVisual();
        var shadow = c.CreateDropShadow();
        shadow.Color = color;
        shadow.BlurRadius = blur;
        shadow.Offset = new Vector3(0, offsetY, 0);
        sprite.Shadow = shadow;
        // 投影按精灵的画刷取形；画刷用投影色的不透明版，被上面的元素整块盖住。
        sprite.Brush = c.CreateColorBrush(Windows.UI.Color.FromArgb(255, color.R, color.G, color.B));
        // 圆角：投影源只取精灵的矩形；把精灵四边各收进圆角的 0.3，模糊后角上不显方。
        var inset = cornerRadius * 0.3f;
        ElementCompositionPreview.SetElementChildVisual(host, sprite);
        host.SizeChanged += (_, e) =>
        {
            sprite.Offset = new Vector3(inset, inset, 0);
            sprite.Size = new Vector2((float)Math.Max(0, e.NewSize.Width - 2 * inset), (float)Math.Max(0, e.NewSize.Height - 2 * inset));
        };
    }

    public FrameworkElement Host { get; }

    public bool Visible
    {
        get => sprite.IsVisible;
        set => sprite.IsVisible = value;
    }
}

/// 24 × 24 网格里的一条路径（描边或填充由调用方给画刷），用于需要自定颜色的设计稿图标。
public static class Glyph24
{
    public static Viewbox Create(string data, double size, out Path path, double stroke = 0)
    {
        path = new Path
        {
            Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data),
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var canvas = new Grid { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        var box = new Viewbox { Width = size, Height = size, Stretch = Stretch.Uniform, Child = canvas, IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(box, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return box;
    }
}

public static class HeaderIcons
{
    /// 把卡片头的图标换成设计稿的线性图标（16，描边 1.8）。
    public static Header WithIcon(this Header header, string data)
    {
        if (header.Children.Count > 0 && header.Children[0] is Border back)
        {
            back.Child = new LineIcon(data, 16);
        }
        return header;
    }
}

/// 设计稿 V2.1 · 02 空状态的拖放区：斜纹底、2px 内描边、红色插画。三种类型共用。
public static class DropArt
{
    private static Geometry G(string d) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), d);

    private static Path Stroke(string d, Windows.UI.Color color, double opacity = 1) => new()
    {
        Data = G(d),
        Stroke = new SolidColorBrush(color),
        StrokeThickness = 3,
        Opacity = opacity,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Path Fill(string d, Windows.UI.Color color) => new() { Data = G(d), Fill = new SolidColorBrush(color) };

    /// 224 × 168 的插画（照搬画布 SVG），外加一圈品牌红柔光 `0 16px 40px rgba(239,57,34,.18)`。
    public static FrameworkElement Illustration()
    {
        var white = Microsoft.UI.Colors.White;
        var ink = Windows.UI.Color.FromArgb(255, 0x11, 0x12, 0x14);
        var brand = Windows.UI.Color.FromArgb(255, 0xEF, 0x39, 0x22);
        var canvas = new Grid { Width = 224, Height = 168 };
        canvas.Children.Add(Stroke(LineIcon.Rect(30, 26, 128, 96, 10), white));
        canvas.Children.Add(Stroke("M38 112L72 74L94 96L112 80L150 116", white, 0.55));
        canvas.Children.Add(Stroke(LineIcon.Circle(128, 50, 9), white, 0.55));
        canvas.Children.Add(Fill(LineIcon.Rect(112, 80, 86, 64, 8), Windows.UI.Color.FromArgb(255, 0xFF, 0xF4, 0xEE)));
        canvas.Children.Add(Fill("M118 138L140 112L154 126L166 116L192 138Z", ink));
        canvas.Children.Add(Fill(LineIcon.Circle(180, 96, 7), brand));
        canvas.Children.Add(Stroke("M14 10L26 22M26 13V22H17", ink));
        canvas.Children.Add(Stroke("M174 10L162 22M162 13V22H171", ink));
        canvas.Children.Add(Stroke("M14 138L26 126M17 126H26V135", ink));
        var art = new Border { Width = 224, Height = 168, CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(brand), Child = canvas };
        var glow = new ColorShadow(Windows.UI.Color.FromArgb(46, 0xEF, 0x39, 0x22), 40, 16, 18);
        glow.Host.Width = 224;
        glow.Host.Height = 168;
        var root = new Grid { Width = 224, Height = 168 };
        root.Children.Add(glow.Host);
        root.Children.Add(art);
        AutomationProperties.SetAccessibilityView(root, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return root;
    }

    /// 斜纹底：135°，14px 一条，浅 #FAFAFB / #F4F5F7；深色换成卡片色上的两档。
    /// 用代码画平行斜线（LinearGradientBrush 的 Repeat 在 WinUI 里会把同一画刷的其他元素画灰）。
    public static FrameworkElement Stripes()
    {
        var root = new Grid { IsHitTestVisible = false };
        var path = new Path { StrokeThickness = 14 / 1.0 };
        root.Children.Add(path);
        AutomationProperties.SetAccessibilityView(root, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        void paint()
        {
            var dark = root.ActualTheme == ElementTheme.Dark;
            root.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 0x29, 0x2A, 0x2D) : Windows.UI.Color.FromArgb(255, 0xFA, 0xFA, 0xFB));
            path.Stroke = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 0x26, 0x27, 0x2A) : Windows.UI.Color.FromArgb(255, 0xF4, 0xF5, 0xF7));
        }
        void build(double w, double h)
        {
            // 条纹沿 45°（↗）走向，垂直方向周期 28；横向间距 28·√2。每条线的中心落在周期的后半段（14–28）。
            var g = new GeometryGroup();
            var step = 28 * Math.Sqrt(2);
            var offset = 21 * Math.Sqrt(2);
            for (var x0 = -h - step; x0 < w + step; x0 += step)
            {
                var x = x0 + offset;
                g.Children.Add(new LineGeometry { StartPoint = new Windows.Foundation.Point(x, h), EndPoint = new Windows.Foundation.Point(x + h, 0) });
            }
            path.Data = g;
        }
        root.SizeChanged += (_, e) => build(e.NewSize.Width, e.NewSize.Height);
        root.Loaded += (_, _) => paint();
        root.ActualThemeChanged += (_, _) => paint();
        return root;
    }

    /// 内描边色：浅 #D5D8DC，深色用分隔线色。
    public static Brush Edge(FrameworkElement e) =>
        e.ActualTheme == ElementTheme.Dark ? Palette.Get(e, "BentoLine") : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xD5, 0xD8, 0xDC));
}
