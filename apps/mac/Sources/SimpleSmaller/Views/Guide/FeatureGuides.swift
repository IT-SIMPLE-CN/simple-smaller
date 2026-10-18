import SwiftUI

/// 功能小教学的每一步：标题、说明、插图（设计稿 V2.2 · E 图片 / E2 视频 / E3 PDF，文案逐字照稿）。
/// 图片按「压缩和裁切合一」重写（共用说明）；视频第 2 步按进入时的分段换。
struct GuidePage {
    let title: String
    let text: String
    let sketch: Sketch
}

@MainActor
enum FeatureGuides {
    static func pages(_ type: MediaType, videoSegment: Workspace.VideoSegment) -> [GuidePage] {
        switch type {
        case .image: [imageFiles, imageRecipes, imageRatio, start("按 ⌘↩ 也行。结果存在原文件旁边，原文件不改动；做完可以一键撤销。")]
        case .video: [videoTabs, videoSecond(videoSegment), videoRecipes, videoStart]
        case .pdf: [pdfTabs, pdfFiles, pdfLevels, start("按 ⌘↩ 也行。压完反而变大的文件会自动保留原件。")]
        }
    }

    private static let w: CGFloat = 308, h: CGFloat = 92, fit: CGFloat = 302
    private static func sketch(_ svg: String, _ texts: [SketchText]) -> Sketch {
        Sketch(width: w, height: h, svg: svg, texts: texts, fitWidth: fit)
    }

    private static let sub = Bento.secondary

    // MARK: 共用

    /// 「这一批都在这」：三张缩略图进虚线框，左上一张正被拖进来（视频版缩略图带播放标）。
    private static func batchSketch(video: Bool) -> Sketch {
        let grads = [("a1", "#D8CFC4", "#8C7B6B"), ("a2", "#C9D6CF", "#4F6B5D"), ("a3", "#E6D9C0", "#A2875A"), ("a4", "#D6D6E2", "#5E5E7A")]
            .map { ##"<linearGradient id="\##($0.0)" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="\##($0.1)"/><stop offset="1" stop-color="\##($0.2)"/></linearGradient>"## }
            .joined()
        func play(_ cx: CGFloat, _ cy: CGFloat) -> String {
            video ? ##"<circle cx="\##(cx)" cy="\##(cy)" r="7" fill="#FFFFFF" fill-opacity="0.9"/><path d="M\##(cx - 2) \##(cy - 3.5)v7l5.5-3.5z" fill="#111214"/>"## : ""
        }
        let svg = """
        <defs>\(grads)</defs>
        <rect x="92" y="34" width="46" height="34" rx="5" fill="url(#a1)"/>\(play(115, 51))
        <rect x="144" y="34" width="46" height="34" rx="5" fill="url(#a2)"/>\(play(167, 51))
        <rect x="196" y="34" width="46" height="34" rx="5" fill="url(#a3)"/>\(play(219, 51))
        <rect x="88" y="30" width="158" height="54" rx="9" fill="none" stroke="{line}" stroke-dasharray="4 4"/>
        <g transform="rotate(-8 60 22)"><rect x="40" y="6" width="40" height="30" rx="5" fill="url(#a4)"/>\(play(60, 21))</g>
        <path d="M84 24 C 100 20, 110 26, 116 40" fill="none" stroke="#EF3922" stroke-width="2" stroke-dasharray="3 3"/>
        <path d="M70 26l0 19.2l4.8 -4.8l4.0 8.8l3.2 -1.6l-4.0 -8.8l6.4 0z" fill="#111214" stroke="#FFFFFF" stroke-width="1.4" stroke-linejoin="round"/>
        """
        return sketch(svg, [SketchText(254, 57, "+1", size: 12, weight: .bold, color: .brand, anchor: .start)])
    }

    /// 最后一步「确认后点开始」/「一批一起出」：开始块 + ⌘ ↩ 两颗键。
    private static var startSketch: Sketch {
        let svg = """
        <rect x="20" y="20" width="168" height="52" rx="16" fill="#EF3922"/>
        <circle cx="164" cy="46" r="16" fill="#FFFFFF"/>
        <path d="M157 46h13M165 41l5 5-5 5" fill="none" stroke="#EF3922" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        <rect x="206" y="35" width="30" height="26" rx="6" fill="{key}"/><rect x="206" y="33" width="30" height="26" rx="6" fill="{card}"/>
        <rect x="254" y="35" width="34" height="26" rx="6" fill="{key}"/><rect x="254" y="33" width="34" height="26" rx="6" fill="{card}"/>
        """
        return sketch(svg, [
            SketchText(40, 46, "开始", size: 18, weight: .heavy, color: .white, anchor: .start),
            SketchText(221, 46, "⌘", size: 13, weight: .semibold),
            SketchText(244, 46, "+", size: 12, weight: .semibold, color: sub),
            SketchText(271, 46, "↩", size: 13, weight: .semibold),
        ])
    }

    private static func start(_ text: String) -> GuidePage {
        GuidePage(title: "确认后点开始", text: text, sketch: startSketch)
    }

    /// 顶部大标签的缩略：白底胶囊，选中墨黑 + 红点。
    private static func tabsSketch(_ items: [(String, CGFloat)], selected: Int, box: (CGFloat, CGFloat), pill: (CGFloat, CGFloat), caption: (CGFloat, String)) -> Sketch {
        let svg = """
        <rect x="\(box.0)" y="\(box.1)" width="\(308 - 2 * box.0)" height="\(box.1 == 28 ? 36 : 34)" rx="\(box.1 == 28 ? 18 : 17)" fill="{card}"/>
        <rect x="\(pill.0)" y="\(pill.1)" width="52" height="\(box.1 == 28 ? 28 : 26)" rx="\(box.1 == 28 ? 14 : 13)" fill="{ink}"/>
        <circle cx="\(pill.0 + 13)" cy="\(pill.1 + (box.1 == 28 ? 14 : 13))" r="5" fill="#EF3922"/>
        """
        let cy = box.1 + (box.1 == 28 ? 18 : 17)
        var texts = items.enumerated().map { i, item in
            SketchText(item.1, cy, item.0, size: 11, weight: i == selected ? .bold : .regular, color: i == selected ? Bento.onInk : .primary)
        }
        texts.append(SketchText(154, caption.0, caption.1, size: 9.5, color: sub))
        return sketch(svg, texts)
    }

    // MARK: 图片（合一版）

    private static var imageFiles: GuidePage {
        GuidePage(title: "这一批都在这", text: "把图片或整个文件夹拖进来，随时还能往里加。选中按空格快速查看。", sketch: batchSketch(video: false))
    }

    private static var imageRecipes: GuidePage {
        let svg = """
        <rect x="14" y="34" width="72" height="24" rx="12" fill="{ink}"/><circle cx="76" cy="46" r="3" fill="#EF3922"/>
        <rect x="92" y="34" width="70" height="24" rx="12" fill="{card}"/>
        <rect x="168" y="34" width="64" height="24" rx="12" fill="{card}"/>
        <rect x="238" y="34" width="58" height="24" rx="12" fill="{alertFill}"/>
        <path d="M232 46 h4" stroke="#EF3922"/>
        """
        return GuidePage(title: "先选一个常用", text: "一点就把下面全部填好。改了任何参数，胶囊上出现红点，可以「存为常用」留着下次用。", sketch: sketch(svg, [
            SketchText(46, 46, "网站用", size: 10.5, weight: .semibold, color: Bento.onInk),
            SketchText(127, 46, "小红书 3:4", size: 10.5),
            SketchText(200, 46, "发群预览", size: 10.5),
            SketchText(267, 46, "存为常用", size: 10, weight: .semibold, color: Bento.alertText),
            SketchText(50, 72, "改过 → 红点", size: 9.5, color: sub),
        ]))
    }

    /// 合一版新增：「原比例 / 小红书 3:4」两颗胶囊 + 一张画着裁切框的缩略图。
    private static var imageRatio: GuidePage {
        let svg = """
        <defs><linearGradient id="r1" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#E6D9C0"/><stop offset="1" stop-color="#A2875A"/></linearGradient></defs>
        <rect x="14" y="22" width="62" height="24" rx="12" fill="{card}"/>
        <rect x="82" y="22" width="78" height="24" rx="12" fill="{ink}"/>
        <rect x="190" y="10" width="96" height="66" rx="6" fill="url(#r1)"/>
        <path d="M190 16a6 6 0 0 1 6-6h19.5v66h-19.5a6 6 0 0 1-6-6z" fill="#000000" fill-opacity="0.38"/>
        <path d="M260.5 10h19.5a6 6 0 0 1 6 6v54a6 6 0 0 1-6 6h-19.5z" fill="#000000" fill-opacity="0.38"/>
        <rect x="215.5" y="10" width="45" height="66" rx="2" fill="none" stroke="#EF3922" stroke-width="2.4"/>
        <path d="M244 50l0 19.2l4.8 -4.8l4.0 8.8l3.2 -1.6l-4.0 -8.8l6.4 0z" fill="#111214" stroke="#FFFFFF" stroke-width="1.4" stroke-linejoin="round"/>
        """
        return GuidePage(title: "比例决定裁不裁", text: "选「原比例」只压缩；选了比例就一起裁，每张的框直接画在缩略图上，点一张进大图调。", sketch: sketch(svg, [
            SketchText(45, 34, "原比例", size: 10.5),
            SketchText(121, 34, "小红书 3:4", size: 10.5, weight: .semibold, color: Bento.onInk),
            SketchText(45, 60, "只压缩", size: 9.5, color: sub),
            SketchText(121, 60, "一起裁", size: 9.5, color: sub),
            SketchText(238, 86, "点一张进大图调", size: 9.5, color: sub),
        ]))
    }

    // MARK: 视频

    private static var videoTabs: GuidePage {
        GuidePage(title: "一个视频，四件事", text: "顶部切换压缩、剪辑、序列帧、实况。切过去文件还在，不用重新拖。",
                  sketch: tabsSketch([("压缩", 76), ("剪辑", 132.5), ("序列帧", 182), ("实况", 232)], selected: 1, box: (50, 28), pill: (100, 32), caption: (82, "文件留着，换个做法")))
    }

    /// 第 2 步：剪辑照稿框预览 + 时间线；压缩、序列帧框左边文件卡。
    private static func videoSecond(_ segment: Workspace.VideoSegment) -> GuidePage {
        switch segment {
        case .edit:
            var frames = ""
            for i in 0..<10 {
                let x = 20 + i * 27
                frames += ##"<rect x="\##(x)" y="30" width="25" height="34" rx="3" fill="\##((4...6).contains(i) ? "#5A4E44" : "{thumb}")"/>"##
            }
            let svg = frames + """
            <rect x="126" y="26" width="81" height="42" rx="6" fill="none" stroke="#EF3922" stroke-width="3"/>
            <rect x="122" y="26" width="6" height="42" rx="3" fill="#EF3922"/><rect x="205" y="26" width="6" height="42" rx="3" fill="#EF3922"/>
            """
            return GuidePage(title: "拖两端截一段", text: "时间线两端拖动截取，单击一帧设为封面；画面里的框可以拖，决定保留哪块。",
                             sketch: sketch(svg, [SketchText(166, 80, "截取 00:18 – 01:03", size: 9.5, weight: .semibold, color: .brand)]))
        case .compress:
            return GuidePage(title: "这一批都在这", text: "把视频拖进来，随时还能往里加。要截一段、裁画面，切到顶部的「剪辑」。", sketch: batchSketch(video: true))
        case .frames:
            return GuidePage(title: "这一批都在这", text: "把视频拖进来，随时还能往里加。", sketch: batchSketch(video: true))
        case .live:
            // 时间线上一个短红框（会动的那一段），框里一条白线 + 红色小标是封面。
            var frames = ""
            for i in 0..<10 {
                let x = 20 + i * 27
                frames += ##"<rect x="\##(x)" y="30" width="25" height="34" rx="3" fill="\##((4...5).contains(i) ? "#5A4E44" : "{thumb}")"/>"##
            }
            let svg = frames + """
            <rect x="126" y="26" width="54" height="42" rx="6" fill="none" stroke="#EF3922" stroke-width="3"/>
            <rect x="122" y="26" width="6" height="42" rx="3" fill="#EF3922"/><rect x="178" y="26" width="6" height="42" rx="3" fill="#EF3922"/>
            <rect x="151" y="30" width="2" height="34" fill="#FFFFFF"/>
            <rect x="145" y="12" width="14" height="11" rx="2.5" fill="#EF3922"/>
            """
            return GuidePage(title: "选一段，挑封面", text: "红框里的一段（最长 3 秒）就是会动的部分，拖动换位置；单击时间轴选封面，照片用这一帧。",
                             sketch: sketch(svg, [SketchText(153, 80, "会动 3 秒 · 封面", size: 9.5, weight: .semibold, color: .brand)]))
        }
    }

    private static var videoRecipes: GuidePage {
        let svg = """
        <rect x="14" y="34" width="70" height="24" rx="12" fill="{card}"/>
        <rect x="90" y="34" width="72" height="24" rx="12" fill="{ink}"/>
        <rect x="168" y="34" width="64" height="24" rx="12" fill="{card}"/>
        <rect x="238" y="34" width="52" height="24" rx="12" fill="{card}"/>
        """
        return GuidePage(title: "再选一个常用", text: "发群 25 MB、抖音 9:16……一点就把比例、分辨率、画质都填好，改了可以存为常用。", sketch: sketch(svg, [
            SketchText(49, 46, "发群 25 MB", size: 10.5),
            SketchText(126, 46, "抖音 9:16", size: 10.5, weight: .semibold, color: Bento.onInk),
            SketchText(200, 46, "小红书 3:4", size: 10.5),
            SketchText(264, 46, "转 GIF", size: 10.5),
            SketchText(154, 72, "比例 · 分辨率 · 画质一次填好", size: 9.5, color: sub),
        ]))
    }

    private static var videoStart: GuidePage {
        GuidePage(title: "一批一起出", text: "标题旁 ‹ › 切换看第几个。点开始或按 ⌘↩，这一批按同一套设置出。", sketch: startSketch)
    }

    // MARK: PDF

    private static var pdfTabs: GuidePage {
        GuidePage(title: "PDF 能做五件事", text: "压缩、合并、拆分、转图片、图片转 PDF 都在顶部切换。文件留着，换个做法不用重新拖。",
                  sketch: tabsSketch([("压缩", 52), ("合并", 95.5), ("拆分", 139.5), ("转图片", 189), ("图片转PDF", 253.5)], selected: 0,
                                     box: (15.5, 30), pill: (19.5, 34), caption: (80, "文件留着，换个做法不用重拖")))
    }

    private static var pdfFiles: GuidePage {
        var pages = ""
        let cells: [(CGFloat, CGFloat, Bool, Bool)] = [(186, 16, true, false), (214, 16, false, false), (242, 16, false, false), (270, 16, true, true),
                                                        (186, 48, false, false), (214, 48, false, false), (242, 48, true, false), (270, 48, false, false)]
        for c in cells {
            pages += ##"<rect x="\##(c.0)" y="\##(c.1)" width="24" height="28" rx="3" fill="\##(c.2 ? "#D8CFC4" : "{card}")" stroke="\##(c.3 ? "#EF3922" : "{edge}")" stroke-width="\##(c.3 ? 2 : 1)"/>"##
        }
        let svg = """
        <rect x="20" y="14" width="150" height="18" rx="6" fill="{ink}"/>
        <rect x="20" y="36" width="150" height="18" rx="6" fill="{card}"/>
        <rect x="20" y="58" width="150" height="18" rx="6" fill="{card}"/>
        """ + pages
        return GuidePage(title: "先看清这一批", text: "每个文件的页数、大小一目了然，下面是页面预览。排序和删页在「合并」里做。", sketch: sketch(svg, [
            SketchText(30, 23, "品牌提案.pdf", size: 9.5, weight: .semibold, color: Bento.onInk, anchor: .start),
            SketchText(160, 23, "48 页", size: 9, color: Bento.onInkSecondary, anchor: .end),
            SketchText(30, 45, "报价单.pdf", size: 9.5, anchor: .start),
            SketchText(160, 45, "2 页", size: 9, color: sub, anchor: .end),
            SketchText(30, 67, "合同扫描件.pdf", size: 9.5, anchor: .start),
            SketchText(160, 67, "12 页", size: 9, color: sub, anchor: .end),
            SketchText(240, 84, "页面预览", size: 9, color: sub),
        ]))
    }

    private static var pdfLevels: GuidePage {
        let svg = """
        <rect x="14" y="22" width="66" height="24" rx="12" fill="{card}"/>
        <rect x="86" y="22" width="66" height="24" rx="12" fill="{ink}"/>
        <rect x="158" y="22" width="66" height="24" rx="12" fill="{card}"/>
        <rect x="230" y="22" width="64" height="24" rx="12" fill="{card}"/>
        <path d="M30 74 H 278" stroke="{line}" stroke-width="2"/><path d="M30 74 H 120" stroke="#EF3922" stroke-width="2"/>
        """
        return GuidePage(title: "选一个压缩档", text: "屏幕阅读最小，打印保真最清楚；要卡邮件上限就选「指定大小」，填个目标。", sketch: sketch(svg, [
            SketchText(47, 34, "屏幕阅读", size: 10.5),
            SketchText(119, 34, "邮件附件", size: 10.5, weight: .semibold, color: Bento.onInk),
            SketchText(191, 34, "打印保真", size: 10.5),
            SketchText(262, 34, "指定大小", size: 10.5),
            SketchText(46, 60, "最小", size: 9.5, color: sub),
            SketchText(262, 60, "填目标", size: 9.5, color: sub),
            SketchText(190, 60, "最清楚", size: 9.5, color: sub),
        ]))
    }
}
