using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Sub;

/// 关于（设计稿 BentoAbout）：白底 420 宽，点阵背景上的红色应用图标（带红色光晕）、中英文名、版本胶囊、
/// 四张功能小插图、五周年一句话，墨黑「it-simple.cn」与灰底「开源组件与许可证」两个整宽按钮，最底下版权。
/// mac 的红绿灯换成 Windows 右上角的系统关闭按钮。
internal sealed class AboutWindow : SubWindow
{
    public AboutWindow() : base("关于简单小一点", 420, 556, "BentoCard", showTitle: false)
    {
        OverlayTitle();
        var page = new Grid();
        // 点阵：设计稿 400 × 300 的 SVG，放在左 10、上 -60。
        var dots = new Image { Width = 400, Height = 300, Source = new SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "about-dots.svg"))), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10, -60, 0, 0) };
        AutomationProperties.SetAccessibilityView(dots, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        FormTheme.On(dots, () => dots.Opacity = dots.ActualTheme == ElementTheme.Dark ? 0.12 : 1);
        page.Children.Add(dots);

        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(28, 18 + 12, 28, 26) };
        s.Children.Add(Center(AppIcon(), 26));
        var name = FormTheme.Text("简单小一点", 22);
        name.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        AutomationProperties.SetHeadingLevel(name, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        s.Children.Add(Center(name, 18));
        var en = FormTheme.Text("SIMPLE SMALLER", 12, "BentoSecondary");
        en.CharacterSpacing = 160;
        s.Children.Add(Center(en, 0));
        var versionText = FormTheme.Text(Version, 11, "BentoSecondary");
        versionText.VerticalAlignment = VerticalAlignment.Center;
        var version = new Border { Height = 24, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(12), Child = versionText };
        FormTheme.On(version, () => version.Background = Palette.Get(version, "BentoFill"));
        s.Children.Add(Center(version, 8));

        var tiles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var n in new[] { "about-image", "about-video", "about-pdf", "about-ring" })
        {
            var img = new Image { Width = 72, Height = 54, Stretch = Stretch.Fill, Source = new SvgImageSource(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", n + ".svg"))) };
            var tile = new Grid { Width = 72, Height = 54, Children = { img } };
            Views.Common.RoundClip.Apply(tile, 12);
            AutomationProperties.SetAccessibilityView(tile, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            tiles.Children.Add(tile);
        }
        s.Children.Add(Center(tiles, 20));

        var story = new TextBlock { Text = "IT-SIMPLE 成立五周年，\n做了这个小工具，送给一路同行的朋友。", FontSize = 13, LineHeight = 22.75, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        // 设计稿 #3E4248：比主文字浅一档。
        FormTheme.On(story, () => story.Foreground = new SolidColorBrush(story.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 0xC9, 0xCC, 0xD1) : Windows.UI.Color.FromArgb(255, 0x3E, 0x42, 0x48)));
        s.Children.Add(Center(story, 18));

        var link = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 18, 0, 0) };
        Motion.SetPress(link, true);
        var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var linkLogo = new LogoMark(18, "BentoOnInk") { VerticalAlignment = VerticalAlignment.Center };
        var linkText = new TextBlock { Text = "it-simple.cn", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        linkRow.Children.Add(linkLogo);
        linkRow.Children.Add(linkText);
        var linkBack = new Border { Height = 44, CornerRadius = new CornerRadius(22), Child = linkRow };
        link.Content = linkBack;
        FormTheme.On(link, () => { linkBack.Background = Palette.Get(link, "BentoInk"); linkText.Foreground = Palette.Get(link, "BentoOnInk"); });
        link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.it-simple.cn/"));
        AutomationProperties.SetName(link, "it-simple.cn");
        s.Children.Add(link);
        var licenses = new TextCapsule("开源组件与许可证", 44, 13, 16) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 8, 0, 0) };
        licenses.Click += (_, _) => App.OpenLicenses();
        s.Children.Add(licenses);
        var copyright = FormTheme.Text("© 2021–2026 IT-SIMPLE · AGPLv3", 11, "BentoSecondary");
        s.Children.Add(Center(copyright, 16));
        page.Children.Add(s);
        Body.Children.Add(page);
    }

    /// 设计稿的应用图标：96 见方、圆角 26 的品牌红底，白色品牌标，下方红色光晕。
    private static FrameworkElement AppIcon()
    {
        var grid = new Grid { Width = 96, Height = 96 };
        var glow = new Canvas { Width = 96, Height = 96 };
        var back = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 96, Height = 96, RadiusX = 26, RadiusY = 26, Fill = Palette.Brand };
        ShapeShadow.Attach(back, glow, Windows.UI.Color.FromArgb(0x59, 0xEF, 0x39, 0x22), 28, 12);
        grid.Children.Add(glow);
        grid.Children.Add(back);
        grid.Children.Add(new LogoMark(62, "OnBrand") { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(grid, "应用图标");
        return grid;
    }

    private static FrameworkElement Center(FrameworkElement e, double top)
    {
        e.HorizontalAlignment = HorizontalAlignment.Center;
        e.Margin = new Thickness(0, top, 0, 0);
        return e;
    }

    /// 「版本 1.0.0（1）」。
    private static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 1);
            return $"版本 {v.Major}.{v.Minor}.{Math.Max(0, v.Build)}（{Math.Max(1, v.Revision)}）";
        }
    }
}

/// 开源组件与许可证（Assets/Licenses.txt）。
internal sealed class LicensesWindow : SubWindow
{
    public LicensesWindow() : base("开源组件与许可证", 600, 500, "BentoCard")
    {
        string text;
        try { text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Licenses.txt")); }
        catch { text = ""; }
        var body = new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(16),
        };
        FormTheme.On(body, () => body.Foreground = Palette.Get(body, "BentoPrimary"));
        var scroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Auto };
        var done = new BrandButton("完成", 34, 13, 20) { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        done.Click += (_, _) => Close();
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(scroll);
        var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top };
        FormTheme.On(line, () => line.Background = Palette.Get(line, "BentoLine"));
        Grid.SetRow(line, 1);
        Grid.SetRow(done, 1);
        g.Children.Add(line);
        g.Children.Add(done);
        Body.Children.Add(g);
    }
}
