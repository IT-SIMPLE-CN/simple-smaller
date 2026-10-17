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
            EmptyView()
        case .ready:
            if ws.type == .image, ws.imageSegment == .crop {
                // 裁切时写确认进度（设计稿 03 顶栏的「已确认 n / 12」）。
                let items = ws.cropItems
                Text("已确认 \(items.filter { ws.cropConfirmed.contains($0.url) }.count) / \(items.count)").monospacedDigit()
            } else if ws.type == .pdf, ws.pdfSegment == .merge {
                Text(verbatim: "合并成 1 个文件 · \(ws.pageOrder.count) 页").monospacedDigit()
            } else if ws.type == .video, ws.videoSegment == .frames {
                // 序列帧：将生成多少张、约多大（设计稿 04b 底栏）。
                Text(verbatim: framesSummary).monospacedDigit()
            }
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
                Image(systemName: okCount > 0 ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                    .foregroundStyle(okCount > 0 ? Color.saving : Color.warning)
                if okCount > 0 {
                    Text("\(okCount) 个完成").foregroundStyle(.primary)
                }
                if failedCount > 0 {
                    if okCount > 0 { Text("·") }
                    Text("\(failedCount) 个失败").foregroundStyle(Color.warning)
                }
                if okCount > 0 {
                    Text("·")
                    Text(sizes).monospacedDigit()
                }
            }
        }
    }

    @ViewBuilder private var trailing: some View {
        switch phase {
        case .finished:
            HStack(spacing: 16) {
                if failedCount > 0 {
                    Button("重试失败的") { model.retryFailed(ws) }
                        .help("按当前配方再处理一遍失败的文件；可以先改配方")
                }
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
            // 序列帧张数太多时提醒；左边已经写了总张数与体积。
            if ws.type == .video, ws.videoSegment == .frames, framesTotal > 200 {
                Text("张数较多，网页动画建议 60–120 张").foregroundStyle(Color.warning)
            }
        }
    }

    /// 「30.4 MB → 483 KB（−98%）」：只统计写出的结果，保留原文件的按原大小计。
    private var okCount: Int { ws.files.filter { [.done, .kept].contains($0.state) }.count }
    private var failedCount: Int { ws.files.filter { if case .failed = $0.state { return true }; return false }.count }

    /// 只算处理成功的文件（失败的不进前后体积）。
    private var sizes: String {
        let ok = ws.files.filter { [.done, .kept].contains($0.state) }
        let before = ok.reduce(UInt64(0)) { $0 + $1.bytes }
        let after = ok.reduce(UInt64(0)) { $0 + ($1.state == .done ? $1.afterBytes : $1.bytes) }
        guard before > 0 else { return "—" }
        return "\(Format.size(before)) → \(Format.size(after))（\(Format.change(before, after))）"
    }

    private var framesTotal: Int { ws.videoItems.reduce(0) { $0 + ws.frameCount($1) } }

    /// 每张的估算体积：按输出像素与格式粗算（WebP 1920 宽约 140 KB，与设计稿一致）。
    private var bytesPerFrame: Double {
        guard let item = ws.currentVideo else { return 140_000 }
        let box = ws.videoCropBox(item)?.size ?? CGSize(width: item.width, height: item.height)
        let w = Double(min(CGFloat(ws.draft.video.frameWidth ?? Int(box.width)), box.width))
        let h = w * Double(box.height / max(1, box.width))
        let bpp: Double = switch ws.draft.video.frameFormat {
        case .png: 1.6
        case .jpeg, .original: 0.11
        case .avif: 0.045
        case .webp: 0.065
        }
        return w * h * bpp * Double(ws.draft.video.frameQuality) / 80
    }

    private var framesSummary: String {
        "将生成 \(framesTotal) 张 · 约 \(Format.size(UInt64(Double(framesTotal) * bytesPerFrame)))"
    }

    private var hint: String {
        ws.type == .image ? "访达中按住 ⌥ 点文件可直接处理" : "也可以把文件拖到 Dock 图标上"
    }
}
