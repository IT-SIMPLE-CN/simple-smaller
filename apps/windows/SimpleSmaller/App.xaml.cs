using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using SimpleSmaller.Auto;
using SimpleSmaller.Views.Sub;

namespace SimpleSmaller;

public partial class App : Application
{
    private static MainWindow? main;
    private static AppNotifier? notifier;

    public App()
    {
        InitializeComponent();
    }

    /// 主窗口句柄（文件选择器、对话框要用）。
    public static IntPtr MainHwnd => main is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(main);

    public static MainWindow? Main => main;

    /// 正在退出：主窗口的「关闭」不再拦成收到托盘。
    internal static bool Quitting { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var argv = Environment.GetCommandLineArgs();
        // 自检：--selftest <输入…>，压完把结果写到 %TEMP%\smaller-selftest.txt 后退出。
        if (argv.Length > 2 && argv[1] == "--selftest")
        {
            SelfTest.Run(argv[2..]);
            Exit();
            return;
        }
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        // 通知先挂好点按事件再注册，之后才能读到「由通知启动」的参数。
        notifier = new AppNotifier();
        notifier.Setup();
        AppModel.Initialize(a => queue.TryEnqueue(() => a()), new AppPlatform(), notifier);
        AppModel.ClearUnlocked();
        main = new MainWindow();
        main.Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) AppModel.Shared.ClearUnseen();
        };
        main.Activate();

        // 后台的入口：托盘图标、剪贴板自动压缩、监视文件夹；设置一改就刷新。
        Updates.Shared.Ask = UpdateDialog.AskAsync;
        Updates.Shared.Start();
        TrayIcon.Refresh();
        ClipboardWatcher.Refresh();
        FolderWatcher.Refresh();
        AppSettings.Shared.PropertyChanged += OnSettingsChanged;

        // 应用退出后点了通知：系统带着通知参数重新启动本应用。
        try
        {
            var activated = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activated.Kind == ExtendedActivationKind.AppNotification && activated.Data is AppNotificationActivatedEventArgs n)
                AppNotifier.RevealFrom(n.Arguments);
        }
        catch { }

        var rest = argv.Skip(1).ToArray();
        LaunchArgs.Apply(rest);
        OpenWindowArg(rest);
    }

    private static void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AppSettings.MenuBarIcon):
                TrayIcon.Refresh();
                // 托盘图标关掉时主窗口如果收着，得拿出来，不然没有入口回来。
                if (!AppSettings.Shared.MenuBarIcon && main is { } m && !m.AppWindow.IsVisible) ShowMain();
                break;
            case nameof(AppSettings.ClipboardAuto):
                ClipboardWatcher.Refresh();
                break;
            case nameof(AppSettings.WatchFolders):
                FolderWatcher.Refresh();
                break;
        }
    }

    /// 验证用：--window 设置[:配方|自动] | 关于 | 许可证 | 新建配方 | 托盘，启动后直接打开对应窗口。
    private static void OpenWindowArg(string[] args)
    {
        var i = Array.IndexOf(args, "--window");
        if (i < 0 || i + 1 >= args.Length) return;
        var name = args[i + 1];
        var target = main!.DispatcherQueue;
        _ = Task.Delay(800).ContinueWith(_ => target.TryEnqueue(() =>
        {
            var parts = name.Split(':');
            switch (parts[0])
            {
                case "设置": OpenSettings(parts.Length > 1 ? parts[1] : null); break;
                case "关于": OpenAbout(); break;
                case "许可证": OpenLicenses(); break;
                case "新建配方": OpenRecipeEditor(AppModel.Shared.Current.Draft with { Id = "", Name = "", BuiltIn = false }, s => AppModel.Shared.Current.Select(s)); break;
                case "托盘": TrayView.Pinned = true; TrayView.Show(); break;
            }
        }));
    }

    /// 把主窗口拿出来（托盘、通知、小窗里的「打开主窗口」）。
    internal static void ShowMain()
    {
        if (main is null) return;
        main.AppWindow.Show();
        main.Activate();
        Bento.SubWindow.SetForegroundWindow(MainHwnd);
    }

    /// 退出：还有文件在处理时先确认，免得丢掉做到一半的视频。
    internal static async void Quit()
    {
        if (Quitting) return;
        var model = AppModel.Shared;
        if (model.IsBusy && main is not null)
        {
            ShowMain();
            var dialog = new ContentDialog
            {
                Title = "还有文件在处理",
                Content = "现在退出会停止处理，做到一半的文件不会留下，原文件不受影响。",
                PrimaryButtonText = "继续处理",
                SecondaryButtonText = "停止并退出",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = main.Content.XamlRoot,
                RequestedTheme = Bento.SubWindow.MainTheme(),
            };
            // 主按钮用品牌红（系统默认是强调色蓝）。
            dialog.Resources["AccentButtonBackground"] = Current.Resources["Brand"];
            dialog.Resources["AccentButtonBackgroundPointerOver"] = Current.Resources["BrandPressed"];
            dialog.Resources["AccentButtonBackgroundPressed"] = Current.Resources["BrandPressed"];
            dialog.Resources["AccentButtonForeground"] = Current.Resources["OnBrand"];
            dialog.Resources["AccentButtonForegroundPointerOver"] = Current.Resources["OnBrand"];
            dialog.Resources["AccentButtonForegroundPressed"] = Current.Resources["OnBrand"];
            if (await dialog.ShowAsync() != ContentDialogResult.Secondary) return;
            foreach (var t in MediaTypes.All) model.Cancel(model.Workspace(t));
        }
        Quitting = true;
        TrayIcon.Remove();
        notifier?.Teardown();
        AppModel.ClearUnlocked();
        foreach (var w in OpenSubWindows().ToList()) w.Close();
        Current.Exit();
    }
}
