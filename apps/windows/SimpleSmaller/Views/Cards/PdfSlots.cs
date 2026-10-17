using Microsoft.UI.Xaml;

namespace SimpleSmaller.Views.Cards;

/// Pdf 的网格格子。
internal static class PdfSlots
{
    public static UIElement MainCard(Workspace ws) => Slots.Placeholder("Pdf · 文件");
    public static UIElement Upper(Workspace ws) => Slots.Placeholder("Pdf · 上");
    public static UIElement LowerLeft(Workspace ws) => Slots.Placeholder("Pdf · 左下");
    public static UIElement LowerRight(Workspace ws) => Slots.Placeholder("Pdf · 开始");
}
