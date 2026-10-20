using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SimpleSmaller.Views.Result;

/// 一个视频做出的实况文件。
internal sealed record LiveFiles(string? Photo, string? Video, string? Motion)
{
    public IReadOnlyList<string> All => new[] { Photo, Video, Motion }.Where(p => p != null).Select(p => p!).ToList();
    public bool IsPair => Photo != null && Video != null;

    public static LiveFiles Of(IEnumerable<SimpleSmaller.Core.Output> outputs)
    {
        string? photo = null, video = null, motion = null;
        foreach (var o in outputs)
        {
            var name = System.IO.Path.GetFileName(o.Path).ToLowerInvariant();
            if (name.EndsWith(".mp.jpg")) motion = o.Path;
            else if (name.EndsWith(".jpg")) photo = o.Path;
            else if (name.EndsWith(".mov")) video = o.Path;
        }
        return new LiveFiles(photo, video, motion);
    }
}

/// 实况的完成页（对应 mac LiveResultView）：左边直接看做出来的实况（悬停或点播放会动），右边是文件与去处：
/// 在资源管理器中显示、拷贝、打开；改设置再做一次、再来一批。实况不是为了变小，所以不放「小了多少」的刻度盘。
/// mac 的「存进照片」「隔空投送」「共享」Windows 没有，换成资源管理器、拷贝（可直接粘贴进聊天窗口）。
internal sealed class LiveResultView : Reactive
{
    private readonly Workspace ws;
    private readonly AppModel model = AppModel.Shared;
    private readonly Grid layout = new() { ColumnSpacing = 24 };
    private readonly StackPanel header = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private readonly Border badge = new() { Width = 40, Height = 40, CornerRadius = new CornerRadius(20) };
    private readonly LineIcon badgeCheck = new(ResultIcons.Check, 20, 2.4, "OnBrand");
    private readonly LineIcon badgeAlert = new(ResultIcons.Alert, 20, 2.6, "OnBrand");
    private readonly TextBlock title = new() { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock subtitle = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
    private readonly LivePreview preview = new();
    private readonly ScrollViewer strip = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
        Height = 64,
    };
    private readonly ContentPresenter panelHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private int index;

    public LiveResultView(Workspace ws)
    {
        this.ws = ws;
        var icons = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        icons.Children.Add(badgeCheck);
        icons.Children.Add(badgeAlert);
        badge.Child = icons;
        AutomationProperties.SetAccessibilityView(badge, AccessibilityView.Raw);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        header.Children.Add(badge);
        header.Children.Add(texts);
        AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level1);
        AutomationProperties.SetName(strip, "实况");

        var left = new Grid { RowSpacing = 14 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(header);
        Grid.SetRow(preview, 1);
        left.Children.Add(preview);
        Grid.SetRow(strip, 2);
        left.Children.Add(strip);

        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(336) });
        layout.Children.Add(left);
        Grid.SetColumn(panelHost, 1);
        layout.Children.Add(panelHost);
        layout.SizeChanged += (_, e) => layout.ColumnDefinitions[1].Width = new GridLength(Math.Min(336, e.NewSize.Width * 0.36));

        var card = Card.Create(layout, new Thickness(28, 26, 28, 24));
        card.CornerRadius = new CornerRadius(26);
        Content = card;
        AutomationProperties.SetName(this, "完成");
        Watch(ws, nameof(Workspace.ShowResult), nameof(Workspace.FinishedCount));
        Watch(model.Settings, nameof(AppSettings.OutputLocation), nameof(AppSettings.OutputFolder));
        ActualThemeChanged += (_, _) => Schedule();
    }

    private List<FileItem> Done => ws.Files.Where(f => f.State.Kind == FileStateKind.Done).ToList();
    private List<FileItem> Failed => ws.Files.Where(f => f.State.IsFailed).ToList();
    private FileItem? Current => Done is { Count: > 0 } d ? d[Math.Min(index, d.Count - 1)] : null;
    private LiveFiles CurrentFiles => LiveFiles.Of(Current?.Outputs ?? []);
    private List<string> AllPaths => Done.SelectMany(f => LiveFiles.Of(f.Outputs).All).ToList();

    private string Subtitle
    {
        get
        {
            var parts = new List<string> { $"{Done.Count} 个实况" };
            if (Failed.Count > 0) parts.Add($"{Failed.Count} 个失败");
            // 按这次实际做成的种类写，不用常用名（改过「做成」时常用名会对不上）。
            parts.Add(ws.Draft.Video.LiveKind switch { LiveKind.Apple => "苹果实况", LiveKind.Android => "安卓动态照片", _ => "两种都要" });
            var s = model.Settings;
            parts.Add(s.OutputLocation == OutputLocation.Beside ? "存在原视频旁边" : $"存在「{System.IO.Path.GetFileName(s.OutputFolder.TrimEnd('\\', '/'))}」");
            return string.Join(" · ", parts);
        }
    }

    protected override void Refresh()
    {
        var done = Done;
        var none = done.Count == 0;
        title.Text = none ? "没有做成" : "实况做好了";
        subtitle.Text = Subtitle;
        AutomationProperties.SetName(header, $"{title.Text}，{subtitle.Text}");
        badgeCheck.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        badgeAlert.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        badge.Background = none ? Palette.Get(this, "BentoFailed") : Palette.Brand;
        title.Foreground = Palette.Get(this, "BentoPrimary");
        subtitle.Foreground = Palette.Get(this, "BentoSecondary");
        preview.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
        preview.Show(CurrentFiles);
        BuildStrip(done);
        panelHost.Content = BuildPanel();
    }

    // ---------- 多个实况时底部切换 ----------

    private void BuildStrip(List<FileItem> done)
    {
        strip.Visibility = done.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (done.Count <= 1) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(2, 0, 2, 0) };
        for (var i = 0; i < done.Count; i++)
        {
            var k = i;
            var on = k == Math.Min(index, done.Count - 1);
            var image = new Image { Stretch = Stretch.UniformToFill };
            AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
            if (LiveFiles.Of(done[k].Outputs) is { } f && (f.Photo ?? f.Motion) is { } p)
                image.Source = new BitmapImage(new Uri(p)) { DecodePixelHeight = 96 };
            var thumb = new Border { Width = 72, Height = 48, CornerRadius = new CornerRadius(9), Child = image, Background = Palette.Get(this, "BentoFill") };
            var inner = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(11), Child = thumb, BorderBrush = on ? Palette.Get(this, "BentoCard") : null };
            var outer = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Child = inner, BorderBrush = on ? Palette.Brand : null };
            var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = outer };
            Motion.SetPress(b, true);
            AutomationProperties.SetName(b, done[k].Name);
            AutomationProperties.SetItemStatus(b, on ? "已选" : "");
            ToolTipService.SetToolTip(b, done[k].Name);
            b.Click += (_, _) => { index = k; Refresh(); };
            row.Children.Add(b);
        }
        strip.Content = row;
    }

    // ---------- 右栏 ----------

    private UIElement BuildPanel()
    {
        var files = CurrentFiles;
        var failed = Failed;
        var col = new Grid { RowSpacing = 10 };
        col.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var info = new Grid { RowSpacing = 8 };
        var name = new TextBlock { Text = Current?.Name ?? "", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Foreground = Palette.Get(this, "BentoPrimary") };
        var rows = new StackPanel { Spacing = 8 };
        foreach (var p in files.All) rows.Children.Add(FileRow(p));
        var hint = new TextBlock { Text = Hint(files), FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = Palette.Get(this, "BentoSecondary") };
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        info.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        info.Children.Add(name);
        Grid.SetRow(rows, 1);
        info.Children.Add(rows);
        Grid.SetRow(hint, 3);
        info.Children.Add(hint);
        if (Current == null) hint.Visibility = Visibility.Collapsed;
        col.Children.Add(new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 16, 18, 16), Background = Palette.Get(this, "BentoFill"), Child = info });

        var next = 1;
        void add(UIElement e)
        {
            col.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)e, next++);
            col.Children.Add(e);
        }

        if (failed.Count > 0) add(FailBlock(failed));
        var reveal = new BrandButton("在资源管理器中显示", 56, 15) { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = Current != null };
        ToolTipService.SetToolTip(reveal, "这一批做好的实况文件，一起选中");
        reveal.Click += (_, _) => model.Reveal(AllPaths);
        add(reveal);
        var copy = Wide("拷贝", "拷贝这一个实况的文件，可以直接粘贴进聊天窗口或文件夹", CopyIcon);
        copy.IsEnabled = Current != null;
        copy.Click += async (_, _) =>
        {
            var paths = CurrentFiles.All;
            if (paths.Count == 0) return;
            await model.Platform.CopyFilesAsync(paths);
            model.Flash(paths.Count > 1 ? $"已拷贝 {paths.Count} 个文件，可以直接粘贴" : "已拷贝，可以直接粘贴");
        };
        var open = Wide("打开", "用默认应用打开照片");
        open.IsEnabled = Current != null;
        open.Click += (_, _) => { if ((CurrentFiles.Photo ?? CurrentFiles.Motion) is { } p) Open(p); };
        add(Pair(copy, open));
        var again = Wide("改设置再做一次", "回到设置，这一批视频还在");
        again.Click += (_, _) => ws.ShowResult = false;
        var fresh = Wide("再来一批", "清空这一批，拖新的视频进来");
        fresh.Click += (_, _) => model.Clear(ws);
        add(Pair(again, fresh));
        return col;
    }

    private static string Hint(LiveFiles f)
    {
        if (f.IsPair && f.Motion != null) return "照片和视频是一对，要一起传、放在一起，到了 iPhone 的「照片」里就是实况。.MP.jpg 是给安卓手机的，传过去在相册里会动。";
        if (f.IsPair) return "照片和视频是一对，要一起传、放在一起，到了 iPhone 的「照片」里就是实况。";
        return "传到安卓手机（数据线、网盘、聊天软件发原图）后，在相册里会动。";
    }

    private static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { AppModel.Shared.Flash($"打不开：{e.Message}"); }
    }

    /// 一行文件：图标、文件名、「实况照片 · 215 KB」；可以直接拖出，双击在资源管理器中显示。
    private FrameworkElement FileRow(string path)
    {
        var lower = path.ToLowerInvariant();
        var isMov = lower.EndsWith(".mov");
        var kind = lower.EndsWith(".mp.jpg") ? "安卓动态照片" : isMov ? "实况视频" : "实况照片";
        ulong bytes = 0;
        try { bytes = (ulong)new System.IO.FileInfo(path).Length; } catch { }
        var icon = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Palette.Get(this, "BentoCard"), Child = new LineIcon(isMov ? Icons.Film : Icons.Image, 14) };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, Foreground = Palette.Get(this, "BentoPrimary") });
        texts.Children.Add(new TextBlock { Text = $"{kind} · {Format.Size(bytes)}", FontSize = 11, Foreground = Palette.Get(this, "BentoSecondary") });
        var g = new Grid { ColumnSpacing = 10, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(icon);
        Grid.SetColumn(texts, 1);
        g.Children.Add(texts);
        AutomationProperties.SetName(g, $"{System.IO.Path.GetFileName(path)}，{kind}，{Format.Size(bytes)}");
        ToolTipService.SetToolTip(g, "可以直接拖出；双击在资源管理器中显示");
        FileInteractions.EnableDragOut(g, () => [path]);
        g.DoubleTapped += (_, _) => model.Reveal([path]);
        return g;
    }

    private FrameworkElement FailBlock(List<FileItem> failed)
    {
        var g = new Grid { ColumnSpacing = 10, Padding = new Thickness(14, 12, 10, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var alert = Palette.Get(this, "BentoAlertText");
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = $"{failed.Count} 个失败", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = alert });
        var note = failed.Count == 1 ? $"{failed[0].Name} {failed[0].State.Message}" : "回到设置看每个视频的原因";
        texts.Children.Add(new TextBlock { Text = note, FontSize = 11, Foreground = alert, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        var retry = new CapsuleButton("重试失败的", 30, "BentoCard", "BentoAlertText") { VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(retry, "按当前设置再处理一遍失败的视频（Ctrl+Shift+R）");
        retry.Click += (_, _) => model.RetryFailed(ws);
        Grid.SetColumn(retry, 1);
        g.Children.Add(texts);
        g.Children.Add(retry);
        return new Border { CornerRadius = new CornerRadius(18), Background = Palette.Get(this, "BentoAlertFill"), Child = g };
    }

    private static readonly string CopyIcon = LineIcon.Rect(8, 8, 12, 12, 2) + "M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2";

    private static Grid Pair(UIElement a, UIElement b)
    {
        var pair = new Grid { ColumnSpacing = 8 };
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        pair.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn((FrameworkElement)b, 1);
        pair.Children.Add(a);
        pair.Children.Add(b);
        return pair;
    }

    private Button Wide(string text, string help, string? icon = null)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Stretch };
        Motion.SetPress(b, true);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) row.Children.Add(new LineIcon(icon, 14) { VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, FontSize = 13, Foreground = Palette.Get(this, "BentoPrimary"), VerticalAlignment = VerticalAlignment.Center });
        var back = new Border { Height = 44, CornerRadius = new CornerRadius(22), Background = Palette.Get(this, "BentoFill"), Child = row };
        b.Content = back;
        b.IsEnabledChanged += (_, _) => back.Opacity = b.IsEnabled ? 1 : 0.45;
        AutomationProperties.SetName(b, text);
        ToolTipService.SetToolTip(b, help);
        return b;
    }
}

/// 左边的预览：深色底上放实况的照片；苹果实况悬停或点播放时播一遍那段视频（像在 iPhone 上按住），播完回到照片。
/// 只有安卓动态照片时显示照片，左下角标「动态照片」。
internal sealed class LivePreview : UserControl
{
    private static readonly SolidColorBrush White = new(Microsoft.UI.Colors.White);
    private static readonly SolidColorBrush Veil = new(Windows.UI.Color.FromArgb(140, 0, 0, 0));
    private readonly Grid stage = new();
    private readonly Image still = new() { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
    private readonly Border tag;
    private readonly TextBlock tagText = new() { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = White, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button play = new() { Style = (Style)Application.Current.Resources["BentoBareButton"], HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20) };
    private MediaPlayer? player;
    private MediaPlayerElement? element;
    private LiveFiles? files;

    public LivePreview()
    {
        var back = new Border { CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1C, 0x1C, 0x1E)), Child = stage, RequestedTheme = ElementTheme.Dark };
        AutomationProperties.SetAccessibilityView(still, AccessibilityView.Raw);
        stage.Children.Add(still);
        var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        tagRow.Children.Add(new LineIcon(FeatureIcons.Live, 13, 1.8, "OnBrand") { VerticalAlignment = VerticalAlignment.Center });
        tagRow.Children.Add(tagText);
        tag = new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(12), Background = Veil, Child = tagRow, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20), IsHitTestVisible = false };
        AutomationProperties.SetAccessibilityView(tag, AccessibilityView.Raw);
        stage.Children.Add(tag);
        var playBack = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = Veil };
        playBack.Child = Glyph24.Create(Icons.PlayFill, 13, out var path);
        path.Fill = White;
        play.Content = playBack;
        Motion.SetPress(play, true);
        AutomationProperties.SetName(play, "播放实况");
        ToolTipService.SetToolTip(play, "播放实况");
        play.Click += (_, _) => Play();
        stage.Children.Add(play);
        Content = back;
        PointerEntered += (_, _) => Play();
        Unloaded += (_, _) => Release();
    }

    public void Show(LiveFiles f)
    {
        if (files == f) return;
        files = f;
        StopVideo();
        still.Source = (f.Photo ?? f.Motion) is { } p ? new BitmapImage(new Uri(p)) : null;
        tagText.Text = f.IsPair ? "实况" : "动态照片";
        play.Visibility = f.IsPair ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(this, f.IsPair ? "实况预览" : "动态照片预览");
    }

    /// 播一遍那段视频（苹果实况的 .mov），播完回到照片。
    private void Play()
    {
        if (files is not { IsPair: true, Video: { } video }) return;
        if (player == null)
        {
            player = new MediaPlayer { AutoPlay = false, IsMuted = false };
            player.CommandManager.IsEnabled = false;
            player.MediaEnded += (_, _) => DispatcherQueue?.TryEnqueue(StopVideo);
            player.MediaFailed += (_, _) => DispatcherQueue?.TryEnqueue(StopVideo);
        }
        if (element == null)
        {
            element = new MediaPlayerElement { AreTransportControlsEnabled = false, Stretch = Stretch.Uniform, IsHitTestVisible = false, Margin = new Thickness(12) };
            AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
            element.SetMediaPlayer(player);
            stage.Children.Insert(1, element);
        }
        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) return;
        player.Source = MediaSource.CreateFromUri(new Uri(video));
        element.Visibility = Visibility.Visible;
        player.Play();
    }

    private void StopVideo()
    {
        try { player?.Pause(); } catch { }
        if (element != null) element.Visibility = Visibility.Collapsed;
    }

    /// 卸载时先放开画面元素，等这一轮走完再释放播放器（同剪辑页，否则 XAML 会崩）。
    private void Release()
    {
        if (element != null)
        {
            element.SetMediaPlayer(null);
            stage.Children.Remove(element);
            element = null;
        }
        if (player is { } p)
        {
            player = null;
            try { p.Pause(); } catch { }
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                try { p.Source = null; p.Dispose(); } catch { }
            });
        }
        files = null;
    }
}
