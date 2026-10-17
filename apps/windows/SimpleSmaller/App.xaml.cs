using Microsoft.UI.Xaml;

namespace SimpleSmaller;

public partial class App : Application
{
    private static MainWindow? main;

    public App()
    {
        InitializeComponent();
    }

    /// 主窗口句柄（文件选择器、对话框要用）。
    public static IntPtr MainHwnd => main is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(main);

    public static MainWindow? Main => main;

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
        AppModel.Initialize(a => queue.TryEnqueue(() => a()), new AppPlatform());
        main = new MainWindow();
        main.Activate();
        LaunchArgs.Apply(argv.Skip(1).ToArray());
    }
}
