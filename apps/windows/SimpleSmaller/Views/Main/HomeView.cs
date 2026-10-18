using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace SimpleSmaller.Views.Main;

/// 首页（设计稿 V2.2 H1）：打开应用、还没选类型时显示。
/// 卡片标题下一排小标签，点了直接进那个功能。拖文件进来：指针停在能接这些文件的卡上时，卡片分成几格，
/// 放到哪格进哪个（这一类进那个功能，其余类型照旧分流）；拖到卡片外、窗口别处按类型自动分流。
internal sealed class HomeView : UserControl
{
    private readonly Grid root = new() { Padding = new Thickness(40, 0, 40, 0) };
    private readonly TextBlock headline = new() { Text = "拖进来，选一下", FontSize = 46, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, CharacterSpacing = -20 };
    private readonly LineIcon headlineArrow = new(Icons.TurnDownRight, 34, 2);
    private readonly StackPanel cards = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private readonly List<HomeCard> cardViews = new();
    private readonly TextBlock note = new() { Text = "点标签直接进；拖文件到卡片上，卡片会分成几格，放到哪格进哪个。", FontSize = 12, Margin = new Thickness(176, 28, 0, 0) };
    private readonly TextBlock local = new() { Text = "图片、视频、PDF\n都在本机处理，不上传。", FontSize = 12, LineHeight = 18 };
    private readonly StackPanel column = new();
    private readonly Microsoft.UI.Xaml.Shapes.Path dots = new() { Width = 444, Height = 444, Data = DotGrid() };

    public HomeView()
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        head.Children.Add(headline);
        headlineArrow.VerticalAlignment = VerticalAlignment.Center;
        headlineArrow.Margin = new Thickness(0, 8, 0, 0);
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
        // 右上角渐隐点阵（设计稿 V2.1 · 01），装饰，不响应点击、旁白不读。
        dots.HorizontalAlignment = HorizontalAlignment.Right;
        dots.VerticalAlignment = VerticalAlignment.Top;
        dots.Margin = new Thickness(0, 27, -31, 0);
        dots.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(dots, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.Children.Add(dots);
        root.Children.Add(column);
        Content = root;
        SizeChanged += (_, _) => Layout();
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => { Paint(); Layout(); };
        HookDrag();
    }

    // ---------- 拖文件进来：卡片分格 ----------

    /// 这一次拖进来的东西：各文件的类型、有没有文件夹（文件夹按落点那一类处理）。读出来之前不分格。
    private sealed record DragInfo(HashSet<MediaType> Types, bool HasFolder);

    private DragInfo? drag;
    private bool dragLoading;

    private void HookDrag()
    {
        AllowDrop = true;
        DragEnter += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            if (dragLoading) return;
            dragLoading = true;
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                drag = Describe(items);
            }
            catch
            {
                drag = new DragInfo([], false);
            }
            finally
            {
                dragLoading = false;
                deferral.Complete();
            }
        };
        DragOver += (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = DataPackageOperation.Copy;
            var (card, cell) = Hover(e.GetPosition(this));
            e.DragUIOverride.Caption = card is not null && cell >= 0 ? $"松手 → {card.Cells[cell]}" : "加进来";
            e.Handled = true;
        };
        // 拖离首页或在首页上取消拖动（Esc、松手在别处）：卡片收回；再拖进来时重新读。
        DragLeave += (_, _) => EndDrag();
        Drop += async (_, e) =>
        {
            e.Handled = true;
            var (card, cell) = Hover(e.GetPosition(this));
            EndDrag();
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (card?.Type is { } type && cell >= 0) AppModel.Shared.AddInto(paths, type, card.Cells[cell]);
            else AppModel.Shared.Add(paths, null);
        };
    }

    private static DragInfo Describe(IReadOnlyList<IStorageItem> items)
    {
        var types = new HashSet<MediaType>();
        var folder = false;
        foreach (var i in items)
        {
            if (i is IStorageFolder) folder = true;
            else if (MediaTypes.Of(i.Path) is { } t) types.Add(t);
        }
        return new DragInfo(types, folder);
    }

    /// 指针在首页上的位置 → 落在哪张卡的哪一格；同时让那张卡分格、别的卡收回。
    /// 接不了这批文件的卡（里面没有一个是它的类型）不分格。
    private (HomeCard? Card, int Cell) Hover(Point p)
    {
        HomeCard? hit = null;
        var cell = -1;
        foreach (var c in cardViews)
        {
            var at = TransformToVisual(c).TransformPoint(p);
            if (hit is null && drag is { } d && c.Accepts(d.Types, d.HasFolder) && HomeCard.Inside(at, c.ActualWidth, c.ActualHeight))
            {
                hit = c;
                var enabled = c.EnabledCells(d.Types, d.HasFolder);
                cell = HomeCard.CellAt(at.Y, c.ActualHeight, c.Cells.Count);
                // 落在不可放的格子上：卡照样分格，但这一下不算放进格子（松手按类型分流）。
                if (cell >= 0 && !enabled[cell]) cell = -1;
                c.ShowSplit(cell, enabled);
            }
            else
            {
                c.HideSplit();
            }
        }
        return (hit, cell);
    }

    private void EndDrag()
    {
        drag = null;
        foreach (var c in cardViews) c.HideSplit();
    }

    private Button AboutButton()
    {
        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "关于", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(new LineIcon(Icons.ArrowUpRight, 15, 2) { VerticalAlignment = VerticalAlignment.Center });
        var border = new Border { Height = 38, Padding = new Thickness(16, 0, 12, 0), CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1.2), Child = text };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = border, HorizontalAlignment = HorizontalAlignment.Left };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, "关于");
        b.Click += (_, _) => App.OpenAbout();
        b.Loaded += (_, _) => border.BorderBrush = Palette.Get(b, "BentoPrimary");
        b.ActualThemeChanged += (_, _) => border.BorderBrush = Palette.Get(b, "BentoPrimary");
        return b;
    }

    /// 设计稿右上角的点阵：41 × 41 格、间距 11，圆点半径从中心向外线性变小（照画布 SVG 拟合），小于 0.5 不画。
    private static Geometry DotGrid()
    {
        var group = new GeometryGroup();
        for (var i = 0; i < 41; i++)
        {
            for (var j = 0; j < 41; j++)
            {
                double x = 2 + i * 11, y = 2 + j * 11;
                var d = Math.Sqrt((x - 222) * (x - 222) + (y - 222) * (y - 222));
                var r = Math.Round((2.745 - 0.0105 * d) * 10) / 10;
                if (r >= 0.5) group.Children.Add(new EllipseGeometry { Center = new Windows.Foundation.Point(x, y), RadiusX = r, RadiusY = r });
            }
        }
        return group;
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
        // 一排卡同高：默认 392，哪张放不下（视频卡多了「实况」标签换行）就一起加高。
        var cardH = Math.Max(392, cardViews.Max(c => c.NeededHeight(cardW)));
        foreach (var c in cardViews)
        {
            c.Width = cardW;
            c.Height = Math.Ceiling(cardH);
        }
        column.Margin = new Thickness(0, Math.Max(20, (h - 640) / 2), 0, 0);
        cards.Margin = new Thickness(0, Math.Min(56, Math.Max(24, h - 600)), 0, 0);
    }

    private void Paint()
    {
        headline.Foreground = Palette.Get(this, "BentoPrimary");
        dots.Fill = new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 0x2C, 0x2E, 0x33) : Windows.UI.Color.FromArgb(255, 0xD5, 0xD8, 0xDC));
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

/// 首页的一张功能卡（设计稿 V2.2 H1）：插图、标题、一排小标签（点了直接进那个功能）、说明、箭头（进第一个功能）。
/// 拖文件进来、指针停在卡上时分成几格（「放到哪一格？」），悬停的格子品牌红底。
/// 资源管理器圆环卡没有标签、不分格，整张卡点了打开设置。
internal sealed class HomeCard : UserControl
{
    public enum Kind { Image, Video, Pdf, Ring }

    // 分格的版式（设计稿：内边距 12、「放到哪一格？」一行、格间距 8），落点判断按它算。
    private const double SplitPad = 12;
    private const double SplitLabel = 20;
    private const double SplitGap = 8;

    private readonly Kind kind;
    private readonly Grid root = new();
    private readonly Border back = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(14) };
    private readonly Border notch = new() { Padding = new Thickness(6, 0, 0, 6), CornerRadius = new CornerRadius(0, 0, 0, 16), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
    private readonly Border notchIcon = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(17) };
    private readonly LineIcon glyph;
    private readonly TextBlock title = new() { FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 0) };
    private readonly TextBlock subtitle = new() { FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel tags = new() { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
    private readonly List<(Border Back, LineIcon Icon, TextBlock Text)> tagParts = [];
    private readonly Rectangle rule = new() { Width = 18, Height = 1.5, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock text = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border arrow = new() { Width = 36, Height = 36, CornerRadius = new CornerRadius(18) };
    private readonly LineIcon arrowIcon = new(Icons.ArrowUpRight, 16, 2);
    private readonly TextBlock formats = new() { FontSize = 10, CharacterSpacing = 40, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    // 分格层盖在卡上，品牌红 3 的描边画在卡外（设计稿 box-shadow: 0 0 0 3px）。
    private readonly Border split = new() { Margin = new Thickness(-3), CornerRadius = new CornerRadius(23), BorderThickness = new Thickness(3), Visibility = Visibility.Collapsed };
    private readonly List<SplitCell> cells = [];
    private readonly TextBlock splitLabel = new() { Text = "放到哪一格？", FontSize = 12, Height = SplitLabel, Padding = new Thickness(4, 2, 4, 0) };
    private readonly Grid picture = new();
    private readonly StackPanel texts = new();
    private readonly Grid bottom = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private bool hovering;
    private int hovered = -1;
    private bool[] enabled = [];

    /// 卡宽为 width 时放下全部内容要多高（视频卡标签两行、说明两行时比 392 高）。
    public double NeededHeight(double width)
    {
        var inner = Math.Max(1, width - 28);
        picture.Height = inner * 3 / 4;
        texts.Measure(new Size(inner, double.PositiveInfinity));
        bottom.Measure(new Size(inner, double.PositiveInfinity));
        return texts.DesiredSize.Height + 16 + bottom.DesiredSize.Height + 28;
    }

    /// 这张卡能进的子功能（和标签、分格一一对应）。
    public IReadOnlyList<string> Cells { get; }

    public MediaType? Type => kind switch { Kind.Image => MediaType.Image, Kind.Video => MediaType.Video, Kind.Pdf => MediaType.Pdf, _ => null };

    public HomeCard(Kind kind)
    {
        this.kind = kind;
        Cells = kind switch
        {
            Kind.Image => ["压缩", "裁切"],
            Kind.Video => Segments.AllVideo.Select(s => s.Title()).ToList(),
            Kind.Pdf => Segments.AllPdf.Select(s => s.Title()).ToList(),
            _ => [],
        };
        title.Text = kind switch { Kind.Image => "图片", Kind.Video => "视频", Kind.Pdf => "PDF", _ => "资源管理器圆环" };
        subtitle.Text = "不开窗口也能压";
        text.Text = kind switch
        {
            Kind.Image => "WebP、AVIF、JPG 一键变小，一次出多档尺寸；按比例批量裁切。",
            Kind.Video => "按体积压到 25 MB，裁成 9:16，拆成序列帧，或者做成实况。",
            Kind.Pdf => "邮件、阅读、打印三档。",
            _ => "在资源管理器里按住 Alt 点文件，圆环里点一下就好。第一次用要在设置里开启。",
        };
        formats.Text = kind switch { Kind.Image => "JPG · PNG · HEIC", Kind.Video => "MP4 · MOV · GIF", Kind.Pdf => "PDF", _ => "Windows" };
        glyph = new LineIcon(kind switch { Kind.Image => Icons.Image, Kind.Video => Icons.Video, Kind.Pdf => Icons.Pdf, _ => Icons.Ring }, 16);
        var art = kind switch { Kind.Image => "home-image", Kind.Video => "home-video", Kind.Pdf => "home-pdf", _ => "home-ring" };

        var artBox = new Border { CornerRadius = new CornerRadius(14) };
        artBox.Child = new Image { Source = new SvgImageSource(new Uri($"ms-appx:///Assets/{art}.svg")), Stretch = Stretch.UniformToFill };
        AutomationProperties.SetAccessibilityView(artBox, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        picture.Children.Add(artBox);
        notchIcon.Child = glyph;
        notch.Child = notchIcon;
        picture.Children.Add(notch);
        picture.SizeChanged += (_, e) => picture.Height = e.NewSize.Width * 3 / 4;

        arrow.Child = arrowIcon;
        bottom.Children.Add(formats);

        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        texts.Children.Add(picture);
        texts.Children.Add(title);
        if (Type is null)
        {
            texts.Children.Add(subtitle);
        }
        else
        {
            foreach (var name in Cells) tags.Children.Add(Tag(name));
            texts.Children.Add(tags);
        }
        texts.Children.Add(rule);
        texts.Children.Add(text);
        stack.Children.Add(texts);
        Grid.SetRow(bottom, 2);
        stack.Children.Add(bottom);
        back.Child = stack;

        if (Type is null)
        {
            // 圆环卡：整张卡是一个按钮。
            arrow.HorizontalAlignment = HorizontalAlignment.Left;
            bottom.Children.Add(arrow);
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            Motion.SetPress(b, true);
            AutomationProperties.SetName(b, $"{title.Text}：{subtitle.Text}");
            AutomationProperties.SetHelpText(b, text.Text);
            b.Click += (_, _) => App.OpenSettings("自动");
            root.Children.Add(b);
        }
        else
        {
            // 功能卡：标签和右下箭头可点（箭头进第一个功能），卡本身是一组。
            root.Children.Add(back);
            var go = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = arrow, HorizontalAlignment = HorizontalAlignment.Left };
            Motion.SetPress(go, true);
            AutomationProperties.SetName(go, $"进入{title.Text}");
            AutomationProperties.SetHelpText(go, text.Text);
            var type = Type.Value;
            go.Click += (_, _) => AppModel.Shared.SelectedType = type;
            bottom.Children.Add(go);
            AutomationProperties.SetName(this, $"{title.Text}：{string.Join(" · ", Cells)}");
            BuildSplit();
        }
        Content = root;

        PointerEntered += (_, _) => { hovering = true; Paint(); };
        PointerExited += (_, _) => { hovering = false; Paint(); };
        ActualThemeChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
    }

    /// 标题下的小标签：高 24、灰底胶囊、11 的图标 + 11.5 粗体字（设计稿 H1）。
    private Button Tag(string name)
    {
        var icon = new LineIcon(FeatureIcons.For(name), 11, 2) { VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = name, FontSize = 11.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(label);
        var pill = new Border { Height = 24, Padding = new Thickness(6, 0, 9, 0), CornerRadius = new CornerRadius(12), Child = row };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = pill };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, $"{title.Text}：{name}");
        ToolTipService.SetToolTip(b, $"进入{title.Text} · {name}");
        var type = Type!.Value;
        b.Click += (_, _) => AppModel.Shared.Enter(type, name);
        tagParts.Add((pill, icon, label));
        return b;
    }

    // ---------- 分格 ----------

    private sealed class SplitCell
    {
        public readonly Grid Root = new();
        public readonly Rectangle Frame = new() { RadiusX = 14, RadiusY = 14, StrokeThickness = 2 };
        public readonly LineIcon Icon;
        public readonly TextBlock Title = new() { FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
        public readonly TextBlock Hint = new() { FontSize = 11, Opacity = 0.85, Visibility = Visibility.Collapsed };

        public SplitCell(string name)
        {
            Icon = new LineIcon(FeatureIcons.For(name), 18, 2) { VerticalAlignment = VerticalAlignment.Center };
            Title.Text = name;
            Hint.Text = $"松手 → {name}";
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Title);
            words.Children.Add(Hint);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(Icon);
            row.Children.Add(words);
            Root.Children.Add(Frame);
            Root.Children.Add(row);
        }
    }

    private void BuildSplit()
    {
        var g = new Grid { RowSpacing = SplitGap };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitLabel) });
        g.Children.Add(splitLabel);
        for (var i = 0; i < Cells.Count; i++)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var cell = new SplitCell(Cells[i]);
            Grid.SetRow(cell.Root, i + 1);
            g.Children.Add(cell.Root);
            cells.Add(cell);
        }
        // 描边 3 在卡外，内容离卡边 12。
        split.Padding = new Thickness(SplitPad);
        split.Child = g;
        split.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(split, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.Children.Add(split);
    }

    /// 能不能接这批文件：里面至少有一个是这类文件（文件夹按落点那一类处理，都能接）。圆环卡不接。
    /// PDF 卡的「图片转 PDF」格接图片，所以拖的里面有图片时 PDF 卡也分格。
    public bool Accepts(IReadOnlySet<MediaType> types, bool hasFolder) =>
        Type is { } t && (hasFolder || types.Contains(t) || (t == MediaType.Pdf && types.Contains(MediaType.Image)));

    /// 每一格能不能放：PDF 卡只拖了图片（没有 PDF、没有文件夹）时，只有「图片转 PDF」能放，其余 4 格变淡、不响应。
    public bool[] EnabledCells(IReadOnlySet<MediaType> types, bool hasFolder) =>
        Cells.Select(name => Type != MediaType.Pdf || hasFolder || types.Contains(MediaType.Pdf) || name == PdfSegment.FromImages.Title()).ToArray();

    /// 点是否在卡上（卡自己的坐标）。
    public static bool Inside(Point p, double width, double height) => p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height;

    /// 卡上纵坐标 y 落在第几格（卡高 height、count 格）。格子之间的空隙和顶上的说明行归最近的一格，松手不会落空。
    public static int CellAt(double y, double height, int count)
    {
        if (count <= 0) return -1;
        var top = SplitPad + SplitLabel + SplitGap;
        var cellH = (height - SplitPad - top - SplitGap * (count - 1)) / count;
        if (cellH <= 0) return 0;
        var i = (int)Math.Floor((y - top + SplitGap / 2) / (cellH + SplitGap));
        return Math.Clamp(i, 0, count - 1);
    }

    /// 分格并高亮第 cell 格（伸缩出现；已经分着格就只换高亮）。
    public void ShowSplit(int cell, bool[] enabled)
    {
        if (Type is null) return;
        var first = split.Visibility != Visibility.Visible;
        if (!first && cell == hovered && enabled.SequenceEqual(this.enabled)) return;
        hovered = cell;
        this.enabled = enabled;
        split.Visibility = Visibility.Visible;
        PaintSplit();
        if (first)
        {
            split.Translation = new System.Numerics.Vector3(0, 0, 32);
            split.Shadow ??= new ThemeShadow();
            Motion.Appear(split);
            for (var i = 0; i < cells.Count; i++) Motion.Appear(cells[i].Root, i);
        }
    }

    public void HideSplit()
    {
        if (split.Visibility != Visibility.Visible) return;
        split.Visibility = Visibility.Collapsed;
        hovered = -1;
    }

    private void PaintSplit()
    {
        split.Background = Palette.Get(this, "BentoCard");
        split.BorderBrush = Palette.Brand;
        splitLabel.Foreground = Palette.Get(this, "BentoSecondary");
        for (var i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            var on = i == hovered;
            // 不可放的格子（只拖了图片时 PDF 卡的压缩 / 合并 / 拆分 / 转图片）：整格变淡。
            c.Root.Opacity = i < enabled.Length && !enabled[i] ? 0.35 : 1;
            c.Frame.Fill = on ? Palette.Brand : Palette.Get(this, "BentoCard");
            c.Frame.Stroke = on ? Palette.Brand : Palette.Get(this, "BentoLine");
            c.Frame.StrokeDashArray = on ? [] : [3, 2];
            c.Title.Foreground = on ? (Brush)Application.Current.Resources["OnBrand"] : Palette.Get(this, "BentoPrimary");
            c.Hint.Foreground = c.Title.Foreground;
            c.Hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            c.Icon.ColorKey = on ? "OnBrand" : "BentoPrimary";
        }
    }

    private void Paint()
    {
        back.Background = Palette.Get(this, "BentoCard");
        notch.Background = Palette.Get(this, "BentoCard");
        notchIcon.Background = Palette.Get(this, "BentoFill");
        title.Foreground = Palette.Get(this, "BentoPrimary");
        subtitle.Foreground = Palette.Get(this, "BentoSecondary");
        rule.Fill = Palette.Get(this, "BentoPrimary");
        text.Foreground = Palette.Get(this, "BentoSoft");
        formats.Foreground = Palette.Get(this, "BentoSecondary");
        foreach (var (pill, icon, label) in tagParts)
        {
            pill.Background = Palette.Get(this, "BentoFill");
            label.Foreground = Palette.Get(this, "BentoPrimary");
            icon.ColorKey = "BentoPrimary";
        }
        var red = kind == Kind.Image;
        arrow.Background = red ? Palette.Brand : Palette.Get(this, "BentoFill");
        arrowIcon.ColorKey = red ? "OnBrand" : "BentoPrimary";
        back.Translation = new System.Numerics.Vector3(0, hovering ? -2 : 0, hovering ? 16 : 2);
        back.Shadow ??= new ThemeShadow();
        if (split.Visibility == Visibility.Visible) PaintSplit();
    }
}
