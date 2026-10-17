using Microsoft.UI.Xaml;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Preview;

namespace SimpleSmaller.Views.Cards;

/// Pdf 的网格格子（对应 mac WorkView 的 pdf 分支）：
/// 左大卡没文件时是拖放区，有文件是详情（文件列表 + 页面条 / 页面排序 / 图片排序）；
/// 上方主设置，左下选项，右下压缩用「这一批 + 开始」、其余分段用「输出 + 开始」。
internal static class PdfSlots
{
    public static UIElement MainCard(Workspace ws) =>
        ws.Files.Count == 0 ? new PdfDropCard(ws) : Card.Create(PdfDetailView.Create(ws), new Thickness(14));

    public static UIElement Upper(Workspace ws) => new PdfMainCard(ws);
    public static UIElement LowerLeft(Workspace ws) => new PdfOptionsCard(ws);

    public static UIElement LowerRight(Workspace ws) =>
        ws.PdfSegment == PdfSegment.Compress ? new PdfStartCard(ws) : new PdfOutputCard(ws);
}
