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
    private readonly Grid titleBar = new() { Height = 52 };
    private readonly StackPanel leading = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentPresenter center = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel trailing = new() { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Grid content = new();
    private readonly Border toast = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 28), Padding = new Thickness(14, 8, 14, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock toastText = new() { FontSize = 13 };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock subtitle = new() { FontSize = 11 };
    private readonly CircleIconButton back = new("", "回首页", 34, "BentoCard");
    private UIElement? page;
    private string pageKey = "";

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
        SetTitleBar(titleBar);

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

        model.PropertyChanged += OnModelChanged;
        WatchWorkspaces();
        Root.ActualThemeChanged += (_, _) => Paint();
        Root.Loaded += (_, _) => { Paint(); Show(animated: false); };
        titleBar.SizeChanged += (_, _) => UpdateDragRegions();
        trailing.SizeChanged += (_, _) => UpdateDragRegions();
        leading.SizeChanged += (_, _) => UpdateDragRegions();
        center.SizeChanged += (_, _) => UpdateDragRegions();
        HookShortcuts();
        HookClose();
    }

    // ---------- 快捷键（mac 的 AppCommands）与关闭 ----------

    /// Ctrl+O 选取文件、Ctrl+Shift+V 粘贴、Ctrl+0 首页、Ctrl+1/2/3 切类型、Ctrl+, 设置、
    /// Ctrl+Shift+R 重试失败的、Ctrl+Delete 从列表移除所选。开始（Ctrl+Enter）与停止（Ctrl+.）在开始块上。
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
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(title);
        texts.Children.Add(subtitle);
        leading.Children.Add(back);
        leading.Children.Add(texts);
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
        trailing.Children.Clear();
        if (home)
        {
            center.Content = HomeView.Brand();
            segmentKey = "";
            title.Text = "";
            subtitle.Text = "";
            var paste = new CircleIconButton("", "处理剪贴板里的图片或文件（Ctrl+Shift+V）", 34, "BentoCard");
            paste.Click += async (_, _) => await model.PasteFromClipboardAsync();
            var settings = new CircleIconButton("", "设置（Ctrl+,）", 34, "BentoCard");
            settings.Click += (_, _) => App.OpenSettings();
            trailing.Children.Add(paste);
            trailing.Children.Add(settings);
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
            }
            WorkView.SyncSegments(center.Content as UIElement, ws);
            title.Text = Titles.Title(ws);
            subtitle.Text = Titles.Subtitle(ws);
            if (ws.ShowResult && !ws.Running)
            {
                var undo = new CapsuleButton("撤销这一批", 34, "BentoCard");
                ToolTipService.SetToolTip(undo, "把这一批写出的文件移到回收站，原文件不动");
                undo.Click += (_, _) => model.Undo(ws);
                trailing.Children.Add(undo);
            }
        }
        AutomationProperties.SetName(titleBar, home ? "简单小一点" : title.Text);
        UpdateDragRegions();
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
