using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Preview;
using Windows.Storage.Pickers;
using static SimpleSmaller.Views.Cards.VideoParts;

namespace SimpleSmaller.Views.Cards;

/// 视频「压缩」的左大卡：这一批视频的列表（名字、时长、分辨率、体积、状态），底部一条「拖进更多文件」。
/// 没有文件时整张卡是拖放区。最小实现，合并时与图片页的通用 FilesCard 统一。
internal sealed class VideoFilesCard : Reactive
{
    private readonly Workspace ws;
    private readonly Header header = Header("", "");
    private readonly StackPanel list = new() { Spacing = 6 };
    private readonly Border more;
    private string built = "";

    public VideoFilesCard(Workspace ws)
    {
        this.ws = ws;
        var add = new CircleIconButton("", "添加文件（Ctrl+O）");
        add.Click += (_, _) => PickVideos();
        header.Trailing = add;
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        AutomationProperties.SetName(scroll, "文件");
        var moreIcon = new FontIcon { Glyph = "", FontSize = 11 };
        var moreRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        moreRow.Children.Add(moreIcon);
        moreRow.Children.Add(Caption("拖进更多文件", 12));
        more = Paint(new Border { Height = 40, CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1.2), Child = moreRow },
                     b => { b.BorderBrush = Palette.Get(b, "BentoLine"); moreIcon.Foreground = Palette.Get(b, "BentoSecondary"); });
        AutomationProperties.SetAccessibilityView(more, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var grid = new Grid { RowSpacing = 14 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(header);
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        Grid.SetRow(more, 2);
        grid.Children.Add(more);
        Content = Card.Create(grid);
        Watch(ws, nameof(Workspace.Running));
        WatchList(ws.Files);
    }

    private void ItemChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.Bytes)) Schedule();
    }

    protected override void Refresh()
    {
        foreach (var f in ws.Files)
        {
            f.PropertyChanged -= ItemChanged;
            f.PropertyChanged += ItemChanged;
        }
        var n = ws.Files.Count;
        header.Title = ws.Running ? $"{n} 个文件 · 处理中" : n > 1 ? $"{n} 个文件 · 统一设置" : "1 个文件";
        header.Subtitle = ws.Running ? "完成一个写出一个 · 按 Ctrl+. 停止"
            : n > 1 ? $"改动对这一批全部生效 · {Format.Size(ws.TotalBytes)}" : Format.Size(ws.TotalBytes);
        Show(more, !ws.Running);
        var key = string.Join("|", ws.Files.Select(f => f.Id));
        if (key == built) return;
        built = key;
        list.Children.Clear();
        foreach (var f in ws.Files) list.Children.Add(new VideoFileRow(f));
    }

    /// 选取视频（Ctrl+O 同款）。
    public static async void PickVideos()
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var ext in new[] { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".gif" }) picker.FileTypeFilter.Add(ext);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 }) AppModel.Shared.Add(files.Select(f => f.Path), MediaType.Video);
        }
        catch (Exception e)
        {
            Log.Error($"选取文件 {e.Message}");
        }
    }
}

/// 列表里的一行：缩略图、名字、时长 · 分辨率 · 编码、体积、状态标。
internal sealed class VideoFileRow : Reactive
{
    private readonly FileItem item;
    private readonly Image thumb = VideoFrames.DecorImage();
    private readonly Border thumbBox;
    private readonly TextBlock name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock meta = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly TextBlock size = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock badgeText = new() { FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border badge;
    private readonly Border back;
    private bool thumbLoading;

    public VideoFileRow(FileItem item)
    {
        this.item = item;
        thumbBox = new Border { Width = 64, Height = 40, CornerRadius = new CornerRadius(8), Child = thumb };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(thumbBox, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        badge = new Border { Height = 20, MinWidth = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(7, 0, 7, 0), Child = badgeText, MaxWidth = 160 };
        badgeText.VerticalAlignment = VerticalAlignment.Center;
        badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        texts.Children.Add(name);
        texts.Children.Add(meta);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(size);
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(badge);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(thumbBox);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);
        Grid.SetColumn(right, 2);
        row.Children.Add(right);
        back = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 8, 12, 8), Child = row };
        Content = back;
        ToolTipService.SetToolTip(this, item.FilePath);
        Watch(item);
    }

    protected override void Refresh()
    {
        back.Background = Palette.Get(this, "BentoFill");
        thumbBox.Background = Palette.Get(this, "BentoLine");
        name.Foreground = size.Foreground = Palette.Get(this, "BentoPrimary");
        meta.Foreground = Palette.Get(this, "BentoSecondary");
        name.Text = item.Name;
        var parts = new List<string>();
        if (item.Duration > 0) parts.Add(Format.Duration(item.Duration));
        if (item.Width > 0) parts.Add(item.Dimensions);
        if (item.Fps > 0) parts.Add($"{(int)Math.Round(item.Fps)} fps");
        if (item.Hdr) parts.Add("HDR");
        meta.Text = item.MetaError ?? (parts.Count > 0 ? string.Join(" · ", parts) : "读取中…");
        var s = item.State;
        size.Text = s.Kind == FileStateKind.Done && item.AfterBytes > 0
            ? $"{Format.Size(item.Bytes)} → {Format.Size(item.AfterBytes)}"
            : Format.Size(item.Bytes);
        (string? text, Brush? fill) = s.Kind switch
        {
            FileStateKind.Running => (s.Fraction < 0.02 ? "处理中" : $"{(int)(s.Fraction * 100)}%", Palette.Brand),
            FileStateKind.Done => ($"✓ {Format.Change(item.Bytes, item.AfterBytes)}", Palette.Saving),
            FileStateKind.Kept => (item.KeptText, Palette.Warning),
            FileStateKind.Failed => (s.Message ?? "失败", Palette.Get(this, "BentoFailed")),
            FileStateKind.Cancelled => ("已停止", Palette.Get(this, "BentoSecondary")),
            _ => item.MetaError is { } e ? (e, Palette.Warning) : ((string?)null, (Brush?)null),
        };
        badge.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        badgeText.Text = text ?? "";
        badge.Background = fill;
        if (s.Kind == FileStateKind.Failed && s.Message is { } m) ToolTipService.SetToolTip(badge, m);
        AutomationProperties.SetName(this, $"{item.Name}，{meta.Text}，{size.Text}{(text != null ? "，" + text : "")}");
        if (!thumbLoading && item.Duration > 0)
        {
            thumbLoading = true;
            _ = LoadThumb();
        }
    }

    private async Task LoadThumb()
    {
        var t = Math.Min(1.0, item.Duration * 0.1);
        var path = item.FilePath;
        var bytes = await Task.Run(() => VideoFrames.GrabAsync(path, t, 160));
        if (bytes != null && await VideoFrames.ToImageAsync(bytes) is { } img) thumb.Source = img;
    }
}

/// 没有视频时的左大卡：整张卡就是拖放区，也可以选取（设计稿 02）。拖入由主窗口统一接。
internal sealed class VideoDropCard : UserControl
{
    public VideoDropCard()
    {
        var icon = new FontIcon { Glyph = "", FontSize = 30 };
        var circle = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(38), Child = icon };
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var title = new TextBlock { Text = "把视频或文件夹拖到这里", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var formats = Caption("MP4 · MOV · M4V · MKV · WebM · AVI · GIF 动图", 12);
        formats.HorizontalAlignment = HorizontalAlignment.Center;
        var pick = new CapsuleButton("选取文件…", 40, "BentoInk", "BentoOnInk") { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        pick.Click += (_, _) => VideoFilesCard.PickVideos();
        var stack = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(circle);
        circle.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        stack.Children.Add(formats);
        stack.Children.Add(pick);
        var dash = new Rectangle { RadiusX = 18, RadiusY = 18, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 4 } };
        var grid = new Grid();
        grid.Children.Add(dash);
        grid.Children.Add(stack);
        Content = Card.Create(grid);
        void paint()
        {
            circle.Background = Palette.Get(this, "BentoFill");
            icon.Foreground = title.Foreground = Palette.Get(this, "BentoPrimary");
            dash.Stroke = Palette.Get(this, "BentoLine");
        }
        Loaded += (_, _) => paint();
        ActualThemeChanged += (_, _) => paint();
    }
}
