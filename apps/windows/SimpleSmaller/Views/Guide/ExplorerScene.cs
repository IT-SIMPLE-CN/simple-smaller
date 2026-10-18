using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Ring;
using Windows.UI;

namespace SimpleSmaller.Views.Guide;

/// 圆环教学第 1 步的画面：用代码画一个资源管理器窗口（选中了 4 张图），上面放应用里真实的圆环（RingView，
/// 示例数据、静态、不接指针）。设计稿是把 1280 × 800 的访达画面缩到 0.7 放进 672 × 296 的框，
/// 这里同样按原尺寸画再整体缩 0.7；指针、「按住点一下」、场景标签按框的坐标叠在上面。
/// 画面固定浅色（像一张截图），旁白只读一句说明。
internal static class ExplorerScene
{
    private const double BoxWidth = 672, BoxHeight = 296, Scale = 0.7;
    private const double SceneWidth = BoxWidth / Scale, SceneHeight = BoxHeight / Scale;

    private static Color C(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static SolidColorBrush B(uint rgb) => new(C(rgb));
    private static Brush Grad(uint from, uint to) => new LinearGradientBrush
    {
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(1, 1),
        GradientStops = { new GradientStop { Color = C(from), Offset = 0 }, new GradientStop { Color = C(to), Offset = 1 } },
    };

    public static FrameworkElement Create(string[] triggerKeys)
    {
        var scene = new Canvas { Width = SceneWidth, Height = SceneHeight, Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, SceneWidth, SceneHeight) } };
        void at(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            scene.Children.Add(e);
        }
        TextBlock text(string s, double size, uint color, bool bold = false) => new()
        {
            Text = s,
            FontSize = size,
            Foreground = B(color),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 资源管理器窗口：白底、左上圆角 10；左边导航栏 180 宽 #F3F3F5、右边线 #E2E2E5。
        at(new Border { Width = SceneWidth + 40, Height = SceneHeight + 40, CornerRadius = new CornerRadius(10, 0, 0, 0), Background = B(0xFFFFFF) }, 0, 0);
        var side = new StackPanel { Width = 180, Height = SceneHeight + 40, Padding = new Thickness(10, 0, 10, 0), Spacing = 2, Background = B(0xF3F3F5), BorderBrush = B(0xE2E2E5), BorderThickness = new Thickness(0, 0, 1, 0) };
        // 顶行：后退、前进、向上（资源管理器的导航按钮）。
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Height = 52, Spacing = 14, Padding = new Thickness(8, 0, 0, 0) };
        foreach (var g in new[] { "←", "→", "↑" }) nav.Children.Add(text(g, 14, 0x8E8E93));
        side.Children.Add(nav);
        side.Children.Add(new Border { Padding = new Thickness(8, 4, 8, 4), Child = text("快速访问", 11, 0x8E8E93, bold: true) });
        foreach (var (name, on) in new[] { ("桌面", false), ("下载", false), ("品牌素材", true), ("文档", false) })
        {
            side.Children.Add(new Border { Height = 28, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(6), Background = on ? B(0xDCDCE0) : null, Child = text(name, 13, 0x1D1D1F) });
        }
        var sideHost = new Border { CornerRadius = new CornerRadius(10, 0, 0, 0), Child = side };
        at(sideHost, 0, 0);

        // 地址栏一行：52 高、下边线。
        at(new Border
        {
            Width = SceneWidth - 180 + 40,
            Height = 52,
            Padding = new Thickness(16, 0, 0, 0),
            BorderBrush = B(0xE2E2E5),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = text("品牌素材 › 2026 秋季新品", 13, 0x1D1D1F, bold: true),
        }, 180, 0);

        // 文件网格：每格 120 × 130，横向间隔 30、纵向 18；第二行前 4 张选中（灰底 + 红色文件名）。
        (string Name, uint From, uint To, bool Selected)[] files =
        [
            ("hero-01.jpg", 0xD8CFC4, 0x8C7B6B, false), ("hero-02.jpg", 0xC9D6CF, 0x4F6B5D, false), ("detail-03.jpg", 0xE6D9C0, 0xA2875A, false),
            ("detail-04.jpg", 0xD6D6E2, 0x5E5E7A, false), ("teaser.mov", 0x1D1D1F, 0x1D1D1F, false), ("提案.pdf", 0xFFFFFF, 0xFFFFFF, false),
            ("model-05.jpg", 0xEADFD3, 0x6B5444, true), ("model-06.jpg", 0xE2DED8, 0x5F574C, true), ("model-07.jpg", 0xEFE6E0, 0x7A5C4E, true),
            ("scene-08.jpg", 0xDCE3EA, 0x4A5A6C, true), ("scene-09.jpg", 0xE8E3D3, 0x6E6545, false), ("scene-10.jpg", 0xE6E0EA, 0x5F4E6B, false),
        ];
        for (var i = 0; i < files.Length; i++)
        {
            var f = files[i];
            double x = 208 + (i % 6) * 150, y = 80 + (i / 6) * 148;
            if (f.Selected) at(new Border { Width = 120, Height = 130, CornerRadius = new CornerRadius(6), Background = B(0xE3E3E8) }, x, y);
            var cell = new StackPanel { Width = 120, Spacing = 6, Padding = new Thickness(0, f.Selected ? 2 : 0, 0, 0) };
            var pdf = f.Name.EndsWith(".pdf");
            var thumb = new Border
            {
                Width = pdf ? 60 : 96,
                Height = pdf ? 76 : 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = f.From == f.To ? B(f.From) : Grad(f.From, f.To),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
                BorderThickness = new Thickness(0.5),
            };
            cell.Children.Add(thumb);
            var label = text(f.Name, 12, f.Selected ? 0xFFFFFFu : 0x1D1D1Fu);
            cell.Children.Add(f.Selected
                ? new Border { HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(4), Background = B(0xEF3922), Child = label }
                : new Border { HorizontalAlignment = HorizontalAlignment.Center, Child = label });
            at(cell, x, y);
        }

        // 真实的圆环：图片的内圈 / 外圈，上沿标「4 张」；只看不点。
        var ring = new RingView(RingMenu.Slots(MediaType.Image, AppModel.Shared.Store), "4 张", _ => { }, () => { }) { IsHitTestVisible = false };
        at(ring, 198, 52);

        var zoom = new Viewbox { Width = BoxWidth, Height = BoxHeight, Stretch = Stretch.Uniform, Child = scene };

        // 叠在框上的：指针、「Alt 按住点一下」、左下场景标签（框坐标，不缩放）。
        var overlay = new Canvas { Width = BoxWidth, Height = BoxHeight };
        void over(UIElement e, double x, double y)
        {
            Canvas.SetLeft(e, x);
            Canvas.SetTop(e, y);
            overlay.Children.Add(e);
        }
        var pointer = new Sketch(22, 30, false);
        CoachSteps.Pointer(pointer, "M2 2l0 24l6 -6l5 11l4 -2l-5 -11l8 0z", 1.6);
        over(pointer.Canvas, 299, 197);

        var tag = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Height = 24, Padding = new Thickness(4, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var k in triggerKeys)
        {
            tag.Children.Add(new Border { MinWidth = 18, Height = 18, Padding = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(5), Background = B(0xFFFFFF), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = k, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(0x111214), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        }
        tag.Children.Add(new TextBlock { Text = "按住点一下", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(0xFFFFFF), VerticalAlignment = VerticalAlignment.Center });
        over(new Border { CornerRadius = new CornerRadius(12), Background = B(0x111214), Child = tag }, 321, 219);

        var place = new Border
        {
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0xEB, 0xFF, 0xFF, 0xFF)),
            Child = new TextBlock { Text = "资源管理器 · 选中了 4 张图", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B(0x111214), VerticalAlignment = VerticalAlignment.Center },
        };
        over(place, 14, BoxHeight - 12 - 24);

        var stack = new Grid { Width = BoxWidth, Height = BoxHeight };
        stack.Children.Add(zoom);
        stack.Children.Add(overlay);
        var box = new Border
        {
            Height = BoxHeight,
            CornerRadius = new CornerRadius(20),
            Background = Grad(0xB9C3CF, 0x8E99A8),
            Child = new Sketch.Silent(stack, $"资源管理器里按住 {string.Join(" + ", triggerKeys)} 点文件，圆环在指针旁弹出"),
            RequestedTheme = ElementTheme.Light,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return box;
    }
}
