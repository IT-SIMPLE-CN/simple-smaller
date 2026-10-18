using Microsoft.UI.Xaml;
using SimpleSmaller.Views.Result;

namespace SimpleSmaller.Views.Cards;

/// 完成页：整块换成一张大卡（刻度盘、原来 → 现在、逐个文件）。
internal static class ResultSlots
{
    public static UIElement Result(Workspace ws) =>
        ws.Type == MediaType.Video && ws.VideoSegment == VideoSegment.Live ? new LiveResultView(ws) : new ResultView(ws);
}
