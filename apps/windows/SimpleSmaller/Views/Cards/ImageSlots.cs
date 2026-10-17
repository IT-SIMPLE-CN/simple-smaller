using Microsoft.UI.Xaml;

namespace SimpleSmaller.Views.Cards;

/// Image 的网格格子。
internal static class ImageSlots
{
    public static UIElement Main(Workspace ws) => Slots.Placeholder("Image · 文件");
    public static UIElement Upper(Workspace ws) => Slots.Placeholder("Image · 上");
    public static UIElement LowerLeft(Workspace ws) => Slots.Placeholder("Image · 左下");
    public static UIElement LowerRight(Workspace ws) => Slots.Placeholder("Image · 开始");
}
