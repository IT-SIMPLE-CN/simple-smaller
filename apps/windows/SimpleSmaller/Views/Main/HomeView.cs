using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;

namespace SimpleSmaller.Views.Main;

/// 首页（设计稿 V2.1 · 01）：打开应用、还没选类型时显示。
/// 文件拖到哪张卡就进哪个功能；拖到窗口别处按类型自动分流。
internal sealed class HomeView : UserControl
{
    private readonly Grid root = new() { Padding = new Thickness(40, 0, 40, 0) };
    private readonly TextBlock headline = new() { Text = "拖进来，选一下", FontSize = 46, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = -20 };
    private readonly FontIcon headlineArrow = new() { Glyph = "", FontSize = 30 };
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private readonly List<HomeCard> cardViews = new();
    private readonly TextBlock note = new() { Text = "把文件拖到卡片上就进对应的功能；拖到窗口别处按类型自动分流。", FontSize = 12, Margin = new Thickness(176, 28, 0, 0) };
    private readonly TextBlock local = new() { Text = "图片、视频、PDF\n都在本机处理，不上传。", FontSize = 12, LineHeight = 18 };
    private readonly StackPanel column = new();

    public HomeView()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(headline);
        headlineArrow.RenderTransform = new RotateTransform { Angle = 45, CenterX = 15, CenterY = 15 };
        AutomationProperties.SetAccessibilityView(headlineArrow, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        head.Children.Add(headlineArrow);
        AutomationProperties.SetHeadingLevel(headline, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);

        var side = new Grid { Width = 160, Height = 392 };
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        side.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var about = AboutButton();
        side.Children.Add(about);
        Grid.SetRow(local, 2);
        side.Children.Add(local);
        cards.Children.Add(side);
        var i = 0;
        foreach (var kind in Enum.GetValues<HomeCard.Kind>())
        {
            var card = new HomeCard(kind);
            cardViews.Add(card);
            cards.Children.Add(card);
            var index = i++;
            card.Loaded += (_, _) => Motion.Appear(card, index);
        }

        column.Children.Add(head);
        column.Children.Add(cards);
        column.Children.Add(note);
        root.Children.Add(column);
        Content = root;
        SizeChanged += (_, _) => Layout();
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); Layout(); };
    }

    private Button AboutButton()
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "关于", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(new FontIcon { Glyph = "", FontSize = 11 });
        var border = new Border { Height = 38, Padding = new Thickness(16, 0, 12, 0), CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1.2), Child = text };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = border, HorizontalAlignment = HorizontalAlignment.Left };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, "关于");
        b.Click += (_, _) => App.OpenAbout();
        b.Loaded += (_, _) => border.BorderBrush = Palette.Get(b, "BentoPrimary");
        b.ActualThemeChanged += (_, _) => border.BorderBrush = Palette.Get(b, "BentoPrimary");
        return b;
    }

    private void Layout()
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0)
        {
            return;
        }
        var cardW = Math.Min(260, Math.Max(200, (w - 40 - 176 - 3 * 16 - 40) / 4));
        foreach (var c in cardViews)
        {
            c.Width = cardW;
            c.Height = 392;
        }
        column.Margin = new Thickness(0, Math.Max(20, (h - 640) / 2), 0, 0);
        cards.Margin = new Thickness(0, Math.Min(56, Math.Max(24, h - 600)), 0, 0);
    }

    private void Paint()
    {
        headline.Foreground = Palette.Get(this, "BentoPrimary");
        headlineArrow.Foreground = Palette.Get(this, "BentoPrimary");
        note.Foreground = Palette.Get(this, "BentoSecondary");
        local.Foreground = Palette.Get(this, "BentoSecondary");
    }

    /// 首页标题栏正中的品牌：标 + 简单小一点 + SIMPLE SMALLER。
    public static UIElement Brand()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/logo-mark.svg")), Width = 22, Height = 21 });
        var name = new TextBlock { Text = "简单小一点", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var en = new TextBlock { Text = "SIMPLE SMALLER", FontSize = 11, CharacterSpacing = 150, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(name);
        row.Children.Add(en);
        void paint()
        {
            name.Foreground = Palette.Get(row, "BentoPrimary");
            en.Foreground = Palette.Get(row, "BentoSecondary");
        }
        row.Loaded += (_, _) => paint();
        row.ActualThemeChanged += (_, _) => paint();
        AutomationProperties.SetName(row, "简单小一点");
        return row;
    }
}

/// 首页的一张功能卡：插图、标题、说明、箭头；文件拖到卡上就进这个功能。
internal sealed class HomeCard : Button
{
    public enum Kind { Image, Video, Pdf, Ring }

    private readonly Kind kind;
    private readonly Border back = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(14) };
    private readonly Border notch = new() { Padding = new Thickness(6, 0, 0, 6), CornerRadius = new CornerRadius(0, 0, 0, 16), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border notchIcon = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17) };
    private readonly FontIcon glyph = new() { FontSize = 14 };
    private readonly TextBlock title = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) };
    private readonly TextBlock subtitle = new() { FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Rectangle rule = new() { Width = 18, Height = 1.5, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock text = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border arrow = new() { Width = 36, Height = 36, CornerRadius = new CornerRadius(18) };
    private readonly FontIcon arrowIcon = new() { Glyph = "", FontSize = 15 };
    private readonly TextBlock formats = new() { FontSize = 10, CharacterSpacing = 40, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private bool targeted;
    private bool hovering;

    public HomeCard(Kind kind)
    {
        this.kind = kind;
        Style = (Style)Application.Current.Resources["BentoBareButton"];
        Motion.SetPress(this, true);
        title.Text = kind switch { Kind.Image => "图片", Kind.Video => "视频", Kind.Pdf => "PDF", _ => "资源管理器圆环" };
        subtitle.Text = kind switch { Kind.Image => "压缩 · 裁切", Kind.Video => "压缩 · 剪辑 · 序列帧", Kind.Pdf => "压缩 · 合并 · 拆分", _ => "不开窗口也能压" };
        text.Text = kind switch
        {
            Kind.Image => "WebP、AVIF、JPG 一键变小，一次出多档尺寸；按比例批量裁切。",
            Kind.Video => "按体积压到 25 MB，裁成 9:16，或者拆成一张张序列帧。",
            Kind.Pdf => "邮件附件、屏幕阅读、打印保真三档；合并、拆分、转图片。",
            _ => "在资源管理器里按住 Alt 点文件，圆环里点一下就好。第一次用要在设置里开启。",
        };
        formats.Text = kind switch { Kind.Image => "JPG · PNG · HEIC", Kind.Video => "MP4 · MOV · GIF", Kind.Pdf => "PDF", _ => "Windows" };
        glyph.Glyph = kind switch { Kind.Image => "", Kind.Video => "", Kind.Pdf => "", _ => "" };
        var art = kind switch { Kind.Image => "home-image", Kind.Video => "home-video", Kind.Pdf => "home-pdf", _ => "home-ring" };

        var picture = new Grid();
        var artBox = new Border { CornerRadius = new CornerRadius(14) };
        artBox.Child = new Image { Source = new SvgImageSource(new Uri($"ms-appx:///Assets/{art}.svg")), Stretch = Stretch.UniformToFill };
        picture.Children.Add(artBox);
        notchIcon.Child = glyph;
        notch.Child = notchIcon;
        picture.Children.Add(notch);
        picture.SizeChanged += (_, e) => picture.Height = e.NewSize.Width * 3 / 4;

        var bottom = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
        arrow.Child = arrowIcon;
        arrow.HorizontalAlignment = HorizontalAlignment.Left;
        bottom.Children.Add(arrow);
        bottom.Children.Add(formats);

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var texts = new StackPanel();
        texts.Children.Add(picture);
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        texts.Children.Add(rule);
        texts.Children.Add(text);
        stack.Children.Add(texts);
        Grid.SetRow(bottom, 2);
        stack.Children.Add(bottom);
        back.Child = stack;
        Content = back;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        AutomationProperties.SetName(this, $"{title.Text}：{subtitle.Text}");
        AutomationProperties.SetHelpText(this, text.Text);
        Click += (_, _) => Open();
        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();

        AllowDrop = true;
        DragEnter += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                targeted = true;
                Paint();
            }
        };
        DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = kind == Kind.Ring ? "加进来" : $"用{title.Text}处理";
            }
        };
        DragLeave += (_, _) => { targeted = false; Paint(); };
        Drop += async (_, e) =>
        {
            e.Handled = true;
            targeted = false;
            Paint();
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            AppModel.Shared.Add(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)), Type);
        };
    }

    private MediaType? Type => kind switch { Kind.Image => MediaType.Image, Kind.Video => MediaType.Video, Kind.Pdf => MediaType.Pdf, _ => null };

    private void Open()
    {
        if (Type is { } t)
        {
            AppModel.Shared.SelectedType = t;
        }
        else
        {
            App.OpenSettings("自动");
        }
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, "BentoCard");
        back.BorderBrush = Palette.Brand;
        back.BorderThickness = new Thickness(targeted ? 2.5 : 0);
        notch.Background = Palette.Get(this, "BentoCard");
        notchIcon.Background = Palette.Get(this, "BentoFill");
        glyph.Foreground = Palette.Get(this, "BentoPrimary");
        title.Foreground = Palette.Get(this, "BentoPrimary");
        subtitle.Foreground = Palette.Get(this, "BentoSecondary");
        rule.Fill = Palette.Get(this, "BentoPrimary");
        text.Foreground = Palette.Get(this, "BentoPrimary");
        text.Opacity = 0.78;
        formats.Foreground = Palette.Get(this, "BentoSecondary");
        var red = kind == Kind.Image || targeted;
        arrow.Background = red ? Palette.Brand : Palette.Get(this, "BentoFill");
        arrowIcon.Foreground = red ? (Brush)Application.Current.Resources["OnBrand"] : Palette.Get(this, "BentoPrimary");
        back.Translation = new System.Numerics.Vector3(0, hovering ? -2 : 0, hovering ? 16 : 2);
        back.Shadow ??= new ThemeShadow();
    }
}
