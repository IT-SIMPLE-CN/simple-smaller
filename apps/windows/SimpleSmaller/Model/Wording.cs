namespace SimpleSmaller;

internal static class VideoCards
{
    public static string RateTitle(VideoRateChoice r) => r switch
    {
        VideoRateChoice.High => "高",
        VideoRateChoice.Medium => "中",
        VideoRateChoice.Low => "低",
        _ => "按大小",
    };
}

internal static class PdfNotes
{
    /// 合并、拆分、图片转 PDF 没有配方：顶部横条写一句这个分段做什么。
    public static string SegmentNote(PdfSegment s) => s switch
    {
        PdfSegment.Merge => "把几个 PDF 按页面顺序合成一个文件，可排序、删页。",
        PdfSegment.Split => "把一个 PDF 按每 N 页拆成多个文件。",
        PdfSegment.FromImages => "把图片按顺序做成一个 PDF，一张一页。",
        _ => "",
    };
}

/// 质量数值对应的说法：高 / 均衡 / 偏小。
internal static class QualityWord
{
    public static string Of(int q) => q >= 88 ? "高" : q >= 70 ? "均衡" : "偏小";
}
