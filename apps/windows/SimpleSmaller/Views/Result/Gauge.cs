using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using SimpleSmaller.Bento;
using Windows.Foundation;

namespace SimpleSmaller.Views.Result;

/// 半圆刻度盘（设计稿 840 × 432 的坐标按比例缩放）：红色是这一批小掉的比例，中间大号百分比。
/// 红色弧带和刻度随进度重画，出现时用弹簧从 0 走到目标值（mac 的 GaugeBand / GaugeTicks 可动画形状）。
internal sealed class Gauge : UserControl
{
    private const int Ticks = 48;
    private readonly double fraction;
    private double shown;
    private double velocity;
    private bool animating;
    private TimeSpan? lastTick;
    private DateTime startAt;

    private readonly Canvas canvas = new();
    private readonly Path inner = new() { StrokeThickness = 1.2 };
    private readonly Path outer = new() { StrokeThickness = 1.2 };
    private readonly Path band = new();
    private readonly Path hub = new();
    private readonly TextBlock[] marks = new TextBlock[3];
    private readonly TextBlock caption = new() { Text = "这一批小了", FontSize = 14 };
    private readonly TextBlock number = new();
    private readonly Run digits = new() { FontWeight = Microsoft.UI.Text.FontWeights.Light };
    private readonly Run percent = new() { Text = "%" };

    public Gauge(double fraction)
    {
        this.fraction = Math.Clamp(fraction, 0, 1);
        Content = canvas;
        canvas.Children.Add(inner);
        canvas.Children.Add(outer);
        canvas.Children.Add(band);
        canvas.Children.Add(hub);
        int[] values = [25, 50, 75];
        for (var i = 0; i < 3; i++)
        {
            marks[i] = new TextBlock { Text = $"{values[i]}", FontSize = 12 };
            canvas.Children.Add(marks[i]);
        }
        canvas.Children.Add(caption);
        digits.Text = $"{(int)Num.Round(this.fraction * 100)}";
        number.Inlines.Add(digits);
        number.Inlines.Add(percent);
        canvas.Children.Add(number);
        foreach (var t in marks.Append(caption).Append(number))
        {
            t.SizeChanged += (_, _) => Layout();
        }

        AutomationProperties.SetName(this, $"这一批小了 {(int)Num.Round(this.fraction * 100)}%");
        AutomationProperties.SetAccessibilityView(canvas, AccessibilityView.Raw);
        SizeChanged += (_, _) => Layout();
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); Start(); };
        Unloaded += (_, _) => Stop();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ImagePeer(this);

    /// 旁白读成一张图，名字就是「这一批小了 N%」。
    private sealed class ImagePeer(FrameworkElement owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override IList<AutomationPeer>? GetChildrenCore() => null;
    }

    private void Paint()
    {
        var primary = Palette.Get(this, "BentoPrimary");
        var secondary = Palette.Get(this, "BentoSecondary");
        inner.Stroke = primary;
        inner.Opacity = 0.15;
        outer.Stroke = primary;
        band.Fill = Palette.Brand;
        hub.Fill = Palette.Brand;
        foreach (var m in marks) m.Foreground = secondary;
        caption.Foreground = secondary;
        number.Foreground = primary;
    }

    // ---------- 动画 ----------

    private void Start()
    {
        if (!Motion.Enabled)
        {
            shown = fraction;
            Layout();
            return;
        }
        shown = 0;
        velocity = 0;
        startAt = DateTime.Now.AddMilliseconds(150);
        lastTick = null;
        animating = true;
        CompositionTarget.Rendering += OnRendering;
        Layout();
    }

    private void Stop()
    {
        if (!animating) return;
        animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// 弹簧（response 0.9 秒、阻尼 0.85，与 mac 一致），逐帧积分。
    private void OnRendering(object? sender, object e)
    {
        if (DateTime.Now < startAt) return;
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(DateTime.Now.Ticks);
        var dt = lastTick is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 1.0 / 30) : 1.0 / 60;
        lastTick = now;
        const double response = 0.9, damping = 0.85;
        var omega = 2 * Math.PI / response;
        // 分几小步积分，帧率低时也稳定。
        for (var i = 0; i < 4; i++)
        {
            var h = dt / 4;
            var accel = -omega * omega * (shown - fraction) - 2 * damping * omega * velocity;
            velocity += accel * h;
            shown += velocity * h;
        }
        if (Math.Abs(shown - fraction) < 0.0005 && Math.Abs(velocity) < 0.001)
        {
            shown = fraction;
            Stop();
        }
        Layout();
    }

    // ---------- 画 ----------

    private void Layout()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var s = Math.Min(w / 840, h / 432);
        var c = new Point(412 * s, 432 * s);
        var p = Math.Clamp(shown, 0, 1);
        var r1 = 232 * s;
        var r2 = 290 * s;

        inner.Data = TickGeometry(c, s, p, outerTicks: false, r1, r2);
        outer.Data = TickGeometry(c, s, p, outerTicks: true, r1, r2);
        band.Data = BandGeometry(c, p, r1, r2);
        hub.Data = HalfDisc(c, 22 * s);

        for (var i = 0; i < 3; i++)
        {
            var a = Math.PI * (1 + (i + 1) * 0.25);
            Place(marks[i], c.X + Math.Cos(a) * 330 * s, c.Y + Math.Sin(a) * 330 * s);
        }
        Place(caption, c.X, 254 * s);
        digits.FontSize = Math.Max(1, 146 * s);
        digits.CharacterSpacing = -41;
        percent.FontSize = Math.Max(1, 48 * s);
        Place(number, c.X - 10 * s, 340 * s);
    }

    private static void Place(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x - e.ActualWidth / 2);
        Canvas.SetTop(e, y - e.ActualHeight / 2);
    }

    private static Point At(Point c, double angle, double r) => new(c.X + Math.Cos(angle) * r, c.Y + Math.Sin(angle) * r);

    /// 刻度：红色以外的一段在弧带里画细线；红色那段在外侧画黑色刻度。
    private static Geometry TickGeometry(Point c, double s, double progress, bool outerTicks, double r1, double r2)
    {
        var g = new PathGeometry();
        for (var i = 0; i <= Ticks; i++)
        {
            var t = (double)i / Ticks;
            if (outerTicks ? t > progress : t <= progress) continue;
            var a = Math.PI * (1 + t);
            var (from, to) = outerTicks ? (r2 + 10 * s, r2 + (i % 12 == 0 ? 24 : 18) * s) : (r1 + 6 * s, r2 - 2 * s);
            var f = new PathFigure { StartPoint = At(c, a, from), IsClosed = false, IsFilled = false };
            f.Segments.Add(new LineSegment { Point = At(c, a, to) });
            g.Figures.Add(f);
        }
        return g;
    }

    /// 红色弧带：从左端顺时针走到 progress。
    private static Geometry BandGeometry(Point c, double progress, double r1, double r2)
    {
        var g = new PathGeometry();
        if (progress <= 0.001) return g;
        var end = Math.PI * (1 + progress);
        var f = new PathFigure { StartPoint = At(c, Math.PI, r2), IsClosed = true, IsFilled = true };
        f.Segments.Add(new ArcSegment { Point = At(c, end, r2), Size = new Size(r2, r2), SweepDirection = SweepDirection.Clockwise });
        f.Segments.Add(new LineSegment { Point = At(c, end, r1) });
        f.Segments.Add(new ArcSegment { Point = At(c, Math.PI, r1), Size = new Size(r1, r1), SweepDirection = SweepDirection.Counterclockwise });
        g.Figures.Add(f);
        return g;
    }

    private static Geometry HalfDisc(Point c, double r)
    {
        var g = new PathGeometry();
        var f = new PathFigure { StartPoint = new Point(c.X - r, c.Y), IsClosed = true, IsFilled = true };
        f.Segments.Add(new ArcSegment { Point = new Point(c.X + r, c.Y), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
        g.Figures.Add(f);
        return g;
    }
}
