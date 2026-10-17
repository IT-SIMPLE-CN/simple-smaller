import SwiftUI

/// 工作区工具栏：左边返回首页；正中是业务分段；完成页右边是「撤销这一批」。
struct WorkToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            Button { model.showHome = true } label: {
                Image(systemName: "arrow.left")
            }
            .help("回首页（这一批文件保留）")
            .accessibilityLabel("回首页")
        }
        SegmentToolbarItem(ws: ws)
        if ws.showResult, !ws.running {
            ToolbarItem(placement: .primaryAction) {
                Button("撤销这一批") { model.undo(ws) }
                    .help("把这一批写出的文件移到废纸篓，原文件不动")
            }
        }
    }
}

/// 居中的业务分段。macOS 26 起工具栏会给每项套一层玻璃胶囊，分段自带白底，这里关掉。
struct SegmentToolbarItem: ToolbarContent {
    let ws: Workspace

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws) }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws) }
        }
    }
}

struct SegmentPicker: View {
    let ws: Workspace

    var body: some View {
        @Bindable var ws = ws
        Group {
            switch ws.type {
            case .image:
                TopTabs(selection: $ws.imageSegment, options: Workspace.ImageSegment.allCases.map { ($0, $0.rawValue) })
            case .video:
                TopTabs(selection: $ws.videoSegment, options: Workspace.VideoSegment.allCases.map { ($0, $0.rawValue) })
            case .pdf:
                TopTabs(selection: $ws.pdfSegment, options: Workspace.PdfSegment.allCases.map { ($0, $0.rawValue) })
            }
        }
        .accessibilityLabel("视图")
        .disabled(ws.running)
        .onChange(of: ws.imageSegment) { _, new in
            ws.enterImageSegment(new)
            ws.showResult = false
        }
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
