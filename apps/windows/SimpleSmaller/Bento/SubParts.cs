using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace SimpleSmaller.Bento;

/// 独立窗口（设置、关于、配方编辑、托盘小窗、更新询问）用到的设计稿图标，路径照搬设计稿 SVG。
public static class SubIcons
{
    /// 文件夹（设置 · 输出）。
    public const string Folder = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
    /// 锁（设置 · 隐私）。
    public static readonly string Lock = LineIcon.Rect(5, 11, 14, 10, 2) + "M8 11V8a4 4 0 0 1 8 0v3";
    /// 上下箭头（弹出菜单胶囊右侧）。
    public const string UpDown = "M7 9l5-5 5 5M7 15l5 5 5-5";
    /// 六个点的拖动把手（配方列表）。
    public const string Grip = "M9 6h.01M15 6h.01M9 12h.01M15 12h.01M9 18h.01M15 18h.01";
    /// 加号（新建配方、托盘拖入区）。
    public const string Plus = "M12 5v14M5 12h14";
    /// 对勾（多选胶囊）。
    public const string Check = "M5 12l5 5 9-10";
    /// 滑杆（配方编辑的标题图标）。
    public static readonly string Sliders = "M4 8h9M17 8h3M4 16h3M11 16h9" + LineIcon.Circle(15, 8, 2) + LineIcon.Circle(9, 16, 2);
    /// 下载箭头（更新询问的标题图标）。
    public const string Download = "M12 4v11M7 10l5 5 5-5M5 20h14";

    /// 品牌标（95 × 90 画布，填充）。
    public const string Logo =
        "M37.1911 33.3908C38.2199 32.8075 39.1815 32.0025 39.9416 31.1071C41.7262 29.0083 41.617 28.3553 39.5678 26.5193C39.5426 26.4988 39.5258 26.4701 39.5006 26.4536C38.4592 25.9484 37.1995 26.1374 36.2085 25.4679C35.0327 26.1867 33.7981 26.8274 32.7106 27.6612C32.2276 28.0308 32.0429 28.7824 31.8077 29.2014C32.0051 30.7046 32.5678 31.7889 33.4286 32.7048C34.6296 33.9781 35.6794 34.241 37.1911 33.3866V33.3908Z" +
        "M91.1884 26.963C84.8896 17.6478 77.3983 9.22792 66.3208 4.89068C63.2764 3.69958 60.2781 2.30312 57.1246 1.494C53.8954 0.664336 50.41 -0.300865 47.2019 0.0893223C35.6205 1.494 25.4626 6.66912 16.5142 13.6555C6.57048 21.4182 0.175128 31.4275 0.128937 45.31C-0.895665 54.3048 4.27772 64.5236 12.3906 74.3276C25.6894 90.391 42.2636 93.915 61.2607 85.2611C75.3742 78.8292 84.8014 67.7191 91.1548 54.3007C95.3162 45.5195 97.1512 35.7771 91.1884 26.9589V26.963ZM54.2019 30.0845C54.5 29.0659 54.6302 27.998 54.8738 26.9589C54.92 26.7699 55.1425 26.5317 55.3315 26.4824C55.8102 26.3592 56.3141 26.2812 56.8054 26.273C58.5187 26.2524 60.2361 26.2483 61.9494 26.2812C63.5703 26.314 65.1954 26.3756 66.8163 26.4783C68.4372 26.581 68.5883 26.774 68.5337 28.7127C68.4834 29.1768 68.3448 29.9982 68.3112 30.8238C68.2776 31.5959 67.8409 31.715 67.19 31.6986C64.4185 31.637 61.6429 31.6452 58.8756 31.5343C57.5319 31.4809 56.1545 31.8834 54.8318 31.255C54.2691 30.9881 54.0045 30.7622 54.2061 30.0763L54.2019 30.0845ZM26.6594 29.6409C26.6678 26.7289 28.0116 24.6301 30.0986 22.9995C31.9756 21.5332 34.1088 20.4489 36.5359 20.1532C37.2078 20.071 37.9384 20.2723 38.6187 20.4489C39.278 20.6173 39.9079 20.913 40.5462 21.1636C42.8683 22.0713 44.443 23.8415 45.7615 25.7924C47.0129 27.6407 46.6266 29.6861 45.7531 31.5261C44.4178 34.3396 42.4442 36.6725 39.4795 38.0196C35.4945 39.835 32.0176 39.0957 29.2251 35.7278C27.5245 33.6701 26.4369 31.5261 26.651 29.6409H26.6594ZM72.2164 45.9466C72.1661 49.0681 72.1493 52.1937 71.9855 55.307C71.8049 58.6955 71.49 62.0798 71.1709 65.4601C71.1457 65.7394 70.6459 66.1748 70.3436 66.1953C69.2224 66.2692 68.0928 66.1542 66.9675 66.1953C66.1444 66.2281 65.9849 65.7764 66.0268 65.1603C66.1024 64.0595 66.2326 62.967 66.325 61.8663C66.4635 60.2193 66.5853 58.5723 66.7155 56.9253C66.7743 56.1695 66.4636 55.8738 65.6615 55.9806C64.5739 56.1243 63.4695 56.1901 62.3777 56.2886C62.3819 56.3502 62.3903 56.4119 62.3945 56.4735C59.7364 56.7117 57.0868 57.0444 54.4245 57.1676C51.0483 57.3237 47.6638 57.2744 44.2876 57.3935C42.4064 57.4592 40.5293 57.7138 38.6481 57.8494C36.7711 57.9849 34.8856 58.0424 33.0086 58.2026C32.1142 58.2806 31.2155 58.4778 30.3505 58.7119C29.6492 58.9008 27.6588 58.3956 27.5706 57.7344C27.4153 56.6172 27.2641 55.2906 27.7428 54.3623C28.0829 53.7011 29.4267 53.2986 30.3631 53.2C34.5707 52.7605 38.7951 52.436 43.0195 52.1485C45.6733 51.9678 48.344 51.9596 50.9979 51.8035C55.9488 51.5078 60.8954 51.1423 65.8505 50.8507C66.8163 50.7932 67.106 50.4317 66.9969 49.5322C66.9171 48.8669 67.0556 48.1768 66.9675 47.5156C66.8415 46.5586 66.6441 45.6057 66.3922 44.6775C66.2074 44.0039 66.1234 43.5316 67.0011 43.2646C68.1012 42.9319 69.1468 42.4349 70.2386 42.0817C70.4696 42.0078 70.8727 42.1926 71.0911 42.3774C72.1955 43.3303 72.2459 44.6446 72.2249 45.9425L72.2164 45.9466Z";
}

/// 品牌标（填充图形）：颜色是色板键或 OnBrand / Brand。装饰图，旁白不读。
public sealed class LogoMark : UserControl
{
    private readonly Path path = new();
    private string colorKey;

    public LogoMark(double width, string colorKey = "BentoPrimary")
    {
        this.colorKey = colorKey;
        Width = width;
        Height = width * 90 / 95;
        IsTabStop = false;
        path.Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), "F1" + SubIcons.Logo);
        var canvas = new Grid { Width = 95, Height = 90 };
        canvas.Children.Add(path);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    public string ColorKey { get => colorKey; set { colorKey = value; Paint(); } }

    private void Paint() => path.Fill = colorKey switch
    {
        "OnBrand" => (Brush)Application.Current.Resources["OnBrand"],
        "Brand" => Palette.Brand,
        _ => Palette.Get(this, colorKey),
    };
}

/// 便当盒开关（设计稿：36 × 22 胶囊、16 圆钮；开着品牌红、关着线色）。
/// 是 ToggleButton：空格 / 回车切换，旁白读「切换按钮，开 / 关」，名字是这一行的文字。
public sealed class BentoSwitch : ToggleButton
{
    private static Style? style;
    private readonly Border track = new();
    private readonly Border knob = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly double w, h, k;
    private bool placed;

    public event Action<bool>? Changed;

    public BentoSwitch(string label, bool on, double width = 36, double height = 22, double knobSize = 16)
    {
        Style = style ??= (Style)XamlReader.Load(Xaml);
        w = width;
        h = height;
        k = knobSize;
        track.Width = w;
        track.Height = h;
        track.CornerRadius = new CornerRadius(h / 2);
        knob.Width = knob.Height = k;
        knob.CornerRadius = new CornerRadius(k / 2);
        knob.Margin = new Thickness((h - k) / 2);
        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(track);
        grid.Children.Add(knob);
        Content = grid;
        IsChecked = on;
        AutomationProperties.SetName(this, label);
        Motion.SetPress(this, true);
        Checked += (_, _) => { Paint(); Changed?.Invoke(true); };
        Unchecked += (_, _) => { Paint(); Changed?.Invoke(false); };
        Loaded += (_, _) => { Paint(); placed = true; };
        ActualThemeChanged += (_, _) => Paint();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
    }

    private void Paint()
    {
        var on = IsChecked == true;
        track.Background = on ? Palette.Brand : Palette.Get(this, "BentoLine");
        knob.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
        // 关着时圆钮带一点投影（设计稿 0 1px 2px rgba(0,0,0,.2)）。
        knob.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(on ? (byte)0 : (byte)0x22, 0, 0, 0));
        knob.BorderThickness = new Thickness(on ? 0 : 0.5);
        var to = new Vector3(on ? (float)(w - h) : 0, 0, 0);
        if (placed && Motion.Enabled) Motion.SpringOffset(knob, to, 0.75f, 45);
        else Motion.SetTranslation(knob, to);
    }

    private const string Xaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ToggleButton">
  <Setter Property="Background" Value="Transparent" />
  <Setter Property="BorderThickness" Value="0" />
  <Setter Property="Padding" Value="0" />
  <Setter Property="MinWidth" Value="0" />
  <Setter Property="MinHeight" Value="0" />
  <Setter Property="UseSystemFocusVisuals" Value="True" />
  <Setter Property="FocusVisualMargin" Value="-3" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ToggleButton">
        <ContentPresenter Content="{TemplateBinding Content}" Background="Transparent"
                          AutomationProperties.AccessibilityView="Raw" />
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";
}

/// 弹出菜单胶囊（设计稿：白底 28 高、12 号字、右侧上下箭头；配方编辑里是 32 高、细边、文字后「›」）。
/// 点开是系统菜单：单选项打勾，下面可以再加别的操作。
public sealed class MenuCapsule : Button
{
    private readonly Border back = new();
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly LineIcon? chevron;
    private readonly string fillKey;
    private readonly bool bordered;
    private readonly string label;
    private readonly bool arrowSuffix;
    private string value = "";

    public MenuCapsule(string label, string value, double height = 28, string fillKey = "BentoCard", bool bordered = false, bool arrowSuffix = false)
    {
        this.label = label;
        this.fillKey = fillKey;
        this.bordered = bordered;
        this.arrowSuffix = arrowSuffix;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        back.Height = height;
        back.CornerRadius = new CornerRadius(height / 2);
        back.Padding = new Thickness(height >= 32 ? 12 : 10, 0, height >= 32 ? 12 : 10, 0);
        back.BorderThickness = new Thickness(bordered ? 1 : 0);
        text.FontSize = 12;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(text);
        if (!arrowSuffix)
        {
            chevron = new LineIcon(SubIcons.UpDown, 9, 2.6, "BentoSecondary") { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(chevron);
        }
        back.Child = row;
        Content = back;
        Value = value;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    /// 胶囊上显示的当前值。
    public string Value
    {
        get => value;
        set
        {
            this.value = value;
            text.Text = arrowSuffix ? value + " ›" : value;
            AutomationProperties.SetName(this, $"{label}：{value}");
        }
    }

    /// 单选菜单：选项（值、显示文字），选中后回调并更新胶囊文字。extra 是追加在分隔线下面的操作。
    public void Options<T>(IEnumerable<(T Value, string Title)> options, T selected, Action<T> changed, params (string Title, Action Run)[] extra)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var group = Guid.NewGuid().ToString();
        foreach (var (v, title) in options)
        {
            var on = EqualityComparer<T>.Default.Equals(v, selected);
            var item = new RadioMenuFlyoutItem { Text = title, GroupName = group, IsChecked = on };
            if (on) Value = title;
            item.Click += (_, _) => { Value = title; changed(v); };
            menu.Items.Add(item);
        }
        if (extra.Length > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var (title, run) in extra)
            {
                var item = new MenuFlyoutItem { Text = title };
                item.Click += (_, _) => run();
                menu.Items.Add(item);
            }
        }
        Flyout = menu;
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, fillKey);
        back.BorderBrush = bordered ? Palette.Get(this, "BentoLine") : null;
        text.Foreground = Palette.Get(this, "BentoPrimary");
    }
}

/// 灰底文字胶囊按钮（设计稿：30 / 36 高，12 / 13 号字），可带左侧图标。和 CapsuleButton 一样，只是字号、内边距可调。
public sealed class TextCapsule : Button
{
    private readonly Border back = new();
    private readonly TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly string fillKey, textKey;
    private readonly LineIcon? icon;

    public TextCapsule(string title, double height, double fontSize, double padding, string fillKey = "BentoFill", string textKey = "BentoPrimary", string? iconPath = null, bool semibold = false)
    {
        this.fillKey = fillKey;
        this.textKey = textKey;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        text.Text = title;
        text.FontSize = fontSize;
        if (semibold) text.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        back.Height = height;
        back.CornerRadius = new CornerRadius(height / 2);
        back.Padding = new Thickness(padding, 0, padding, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (iconPath is not null)
        {
            icon = new LineIcon(iconPath, 14, 2.4, textKey) { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
        }
        row.Children.Add(text);
        back.Child = row;
        Content = back;
        AutomationProperties.SetName(this, title);
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
        IsEnabledChanged += (_, _) => back.Opacity = IsEnabled ? 1 : 0.4;
    }

    public string Title { get => text.Text; set { text.Text = value; AutomationProperties.SetName(this, value); } }

    private void Paint()
    {
        back.Background = Themed.Resolve(this, fillKey);
        text.Foreground = textKey == "OnBrand" ? (Brush)Application.Current.Resources["OnBrand"] : Themed.Resolve(this, textKey);
    }
}
