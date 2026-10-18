using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Guide;

/// 主窗口上的教学：什么时候弹、弹在哪、看过没有。
/// - 圆环教学：第一次打开应用时出现一次；设置 › 入口 的「再看一遍」随时打开。
/// - 小教学：图片 / 视频 / PDF 各自第一次有文件之后自动出现一次（没文件不弹）；「跳过」「知道了」、Esc 都记为看过。
///   标题栏右上角常驻「? 再看一遍教学」，点了从第 1 步再看。
/// 两层都盖在主窗口最上面（跨标题栏和内容两行）。
internal sealed class GuideHost
{
    /// 主窗口的那一个（设置里的「再看一遍」要找到它）。
    public static GuideHost? Shared { get; private set; }

    private readonly Grid root;
    private readonly AppModel model = AppModel.Shared;
    private readonly GuideStore store = GuideStore.Shared;
    private Coach? coach;
    private RingGuide? ringGuide;
    private Button? replay;
    private int checkTicket;

    public GuideHost(Grid root, FrameworkElement tabs)
    {
        Shared = this;
        this.root = root;
        GuideTargets.Register(GuideTargets.Tabs, tabs);
        model.PropertyChanged += OnModelChanged;
        foreach (var type in MediaTypes.All)
        {
            var ws = model.Workspace(type);
            ws.Files.CollectionChanged += (_, _) => { if (ws == model.Current) Check(); };
        }
        var shown = false;
        root.Loaded += (_, _) =>
        {
            if (shown) return;
            shown = true;
            // 第一次打开应用：等主窗口排好版、首页卡片弹完再出。
            if (!store.IsSeen(GuideStore.Ring)) Later(700, () => { if (!store.IsSeen(GuideStore.Ring)) ShowRing(); });
            else Check();
        };
    }

    /// 有教学开着（主窗口的快捷键先别响应）。
    public bool IsOpen => (coach is { IsClosing: false }) || ringGuide is not null;

    private void Later(int ms, Action action)
    {
        _ = Task.Delay(ms).ContinueWith(_ => root.DispatcherQueue.TryEnqueue(() => action()));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppModel.ShowHome) or nameof(AppModel.SelectedType) or nameof(AppModel.Current))) return;
        // 换了页面：框着的卡已经不在了，收起（记为看过）。
        if (coach is { IsClosing: false } c) c.Close();
        Check();
    }

    // MARK: 小教学

    /// 当前这一类有文件、还没看过、没在处理 / 完成页 → 稍等卡片弹完后出现。
    private void Check()
    {
        if (!Eligible(out _)) return;
        var ticket = ++checkTicket;
        Later(800, () =>
        {
            if (ticket != checkTicket || !Eligible(out var type)) return;
            ShowCoach(type);
        });
    }

    private bool Eligible(out MediaType type)
    {
        var ws = model.Current;
        type = ws.Type;
        return !IsOpen && !model.ShowHome && ws.Files.Count > 0 && !ws.Running && !ws.ShowResult
            && !store.IsSeen(GuideStore.KeyOf(ws.Type)) && store.IsSeen(GuideStore.Ring);
    }

    /// 从第 1 步打开这一类的小教学。
    public void ShowCoach(MediaType type)
    {
        if (IsOpen) return;
        var key = GuideStore.KeyOf(type);
        var (name, steps) = CoachSteps.For(type);
        var c = new Coach(name, steps);
        coach = c;
        c.Closed += () =>
        {
            store.MarkSeen(key);
            if (coach == c) coach = null;
            SetReplayVisible(true);
        };
        Cover(c);
        SetReplayVisible(false);
    }

    /// 标题栏右上「? 再看一遍教学」（设计稿：墨黑胶囊 34 高，左边 24 的品牌红圆里一个 ?）。
    public Button ReplayButton()
    {
        var mark = new TextBlock { Text = "?", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Palette.Brand, Child = mark };
        var label = new TextBlock { Text = "再看一遍教学", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(dot);
        row.Children.Add(label);
        var pill = new Border { Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(6, 0, 14, 0), Child = row };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = pill, VerticalAlignment = VerticalAlignment.Center };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, "再看一遍教学");
        ToolTipService.SetToolTip(b, "从第 1 步再看一遍这个功能的教学");
        void paint()
        {
            pill.Background = Palette.Get(b, "BentoInk");
            label.Foreground = Palette.Get(b, "BentoOnInk");
        }
        b.Loaded += (_, _) => paint();
        b.ActualThemeChanged += (_, _) => paint();
        b.Click += (_, _) => { if (!model.ShowHome) ShowCoach(model.Current.Type); };
        b.Visibility = IsOpen ? Visibility.Collapsed : Visibility.Visible;
        replay = b;
        return b;
    }

    private void SetReplayVisible(bool visible)
    {
        if (replay is null) return;
        replay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) Motion.Appear(replay);
    }

    // MARK: 圆环教学

    public void ShowRing()
    {
        if (ringGuide is not null) return;
        if (coach is { IsClosing: false } c) c.Close();
        var g = new RingGuide();
        ringGuide = g;
        g.Closed += () =>
        {
            store.MarkSeen(GuideStore.Ring);
            if (ringGuide == g) ringGuide = null;
            // 第一次启动就带着文件（右键菜单打开）：圆环教学关掉后再看要不要出小教学。
            Check();
        };
        Cover(g);
    }

    /// 盖在主窗口最上层：跨标题栏与内容两行。
    private void Cover(FrameworkElement layer)
    {
        Grid.SetRow(layer, 0);
        Grid.SetRowSpan(layer, Math.Max(1, root.RowDefinitions.Count));
        root.Children.Add(layer);
    }
}
