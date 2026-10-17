using Microsoft.UI.Xaml;

namespace SimpleSmaller.Views.Cards;

/// Video 的网格格子。
internal static class VideoSlots
{
    public static UIElement Main(Workspace ws) => Slots.Placeholder("Video · 文件");
    public static UIElement Upper(Workspace ws) => Slots.Placeholder("Video · 上");
    public static UIElement LowerLeft(Workspace ws) => Slots.Placeholder("Video · 左下");
    public static UIElement LowerRight(Workspace ws) => Slots.Placeholder("Video · 开始");
}
