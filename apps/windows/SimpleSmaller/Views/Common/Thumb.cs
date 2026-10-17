using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Common;

/// 文件缩略图：撑满外部给的方框再裁（横图、竖图都裁满），还没生成时是灰底。
/// 跟着 FileItem.Thumbnail 变（缩略图在后台生成，好了再换上）。装饰图，旁白不读。
internal sealed class Thumb : Grid
{
    private FileItem? item;
    private bool attached;

    private readonly Image image = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public Thumb(FileItem? item = null, double radius = 3)
    {
        // 圆角用合成层裁：连同图片一起裁（Panel 的 CornerRadius 只裁底色）。
        if (radius > 0)
        {
            RoundClip.Apply(this, (float)radius);
        }
        Children.Add(image);
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => { attached = true; Subscribe(true); Paint(); };
        Unloaded += (_, _) => { attached = false; Subscribe(false); };
        ActualThemeChanged += (_, _) => Paint();
        Item = item;
    }

    public FileItem? Item
    {
        get => item;
        set
        {
            if (ReferenceEquals(item, value))
            {
                return;
            }
            if (attached) Subscribe(false);
            item = value;
            if (attached) Subscribe(true);
            Paint();
        }
    }

    private void Subscribe(bool on)
    {
        if (item is null) return;
        if (on) item.PropertyChanged += Changed; else item.PropertyChanged -= Changed;
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileItem.Thumbnail))
        {
            Paint();
        }
    }

    private object? shown;

    private void Paint()
    {
        Background = Palette.Get(this, "BentoFill");
        var source = item?.Thumbnail as ImageSource;
        if (!ReferenceEquals(shown, source))
        {
            shown = source;
            image.Source = source;
        }
    }
}

/// 圆角裁切（连同子元素、叠加层一起裁），大小变了跟着变。Border 的圆角只裁自己的底色，不裁内容。
internal static class RoundClip
{
    public static void Apply(FrameworkElement e, float radius)
    {
        void update()
        {
            var visual = ElementCompositionPreview.GetElementVisual(e);
            var c = visual.Compositor;
            var geo = c.CreateRoundedRectangleGeometry();
            geo.Size = new Vector2((float)e.ActualWidth, (float)e.ActualHeight);
            geo.CornerRadius = new Vector2(radius);
            visual.Clip = c.CreateGeometricClip(geo);
        }
        e.SizeChanged += (_, _) => update();
        e.Loaded += (_, _) => update();
    }
}

/// 图片上的小标签（黑底白字，如「原图 1.2 MB」）。
internal static class ImageTag
{
    public static Border Create(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) };
        return new Border
        {
            Child = t,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 0, 0, 0)),
        };
    }
}
