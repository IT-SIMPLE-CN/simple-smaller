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

/// 半圆刻度盘（设计稿 V2.1 · 04 的 840 × 432 坐标按比例缩放）：红色弧带是这一批小掉的比例，中间大号百分比。
/// 外圈 80 格刻度（每 8 格一根长的），走过的黑色、没走到的浅灰；弧带剩下的一段画浅灰细线；
/// 弧带末端一根黑色指针，圆心红色半圆上一根短指针。出现时用弹簧从 0 走到目标值。
internal sealed class Gauge : UserControl
{
    private const int Ticks = 80;
    private readonly double fraction;
    private double shown;
    private double velocity;
    private bool animating;
    private TimeSpan? lastTick;
    private DateTime startAt;

    private readonly Canvas canvas = new();
    private readonly Path inner = new() { StrokeThickness = 1.2 };
    private readonly Path outer = new() { StrokeThickness = 1.1 };
    private readonly Path outerMajor = new() { StrokeThickness = 1.6 };
    private readonly Path rest = new() { StrokeThickness = 1.1 };
    private readonly Path restMajor = new() { StrokeThickness = 1.6 };
    private readonly Path needle = new() { StrokeThickness = 2.2 };
    private readonly Path pointer = new() { StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
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
        canvas.Children.Add(rest);
        canvas.Children.Add(restMajor);
        canvas.Children.Add(outer);
        canvas.Children.Add(outerMajor);
        canvas.Children.Add(band);
        canvas.Children.Add(needle);
        canvas.Children.Add(hub);
        canvas.Children.Add(pointer);
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
        // 浅灰：设计稿 #D3D6DA / #C9CCD1，深色时取描边灰。
        var line = Palette.Get(this, "BentoLine");
        inner.Stroke = line;
        rest.Stroke = restMajor.Stroke = line;
        outer.Stroke = outerMajor.Stroke = primary;
        needle.Stroke = pointer.Stroke = primary;
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

        inner.Data = HatchGeometry(c, p, r1, r2);
        outer.Data = TickGeometry(c, s, p, done: true, major: false, r2);
        outerMajor.Data = TickGeometry(c, s, p, done: true, major: true, r2);
        rest.Data = TickGeometry(c, s, p, done: false, major: false, r2);
        restMajor.Data = TickGeometry(c, s, p, done: false, major: true, r2);
        var end = Math.PI * (1 + p);
        needle.Data = Segment(At(c, end, r1 - 10 * s), At(c, end, r2 + 34 * s));
        pointer.Data = Segment(At(c, end, 30 * s), At(c, end, 56 * s));
        needle.Visibility = pointer.Visibility = p > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        band.Data = BandGeometry(c, p, r1, r2);
        hub.Data = HalfDisc(c, 22 * s);

        for (var i = 0; i < 3; i++)
        {
            var a = Math.PI * (1 + (i + 1) * 0.25);
            Place(marks[i], c.X + Math.Cos(a) * 330 * s, c.Y + Math.Sin(a) * 330 * s);
        }
        Place(caption, c.X, 249 * s);
        digits.FontSize = Math.Max(1, 146 * s);
        digits.CharacterSpacing = -41;
        percent.FontSize = Math.Max(1, 48 * s);
        // 设计稿：数字基线在 y = 382、居中于 x = 402；MiSans 146 号字的文字框中心约在基线上方 54。
        Place(number, c.X - 10 * s, 328 * s);
    }

    private static void Place(FrameworkElement e, double x, double y)
    {
        Canvas.SetLeft(e, x - e.ActualWidth / 2);
        Canvas.SetTop(e, y - e.ActualHeight / 2);
    }

    private static Point At(Point c, double angle, double r) => new(c.X + Math.Cos(angle) * r, c.Y + Math.Sin(angle) * r);

    /// 外圈刻度：走过的（t ≤ progress）黑色、没走到的浅灰；每 8 格一根长的。
    private static Geometry TickGeometry(Point c, double s, double progress, bool done, bool major, double r2)
    {
        var g = new PathGeometry();
        for (var i = 0; i <= Ticks; i++)
        {
            var t = (double)i / Ticks;
            if (done != (t <= progress + 1e-9) || major != (i % 8 == 0)) continue;
            var a = Math.PI * (1 + t);
            g.Figures.Add(Line(At(c, a, r2 + 10 * s), At(c, a, r2 + (major ? 24 : 18) * s)));
        }
        return g;
    }

    /// 弧带没填到的一段：每隔 1.6° 一根浅灰细线，从内圈画到外圈。
    private static Geometry HatchGeometry(Point c, double progress, double r1, double r2)
    {
        var g = new PathGeometry();
        const double step = 1.6 / 180;
        for (var t = progress + step; t < 1 + 1e-9; t += step)
        {
            var a = Math.PI * (1 + t);
            g.Figures.Add(Line(At(c, a, r1), At(c, a, r2)));
        }
        return g;
    }

    private static PathFigure Line(Point a, Point b)
    {
        var f = new PathFigure { StartPoint = a, IsClosed = false, IsFilled = false };
        f.Segments.Add(new LineSegment { Point = b });
        return f;
    }

    private static Geometry Segment(Point a, Point b)
    {
        var g = new PathGeometry();
        g.Figures.Add(Line(a, b));
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
