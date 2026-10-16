import SwiftUI

/// 窗口底部的状态栏：完成进度、前后体积、操作提示。
struct StatusBar: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        HStack(spacing: 12) {
            if ws.files.isEmpty {
                Text("在本机处理")
                Spacer()
                Text(ws.type == .image ? "访达中按住 ⌥ 点文件可直接处理" : "也可以把文件拖到 Dock 图标上")
            } else {
                if ws.running || ws.finishedCount > 0 {
                    Text("\(ws.finishedCount) / \(ws.files.count) 完成").monospacedDigit()
                    ProgressView(value: Double(ws.finishedCount), total: Double(max(1, ws.files.count)))
                        .progressViewStyle(.linear)
                        .tint(.brand)
                        .frame(width: 120)
                }
                Text(totals).monospacedDigit()
                if !ws.running, ws.finishedCount > 0, let job = ws.job, job.isFinished() {
                    Button("在访达中显示") { model.reveal(ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }) }
                        .buttonStyle(.link)
                    Button("撤销这一批") { model.undo(ws) }
                        .buttonStyle(.link)
                }
                Spacer()
                Text(hint)
            }
        }
        .font(.system(size: 11))
        .foregroundStyle(.secondary)
        .padding(.horizontal, 16)
        .frame(height: 28)
        .background(.bar)
        .overlay(alignment: .top) { Divider() }
    }

    private var totals: String {
        let before = ws.files.reduce(UInt64(0)) { $0 + $1.bytes }
        let doneAfter = ws.files.reduce(UInt64(0)) { sum, f in
            switch f.state {
            case .done: sum + f.afterBytes
            case .kept: sum + f.bytes
            default: sum + (f.estimateKept ? f.bytes : (f.estimate ?? f.bytes))
            }
        }
        let allDone = ws.files.allSatisfy { [.done, .kept].contains($0.state) }
        let estimated = ws.files.contains { $0.estimate != nil || $0.state == .done }
        guard estimated, before > 0 else { return Format.size(before) }
        let pct = Format.change(before, doneAfter)
        return "\(Format.size(before)) → \(Format.size(doneAfter))\(allDone ? " \(pct)" : "（预估）")"
    }

    private var hint: String {
        switch ws.type {
        case .image: ws.imageSegment == .crop ? "↩ 确认并到下一张 · ←/→ 切换" : "选中行看前后对比 · 双击在访达中显示"
        case .video: ws.videoSegment == .compress ? "按码率估算的上限，画面简单时会更小" : "双击在访达中显示"
        case .pdf: ws.pdfSegment == .merge ? "拖动可排序 · 选中后按 Delete 删除" : "双击在访达中显示"
        }
    }
}
