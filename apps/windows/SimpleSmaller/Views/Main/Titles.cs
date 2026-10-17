namespace SimpleSmaller.Views.Main;

/// 工作区标题与副标题（对应 mac MainView 的 navigationTitle / navigationSubtitle）。
internal static class Titles
{
    /// 视频剪辑、序列帧一次看一段：标题写这段的文件名。
    private static FileItem? SingleVideo(Workspace ws) =>
        ws.Type == MediaType.Video && ws.VideoSegment != VideoSegment.Compress ? ws.CurrentVideo : null;

    public static string Title(Workspace ws) => SingleVideo(ws)?.Name ?? ws.Type.Title();

    public static string Subtitle(Workspace ws)
    {
        if (SingleVideo(ws) is { } v)
        {
            var parts = new List<string>();
            var items = ws.VideoItems;
            var i = items.ToList().FindIndex(x => x.Id == v.Id);
            if (items.Count > 1 && i >= 0)
            {
                parts.Add($"第 {i + 1} / {items.Count} 个");
            }
            parts.Add($"{v.Width} × {v.Height}");
            parts.Add(Format.Duration(v.Duration));
            if (ws.VideoSegment == VideoSegment.Frames)
            {
                parts.Add($"{(int)Math.Round(v.Fps)} fps");
                parts.Add($"{(int)Math.Round(v.Duration * v.Fps)} 帧");
            }
            else
            {
                if (v.Hdr) parts.Add("HDR");
                parts.Add(Format.Size(v.Bytes));
            }
            return string.Join(" · ", parts);
        }
        if (ws.Files.Count == 0) return "没有文件";
        if (ws.Running) return $"处理中 {ws.FinishedCount} / {ws.Files.Count}";
        if (ws.ShowResult) return $"{ws.Files.Count} 个文件 · 已完成";
        var p = new List<string> { $"{ws.Files.Count} 个文件" };
        if (ws.IsCropping)
        {
            p.Add(ws.CropTitle);
            if (ws.CropAdjustedCount > 0) p.Add($"调过 {ws.CropAdjustedCount} / {ws.CropItems.Count} 张");
            return string.Join(" · ", p);
        }
        if (ws.Type == MediaType.Pdf && ws.PdfSegment != PdfSegment.FromImages)
        {
            p.Add($"{(ws.PdfSegment == PdfSegment.Merge ? ws.PageOrder.Count : ws.Files.Sum(f => f.Pages))} 页");
        }
        p.Add(Format.Size(ws.TotalBytes));
        return string.Join(" · ", p);
    }
}
