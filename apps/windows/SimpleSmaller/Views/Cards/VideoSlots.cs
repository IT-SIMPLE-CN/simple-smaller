using Microsoft.UI.Xaml;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Preview;

namespace SimpleSmaller.Views.Cards;

/// Video 的网格格子（对应 mac WorkView 的 video 分支）：
/// 没有文件是共用的拖放区；压缩是共用的文件卡（缩略图 / 列表）；剪辑、序列帧左大卡换成预览。
/// 参数卡：压缩 / 剪辑共用「画面与画质 · 格式 · 声音与开始」，序列帧是「抽帧 · 每一帧 · 输出与开始」。
internal static class VideoSlots
{
    public static UIElement MainCard(Workspace ws)
    {
        if (ws.Files.Count == 0) return new DropCard(ws);
        return ws.VideoSegment switch
        {
            VideoSegment.Compress => new FilesCard(ws),
            VideoSegment.Edit => Card.Create(new VideoEditView(ws), new Thickness(14)),
            _ => Card.Create(new VideoFramesView(ws), new Thickness(14)),
        };
    }

    public static UIElement Upper(Workspace ws) =>
        ws.VideoSegment == VideoSegment.Frames ? new FramesCard(ws) : new VideoPictureCard(ws);

    public static UIElement LowerLeft(Workspace ws) =>
        ws.VideoSegment == VideoSegment.Frames ? new FrameEachCard(ws) : new VideoFormatCard(ws);

    public static UIElement LowerRight(Workspace ws) =>
        ws.VideoSegment == VideoSegment.Frames ? new FramesOutputStartCard(ws) : new VideoAudioStartCard(ws);
}
