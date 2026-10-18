import SwiftUI
import UniformTypeIdentifiers

/// 红色「开始」块，四种状态大小不变：
/// 没文件置灰 →「开始」→ 处理中（进度，点按停止）→「已完成」（改了设置又变回「开始」）。
struct StartBlock: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    private enum Phase { case empty, ready, running, done }

    private var phase: Phase {
        if ws.files.isEmpty || !ws.canStart { return .empty }
        if ws.running { return .running }
        if ws.isUpToDate { return .done }
        return .ready
    }

    var body: some View {
        Button(action: tap) {
            HStack(spacing: 12) {
                content
                Spacer(minLength: 0)
                knob
            }
            .padding(.leading, 22)
            .padding(.trailing, 14)
            .frame(maxWidth: .infinity, minHeight: 84, maxHeight: .infinity)
            .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(background))
            .shadow(color: Color.brand.opacity(phase == .ready || phase == .running ? 0.3 : 0), radius: 12, y: 8)
            .contentShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .keyboardShortcut(.return, modifiers: .command)
        .disabled(phase == .empty || phase == .done)
        .animation(.spring(response: 0.4, dampingFraction: 0.85), value: phase)
        .help(help)
        .accessibilityLabel(accessibility)
    }

    private func tap() {
        switch phase {
        case .ready: model.start(ws)
        case .running: model.cancel(ws)
        default: break
        }
    }

    private var background: Color {
        switch phase {
        case .empty: Bento.fill
        case .ready, .running: .brand
        case .done: Bento.ink
        }
    }

    private var foreground: Color {
        switch phase {
        case .empty: Bento.secondary
        case .ready, .running: .white
        case .done: Bento.onInk
        }
    }

    @ViewBuilder private var content: some View {
        switch phase {
        case .running:
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("处理中").font(.system(size: 24, weight: .bold))
                    Text(verbatim: "\(ws.finishedCount) / \(ws.files.count)").font(Bento.number(20))
                }
                ProgressBar(value: Double(ws.finishedCount) + runningFraction, total: Double(max(1, ws.files.count)))
                    .frame(height: 6)
                Text("停止 · ⌘.").font(.system(size: 12)).opacity(0.95)
            }
            .foregroundStyle(.white)
        default:
            VStack(alignment: .leading, spacing: 4) {
                Text(phase == .done ? "已完成" : "开始")
                    .font(.system(size: 30, weight: .bold))
                    .tracking(1)
                ViewThatFits(in: .horizontal) {
                    Text(subtitle + shortcut).lineLimit(1)
                    Text(subtitle).lineLimit(1)
                    Text(subtitle).lineLimit(2)
                }
                .font(.system(size: 12)).opacity(0.95).monospacedDigit()
            }
            .foregroundStyle(foreground)
        }
    }

    private var knob: some View {
        ZStack {
            Circle().fill(phase == .empty ? Bento.card : (phase == .done ? Bento.onInk.opacity(0.14) : .white))
            Image(systemName: phase == .running ? "stop.fill" : (phase == .done ? "checkmark" : "arrow.right"))
                .font(.system(size: phase == .running ? 18 : 22, weight: .semibold))
                .foregroundStyle(phase == .empty ? Bento.secondary : (phase == .done ? Bento.onInk : Color.brand))
        }
        .frame(width: 56, height: 56)
        .accessibilityHidden(true)
    }

    /// 正在处理的那一个走到哪了。
    private var runningFraction: Double {
        ws.files.compactMap { if case .running(let f) = $0.state { return f }; return nil }.reduce(0, +)
    }

    private var subtitle: String {
        switch phase {
        case .empty: return ws.files.isEmpty ? "先把文件拖进来" : (ws.blockReason ?? "")
        case .done: return "改了设置可以再开始"
        default: return ws.startSummary
        }
    }

    private var shortcut: String { phase == .ready ? " · ⌘↩" : "" }

    private var help: String {
        switch phase {
        case .running: "停止处理（⌘.）"
        case .done: "这一批已按当前设置处理完；改设置或加文件后可以再开始"
        default: "按当前设置处理全部文件（⌘↩）"
        }
    }

    private var accessibility: String {
        switch phase {
        case .empty: "开始，\(subtitle)"
        case .ready: "开始，\(ws.startSummary)"
        case .running: "处理中 \(ws.finishedCount) / \(ws.files.count)，按下停止"
        case .done: "已完成"
        }
    }
}

/// 白色进度条（红底块上用）。
struct ProgressBar: View {
    let value: Double
    let total: Double
    var track: Color = .white.opacity(0.32)
    var bar: Color = .white

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(track)
                Capsule().fill(bar).frame(width: max(6, geo.size.width * min(1, value / max(1, total))))
            }
        }
        .animation(.smooth(duration: 0.3), value: value)
        .accessibilityHidden(true)
    }
}
