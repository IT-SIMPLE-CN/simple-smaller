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
            if ws.type == .image, ws.imageSegment == .crop {
                // 裁切时写确认进度（设计稿 03 顶栏的「已确认 n / 12」）。
                let items = ws.cropItems
                Text("已确认 \(items.filter { ws.cropConfirmed.contains($0.url) }.count) / \(items.count)").monospacedDigit()
            } else if ws.type == .pdf, ws.pdfSegment == .merge || ws.pdfSegment == .fromImages {
                Text(verbatim: ws.pdfSegment == .merge ? "合并成 1 个文件 · \(ws.pageOrder.count) 页" : "做成 1 个 PDF · \(ws.files.filter { $0.type == .image }.count) 页")
                    .monospacedDigit()
            } else if ws.type == .video, ws.videoSegment == .frames {
                // 序列帧：将生成多少张、约多大（设计稿 04b 底栏）。
                Text(verbatim: framesSummary).monospacedDigit()
            } else {
                Text("\(ws.files.filter { $0.state == .waiting }.count) 个待处理").monospacedDigit()
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
            if ws.type == .video, ws.videoSegment == .frames {
                Text(framesHint).foregroundStyle(framesTotal > 200 ? AnyShapeStyle(Color.warning) : AnyShapeStyle(.tertiary))
            }
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

    private var framesHint: String {
        if framesTotal > 200 { return "张数较多，网页动画建议 60–120 张" }
        let w = ws.draft.video.frameWidth.map { "，\($0)" } ?? ""
        return "每张约 \(Format.size(UInt64(bytesPerFrame)))（\(ws.draft.video.frameFormat.title)\(w)）"
    }

    private var hint: String {
        ws.type == .image ? "访达中按住 ⌥ 点文件可直接处理" : "也可以把文件拖到 Dock 图标上"
    }
}
