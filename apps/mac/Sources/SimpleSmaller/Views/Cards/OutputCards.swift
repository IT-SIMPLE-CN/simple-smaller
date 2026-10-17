import SwiftUI
import UniformTypeIdentifiers

/// 右下一格：输出（文件名、去掉位置信息）与红色「开始」块。
struct OutputStartCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "folder", title: "输出", subtitle: ws.running ? "处理中，设置先锁住" : "原文件不改动")
                Group {
                    if let fixed = OutputNaming.fixedName(ws) {
                        BentoRow(label: "文件名") {
                            Text(verbatim: fixed).font(.system(size: 12, design: .monospaced)).foregroundStyle(Bento.secondary)
                                .lineLimit(1).truncationMode(.head)
                        }
                    } else {
                        BentoRow(label: "文件名") {
                            TextField("", text: $ws.draft.naming.template)
                                .textFieldStyle(.plain)
                                .font(.system(size: 12, design: .monospaced))
                                .foregroundStyle(Bento.secondary)
                                .multilineTextAlignment(.trailing)
                                .accessibilityLabel("文件名规则")
                        }
                    }
                    if ws.type != .pdf {
                        BentoToggleRow(label: "去掉位置与设备信息", isOn: $ws.draft.stripMetadata)
                    }
                }
                .disabled(ws.running)
                StartBlock(ws: ws)
                    .padding(.top, 4)
            }
        }
    }
}

enum OutputNaming {
    /// 拆分、转图片按固定规则命名（文件夹 + 编号），不用配方里的模板。
    @MainActor static func fixedName(_ ws: Workspace) -> String? {
        if ws.type == .video, ws.videoSegment == .frames { return "{name}_frames/{name}_0001" }
        guard ws.type == .pdf else { return nil }
        switch ws.pdfSegment {
        case .split: return "{name}-拆分/{name}-01"
        case .toImages: return ws.draft.pdf.longImage ? "{name}-长图" : "{name}-图片/{name}-01"
        default: return nil
        }
    }
}
