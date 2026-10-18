import SwiftUI

/// 工作区工具栏：左边返回首页；正中是功能页的大标签（视频、PDF；图片只有一页不放）；完成页右边是「撤销这一批」。
struct WorkToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    /// 窗口窄、放不下标签文字时只显示图标。
    var compactTabs = false

    var body: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            Button { model.showHome = true } label: {
                Image(systemName: "arrow.left")
            }
            .help("回首页（这一批文件保留）")
            .accessibilityLabel("回首页")
        }
        SegmentToolbarItem(ws: ws, compact: compactTabs)
        if ws.showResult, !ws.running {
            ToolbarItem(placement: .primaryAction) {
                Button("撤销这一批") { model.undo(ws) }
                    .help("把这一批写出的文件移到废纸篓，原文件不动")
            }
        }
        if GuideChip.visible(ws) {
            GuideChipItem(type: ws.type)
        }
    }
}

/// 标题栏右上角常驻的「? 再看一遍教学」（设计稿 V2.2 · E 关掉后）：墨黑胶囊，左边一颗品牌红圆底问号。
/// 有文件的功能页才有（没文件时教学指着空卡片讲不清）；完成页的卡片不同，也不放。
struct GuideChip: View {
    let type: MediaType

    static func visible(_ ws: Workspace) -> Bool {
        !ws.files.isEmpty && !ws.showResult && !ws.running
    }

    /// 大约多宽（顶部大标签能不能放下文字的估算用）：6 + 24 + 8 + 字宽 + 14。
    @MainActor static var width: CGFloat {
        ("再看一遍教学" as NSString).size(withAttributes: [.font: NSFont.systemFont(ofSize: 12)]).width.rounded(.up) + 52
    }

    var body: some View {
        Button { GuideCenter.shared.showFeature(type) } label: {
            HStack(spacing: 8) {
                Text("?")
                    .font(.system(size: 12, weight: .bold))
                    .foregroundStyle(.white)
                    .frame(width: 24, height: 24)
                    .background(Circle().fill(Color.brand))
                Text("再看一遍教学")
                    .font(.system(size: 12))
                    .foregroundStyle(Bento.onInk)
                    .fixedSize()
            }
            .padding(.leading, 6)
            .padding(.trailing, 14)
            .frame(height: 34)
            .background(Capsule().fill(Bento.ink))
            .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
        .help("从第 1 步再看一遍\(type.title)怎么用")
        .accessibilityLabel("再看一遍教学")
    }
}

/// macOS 26 起工具栏给每项套玻璃胶囊，胶囊自带墨黑底，这里关掉。
struct GuideChipItem: ToolbarContent {
    let type: MediaType

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .primaryAction) { GuideChip(type: type) }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .primaryAction) { GuideChip(type: type) }
        }
    }
}

/// 居中的大标签。macOS 26 起工具栏会给每项套一层玻璃胶囊，大标签自带白底，这里关掉。
struct SegmentToolbarItem: ToolbarContent {
    let ws: Workspace
    var compact = false

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws, compact: compact) }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws, compact: compact) }
        }
    }
}

extension Workspace.VideoSegment {
    var glyph: LineIcon.Glyph {
        switch self {
        case .compress: .compress
        case .edit: .scissors
        case .frames: .frames
        case .live: .live
        }
    }
}

extension Workspace.PdfSegment {
    var glyph: LineIcon.Glyph {
        switch self {
        case .compress: .compress
        case .merge: .merge
        case .split: .split
        case .toImages: .toImages
        case .fromImages: .fromImages
        }
    }
}

struct SegmentPicker: View {
    let ws: Workspace
    var compact = false

    /// 这一类的大标签文字（估算宽度用）；图片没有。
    static func titles(_ type: MediaType) -> [String] {
        switch type {
        case .image: []
        case .video: Workspace.VideoSegment.allCases.map(\.rawValue)
        case .pdf: Workspace.PdfSegment.allCases.map(\.rawValue)
        }
    }

    var body: some View {
        @Bindable var ws = ws
        Group {
            switch ws.type {
            case .image:
                // 图片只有一页：比例在「画面」卡里选，不放大标签。
                EmptyView()
            case .video:
                FeatureTabs(selection: $ws.videoSegment, options: Workspace.VideoSegment.allCases.map { ($0, $0.rawValue, $0.glyph) },
                            compact: compact, label: "视频能做的事")
            case .pdf:
                FeatureTabs(selection: $ws.pdfSegment, options: Workspace.PdfSegment.allCases.map { ($0, $0.rawValue, $0.glyph) },
                            compact: compact, label: "PDF 能做的事")
            }
        }
        .guideTarget(.tabs)
        .disabled(ws.running)
        .onChange(of: ws.videoSegment) { _, new in
            ws.enterVideoSegment(new)
            ws.showResult = false
        }
        .onChange(of: ws.pdfSegment) { _, new in
            ws.enterPdfSegment(new)
            ws.showResult = false
        }
    }
}
