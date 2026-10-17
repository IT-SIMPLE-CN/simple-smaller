using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Graphics;

namespace SimpleSmaller.Auto;

/// 屏幕右上角的轻提示，2.5 秒后消失；不抢焦点、鼠标穿透、不进任务栏。
internal static class Hud
{
    private static Window? window;
    private static TextBlock? text;
    private static Grid? root;
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? timer;

    public static void Show(string message)
    {
        try
        {
            Ensure();
            text!.Text = message;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(root!, message);
            ((FrameworkElement)window!.Content).RequestedTheme = SubWindow.MainTheme();
            // 先量出文字宽度再定窗口大小。
            root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96.0;
            var w = (int)Math.Ceiling(root.DesiredSize.Width * scale);
            var h = (int)Math.Ceiling(34 * scale);
            var area = DisplayArea.GetFromPoint(Cursor(), DisplayAreaFallback.Primary).WorkArea;
            window!.AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - w - (int)(16 * scale), area.Y + (int)(16 * scale), w, h));
            window.AppWindow.Show(false);
            timer!.Stop();
            timer.Start();
        }
        catch (Exception e)
        {
            Log.Error($"提示：{e.Message}");
        }
    }

    private static void Ensure()
    {
        if (window is not null) return;
        window = new Window { Title = "简单小一点 提示", SystemBackdrop = new DesktopAcrylicBackdrop() };
        var icon = new FontIcon { Glyph = "", FontSize = 14, Foreground = Palette.Saving, VerticalAlignment = VerticalAlignment.Center };
        text = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(14, 0, 16, 0), Height = 34 };
        row.Children.Add(icon);
        row.Children.Add(text);
        root = new Grid { Children = { row } };
        Themed.On(root, () => text.Foreground = Palette.Get(root, "BentoPrimary"));
        window.Content = new UserControl { FontFamily = (FontFamily)Application.Current.Resources["BentoFont"], Content = root };
        if (window.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false);
            p.IsAlwaysOnTop = true;
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }
        window.AppWindow.IsShownInSwitchers = false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        // 不激活、鼠标穿透、不进 Alt+Tab。
        SetWindowLongPtr(hwnd, -20, (IntPtr)(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000 | 0x20 | 0x80 | 0x80000));
        var round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2.5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => window.AppWindow.Hide();
    }

    private static PointInt32 Cursor()
    {
        GetCursorPos(out var p);
        return new PointInt32(p.X, p.Y);
    }

    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
