import SwiftUI

/// 列表下方的状态栏，始终一行：
/// 左边一句这一批的状态（处理前是预估、处理中是进度、做完是结果）；
/// 右边是完成后的操作；空状态时写一句入口提示。「开始」在检查器底部，与这条同高。
struct StatusBar: View {
    static let height: CGFloat = 52

    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        HStack(spacing: 14) {
            status
                .layoutPriority(1)
            Spacer(minLength: 12)
            trailing
        }
        .font(.system(size: 12))
        .foregroundStyle(.secondary)
        .lineLimit(1)
        .padding(.horizontal, 16)
        .frame(height: StatusBar.height)
        .background(.bar)
        .overlay(alignment: .top) { Divider() }
        .animation(.smooth(duration: 0.25), value: phase)
    }

    private enum Phase: Equatable { case empty, ready, running, finished }

    private var phase: Phase {
        if ws.files.isEmpty { return .empty }
        if ws.running { return .running }
        if ws.files.allSatisfy({ [.done, .kept].contains($0.state) }) || (ws.finishedCount > 0 && ws.job?.isFinished() == true) {
            return .finished
        }
        return .ready
    }

    @ViewBuilder private var status: some View {
        switch phase {
        case .empty:
            Text("全部在本机处理")
        case .ready:
            Text("\(ws.files.filter { $0.state == .waiting }.count) 个待处理").monospacedDigit()
        case .running:
            HStack(spacing: 10) {
                Text("\(ws.finishedCount) / \(ws.files.count)").monospacedDigit().foregroundStyle(.primary)
                ProgressView(value: Double(ws.finishedCount), total: Double(max(1, ws.files.count)))
                    .progressViewStyle(.linear)
                    .tint(.brand)
                    .frame(width: 140)
            }
        case .finished:
            HStack(spacing: 6) {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.saving)
                Text("\(ws.finishedCount) 个完成").foregroundStyle(.primary)
                Text("·")
                Text(sizes).monospacedDigit()
            }
        }
    }

    @ViewBuilder private var trailing: some View {
        switch phase {
        case .finished:
            HStack(spacing: 16) {
                Button("在访达中显示") { model.reveal(ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }) }
                Button("撤销") { model.undo(ws) }
                    .help("把这一批写出的文件移到废纸篓")
            }
            .buttonStyle(.link)
            .foregroundStyle(.secondary)
        case .running:
            EmptyView()
        case .empty:
            // 只在空状态给一句入口提示；有文件后不常驻教学文案。
            ViewThatFits(in: .horizontal) {
                Text(hint).foregroundStyle(.tertiary)
                Color.clear.frame(width: 0, height: 0)
            }
        case .ready:
            EmptyView()
        }
    }

    /// 「30.4 MB → 483 KB（−98%）」：只统计写出的结果，保留原文件的按原大小计。
    private var sizes: String {
        let before = ws.files.reduce(UInt64(0)) { $0 + $1.bytes }
        let after = ws.files.reduce(UInt64(0)) { sum, f in
            switch f.state {
            case .done: sum + f.afterBytes
            default: sum + f.bytes
            }
        }
        guard before > 0 else { return "—" }
        return "\(Format.size(before)) → \(Format.size(after))（\(Format.change(before, after))）"
    }

    private var hint: String {
        ws.type == .image ? "访达中按住 ⌥ 点文件可直接处理" : "也可以把文件拖到 Dock 图标上"
    }
}
