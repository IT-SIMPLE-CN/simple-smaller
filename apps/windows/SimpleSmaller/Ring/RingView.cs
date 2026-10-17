using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Ring;

/// 圆环（设计稿 06）。尺寸取自设计稿：画布 440，镂空半径 64，内圈 64–132，外圈 138–210。
/// 出现：镂空圈先出 → 内圈从 12 点顺时针逐格弹出（每格 18ms）→ 外圈展开，约 0.4 秒；换内圈时外圈重新展开。
/// 「减弱动态效果」时直接显示。
internal sealed class RingView : Grid
{
    public const double Size = 440;
    private const double C = 220;
    public const double Hole = 64, InnerOuter = 132, OuterInner = 138, OuterOuter = 210;
    private const double OuterSpan = 36;

    private readonly IReadOnlyList<RingSlot> slots;
    private readonly string badge;
    private readonly Action<RingChoice> onChoose;
    private readonly Action onDismiss;
    private readonly Canvas canvas = new() { Width = Size, Height = Size };
    private readonly Canvas outerLayer = new() { Width = Size, Height = Size };
    private readonly List<(Path shape, TextBlock text, Canvas group)> inner = [];
    private readonly List<(Path shape, StackPanel text, Canvas group)> outer = [];
    private Ellipse? holeRing, holeShadow;
    private Canvas? holeGroup;
    private FrameworkElement? badgeView;
    private int selected;
    private int? hoverOuter;

    public RingView(IReadOnlyList<RingSlot> slots, string badge, Action<RingChoice> onChoose, Action onDismiss)
    {
        this.slots = slots;
        this.badge = badge;
        this.onChoose = onChoose;
        this.onDismiss = onDismiss;
        Width = Size;
        Height = Size;
        // 透明底也要能收到指针（null 背景点不中）。
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        Children.Add(canvas);
        AutomationProperties.SetName(this, "圆环");
        Build();
        ActualThemeChanged += (_, _) => Paint();
        PointerMoved += (_, e) => Hover(e.GetCurrentPoint(this).Position);
        PointerExited += (_, _) => SetHoverOuter(null);
        PointerPressed += (_, e) =>
        {
            CapturePointer(e.Pointer);
            Hover(e.GetCurrentPoint(this).Position);
            e.Handled = true;
        };
        // 按下拖到外圈松手也执行；点一下等同按下后原地松手。
        PointerReleased += (_, e) =>
        {
            ReleasePointerCapture(e.Pointer);
            Release(e.GetCurrentPoint(this).Position);
            e.Handled = true;
        };
    }

    private IReadOnlyList<RingChoice> CurrentChoices => selected >= 0 && selected < slots.Count ? slots[selected].Choices : [];
    private double InnerSpan => 360.0 / Math.Max(1, slots.Count);

    // MARK: 搭界面

    private void Build()
    {
        // 镂空圈：中心是空的，露出被点的文件。
        holeGroup = Group();
        holeShadow = new Ellipse { Width = Hole * 2 + 2, Height = Hole * 2 + 2, StrokeThickness = 3, Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)) };
        Canvas.SetLeft(holeShadow, C - Hole - 1);
        Canvas.SetTop(holeShadow, C - Hole - 1);
        holeRing = new Ellipse { Width = Hole * 2, Height = Hole * 2, StrokeThickness = 1.5 };
        Canvas.SetLeft(holeRing, C - Hole);
        Canvas.SetTop(holeRing, C - Hole);
        holeGroup.Children.Add(holeShadow);
        holeGroup.Children.Add(holeRing);
        AutomationProperties.SetAccessibilityView(holeGroup, AccessibilityView.Raw);
        canvas.Children.Add(holeGroup);

        for (var i = 0; i < slots.Count; i++)
        {
            var mid = i * InnerSpan;
            var group = Group();
            var shape = new Path { Data = Annulus(Hole, InnerOuter, mid - InnerSpan / 2, mid + InnerSpan / 2), StrokeThickness = 0.5 };
            var label = new TextBlock { Text = slots[i].Label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
            AutomationProperties.SetAccessibilityView(label, AccessibilityView.Raw);
            group.Children.Add(shape);
            group.Children.Add(Place(label, Point((Hole + InnerOuter) / 2, mid), 90, 22));
            AutomationProperties.SetName(group, slots[i].Label);
            canvas.Children.Add(group);
            inner.Add((shape, label, group));
        }
        canvas.Children.Add(outerLayer);
        BuildOuter();

        if (badge.Length > 0)
        {
            var text = new TextBlock { Text = badge, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var pill = new Border { Background = Palette.Brand, Padding = new Thickness(8, 0, 8, 0), MinWidth = 44, Height = 18, Child = text, HorizontalAlignment = HorizontalAlignment.Center };
            Capsule.Apply(pill);
            var host = new Grid { Width = 140, Height = 18 };
            host.Children.Add(pill);
            Canvas.SetLeft(host, C - 70);
            Canvas.SetTop(host, C - Hole - 9);
            AutomationProperties.SetName(host, badge);
            canvas.Children.Add(host);
            badgeView = host;
        }
        Paint();
    }

    private void BuildOuter()
    {
        outerLayer.Children.Clear();
        outer.Clear();
        var choices = CurrentChoices;
        if (choices.Count == 0) return;
        var n = choices.Count;
        var start = selected * InnerSpan - n * OuterSpan / 2;
        for (var j = 0; j < n; j++)
        {
            var a1 = start + j * OuterSpan;
            var group = Group();
            var shape = new Path { Data = Annulus(OuterInner, OuterOuter, a1 + 0.6, a1 + OuterSpan - 0.6), StrokeThickness = 0.5 };
            var isDefault = j == slots[selected].DefaultIndex;
            var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = (isDefault ? "● " : "") + choices[j].Label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            stack.Children.Add(new TextBlock { Text = choices[j].Sub, FontSize = 10, Opacity = 0.7, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            foreach (var t in stack.Children) AutomationProperties.SetAccessibilityView(t, AccessibilityView.Raw);
            group.Children.Add(shape);
            group.Children.Add(Place(stack, Point((OuterInner + OuterOuter) / 2, a1 + OuterSpan / 2), 90, 34));
            AutomationProperties.SetName(group, $"{choices[j].Label}，{choices[j].Sub}");
            outerLayer.Children.Add(group);
            outer.Add((shape, stack, group));
        }
        Paint();
    }

    /// 每个扇区一组，整组按圆心缩放。
    private static Canvas Group() => new() { Width = Size, Height = Size, IsHitTestVisible = false };

    private static FrameworkElement Place(FrameworkElement content, Point center, double w, double h)
    {
        var box = new Grid { Width = w, Height = h };
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        box.Children.Add(content);
        Canvas.SetLeft(box, center.X - w / 2);
        Canvas.SetTop(box, center.Y - h / 2);
        return box;
    }

    // MARK: 上色

    private void Paint()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var primary = Palette.Get(this, "BentoPrimary");
        // 系统材质的替代：不透明的卡片色，扇区之间一条细线。
        // 透明窗口经 DWM 合成时半透明会被再淡一层（95% 看着像 60%），压在文件图标上读不清，所以用不透明。
        var material = Palette.Get(this, "BentoCard");
        var line = new SolidColorBrush(dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x16, 0, 0, 0));
        if (holeRing is not null) holeRing.Stroke = new SolidColorBrush(Color.FromArgb(dark ? (byte)0x59 : (byte)0xE6, 0xFF, 0xFF, 0xFF));
        for (var i = 0; i < inner.Count; i++)
        {
            var on = i == selected;
            inner[i].shape.Fill = on ? Palette.Brand : material;
            inner[i].shape.Stroke = line;
            inner[i].text.Foreground = on ? new SolidColorBrush(Microsoft.UI.Colors.White) : primary;
            AutomationProperties.SetItemStatus(inner[i].group, on ? "已选" : "");
        }
        var hoverFill = new SolidColorBrush(dark ? Microsoft.UI.Colors.White : Color.FromArgb(255, 14, 14, 14));
        var hoverText = new SolidColorBrush(dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        for (var j = 0; j < outer.Count; j++)
        {
            var hovered = hoverOuter == j;
            outer[j].shape.Fill = hovered ? hoverFill : material;
            outer[j].shape.Stroke = line;
            foreach (var t in outer[j].text.Children.OfType<TextBlock>()) t.Foreground = hovered ? hoverText : primary;
        }
    }

    // MARK: 动效

    /// 出现：镂空圈 → 内圈逐格 → 外圈展开。
    public void Reveal()
    {
        if (!Motion.Enabled)
        {
            return;
        }
        if (holeGroup is not null) Animate(holeGroup, 0.6f, 0, 220);
        for (var i = 0; i < inner.Count; i++) Animate(inner[i].group, 0.86f, 40 + i * 18, 260);
        if (badgeView is not null) Fade(badgeView, 60, 200);
        AnimateOuter(180);
    }

    private void AnimateOuter(int delay)
    {
        if (!Motion.Enabled) return;
        for (var j = 0; j < outer.Count; j++) Animate(outer[j].group, 0.9f, delay + j * 24, 240);
    }

    private static void Animate(UIElement element, float from, int delayMs, int durationMs)
    {
        var visual = Motion.VisualOf(element);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)C, (float)C, 0);
        var scale = c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0, new Vector3(from, from, 1));
        scale.InsertKeyFrame(1, Vector3.One, Motion.Ease(c));
        scale.Duration = TimeSpan.FromMilliseconds(durationMs);
        scale.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        scale.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Scale", scale);
        Fade(element, delayMs, durationMs);
    }

    private static void Fade(UIElement element, int delayMs, int durationMs)
    {
        var visual = Motion.VisualOf(element);
        var c = visual.Compositor;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, Motion.Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(durationMs);
        fade.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);
    }

    // MARK: 交互

    private enum Kind { Center, Inner, Outer, Outside }

    private (Kind kind, int index) Region(Point p)
    {
        double dx = p.X - C, dy = p.Y - C;
        var r = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dx, -dy) * 180 / Math.PI;
        if (angle < 0) angle += 360;
        if (r < Hole) return (Kind.Center, 0);
        if (r <= InnerOuter + 3)
        {
            var i = (int)(((angle + InnerSpan / 2) % 360) / InnerSpan);
            return (Kind.Inner, Math.Min(slots.Count - 1, i));
        }
        if (r <= OuterOuter)
        {
            var n = CurrentChoices.Count;
            var rel = angle - (selected * InnerSpan - n * OuterSpan / 2);
            rel = (rel + 720) % 360;
            var j = (int)(rel / OuterSpan);
            return j >= 0 && j < n ? (Kind.Outer, j) : (Kind.Outside, 0);
        }
        return (Kind.Outside, 0);
    }

    private void Hover(Point p)
    {
        var (kind, index) = Region(p);
        switch (kind)
        {
            case Kind.Inner:
                SetHoverOuter(null);
                if (index != selected) Select(index);
                break;
            case Kind.Outer:
                SetHoverOuter(index);
                break;
            default:
                SetHoverOuter(null);
                break;
        }
    }

    private void SetHoverOuter(int? j)
    {
        if (hoverOuter == j) return;
        hoverOuter = j;
        Paint();
    }

    /// 换内圈：外圈重新展开。
    private void Select(int i)
    {
        selected = i;
        hoverOuter = null;
        BuildOuter();
        AnimateOuter(0);
    }

    private void Release(Point p)
    {
        var (kind, index) = Region(p);
        switch (kind)
        {
            case Kind.Inner:
                onChoose(slots[index].Choices[slots[index].DefaultIndex]);
                break;
            case Kind.Outer:
                onChoose(CurrentChoices[index]);
                break;
            default:
                onDismiss();
                break;
        }
    }

    // MARK: 几何

    /// 角度从 12 点起顺时针。
    private static Point Point(double r, double angle)
    {
        var t = angle * Math.PI / 180;
        return new Point(C + r * Math.Sin(t), C - r * Math.Cos(t));
    }

    /// 环形扇区。
    private static Geometry Annulus(double r1, double r2, double a1, double a2)
    {
        var large = a2 - a1 > 180;
        var figure = new PathFigure { StartPoint = Point(r2, a1), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment { Point = Point(r2, a2), Size = new Windows.Foundation.Size(r2, r2), SweepDirection = SweepDirection.Clockwise, IsLargeArc = large });
        figure.Segments.Add(new LineSegment { Point = Point(r1, a2) });
        figure.Segments.Add(new ArcSegment { Point = Point(r1, a1), Size = new Windows.Foundation.Size(r1, r1), SweepDirection = SweepDirection.Counterclockwise, IsLargeArc = large });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
