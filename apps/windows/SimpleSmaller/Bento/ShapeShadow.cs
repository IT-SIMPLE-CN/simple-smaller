using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace SimpleSmaller.Bento;

/// 形状投影（设计稿的 box-shadow，如关于里图标的红色光晕「0 12px 28px rgba(239,57,34,.35)」）。
/// 投影画在 host（放在形状后面、同样大小的空 Canvas）上，按形状的透明度遮罩取轮廓。
public static class ShapeShadow
{
    public static void Attach(Shape shape, Canvas host, Color color, float blur, float offsetY)
    {
        void update()
        {
            if (shape.ActualWidth <= 0) return;
            var c = ElementCompositionPreview.GetElementVisual(host).Compositor;
            var sprite = c.CreateSpriteVisual();
            sprite.Size = new Vector2((float)shape.ActualWidth, (float)shape.ActualHeight);
            var shadow = c.CreateDropShadow();
            shadow.Mask = shape.GetAlphaMask();
            shadow.Color = color;
            shadow.BlurRadius = blur;
            shadow.Offset = new Vector3(0, offsetY, 0);
            sprite.Shadow = shadow;
            ElementCompositionPreview.SetElementChildVisual(host, sprite);
        }
        shape.SizeChanged += (_, _) => update();
        shape.Loaded += (_, _) => update();
    }
}
