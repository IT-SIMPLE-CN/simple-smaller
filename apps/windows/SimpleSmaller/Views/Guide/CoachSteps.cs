using Windows.UI.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SimpleSmaller.Views.Guide;

/// 小卡片放在聚光框的哪一边。
internal enum CoachSide { Below, BelowEnd, Right, Left }

/// 小教学的一步：框哪个元素、卡片放哪边、插图（308 × 92 的画板）、标题与说明。
internal sealed record CoachStep(string Target, CoachSide Side, string Title, string Text, Action<Sketch> Art);

/// 图片 / 视频 / PDF 三个小教学（设计稿 V2.2 · E / E2 / E3，各 4 步）。
/// 视频、PDF 逐字照稿，⌘↩ 写成 Ctrl+Enter；图片按「压缩、裁切合一」重写（共用说明里定的文案）。
internal static class CoachSteps
{
    public const double ArtWidth = 308, ArtHeight = 92;

    /// 视频第 2 步按进教学时的分段定：剪辑框预览与时间线，压缩 / 序列帧框文件卡（翻页过程中分段不会变）。
    public static (string Name, CoachStep[] Steps) For(MediaType type, VideoSegment segment = VideoSegment.Compress) => type switch
    {
        MediaType.Image => ("图片怎么用", Image),
        MediaType.Video => ("视频怎么用", VideoFor(segment)),
        _ => ("PDF 怎么用", Pdf),
    };

    private static CoachStep[] VideoFor(VideoSegment segment)
    {
        var steps = (CoachStep[])Video.Clone();
        steps[1] = segment switch
        {
            VideoSegment.Edit => steps[1],
            VideoSegment.Compress => new(GuideTargets.Main, CoachSide.Right, "这一批都在这",
                "把视频拖进来，随时还能往里加。要截一段、裁画面，切到顶部的「剪辑」。", VideoBatchArt),
            _ => new(GuideTargets.Main, CoachSide.Right, "这一批都在这",
                "把视频拖进来，随时还能往里加。", VideoBatchArt),
        };
        return steps;
    }

    private static readonly FontWeight Semi = FontWeights.SemiBold;
    private static readonly FontWeight Bold = FontWeights.Bold;

    // MARK: 图片（合一）

    private static readonly CoachStep[] Image =
    [
        new(GuideTargets.Main, CoachSide.Right, "这一批都在这",
            "把图片或整个文件夹拖进来，随时还能往里加。选中按空格用默认应用打开。", BatchArt),
        new(GuideTargets.Recipes, CoachSide.BelowEnd, "先选一个常用",
            "一点就把下面全部填好。改了任何参数，胶囊上出现红点，可以「存为常用」留着下次用。", ImageRecipeArt),
        new(GuideTargets.Upper, CoachSide.Left, "比例决定裁不裁",
            "选「原比例」只压缩；选了比例就一起裁，每张的框直接画在缩略图上，点一张进大图调。", RatioArt),
        new(GuideTargets.Start, CoachSide.Left, "确认后点开始",
            "按 Ctrl+Enter 也行。结果存在原文件旁边，原文件不改动；做完可以一键撤销。", StartArt),
    ];

    // MARK: 视频

    private static readonly CoachStep[] Video =
    [
        new(GuideTargets.Tabs, CoachSide.Below, "一个视频，三件事",
            "顶部切换压缩、剪辑、序列帧。切过去文件还在，不用重新拖。", VideoTabsArt),
        new(GuideTargets.Main, CoachSide.Right, "拖两端截一段",
            "时间线两端拖动截取，单击一帧设为封面；画面里的框可以拖，决定保留哪块。", TrimArt),
        new(GuideTargets.Recipes, CoachSide.BelowEnd, "再选一个常用",
            "发群 25 MB、抖音 9:16……一点就把比例、分辨率、画质都填好，改了可以存为常用。", VideoRecipeArt),
        new(GuideTargets.Start, CoachSide.Left, "一批一起出",
            "标题旁 ‹ › 切换看第几个。点开始或按 Ctrl+Enter，这一批按同一套设置出。", StartArt),
    ];

    // MARK: PDF

    private static readonly CoachStep[] Pdf =
    [
        new(GuideTargets.Tabs, CoachSide.Below, "PDF 能做五件事",
            "压缩、合并、拆分、转图片、图片转 PDF 都在顶部切换。文件留着，换个做法不用重新拖。", PdfTabsArt),
        new(GuideTargets.Main, CoachSide.Right, "先看清这一批",
            "每个文件的页数、大小一目了然，下面是页面预览。排序和删页在「合并」里做。", PdfBatchArt),
        new(GuideTargets.Upper, CoachSide.Left, "选一个压缩档",
            "屏幕阅读最小，打印保真最清楚；要卡邮件上限就选「指定大小」，填个目标。", PdfLevelArt),
        new(GuideTargets.Start, CoachSide.Left, "确认后点开始",
            "按 Ctrl+Enter 也行。压完反而变大的文件会自动保留原件。", StartArt),
    ];

    // MARK: 插图（坐标照搬设计稿 SVG，viewBox 0 0 308 92）

    private static void VideoTabsArt(Sketch s)
    {
        s.Rect(74.5, 28, 159, 36, 18, "card");
        s.Text(98.5, 46, "压缩", 11, "primary");
        s.Rect(122.5, 32, 52, 28, 14, "ink");
        s.Circle(135.5, 46, 5, "brand");
        s.Text(155, 46, "剪辑", 11, "onInk", Bold);
        s.Text(204, 46, "序列帧", 11, "primary");
        s.Text(154, 82, "文件留着，换个做法", 9.5, "secondary");
    }

    private static void TrimArt(Sketch s)
    {
        for (var i = 0; i < 10; i++)
        {
            s.Rect(20 + 27 * i, 30, 25, 34, 3, i is >= 4 and <= 6 ? "#5A4E44" : "key");
        }
        s.Rect(126, 26, 81, 42, 6, (string?)null, "brand", 3);
        s.Rect(122, 26, 6, 42, 3, "brand");
        s.Rect(205, 26, 6, 42, 3, "brand");
        s.Text(166, 80, "截取 00:18 – 01:03", 9.5, "brand", Semi);
    }

    private static void VideoRecipeArt(Sketch s)
    {
        s.Rect(14, 34, 70, 24, 12, "card");
        s.Text(49, 46, "发群 25 MB", 10.5, "primary");
        s.Rect(90, 34, 72, 24, 12, "ink");
        s.Text(126, 46, "抖音 9:16", 10.5, "onInk", Semi);
        s.Rect(168, 34, 64, 24, 12, "card");
        s.Text(200, 46, "小红书 3:4", 10.5, "primary");
        s.Rect(238, 34, 52, 24, 12, "card");
        s.Text(264, 46, "转 GIF", 10.5, "primary");
        s.Text(154, 72, "比例 · 分辨率 · 画质一次填好", 9.5, "secondary");
    }

    /// 开始块 + 快捷键（设计稿 ⌘ + ↩，Windows 写 Ctrl + Enter，键帽按字宽放宽）。
    private static void StartArt(Sketch s)
    {
        s.Rect(20, 20, 168, 52, 16, "brand");
        s.Text(40, 46, "开始", 18, "white", FontWeights.ExtraBold, 's');
        s.Circle(164, 46, 16, "white");
        s.Path("M157 46h13M165 41l5 5-5 5", null, "brand", 2);
        Key(s, 200, 38, "Ctrl");
        s.Text(246, 46, "+", 12, "secondary", Semi);
        Key(s, 254, 46, "Enter");
    }

    private static void Key(Sketch s, double x, double w, string label)
    {
        s.Rect(x, 35, w, 26, 6, "key");
        s.Rect(x, 33, w, 26, 6, "card");
        s.Text(x + w / 2, 46, label, 12, "primary", Semi);
    }

    private static void PdfTabsArt(Sketch s)
    {
        s.Rect(15.5, 30, 277, 34, 17, "card");
        s.Rect(19.5, 34, 52, 26, 13, "ink");
        s.Circle(32.5, 47, 5, "brand");
        s.Text(52, 47, "压缩", 11, "onInk", Bold);
        s.Text(95.5, 47, "合并", 11, "primary");
        s.Text(139.5, 47, "拆分", 11, "primary");
        s.Text(189, 47, "转图片", 11, "primary");
        s.Text(253.5, 47, "图片转PDF", 11, "primary");
        s.Text(154, 80, "文件留着，换个做法不用重拖", 9.5, "secondary");
    }

    private static void PdfBatchArt(Sketch s)
    {
        s.Rect(20, 14, 150, 18, 6, "ink");
        s.Text(30, 23, "品牌提案.pdf", 9.5, "onInk", Semi, 's');
        s.Text(160, 23, "48 页", 9, "inkSub", null, 'e');
        s.Rect(20, 36, 150, 18, 6, "card");
        s.Text(30, 45, "报价单.pdf", 9.5, "primary", null, 's');
        s.Text(160, 45, "2 页", 9, "secondary", null, 'e');
        s.Rect(20, 58, 150, 18, 6, "card");
        s.Text(30, 67, "合同扫描件.pdf", 9.5, "primary", null, 's');
        s.Text(160, 67, "12 页", 9, "secondary", null, 'e');
        string[] top = ["#D8CFC4", "card", "card", "#D8CFC4"];
        string[] bottom = ["card", "card", "#D8CFC4", "card"];
        for (var i = 0; i < 4; i++)
        {
            var x = 186 + 28 * i;
            var picked = i == 3;
            s.Rect(x, 16, 24, 28, 3, top[i], picked ? "brand" : "keyEdge", picked ? 2 : 1);
            s.Rect(x, 48, 24, 28, 3, bottom[i], "keyEdge", 1);
        }
        s.Text(240, 84, "页面预览", 9, "secondary");
    }

    private static void PdfLevelArt(Sketch s)
    {
        s.Rect(14, 22, 66, 24, 12, "card");
        s.Text(47, 34, "屏幕阅读", 10.5, "primary");
        s.Rect(86, 22, 66, 24, 12, "ink");
        s.Text(119, 34, "邮件附件", 10.5, "onInk", Semi);
        s.Rect(158, 22, 66, 24, 12, "card");
        s.Text(191, 34, "打印保真", 10.5, "primary");
        s.Rect(230, 22, 64, 24, 12, "card");
        s.Text(262, 34, "指定大小", 10.5, "primary");
        s.Text(46, 60, "最小", 9.5, "secondary");
        s.Text(262, 60, "填目标", 9.5, "secondary");
        s.Text(190, 60, "最清楚", 9.5, "secondary");
        s.Path("M30 74 H 278", null, "line", 2, round: false);
        s.Path("M30 74 H 120", null, "brand", 2, round: false);
    }

    /// 图片第 1 步：几张图在虚线框里，再拖进来一张（设计稿「这一批都在这」）。
    private static void BatchArt(Sketch s)
    {
        s.Rect(92, 34, 46, 34, 5, Sketch.Gradient("#D8CFC4", "#8C7B6B"));
        s.Rect(144, 34, 46, 34, 5, Sketch.Gradient("#C9D6CF", "#4F6B5D"));
        s.Rect(196, 34, 46, 34, 5, Sketch.Gradient("#E6D9C0", "#A2875A"));
        s.Rect(88, 30, 158, 54, 9, (string?)null, "line", 1, dash: 4);
        var flying = s.Rect(40, 6, 40, 30, 5, Sketch.Gradient("#D6D6E2", "#5E5E7A"));
        flying.RenderTransform = new RotateTransform { Angle = -8, CenterX = 20, CenterY = 16 };
        var curve = s.Path("M84 24 C 100 20, 110 26, 116 40", null, "brand", 2, round: false);
        curve.StrokeDashArray = new DoubleCollection { 1.5, 1.5 };
        Pointer(s, "M70 26l0 19.2l4.8 -4.8l4.0 8.8l3.2 -1.6l-4.0 -8.8l6.4 0z", 1.4);
        s.Text(254, 57, "+1", 12, "brand", Bold, 's');
    }

    /// 视频的「这一批都在这」：同图片那张的构图，缩略图换成视频（深色画面 + 播放三角 + 时长条）。
    private static void VideoBatchArt(Sketch s)
    {
        (string From, string To, string Time)[] clips = [("#3A4A5C", "#141A22", "02:14"), ("#5A4E44", "#1E1915", "00:45"), ("#2F4A40", "#101A16", "01:03")];
        for (var i = 0; i < clips.Length; i++)
        {
            var x = 92 + 52 * i;
            s.Rect(x, 34, 46, 34, 5, Sketch.Gradient(clips[i].From, clips[i].To));
            s.Path($"M{x + 19} 44l9 7-9 7z", "white", round: false);
            s.Text(x + 42, 63, clips[i].Time, 6.5, "white", Semi, 'e');
        }
        s.Rect(88, 30, 158, 54, 9, (string?)null, "line", 1, dash: 4);
        var flying = s.Rect(40, 6, 40, 30, 5, Sketch.Gradient("#4A4560", "#16141F"));
        flying.RenderTransform = new RotateTransform { Angle = -8, CenterX = 20, CenterY = 16 };
        s.Path("M56 15l8 6-8 6z", "white", round: false).RenderTransform = new RotateTransform { Angle = -8, CenterX = 60, CenterY = 22 };
        var curve = s.Path("M84 24 C 100 20, 110 26, 116 40", null, "brand", 2, round: false);
        curve.StrokeDashArray = new DoubleCollection { 1.5, 1.5 };
        Pointer(s, "M70 26l0 19.2l4.8 -4.8l4.0 8.8l3.2 -1.6l-4.0 -8.8l6.4 0z", 1.4);
        s.Text(254, 57, "+1", 12, "brand", Bold, 's');
    }

    /// 指针：墨黑填充、白描边（两种外观一样）。
    internal static void Pointer(Sketch s, string data, double stroke)
    {
        var p = s.Path(data, "#111214", "white", stroke, round: false);
        p.StrokeLineJoin = PenLineJoin.Round;
    }

    private static void ImageRecipeArt(Sketch s)
    {
        s.Rect(14, 34, 72, 24, 12, "ink");
        s.Text(46, 46, "网站用", 10.5, "onInk", Semi);
        s.Circle(76, 46, 3, "brand");
        s.Rect(92, 34, 70, 24, 12, "card");
        s.Text(127, 46, "小红书 3:4", 10.5, "primary");
        s.Rect(168, 34, 64, 24, 12, "card");
        s.Text(200, 46, "发群预览", 10.5, "primary");
        s.Rect(238, 34, 58, 24, 12, "alertFill");
        s.Text(267, 46, "存为常用", 10, "alertText", Semi);
        s.Text(50, 72, "改过 → 红点", 9.5, "secondary");
        s.Path("M232 46 h4", null, "brand", 1, round: false);
    }

    /// 图片第 3 步（合一版新画）：「原比例 / 小红书 3:4」两颗胶囊 + 一张画着裁切框的缩略图。
    private static void RatioArt(Sketch s)
    {
        s.Rect(14, 24, 58, 24, 12, "card");
        s.Text(43, 36, "原比例", 10.5, "primary");
        s.Rect(78, 24, 76, 24, 12, "ink");
        s.Text(116, 36, "小红书 3:4", 10.5, "onInk", Semi);
        s.Text(43, 62, "只压缩", 9.5, "secondary");
        s.Text(116, 62, "一起裁", 9.5, "secondary");

        // 缩略图：横图上画 3:4 的框，框外压暗（圆角裁掉露出的角）。
        const double x = 176, y = 12, w = 118, h = 64;
        var photo = new Canvas { Width = w, Height = h };
        photo.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = w, Height = h, Fill = Sketch.Gradient("#D8CFC4", "#8C7B6B") });
        const double fw = 45, fh = 60, fx = (w - fw) / 2, fy = (h - fh) / 2;
        var shade = new SolidColorBrush(Sketch.ColorOf("#66000000", false));
        void dim(double l, double t, double ww, double hh)
        {
            var r = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = ww, Height = hh, Fill = shade };
            Canvas.SetLeft(r, l);
            Canvas.SetTop(r, t);
            photo.Children.Add(r);
        }
        dim(0, 0, fx, h);
        dim(fx + fw, 0, w - fx - fw, h);
        dim(fx, 0, fw, fy);
        dim(fx, fy + fh, fw, h - fy - fh);
        s.Add(new Border { Width = w, Height = h, CornerRadius = new CornerRadius(5), Child = photo }, x, y);
        s.Rect(x + fx, y + fy, fw, fh, 2, (string?)null, "brand", 2);
        s.Text(x + w / 2, 85, "点一张进大图调", 9, "secondary");
    }
}
