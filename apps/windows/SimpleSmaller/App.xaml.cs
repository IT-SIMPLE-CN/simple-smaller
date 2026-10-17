using Microsoft.UI.Xaml;

namespace SimpleSmaller;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

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
        window = new MainWindow();
        window.Activate();
    }
}
