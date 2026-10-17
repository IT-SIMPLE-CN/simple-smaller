using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace SimpleSmaller.Bento;

/// 白色圆角卡片（WinUI 的 Border 是密封类，用工厂方法）。
public static class Card
{
    public static Border Create(UIElement? child = null, Thickness? padding = null)
    {
        var b = new Border { Style = (Style)Application.Current.Resources["BentoCardStyle"], Child = child };
        if (padding is { } p)
        {
            b.Padding = p;
        }
        return b;
    }
}

/// 卡片头：圆形图标 + 标题 + 副标题，右边放操作。
public sealed class Header : Grid
{
    private readonly TextBlock title = new() { Style = (Style)Application.Current.Resources["BentoTitle"] };
    private readonly TextBlock subtitle = new() { Style = (Style)Application.Current.Resources["BentoCaption"] };
    private readonly FontIcon icon = new() { FontSize = 14 };
    private readonly Border iconBack = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17) };
    private readonly ContentPresenter trailing = new() { VerticalAlignment = VerticalAlignment.Center };

    public Header()
    {
        ColumnSpacing = 10;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        iconBack.Child = icon;
        AutomationProperties.SetAccessibilityView(iconBack, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        Children.Add(iconBack);
        Children.Add(texts);
        Children.Add(trailing);
        SetColumn(texts, 1);
        SetColumn(trailing, 2);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    public string Glyph { get => icon.Glyph; set => icon.Glyph = value; }
    public string Title { get => title.Text; set => title.Text = value; }

    public string? Subtitle
    {
        get => subtitle.Text;
        set
        {
            subtitle.Text = value ?? "";
            subtitle.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public object? Trailing { get => trailing.Content; set => trailing.Content = value; }

    private void Paint()
    {
        iconBack.Background = Palette.Get(this, "BentoFill");
        icon.Foreground = Palette.Get(this, "BentoPrimary");
    }
}

/// 胶囊按钮：选中墨黑底白字，其余灰底；可带一个「已修改」红点。
public sealed class PillButton : Button
{
    private readonly Border capsule = new();
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, MaxLines = 1 };
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse dot = new() { Width = 6, Height = 6, Visibility = Visibility.Collapsed };
    private bool on;
    private bool hovering;

    public PillButton(string title, double height = 36)
    {
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(text);
        row.Children.Add(dot);
        capsule.Child = row;
        capsule.Height = height;
        capsule.CornerRadius = new CornerRadius(height / 2);
        capsule.Padding = new Thickness(height >= 34 ? 16 : 12, 0, height >= 34 ? 16 : 12, 0);
        text.FontSize = 13;
        text.Text = title;
        Content = capsule;
        AutomationProperties.SetName(this, title);
        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    public string Title { get => text.Text; set { text.Text = value; AutomationProperties.SetName(this, value); } }

    public bool IsOn
    {
        get => on;
        set
        {
            on = value;
            AutomationProperties.SetItemStatus(this, value ? "已选" : "");
            Paint();
        }
    }

    public bool Dot
    {
        get => dot.Visibility == Visibility.Visible;
        set => dot.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Paint()
    {
        capsule.Background = on ? Palette.Get(this, "BentoInk") : Palette.Get(this, hovering ? "BentoLine" : "BentoFill");
        text.Foreground = on ? Palette.Get(this, "BentoOnInk") : Palette.Get(this, "BentoPrimary");
        text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        dot.Fill = Palette.Brand;
    }
}

/// 一排胶囊单选，放不下就换行。
public sealed class PillChoice<T> : WrapPanel where T : notnull
{
    private readonly List<(T Value, PillButton Button)> pills = new();
    private T selected;

    public event Action<T>? Changed;

    public PillChoice(IEnumerable<(T Value, string Title)> options, T selected, double height = 36, string label = "")
    {
        Spacing = 6;
        this.selected = selected;
        AutomationProperties.SetName(this, label);
        foreach (var (value, title) in options)
        {
            var b = new PillButton(title, height) { IsOn = EqualityComparer<T>.Default.Equals(value, selected) };
            b.Click += (_, _) => Select(value, notify: true);
            pills.Add((value, b));
            Children.Add(b);
        }
    }

    public T Selected { get => selected; set => Select(value, notify: false); }

    private void Select(T value, bool notify)
    {
        selected = value;
        foreach (var (v, b) in pills)
        {
            b.IsOn = EqualityComparer<T>.Default.Equals(v, value);
        }
        if (notify)
        {
            Changed?.Invoke(value);
        }
    }
}

/// 滑块分段：选中的底块在选项之间弹簧滑过去。顶栏分段、卡片里的小分段、图标分段共用。
public sealed class Segments<T> : Grid where T : notnull
{
    public enum Kind { Mini, Top, Icon }

    private readonly Kind kind;
    private readonly Border track = new();
    private readonly Border thumb = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly List<(T Value, Button Button, TextBlock? Text, FontIcon? Icon)> items = new();
    private T selected;
    private bool placed;

    public event Action<T>? Changed;

    /// options：值、文字（图标分段时是悬停与旁白的说明）、图标字形（只有图标分段用）。
    public Segments(Kind kind, IEnumerable<(T Value, string Title, string? Glyph)> options, T selected, string label = "")
    {
        this.kind = kind;
        this.selected = selected;
        var inset = kind == Kind.Top ? 4 : 3;
        HorizontalAlignment = HorizontalAlignment.Left;
        track.CornerRadius = new CornerRadius(100);
        row.Margin = new Thickness(inset);
        thumb.Margin = new Thickness(0);
        Children.Add(track);
        Children.Add(thumb);
        Children.Add(row);
        AutomationProperties.SetName(this, label);
        foreach (var (value, title, glyph) in options)
        {
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"] };
            Motion.SetPress(b, true);
            TextBlock? t = null;
            FontIcon? i = null;
            if (kind == Kind.Icon)
            {
                i = new FontIcon { Glyph = glyph ?? "", FontSize = 13, Width = 32, Height = 28 };
                b.Content = new Border { Width = 32, Height = 28, Child = i };
                ToolTipService.SetToolTip(b, title);
            }
            else
            {
                t = new TextBlock
                {
                    Text = title,
                    FontSize = kind == Kind.Top ? 13 : 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                b.Content = new Border
                {
                    Height = kind == Kind.Top ? 30 : 28,
                    Padding = new Thickness(kind == Kind.Top ? 20 : 12, 0, kind == Kind.Top ? 20 : 12, 0),
                    Child = t,
                };
            }
            AutomationProperties.SetName(b, title);
            b.Click += (_, _) => Select(value, notify: true);
            items.Add((value, b, t, i));
            row.Children.Add(b);
        }
        row.SizeChanged += (_, _) => MoveThumb(animated: placed);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); MoveThumb(animated: false); };
    }

    public T Selected { get => selected; set => Select(value, notify: false); }

    private void Select(T value, bool notify)
    {
        if (EqualityComparer<T>.Default.Equals(value, selected) && placed)
        {
            return;
        }
        selected = value;
        Paint();
        MoveThumb(animated: true);
        if (notify)
        {
            Changed?.Invoke(value);
        }
    }

    private void MoveThumb(bool animated)
    {
        var item = items.FirstOrDefault(x => EqualityComparer<T>.Default.Equals(x.Value, selected));
        if (item.Button is null || item.Button.ActualWidth == 0)
        {
            return;
        }
        var origin = item.Button.TransformToVisual(this).TransformPoint(new Point(0, 0));
        thumb.Width = item.Button.ActualWidth;
        thumb.Height = item.Button.ActualHeight;
        thumb.CornerRadius = new CornerRadius(item.Button.ActualHeight / 2);
        var to = new Vector3((float)origin.X, (float)origin.Y, 0);
        if (animated)
        {
            Motion.SpringOffset(thumb, to);
        }
        else
        {
            Motion.VisualOf(thumb).Offset = to;
        }
        placed = true;
    }

    private void Paint()
    {
        track.Background = Palette.Get(this, kind == Kind.Top ? "BentoCard" : "BentoFill");
        thumb.Background = Palette.Get(this, kind == Kind.Top ? "BentoInk" : "BentoCard");
        thumb.Shadow = null;
        foreach (var (value, button, text, icon) in items)
        {
            var on = EqualityComparer<T>.Default.Equals(value, selected);
            AutomationProperties.SetItemStatus(button, on ? "已选" : "");
            Brush fg = kind == Kind.Top
                ? (on ? Palette.Get(this, "BentoOnInk") : Palette.Get(this, "BentoPrimary"))
                : (on ? Palette.Get(this, "BentoPrimary") : Palette.Get(this, "BentoSecondary"));
            if (text is not null)
            {
                text.Foreground = fg;
                text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            }
            if (icon is not null)
            {
                icon.Foreground = fg;
            }
        }
    }
}

/// 圆形图标按钮（灰底或白底）。
public sealed class CircleIconButton : Button
{
    private readonly Border back = new();
    private readonly FontIcon icon = new();
    private readonly string fillKey;
    private bool hovering;

    public CircleIconButton(string glyph, string label, double size = 34, string fillKey = "BentoFill")
    {
        this.fillKey = fillKey;
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
        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, hovering ? "BentoLine" : fillKey);
        icon.Foreground = Palette.Get(this, "BentoPrimary");
    }
}

/// 灰底胶囊文字按钮。
public sealed class CapsuleButton : Button
{
    private readonly Border back = new();
    private readonly TextBlock text = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly string fillKey;
    private readonly string textKey;

    public CapsuleButton(string title, double height = 34, string fillKey = "BentoFill", string textKey = "BentoPrimary")
    {
        this.fillKey = fillKey;
        this.textKey = textKey;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        text.Text = title;
        back.Height = height;
        back.CornerRadius = new CornerRadius(height / 2);
        back.Padding = new Thickness(14, 0, 14, 0);
        back.Child = text;
        Content = back;
        AutomationProperties.SetName(this, title);
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    public string Title { get => text.Text; set { text.Text = value; AutomationProperties.SetName(this, value); } }

    private void Paint()
    {
        back.Background = fillKey == "Brand" ? Palette.Brand : Palette.Get(this, fillKey);
        text.Foreground = textKey == "OnBrand" ? (Brush)Application.Current.Resources["OnBrand"] : Palette.Get(this, textKey);
    }
}

/// 自动换行排布：一排放不下就换行。
public class WrapPanel : Panel
{
    public double Spacing { get; set; } = 6;

    protected override Size MeasureOverride(Size available)
    {
        double x = 0, y = 0, rowH = 0, maxW = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > available.Width)
            {
                y += rowH + Spacing;
                x = 0;
                rowH = 0;
            }
            x += s.Width + Spacing;
            rowH = Math.Max(rowH, s.Height);
            maxW = Math.Max(maxW, x - Spacing);
        }
        return new Size(double.IsInfinity(available.Width) ? maxW : Math.Min(maxW, available.Width), y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > final.Width)
            {
                y += rowH + Spacing;
                x = 0;
                rowH = 0;
            }
            child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + Spacing;
            rowH = Math.Max(rowH, s.Height);
        }
        return final;
    }
}
