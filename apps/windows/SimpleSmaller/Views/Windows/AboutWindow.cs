using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Sub;

/// 关于（设计稿 09）：五周年只在这里写一句。
internal sealed class AboutWindow : SubWindow
{
    public AboutWindow() : base("关于简单小一点", 340, 500, showTitle: false)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(20, 0, 20, 22) };
        var icon = new Image { Width = 96, Height = 96, Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon-256.png"))) };
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        s.Children.Add(icon);
        s.Children.Add(Center(FormTheme.Text("简单小一点", 20, semibold: true), 12));
        var en = FormTheme.Text("SIMPLE SMALLER", 11, "BentoSecondary");
        en.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        en.CharacterSpacing = 110;
        s.Children.Add(Center(en, 2));
        var version = FormTheme.Text(Version, 11, "BentoSecondary");
        version.Opacity = 0.75;
        s.Children.Add(Center(version, 6));
        var story = FormTheme.Text("IT-SIMPLE 成立五周年，\n做了这个小工具，送给一路同行的朋友。", 13);
        story.TextAlignment = TextAlignment.Center;
        story.TextWrapping = TextWrapping.Wrap;
        story.LineHeight = 21;
        s.Children.Add(Center(story, 18));

        var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        linkRow.Children.Add(new Image { Width = 16, Height = 15, Source = new SvgImageSource(new Uri("ms-appx:///Assets/logo-mark.svg")) });
        linkRow.Children.Add(new TextBlock { Text = "it-simple.cn", FontSize = 12 });
        var link = new HyperlinkButton { Content = linkRow, NavigateUri = new Uri("https://www.it-simple.cn/"), Padding = new Thickness(6, 2, 6, 2) };
        AutomationProperties.SetName(link, "it-simple.cn");
        s.Children.Add(Center(link, 12));
        var licenses = new HyperlinkButton { Content = new TextBlock { Text = "开源组件与许可证", FontSize = 12 }, Padding = new Thickness(6, 2, 6, 2) };
        AutomationProperties.SetName(licenses, "开源组件与许可证");
        licenses.Click += (_, _) => App.OpenLicenses();
        s.Children.Add(Center(licenses, 4));
        var font = FormTheme.Text("界面字体使用 MiSans（小米），按其许可协议使用", 11, "BentoSecondary");
        font.Opacity = 0.75;
        font.TextWrapping = TextWrapping.Wrap;
        font.TextAlignment = TextAlignment.Center;
        s.Children.Add(Center(font, 12));
        var copyright = FormTheme.Text("© 2021–2026 IT-SIMPLE", 11, "BentoSecondary");
        copyright.Opacity = 0.75;
        s.Children.Add(Center(copyright, 6));
        Body.Children.Add(s);
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
