using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Views.Guide;

/// 聚光灯小教学（设计稿 V2.2 · E）：整个窗口压暗，品牌红描边的聚光框框住一张卡，旁边一张白色小卡片。
/// 聚光框 = 目标外扩 6、圆角 30，外面 3px 品牌红；压暗 rgba(17,18,20,.52)。
/// 翻页时框和卡片一起伸缩移过去（0.5 秒，cubic-bezier(.22,1,.36,1)）；「减弱动态效果」时直接跳。
/// Esc = 跳过。目标按 GuideTargets 的名字找，找不到这一步就不框、卡片放正中。
internal sealed class Coach : Grid
{
    private const double CardWidth = 330;
    private const double Pad = 6;
    private const double Radius = 30;
    private const double Duration = 500;

    private readonly CoachStep[] steps;
    private readonly Canvas layer = new();
    private readonly Path dim = new() { IsHitTestVisible = false };
    private readonly Border ring = new() { BorderThickness = new Thickness(3), IsHitTestVisible = false };
    private readonly Grid cardHost = new() { Width = CardWidth };
    private readonly Border card = new() { Width = CardWidth, Padding = new Thickness(14), CornerRadius = new CornerRadius(22) };
    private readonly ColorShadow shadow = new(Windows.UI.Color.FromArgb(0x4D, 0, 0, 0), 60, 20, 22);
    private int step;
    private bool closing;
    private Button? primary;

    // 动画：从 from 到 to，now 是当前画出来的位置。
    private Rect? spotNow, spotFrom, spotTo;
    private Point cardNow, cardFrom, cardTo;
    private Stopwatch? clock;

    /// 关掉时回调（跳过、知道了、Esc 都算看过）。
    public event Action? Closed;

    public Coach(string name, CoachStep[] steps)
    {
        this.steps = steps;
        // 透明底也接住指针：教学开着时底下的界面不响应。
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        dim.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0x85, 0x11, 0x12, 0x14));
        ring.BorderBrush = Palette.Brand;
        layer.Children.Add(dim);
        layer.Children.Add(ring);
        cardHost.Children.Add(shadow.Host);
        cardHost.Children.Add(card);
        layer.Children.Add(cardHost);
        Children.Add(layer);
        AutomationProperties.SetAccessibilityView(dim, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(ring, AccessibilityView.Raw);
        AutomationProperties.SetName(card, name);
        AutomationProperties.SetLandmarkType(card, AutomationLandmarkType.Custom);
        AutomationProperties.SetLocalizedLandmarkType(card, "教学");
        TabFocusNavigation = KeyboardNavigationMode.Cycle;

        SizeChanged += (_, _) => Place(animated: false);
        LayoutUpdated += (_, _) => Follow();
        ActualThemeChanged += (_, _) => { Paint(); Fill(appear: false); };
        Loaded += (_, _) =>
        {
            Paint();
            Fill(appear: true);
            Place(animated: false);
            FadeIn();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    public bool IsClosing => closing;

    private void Paint()
    {
        card.Background = Palette.Get(this, "BentoCard");
    }

    // MARK: 卡片内容

    private void Fill(bool appear)
    {
        var s = steps[step];
        var dark = ActualTheme == ElementTheme.Dark;
        var col = new StackPanel { Spacing = 10 };

        var sketch = new Sketch(CoachSteps.ArtWidth, CoachSteps.ArtHeight, dark);
        s.Art(sketch);
        var art = new Border { CornerRadius = new CornerRadius(14), Background = Palette.Get(this, "BentoFill"), Child = sketch.View(CardWidth - 28) };
        col.Children.Add(art);

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var count = new TextBlock { Text = $"{step + 1} / {steps.Length}", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Palette.Brand, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
        var title = new TextBlock { Text = s.Title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Palette.Get(this, "BentoPrimary") };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        AutomationProperties.SetAccessibilityView(count, AccessibilityView.Raw);
        head.Children.Add(count);
        head.Children.Add(title);
        col.Children.Add(head);

        col.Children.Add(new TextBlock
        {
            Text = s.Text,
            FontSize = 12.5,
            LineHeight = 12.5 * 1.65,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Get(this, "BentoSoft"),
        });

        var last = step == steps.Length - 1;
        var buttons = new Grid { Margin = new Thickness(0, 2, 0, 0), ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (last)
        {
            buttons.Children.Add(new TextBlock { Text = "右上角 ? 随时再看", FontSize = 11, Foreground = Palette.Get(this, "BentoSecondary"), VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            var skip = Pill("跳过", 0, 4, null, "BentoSecondary", 12, semibold: false, press: true);
            skip.Click += (_, _) => Close();
            buttons.Children.Add(skip);
        }
        if (step > 0)
        {
            var prev = Pill("上一步", 34, 14, "BentoFill", "BentoPrimary", 13, semibold: false);
            prev.Click += (_, _) => Go(step - 1);
            Grid.SetColumn(prev, 1);
            buttons.Children.Add(prev);
        }
        primary = last
            ? Pill("知道了", 34, 18, "Brand", "OnBrand", 13, semibold: true)
            : Pill("下一步", 34, 18, "BentoInk", "BentoOnInk", 13, semibold: true);
        primary.Click += (_, _) => { if (step < steps.Length - 1) Go(step + 1); else Close(); };
        Grid.SetColumn(primary, 2);
        buttons.Children.Add(primary);
        col.Children.Add(buttons);

        card.Child = col;
        AutomationProperties.SetFullDescription(card, $"第 {step + 1} 步，共 {steps.Length} 步。{s.Title}。{s.Text}");
        if (appear)
        {
            // 等排好版再弹（中心点要用实际尺寸）。
            col.Loaded += (_, _) => Motion.Appear(col);
        }
        // 焦点放进卡片：Esc、回车、Tab 都在卡片里。
        DispatcherQueue.TryEnqueue(() => primary?.Focus(FocusState.Programmatic));
    }

    /// 胶囊按钮：高 34、圆角一半（跳过是无底的文字按钮）。
    private Button Pill(string text, double height, double padX, string? fillKey, string textKey, double size, bool semibold, bool press = true)
    {
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (semibold) label.FontWeight = FontWeights.SemiBold;
        label.Foreground = textKey == "OnBrand" ? (Brush)Application.Current.Resources["OnBrand"] : Palette.Get(this, textKey);
        var back = new Border { Height = 34, Padding = new Thickness(padX, 0, padX, 0), Child = label };
        if (fillKey is not null)
        {
            back.CornerRadius = new CornerRadius(17);
            back.Background = fillKey == "Brand" ? Palette.Brand : Palette.Get(this, fillKey);
        }
        else
        {
            back.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back, VerticalAlignment = VerticalAlignment.Center };
        if (press) Motion.SetPress(b, true);
        AutomationProperties.SetName(b, text);
        return b;
    }

    // MARK: 翻页与位置

    private void Go(int to)
    {
        if (to < 0 || to >= steps.Length || closing) return;
        step = to;
        Fill(appear: true);
        Place(animated: true);
    }

    /// 算出这一步的聚光框与卡片位置；animated 时从当前位置伸缩过去。
    private void Place(bool animated)
    {
        if (ActualWidth <= 0 || closing) return;
        var (spot, pos) = Locate();
        if (!animated || !Motion.Enabled || spotNow is null || spot is null)
        {
            clock = null;
            CompositionTarget.Rendering -= OnFrame;
            spotNow = spot;
            cardNow = pos;
            Apply();
            return;
        }
        spotFrom = spotNow;
        spotTo = spot;
        cardFrom = cardNow;
        cardTo = pos;
        clock = Stopwatch.StartNew();
        CompositionTarget.Rendering -= OnFrame;
        CompositionTarget.Rendering += OnFrame;
    }

    /// 这一步现在该画在哪：目标外扩的聚光框（找不到目标为 null）与卡片左上角。
    private (Rect? Spot, Point Card) Locate()
    {
        var s = steps[step];
        Rect? target = null;
        if (GuideTargets.Find(s.Target) is { } fe && fe.XamlRoot is not null)
        {
            var r = fe.TransformToVisual(this).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            // 窄档时目标在可滚动的一列里：只框露出来的那一截，不框到被滚走、盖住别的卡的部分。
            for (var p = VisualTreeHelper.GetParent(fe); p is not null && !ReferenceEquals(p, this); p = VisualTreeHelper.GetParent(p))
            {
                if (p is ScrollViewer sv && sv.ActualHeight > 0)
                {
                    var v = sv.TransformToVisual(this).TransformBounds(new Rect(0, 0, sv.ActualWidth, sv.ActualHeight));
                    r.Intersect(v);
                    if (r.IsEmpty) break;
                }
            }
            target = r.IsEmpty ? null : r;
        }
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        var cardHeight = card.DesiredSize.Height;
        var spot = target is { } t ? new Rect(t.X - Pad, t.Y - Pad, t.Width + 2 * Pad, t.Height + 2 * Pad) : (Rect?)null;
        var pos = target is { } tt ? CardPosition(tt, s.Side, cardHeight) : new Point((ActualWidth - CardWidth) / 2, (ActualHeight - cardHeight) / 2);
        return (spot, pos);
    }

    /// 窗口改大小、目标换了排布（窄档 / 宽档）时实时跟着目标走，不播翻页动画；翻页动画途中只改终点。
    private void Follow()
    {
        if (ActualWidth <= 0 || closing || !IsLoaded) return;
        var (spot, pos) = Locate();
        if (clock is not null)
        {
            if (spot is not null) spotTo = spot;
            cardTo = pos;
            return;
        }
        if (Same(spot, spotNow) && Math.Abs(pos.X - cardNow.X) < 0.5 && Math.Abs(pos.Y - cardNow.Y) < 0.5) return;
        spotNow = spot;
        cardNow = pos;
        Apply();
    }

    private static bool Same(Rect? a, Rect? b) => (a, b) switch
    {
        (null, null) => true,
        ({ } x, { } y) => Math.Abs(x.X - y.X) < 0.5 && Math.Abs(x.Y - y.Y) < 0.5 && Math.Abs(x.Width - y.Width) < 0.5 && Math.Abs(x.Height - y.Height) < 0.5,
        _ => false,
    };

    /// 卡片放在框的哪一边（量自设计稿：下方隔 22、右边隔 18、左边隔 40），放不下换另一边，最后夹在窗口里。
    private Point CardPosition(Rect t, CoachSide side, double h)
    {
        double w = ActualWidth, height = ActualHeight;
        double x, y;
        if (side == CoachSide.Right && t.Right + 18 + CardWidth > w - 16) side = CoachSide.Left;
        if (side == CoachSide.Left && t.Left - 40 - CardWidth < 16) side = t.Right + 18 + CardWidth <= w - 16 ? CoachSide.Right : CoachSide.Below;
        switch (side)
        {
            case CoachSide.Below:
                x = t.Left + t.Width / 2 - CardWidth / 2;
                y = t.Bottom + 22;
                break;
            case CoachSide.BelowEnd:
                x = t.Right - CardWidth - 10;
                y = t.Bottom + 18;
                break;
            case CoachSide.Right:
                x = t.Right + 18;
                y = t.Top + 58;
                break;
            default:
                x = t.Left - 40 - CardWidth;
                y = t.Top + 2;
                break;
        }
        x = Math.Clamp(x, 16, Math.Max(16, w - CardWidth - 16));
        y = Math.Clamp(y, 16, Math.Max(16, height - h - 16));
        return new Point(x, y);
    }

    private void OnFrame(object? sender, object e)
    {
        if (clock is null || spotFrom is not { } a || spotTo is not { } b)
        {
            CompositionTarget.Rendering -= OnFrame;
            return;
        }
        var p = Math.Min(1, clock.Elapsed.TotalMilliseconds / Duration);
        var k = Ease(p);
        spotNow = new Rect(Lerp(a.X, b.X, k), Lerp(a.Y, b.Y, k), Math.Max(0, Lerp(a.Width, b.Width, k)), Math.Max(0, Lerp(a.Height, b.Height, k)));
        cardNow = new Point(Lerp(cardFrom.X, cardTo.X, k), Lerp(cardFrom.Y, cardTo.Y, k));
        Apply();
        if (p >= 1)
        {
            clock = null;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// cubic-bezier(.22,1,.36,1)：按 x 反解 t（牛顿法），返回 y。
    private static double Ease(double x)
    {
        const double x1 = 0.22, y1 = 1, x2 = 0.36, y2 = 1;
        static double Bez(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        static double Slope(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var d = Slope(t, x1, x2);
            if (Math.Abs(d) < 1e-6) break;
            t -= (Bez(t, x1, x2) - x) / d;
            t = Math.Clamp(t, 0, 1);
        }
        return Bez(t, y1, y2);
    }

    /// 把当前位置画出来：压暗层挖掉聚光框、红框、卡片。
    private void Apply()
    {
        double w = ActualWidth, h = ActualHeight;
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry { Rect = new Rect(0, 0, w, h) });
        if (spotNow is { } s)
        {
            group.Children.Add(RoundRect(s, Radius));
            ring.Visibility = Visibility.Visible;
            Canvas.SetLeft(ring, s.X - 3);
            Canvas.SetTop(ring, s.Y - 3);
            ring.Width = s.Width + 6;
            ring.Height = s.Height + 6;
            ring.CornerRadius = new CornerRadius(Math.Min(Radius + 3, Math.Min(ring.Width, ring.Height) / 2));
        }
        else
        {
            ring.Visibility = Visibility.Collapsed;
        }
        dim.Data = group;
        Canvas.SetLeft(cardHost, cardNow.X);
        Canvas.SetTop(cardHost, cardNow.Y);
    }

    private static PathGeometry RoundRect(Rect r, double radius)
    {
        var k = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        var f = new PathFigure { StartPoint = new Point(r.Left + k, r.Top), IsClosed = true, IsFilled = true };
        void arc(double x, double y) => f.Segments.Add(new ArcSegment { Point = new Point(x, y), Size = new Size(k, k), SweepDirection = SweepDirection.Clockwise });
        f.Segments.Add(new LineSegment { Point = new Point(r.Right - k, r.Top) });
        arc(r.Right, r.Top + k);
        f.Segments.Add(new LineSegment { Point = new Point(r.Right, r.Bottom - k) });
        arc(r.Right - k, r.Bottom);
        f.Segments.Add(new LineSegment { Point = new Point(r.Left + k, r.Bottom) });
        arc(r.Left, r.Bottom - k);
        f.Segments.Add(new LineSegment { Point = new Point(r.Left, r.Top + k) });
        arc(r.Left + k, r.Top);
        var g = new PathGeometry();
        g.Figures.Add(f);
        return g;
    }

    // MARK: 出现与关掉

    private void FadeIn()
    {
        var v = Motion.VisualOf(this);
        var c = v.Compositor;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, Motion.Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(Motion.Enabled ? 260 : 160);
        v.StartAnimation("Opacity", fade);
    }

    /// 关掉（跳过 / 知道了 / Esc）：淡出后从界面上拿掉。
    public void Close()
    {
        if (closing) return;
        closing = true;
        CompositionTarget.Rendering -= OnFrame;
        IsHitTestVisible = false;
        var v = Motion.VisualOf(this);
        var c = v.Compositor;
        var batch = c.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, Motion.Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(180);
        v.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (Parent is Panel p) p.Children.Remove(this);
        });
        Closed?.Invoke();
    }
}
