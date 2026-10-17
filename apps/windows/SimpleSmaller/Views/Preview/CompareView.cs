using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;
using Windows.Foundation;

namespace SimpleSmaller.Views.Preview;

/// 前后对比：拖中间的线，左边原图、右边输出。
internal sealed class CompareView : UserControl
{
    private readonly FileItem item;
    private readonly Grid root = new() { Padding = new Thickness(12) };
    private readonly Grid stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image before = new() { Stretch = Stretch.Fill };
    private readonly Image after = new() { Stretch = Stretch.Fill };
    private readonly RectangleGeometry afterClip = new();
    private readonly Rectangle line = new() { Width = 2, Fill = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Grid knob = new() { Width = 26, Height = 26, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private double split = 0.5;
    private Size fitted;

    public CompareView(FileItem item)
    {
        this.item = item;
        after.Clip = afterClip;
        stage.Children.Add(before);
        stage.Children.Add(after);
        stage.Children.Add(line);
        knob.Children.Add(new Ellipse { Fill = new SolidColorBrush(Microsoft.UI.Colors.White) });
        knob.Children.Add(new FontIcon { Glyph = "", FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black) });
        knob.Translation = new System.Numerics.Vector3(0, 0, 8);
        knob.Shadow = new ThemeShadow();
        stage.Children.Add(knob);
        var tags = new Grid { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        var left = ImageTag.Create($"原图 {Format.Size(item.Bytes)}");
        left.HorizontalAlignment = HorizontalAlignment.Left;
        var right = ImageTag.Create($"输出 {Format.Size(item.Outputs.FirstOrDefault()?.Bytes ?? 0)}");
        right.HorizontalAlignment = HorizontalAlignment.Right;
        tags.Children.Add(left);
        tags.Children.Add(right);
        stage.Children.Add(tags);
        root.Children.Add(stage);
        Content = root;
        AutomationProperties.SetName(this, "前后对比");
        Themed.OnTheme(this, () => root.Background = Palette.Get(this, "BentoFill"));
        root.SizeChanged += (_, _) => Layout();
        root.PointerPressed += (_, e) =>
        {
            root.CapturePointer(e.Pointer);
            Drag(e.GetCurrentPoint(stage).Position.X);
        };
        root.PointerMoved += (_, e) =>
        {
            if (e.Pointer.IsInContact)
            {
                Drag(e.GetCurrentPoint(stage).Position.X);
            }
        };
        root.PointerReleased += (_, e) => root.ReleasePointerCapture(e.Pointer);
        Loaded += async (_, _) =>
        {
            before.Source = await ImageLoader.LoadAsync(item.FilePath, 2400);
            if (item.Outputs.FirstOrDefault() is { } o)
            {
                after.Source = await ImageLoader.LoadAsync(o.Path, 2400);
            }
        };
    }

    private void Drag(double x)
    {
        if (fitted.Width <= 0)
        {
            return;
        }
        split = Math.Clamp(x / fitted.Width, 0, 1);
        Place();
    }

    private void Layout()
    {
        var box = new Size(Math.Max(1, root.ActualWidth - 24), Math.Max(1, root.ActualHeight - 24));
        double w = Math.Max(1, item.Width), h = Math.Max(1, item.Height);
        var s = Math.Min(box.Width / w, box.Height / h);
        fitted = new Size(w * s, h * s);
        stage.Width = fitted.Width;
        stage.Height = fitted.Height;
        Place();
    }

    private void Place()
    {
        var x = fitted.Width * split;
        afterClip.Rect = new Rect(x, 0, Math.Max(0, fitted.Width - x), fitted.Height);
        line.Height = fitted.Height;
        line.Margin = new Thickness(x - 1, 0, 0, 0);
        knob.Margin = new Thickness(x - 13, 0, 0, 0);
    }
}
