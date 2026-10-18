using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace SimpleSmaller;

/// 主窗口（设计稿 V2.1）：没有文件时是首页；选了一类就是这一类的便当盒工作区，做完换到完成页。
/// 标题栏自己画：左边返回与标题、正中分段、右边操作，系统的最小化 / 关闭按钮留在最右。
/// 拖到窗口任何地方：按类型自动分到图片、视频、PDF。
public sealed partial class MainWindow : Window
{
    private readonly AppModel model = AppModel.Shared;
    private readonly Grid titleBar = new() { Height = 60 }; // 放得下顶部大标签（高 48）上下各留 6
    private readonly StackPanel leading = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter center = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel trailing = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid content = new();
    private readonly Border toast = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28), Padding = new Thickness(14, 8, 14, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock toastText = new() { FontSize = 13 };
    private readonly StackPanel titleTexts = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock subtitle = new() { FontSize = 11 };
    private readonly Segments<string> homeTabs = new(Segments<string>.Kind.Mini, new[] { ("首页", "首页", (string?)null), ("设置", "设置", (string?)null) }, "首页", "首页与设置") { TrackKey = "BentoLine" };
    private readonly CircleIconButton back = new("", "回首页", 34, "BentoCard");
    private UIElement? page;
    private string pageKey = "";
    /// 圆环教学与各功能小教学（盖在最上层）。
    private readonly Views.Guide.GuideHost guides;

    public MainWindow()
    {
        InitializeComponent();
        Title = "简单小一点";
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SystemBackdrop = new MicaBackdrop();
        var scale = DisplayScale();
        AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(820 * scale)));
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(960 * scale);
            p.PreferredMinimumHeight = (int)(640 * scale);
        }

        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        BuildTitleBar();
        Root.Children.Add(titleBar);
        Grid.SetRow(content, 1);
        Root.Children.Add(content);
        toast.Child = toastText;
        Capsule.Apply(toast);
        Grid.SetRow(toast, 1);
        Root.Children.Add(toast);
        var undoBar = new UndoBar();
        Grid.SetRow(undoBar, 1);
        Root.Children.Add(undoBar);
        SetTitleBar(titleBar);
        guides = new Views.Guide.GuideHost(Root, center);

        Root.AllowDrop = true;
        Root.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "加进来";
            }
        };
        Root.Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            model.Add(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)), model.ShowHome ? null : model.SelectedType);
        };

        // Ctrl+Enter 开始、Ctrl+. 停止：在窗口一级先拦（焦点在文件列表等会自己吃回车的控件上时也要生效）。
        Root.PreviewKeyDown += (_, e) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (!ctrl || model.ShowHome || guides.IsOpen) return;
            var ws = model.Current;
            if (e.Key == Windows.System.VirtualKey.Enter && !ws.Running && ws.Files.Count > 0 && ws.CanStart && !ws.IsUpToDate)
            {
                model.Start(ws);
                e.Handled = true;
            }
            else if ((int)e.Key == 190 && ws.Running)
            {
                model.Cancel(ws);
                e.Handled = true;
            }
        };
        model.PropertyChanged += OnModelChanged;
        WatchWorkspaces();
        Root.ActualThemeChanged += (_, _) => Paint();
        Root.Loaded += (_, _) => { Paint(); Show(animated: false); };
        titleBar.SizeChanged += (_, _) => { FitCenter(); UpdateDragRegions(); };
        trailing.SizeChanged += (_, _) => { FitCenter(); UpdateDragRegions(); };
        leading.SizeChanged += (_, _) => UpdateDragRegions();
        center.SizeChanged += (_, _) => UpdateDragRegions();
        HookShortcuts();
        HookClose();
    }

    // ---------- 快捷键（mac 的 AppCommands）与关闭 ----------

    /// Ctrl+O 选取文件、Ctrl+Shift+V 粘贴、Ctrl+0 首页、Ctrl+1/2/3 切类型、Ctrl+, 设置、
    /// Ctrl+Shift+R 重试失败的、Ctrl+Delete 从列表移除所选、Ctrl+Shift+Delete 清空这一批、Ctrl+Z 撤销清空 / 移除（提示条在时）。
    /// 开始（Ctrl+Enter）与停止（Ctrl+.）在开始块上。
    private void HookShortcuts()
    {
        void add(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers mods, Action action)
        {
            var a = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = mods };
            a.Invoked += (_, e) => { e.Handled = true; action(); };
            Root.KeyboardAccelerators.Add(a);
        }
        const Windows.System.VirtualKeyModifiers ctrl = Windows.System.VirtualKeyModifiers.Control;
        const Windows.System.VirtualKeyModifiers ctrlShift = ctrl | Windows.System.VirtualKeyModifiers.Shift;
        add(Windows.System.VirtualKey.O, ctrl, PickFiles);
        add(Windows.System.VirtualKey.V, ctrlShift, async () => await model.PasteFromClipboardAsync());
        add(Windows.System.VirtualKey.Number0, ctrl, () => model.ShowHome = true);
        for (var i = 0; i < MediaTypes.All.Length; i++)
        {
            var type = MediaTypes.All[i];
            add(Windows.System.VirtualKey.Number1 + i, ctrl, () => model.SelectedType = type);
        }
        add((Windows.System.VirtualKey)188 /* , */, ctrl, () => App.OpenSettings());
        add(Windows.System.VirtualKey.R, ctrlShift, () =>
        {
            var ws = model.Current;
            if (!ws.Running && ws.Files.Any(f => f.State.IsFailed)) model.RetryFailed(ws);
        });
        add(Windows.System.VirtualKey.Delete, ctrl, () =>
        {
            var ws = model.Current;
            if (!ws.Running && ws.Selection.Count > 0) model.Remove(ws.Selection, ws);
        });
        add(Windows.System.VirtualKey.Delete, ctrlShift, () =>
        {
            var ws = model.Current;
            if (!model.ShowHome && !ws.ShowResult && !guides.IsOpen) model.ClearUndoable(ws);
        });
        // Ctrl+Z 撤销清空 / 移除：只在提示条还在时；图片调框页的「Ctrl+Z 撤回这一张」优先（不拦，交给调框页）。
        var undo = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Z, Modifiers = ctrl };
        undo.Invoked += (_, e) =>
        {
            var ws = model.Current;
            if (model.PendingUndo is null || guides.IsOpen) return;
            if (!model.ShowHome && ws.Type == MediaType.Image && ws.IsCropping && ws.CropEditing && ws.Files.Count > 0) return;
            if (Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox) return;
            e.Handled = true;
            model.UndoRemoval();
        };
        Root.KeyboardAccelerators.Add(undo);
        Root.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
    }

    /// 选取文件（Ctrl+O）。
    private async void PickFiles()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        picker.CommitButtonText = "添加";
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count > 0) model.Add(files.Select(f => f.Path), model.ShowHome ? null : model.SelectedType);
    }

    /// 关闭主窗口：开着托盘图标就收到托盘（后台的剪贴板、监视文件夹照常），否则退出（处理中先确认）。
    private void HookClose()
    {
        AppWindow.Closing += (_, e) =>
        {
            if (App.Quitting) return;
            e.Cancel = true;
            if (Views.Sub.TrayIcon.IsShown) AppWindow.Hide();
            else App.Quit();
        };
    }

    /// 强制深浅色（--appearance，验证用）；其余时候跟随系统。
    public void SetAppearance(string mode)
    {
        Root.RequestedTheme = mode switch { "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => ElementTheme.Default };
    }

    /// 窗口所在显示器的缩放（构造时还没有 XamlRoot，按系统 DPI 算）。
    private double DisplayScale()
    {
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        return dpi > 0 ? dpi / 96.0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    // ---------- 标题栏 ----------

    private void BuildTitleBar()
    {
        titleBar.Padding = new Thickness(16, 0, 0, 0);
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        back.Click += (_, _) => model.ShowHome = true;
        ToolTipService.SetToolTip(back, "回首页（这一批文件保留）");
        title.TextTrimming = subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
        title.TextWrapping = subtitle.TextWrapping = TextWrapping.NoWrap;
        titleTexts.Children.Add(title);
        titleTexts.Children.Add(subtitle);
        leading.Children.Add(homeTabs);
        leading.Children.Add(back);
        leading.Children.Add(titleTexts);
        // 首页左上「首页 | 设置」：点设置打开设置窗口，分段回到首页。
        homeTabs.Changed += v =>
        {
            if (v != "设置") return;
            App.OpenSettings();
            Root.DispatcherQueue.TryEnqueue(() => homeTabs.Selected = "首页");
        };
        titleBar.Children.Add(leading);
        Grid.SetColumn(center, 1);
        titleBar.Children.Add(center);
        Grid.SetColumn(trailing, 2);
        titleBar.Children.Add(trailing);
    }

    /// 标题栏里能点的地方不拖动窗口。
    private void UpdateDragRegions()
    {
        if (Root.XamlRoot is null)
        {
            return;
        }
        var scale = Root.XamlRoot.RasterizationScale;
        var rects = new List<RectInt32>();
        foreach (var fe in Interactive())
        {
            if (fe.ActualWidth <= 0 || fe.Visibility != Visibility.Visible)
            {
                continue;
            }
            var t = fe.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
            rects.Add(new RectInt32((int)(t.X * scale), (int)(t.Y * scale), (int)(fe.ActualWidth * scale), (int)(fe.ActualHeight * scale)));
        }
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
        // 右边留出系统按钮的位置。
        trailing.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset / scale + 8, 0);
    }

    private IEnumerable<FrameworkElement> Interactive()
    {
        yield return back;
        yield return homeTabs;
        if (center.Content is FrameworkElement c)
        {
            yield return c;
        }
        foreach (var child in trailing.Children.OfType<FrameworkElement>())
        {
            yield return child;
        }
    }

    private void Paint()
    {
        Root.Background = Palette.Get(Root, "BentoWindow");
        title.Foreground = Palette.Get(Root, "BentoPrimary");
        subtitle.Foreground = Palette.Get(Root, "BentoSecondary");
        toast.Background = Palette.Get(Root, "BentoInk");
        toastText.Foreground = Palette.Get(Root, "BentoOnInk");
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x14, 0, 0, 0);
    }

    // ---------- 页面 ----------

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppModel.ShowHome):
            case nameof(AppModel.SelectedType):
            case nameof(AppModel.Current):
                Show(animated: true);
                break;
            case nameof(AppModel.Toast):
                ShowToast();
                break;
        }
    }

    private void WatchWorkspaces()
    {
        foreach (var type in MediaTypes.All)
        {
            var ws = model.Workspace(type);
            ws.PropertyChanged += (_, _) => { if (ws == model.Current && !model.ShowHome) QueueTitle(); };
            // 文件的体积、时长等是异步读出来的：每个文件的变化也要刷新副标题。
            System.ComponentModel.PropertyChangedEventHandler item = (_, _) => { if (ws == model.Current && !model.ShowHome) QueueTitle(); };
            ws.Files.CollectionChanged += (_, e) =>
            {
                foreach (var f in e.NewItems?.OfType<FileItem>() ?? []) f.PropertyChanged += item;
                foreach (var f in e.OldItems?.OfType<FileItem>() ?? []) f.PropertyChanged -= item;
                if (ws == model.Current && !model.ShowHome) QueueTitle();
            };
        }
    }

    /// 首页 ↔ 工作区：整体轻微缩放 + 淡入，里面的卡片各自再弹入。
    private void Show(bool animated)
    {
        var key = model.ShowHome ? "home" : model.SelectedType.ToString();
        if (key != pageKey || page is null)
        {
            pageKey = key;
            content.Children.Clear();
            page = model.ShowHome ? new HomeView() : new WorkView(model.Current);
            content.Children.Add(page);
            if (animated)
            {
                Motion.PageIn(page);
            }
        }
        UpdateTitle();
    }

    /// 首页右上：白底胶囊「粘贴剪贴板」+ 快捷键（设计稿 V2.1 · 01）。
    private Button PasteButton()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = "粘贴剪贴板", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var keys = new TextBlock { Text = "Ctrl+Shift+V", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        row.Children.Add(keys);
        var pill = new Border { Height = 36, Padding = new Thickness(16, 0, 14, 0), Child = row };
        Capsule.Apply(pill);
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = pill, VerticalAlignment = VerticalAlignment.Center };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, "粘贴剪贴板");
        ToolTipService.SetToolTip(b, "处理剪贴板里的图片或文件（Ctrl+Shift+V）");
        b.Click += async (_, _) => await model.PasteFromClipboardAsync();
        void paint()
        {
            pill.Background = Palette.Get(b, "BentoCard");
            label.Foreground = Palette.Get(b, "BentoPrimary");
            keys.Foreground = Palette.Get(b, "BentoSecondary");
        }
        b.Loaded += (_, _) => paint();
        b.ActualThemeChanged += (_, _) => paint();
        return b;
    }

    /// 首页右上的菜单按钮：设置、关于、检查更新、退出。
    private Button MenuButton()
    {
        var icon = new LineIcon(Icons.Menu, 18);
        var circle = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = icon };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = circle, VerticalAlignment = VerticalAlignment.Center };
        Motion.SetPress(b, true);
        AutomationProperties.SetName(b, "更多");
        ToolTipService.SetToolTip(b, "更多");
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        void item(string text, Action act)
        {
            var i = new MenuFlyoutItem { Text = text };
            i.Click += (_, _) => act();
            menu.Items.Add(i);
        }
        item("设置…", () => App.OpenSettings());
        item("关于简单小一点", App.OpenAbout);
        item("检查更新…", async () => AppModel.Shared.Flash(await Updates.Shared.CheckAsync()));
        menu.Items.Add(new MenuFlyoutSeparator());
        item("退出", App.Quit);
        b.Flyout = menu;
        b.Loaded += (_, _) => circle.Background = Palette.Get(b, "BentoCard");
        b.ActualThemeChanged += (_, _) => circle.Background = Palette.Get(b, "BentoCard");
        return b;
    }

    private bool titleQueued;
    private string segmentKey = "";

    /// 同一轮里多次变化只更新一次标题。
    private void QueueTitle()
    {
        if (titleQueued) return;
        titleQueued = true;
        Root.DispatcherQueue.TryEnqueue(() => { titleQueued = false; UpdateTitle(); });
    }

    private void UpdateTitle()
    {
        var home = model.ShowHome;
        back.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        homeTabs.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        trailing.Children.Clear();
        if (home)
        {
            center.Content = HomeView.Brand();
            segmentKey = "";
            title.Text = "";
            subtitle.Text = "";
            // 设计稿 V2.1 · 01：右边「粘贴剪贴板 ⌘⇧V」胶囊 + 菜单按钮。
            trailing.Children.Add(PasteButton());
            trailing.Children.Add(MenuButton());
        }
        else
        {
            var ws = model.Current;
            // 分段只在换类型时新建，其余时候原地改选中项，底块才能滑过去。
            var segKey = ws.Type.ToString();
            if (segmentKey != segKey || center.Content is null)
            {
                segmentKey = segKey;
                center.Content = WorkView.Segments(ws);
                if (center.Content is FrameworkElement tabs) tabs.Loaded += (_, _) => { FitCenter(); UpdateDragRegions(); };
            }
            WorkView.SyncSegments(center.Content as UIElement, ws);
            title.Text = Titles.Title(ws);
            subtitle.Text = Titles.Subtitle(ws);
            // 设计稿 V2.2 · E：有文件时右上角常驻「? 再看一遍教学」（完成页不放）。
            if (ws.Files.Count > 0 && !(ws.ShowResult && !ws.Running))
            {
                trailing.Children.Add(guides.ReplayButton());
            }
            if (ws.ShowResult && !ws.Running)
            {
                var undo = new CapsuleButton("撤销这一批", 34, "BentoCard");
                ToolTipService.SetToolTip(undo, "把这一批写出的文件移到回收站，原文件不动");
                undo.Click += (_, _) => model.Undo(ws);
                trailing.Children.Add(undo);
            }
        }
        AutomationProperties.SetName(titleBar, home ? "简单小一点" : title.Text);
        FitCenter();
        UpdateDragRegions();
    }

    /// 顶部大标签放不下时先只显示图标（设计稿 V2.2：不和左边标题、右边按钮重叠）；
    /// 左边的标题按剩下的宽度截断。
    private void FitCenter()
    {
        var w = titleBar.ActualWidth;
        if (w <= 0) return;
        const double gap = 16;
        // 左边：内边距 16 + 返回 34 + 间距 10，后面是标题文字。
        const double lead = 16 + 34 + 10;
        var right = trailing.ActualWidth + trailing.Margin.Right;
        if (center.Content is IFeatureTabs tabs)
        {
            titleTexts.MaxWidth = double.PositiveInfinity;
            title.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            subtitle.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var textW = Math.Max(title.DesiredSize.Width, subtitle.DesiredSize.Width);
            // 文字标签和标题（长文件名按 260 算）放不下时先退成图标，再不够才截断标题。
            var side = Math.Max(lead + Math.Min(textW, 260), right) + gap;
            tabs.Compact = tabs.FullWidth > w - 2 * side;
            var half = (w - (tabs.Compact ? tabs.CompactWidth : tabs.FullWidth)) / 2;
            titleTexts.MaxWidth = Math.Max(60, half - lead - gap);
        }
        else
        {
            titleTexts.MaxWidth = Math.Max(60, w - lead - right - gap);
        }
    }

    private void ShowToast()
    {
        if (model.Toast is { } text)
        {
            toastText.Text = text;
            toast.Visibility = Visibility.Visible;
            Motion.Appear(toast);
            AutomationProperties.SetName(toast, text);
        }
        else
        {
            toast.Visibility = Visibility.Collapsed;
        }
    }
}
