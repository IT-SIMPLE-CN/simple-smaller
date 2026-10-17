using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace SimpleSmaller.Bento;

/// PDF 页用到的小工具。行、开关行、「存为新配方」、处理中锁卡用 Views/Cards/OutputCards.cs 里的通用组件
/// （BentoRow、BentoToggle、SaveRecipeAccessory、Lock），这里不再另做一份。
internal static class PdfUi
{
    /// 文字：按色板键上色，深浅色切换时自己重新上色。
    public static TextBlock Text(string text = "", double size = 13, string key = "BentoPrimary", bool semibold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        if (semibold)
        {
            t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        Tint(t, key);
        return t;
    }

    /// 让文字跟着深浅色取色板里的 key；key 为 "Brand" / "Warning" / "Saving" 时用固定色。
    public static void Tint(TextBlock t, string key)
    {
        t.Tag = key;
        void paint() => t.Foreground = Brush(t, (string)t.Tag);
        t.Loaded += (_, _) => paint();
        t.ActualThemeChanged += (_, _) => paint();
    }

    /// 改文字颜色（会跟着深浅色走）。
    public static void Retint(TextBlock t, string key)
    {
        t.Tag = key;
        t.Foreground = Brush(t, key);
    }

    public static Brush Brush(FrameworkElement e, string key) => key switch
    {
        "Brand" => Palette.Brand,
        "Warning" => Palette.Warning,
        "Saving" => Palette.Saving,
        _ => Palette.Get(e, key),
    };

    /// 卡片里的灰底面板。
    public static Border Panel(UIElement child, Thickness padding)
    {
        var b = new Border { Child = child, Padding = padding, CornerRadius = new CornerRadius(18) };
        b.Loaded += (_, _) => b.Background = Palette.Get(b, "BentoFill");
        b.ActualThemeChanged += (_, _) => b.Background = Palette.Get(b, "BentoFill");
        return b;
    }

    /// 一组小标题 + 胶囊。
    public static StackPanel LabeledPills<T>(string title, PillChoice<T> pills) where T : notnull
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(Text(title, 11, "BentoSecondary"));
        s.Children.Add(pills);
        return s;
    }
}

/// 按住连续加减的圆形图标按钮（拆分页数；WinUI 的 RepeatButton 是密封类，自己做按住连发）。
internal sealed class PdfRepeatCircle : Button
{
    private readonly Border back = new();
    private readonly FontIcon icon = new();
    private readonly DispatcherQueueTimer? timer;
    private bool hovering;
    private bool repeated;

    /// 点一下一次；按住 0.35 秒后每 60 毫秒一次。
    public event Action? Step;

    public PdfRepeatCircle(string glyph, string label, double size = 40)
    {
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        icon.Glyph = glyph;
        icon.FontSize = size * 0.4;
        back.Width = back.Height = size;
        back.CornerRadius = new CornerRadius(size / 2);
        back.Child = icon;
        Content = back;
        AutomationProperties.SetName(this, label);
        ToolTipService.SetToolTip(this, label);
        timer = DispatcherQueue.GetForCurrentThread()?.CreateTimer();
        if (timer is not null)
        {
            timer.Tick += (t, _) =>
            {
                t.Interval = TimeSpan.FromMilliseconds(60);
                repeated = true;
                Step?.Invoke();
            };
        }
        AddHandler(PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        {
            repeated = false;
            if (timer is null) return;
            timer.Interval = TimeSpan.FromMilliseconds(350);
            timer.Start();
        }), true);
        AddHandler(PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => timer?.Stop()), true);
        PointerCaptureLost += (_, _) => timer?.Stop();
        PointerCanceled += (_, _) => timer?.Stop();
        Click += (_, _) =>
        {
            if (!repeated) Step?.Invoke();
            repeated = false;
        };
        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, hovering ? "BentoLine" : "BentoFill");
        icon.Foreground = Palette.Get(this, "BentoPrimary");
    }
}

/// 页面 / 图片缩略图网格：列宽自适应（最小 104、最大 140，间距 14），按 Slot 排位置，
/// 换位置时每格用 Composition 隐式动画滑过去（不重建元素，拖动排序时不闪）。
internal sealed class PdfTileGrid : Panel
{
    public const double MinTile = 104;
    public const double MaxTile = 140;
    public const double Gap = 14;
    public const double RowGap = 16;

    private readonly Dictionary<UIElement, int> slots = new();
    private double tileW = MinTile;
    private double tileH;
    private int cols = 1;

    public void SetSlot(UIElement e, int slot)
    {
        slots[e] = slot;
        InvalidateArrange();
    }

    public int SlotOf(UIElement e) => slots.TryGetValue(e, out var s) ? s : Children.IndexOf(e);

    /// 指针在哪一格上（面板坐标）；不在任何格上返回 -1。
    public int IndexAt(Point p)
    {
        if (tileH <= 0) return -1;
        var col = (int)Math.Floor(p.X / (tileW + Gap));
        var row = (int)Math.Floor(p.Y / (tileH + RowGap));
        if (col < 0 || row < 0) return -1;
        col = Math.Min(col, cols - 1);
        var i = row * cols + col;
        return i < Children.Count ? i : Children.Count - 1;
    }

    protected override Size MeasureOverride(Size available)
    {
        var w = double.IsInfinity(available.Width) ? 600 : available.Width;
        cols = Math.Max(1, (int)Math.Floor((w + Gap) / (MinTile + Gap)));
        tileW = Math.Min(MaxTile, (w - (cols - 1) * Gap) / cols);
        tileH = 0;
        foreach (var c in Children)
        {
            c.Measure(new Size(tileW, double.PositiveInfinity));
            tileH = Math.Max(tileH, c.DesiredSize.Height);
        }
        var rows = (Children.Count + cols - 1) / cols;
        return new Size(w, rows == 0 ? 0 : rows * tileH + (rows - 1) * RowGap);
    }

    private readonly Dictionary<UIElement, (int Slot, Point At)> placed = new();

    /// 换了格子的元素从原位置滑到新位置：先用 Translation 把它推回原处，再动画回 0
    /// （XAML 布局会覆盖 Visual.Offset，所以动 Translation）。窗口改宽、刚加进来的不动画。
    protected override Size ArrangeOverride(Size final)
    {
        foreach (var c in Children)
        {
            var s = SlotOf(c);
            var col = s % cols;
            var row = s / cols;
            var at = new Point(col * (tileW + Gap), row * (tileH + RowGap));
            c.Arrange(new Rect(at.X, at.Y, tileW, tileH));
            if (placed.TryGetValue(c, out var old) && old.Slot != s && Motion.Enabled)
            {
                Slide(c, new Vector3((float)(old.At.X - at.X), (float)(old.At.Y - at.Y), 0));
            }
            placed[c] = (s, at);
        }
        foreach (var gone in placed.Keys.Where(k => !Children.Contains(k)).ToList())
        {
            placed.Remove(gone);
        }
        return final;
    }

    private static void Slide(UIElement e, Vector3 from)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(e, true);
        var v = Motion.VisualOf(e);
        var c = v.Compositor;
        // 正在滑的接着从当前位置算。
        if (v.Properties.TryGetVector3("Translation", out var now) == CompositionGetValueStatus.Succeeded)
        {
            from += now;
        }
        v.Properties.InsertVector3("Translation", from);
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, from);
        a.InsertKeyFrame(1, Vector3.Zero, Motion.Ease(c));
        a.Duration = TimeSpan.FromMilliseconds(250);
        v.StartAnimation("Translation", a);
    }
}

/// 拖放区的虚线圆角框。
internal static class PdfDashed
{
    public static Rectangle Create()
    {
        var r = new Rectangle { RadiusX = 18, RadiusY = 18, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 } };
        AutomationProperties.SetAccessibilityView(r, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return r;
    }
}
