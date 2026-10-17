using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace SimpleSmaller.Bento;

/// 设置、关于、配方编辑这些小窗口的外壳：Mica 底、自绘标题栏（只有标题文字，系统关闭按钮在右）、
/// MiSans、便当盒窗口底色；深浅色跟主窗口（--appearance 强制时也跟）。打开时放在主窗口正中。
public class SubWindow : Window
{
    /// 内容放这里（标题栏下面）。
    protected readonly Grid Body = new();
    protected readonly Grid Root = new();
    private readonly Grid titleBar = new() { Height = 40 };
    private readonly TextBlock titleText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
    private readonly string backgroundKey;
    private FrameworkElement? titleCenter;
    private bool titleIsPrimary;

    public SubWindow(string title, double width, double height, string backgroundKey = "BentoWindow", bool showTitle = true)
    {
        this.backgroundKey = backgroundKey;
        Title = title;
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SystemBackdrop = new MicaBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
        }

        titleText.Text = showTitle ? title : "";
        titleBar.Children.Add(titleText);
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Root.Children.Add(titleBar);
        Grid.SetRow(Body, 1);
        Root.Children.Add(Body);
        var host = new UserControl { FontFamily = (FontFamily)Application.Current.Resources["BentoFont"], Content = Root };
        Content = host;
        SetTitleBar(titleBar);
        Root.RequestedTheme = MainTheme();
        Root.ActualThemeChanged += (_, _) => Paint();
        Root.Loaded += (_, _) => Paint();

        // Esc 关窗。
        var esc = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, e) => { e.Handled = true; Close(); };
        Root.KeyboardAccelerators.Add(esc);
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var size = new SizeInt32((int)(width * scale), (int)(height * scale));
        AppWindow.Resize(size);
        CenterOnMain(size);
    }

    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// 设计稿的窗口标题行（设置：60 高，左边 15 号粗体标题，正中放分段）。中间的控件能点，不拖动窗口。
    protected void UseHeader(double height, double titleSize, UIElement? center)
    {
        titleBar.Height = height;
        titleText.FontSize = titleSize;
        titleText.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        titleText.Margin = new Thickness(18, 0, 0, 0);
        titleIsPrimary = true;
        if (center is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
            titleBar.Children.Add(fe);
            titleCenter = fe;
            fe.SizeChanged += (_, _) => UpdatePassthrough();
            fe.LayoutUpdated += (_, _) => UpdatePassthrough();
        }
    }

    /// 内容铺到标题栏下面（关于、配方编辑、更新询问：设计稿里没有标题行，内容从窗口顶上排）。
    /// 标题栏仍在最上层负责拖动窗口，系统关闭按钮在右上。
    protected void OverlayTitle()
    {
        Grid.SetRow(Body, 0);
        Grid.SetRowSpan(Body, 2);
        Root.Children.Remove(titleBar);
        Root.Children.Add(titleBar);
    }

    private Windows.Graphics.RectInt32 lastPass;

    private void UpdatePassthrough()
    {
        if (titleCenter is null || Root.XamlRoot is null || titleCenter.ActualWidth <= 0) return;
        var scale = Root.XamlRoot.RasterizationScale;
        var t = titleCenter.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
        var rect = new RectInt32((int)(t.X * scale), (int)(t.Y * scale), (int)Math.Ceiling(titleCenter.ActualWidth * scale), (int)Math.Ceiling(titleCenter.ActualHeight * scale));
        if (rect.Equals(lastPass)) return;
        lastPass = rect;
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [rect]);
    }

    /// 再次打开：恢复、提到最前。
    public void Bring()
    {
        AppWindow.Show();
        Activate();
        SetForegroundWindow(Hwnd);
    }

    /// 主窗口强制了深浅色（验证用）就跟着它，否则跟随系统。
    internal static ElementTheme MainTheme()
    {
        if (App.Main?.Content is UserControl { Content: FrameworkElement root }) return root.RequestedTheme;
        return ElementTheme.Default;
    }

    private void CenterOnMain(SizeInt32 size)
    {
        RectInt32 area;
        if (App.Main is { } main && main.AppWindow.IsVisible)
        {
            var p = main.AppWindow.Position;
            var s = main.AppWindow.Size;
            area = new RectInt32(p.X, p.Y, s.Width, s.Height);
        }
        else
        {
            area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        }
        AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + Math.Max(0, (area.Height - size.Height) / 2)));
    }

    protected virtual void Paint()
    {
        Root.Background = Palette.Get(Root, backgroundKey);
        titleText.Foreground = Palette.Get(Root, titleIsPrimary ? "BentoPrimary" : "BentoSecondary");
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x14, 0, 0, 0);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);
}
