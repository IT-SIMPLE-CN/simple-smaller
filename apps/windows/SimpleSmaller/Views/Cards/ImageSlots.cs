using Microsoft.UI.Xaml;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Preview;

namespace SimpleSmaller.Views.Cards;

/// Image 的网格格子（mac WorkView 的 image 分支）：
/// 左大卡是文件（选了比例、点了一张时换成大图调框），上面「画面」，左下「格式与质量」，右下「输出」与开始。
internal static class ImageSlots
{
    public static UIElement MainCard(Workspace ws)
    {
        if (ws.Files.Count == 0) return new DropCard(ws);
        if (ws.IsCropping && ws.CropEditing) return Card.Create(new CropView(ws), new Thickness(14));
        return new FilesCard(ws);
    }

    public static UIElement Upper(Workspace ws) => new FrameCard(ws);
    public static UIElement LowerLeft(Workspace ws) => new FormatQualityCard(ws);
    public static UIElement LowerRight(Workspace ws) => new OutputStartCard(ws);
}
