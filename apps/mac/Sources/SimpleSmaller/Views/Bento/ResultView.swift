import QuickLook
import SwiftUI

/// 完成页（设计稿 V2.1 · 04）：一张大卡。半圆刻度盘看这一批小了多少，右边「原来 → 现在」；
/// 下方逐个文件，有问题的排最前，收起时也看得到。出路：在访达中显示、重试失败的、改设置再做一次、再来一批。
struct ResultView: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var open = false

    private var ok: [FileItem] { ws.files.filter { [.done, .kept].contains($0.state) } }
    private var failed: [FileItem] { ws.files.filter { if case .failed = $0.state { return true }; return false } }
    private var kept: [FileItem] { ws.files.filter { $0.state == .kept } }
    /// 只算处理成功的；保留原图的按原大小计。
    private var before: UInt64 { ok.reduce(0) { $0 + $1.bytes } }
    private var after: UInt64 { ok.reduce(0) { $0 + ($1.state == .done ? $1.afterBytes : $1.bytes) } }
    private var saved: Double { before > 0 ? max(0, 1 - Double(after) / Double(before)) : 0 }

    var body: some View {
        BentoCard(padding: EdgeInsets(top: 26, leading: 28, bottom: 24, trailing: 28), radius: 26) {
            GeometryReader { geo in
                let panelW: CGFloat = min(336, geo.size.width * 0.36)
                let leftW = geo.size.width - panelW - 24
                ZStack(alignment: .topLeading) {
                    if !open {
                        Gauge(fraction: saved)
                            .frame(width: leftW, height: min(geo.size.height * 0.62, leftW * 432 / 840))
                            .padding(.top, 8)
                            .transition(.opacity)
                    }
                    header
                    ResultList(ws: ws, open: $open, rows: rows, summary: listSummary)
                        .frame(width: leftW, height: open ? geo.size.height - 70 : listHeight)
                        .frame(maxHeight: .infinity, alignment: .bottom)
                    panel
                        .frame(width: panelW)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing)
                }
                .animation(.spring(response: 0.45, dampingFraction: 0.82), value: open)
            }
        }
        .bentoAppear(0)
    }

    private var listHeight: CGFloat { 56 + 26 + CGFloat(min(3, rows.count)) * 36 + (rows.count > 3 ? 34 : 6) + 10 }

    private var header: some View {
        HStack(spacing: 12) {
            Image(systemName: failed.count == ws.files.count ? "exclamationmark" : "checkmark")
                .font(.system(size: 18, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 40, height: 40)
                .background(Circle().fill(failed.count == ws.files.count ? Bento.failed : Color.brand))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.system(size: 20, weight: .semibold))
                Text(subtitle).font(.system(size: 12)).foregroundStyle(Bento.secondary).lineLimit(1)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isHeader)
    }

    private var title: String {
        if failed.count == ws.files.count { return "没有处理成功" }
        switch (ws.type, ws.imageSegment, ws.videoSegment) {
        case (.image, .crop, _): return "裁切完成"
        case (.image, _, _): return "压缩完成"
        case (.video, _, .frames): return "序列帧完成"
        case (.video, _, .edit): return "剪辑完成"
        case (.video, _, .compress): return "压缩完成"
        default: return ws.type == .pdf && ws.pdfSegment == .compress ? "压缩完成" : "处理完成"
        }
    }

    private var subtitle: String {
        let noun = ws.type == .image ? "图片" : ws.type == .video ? "视频" : "文件"
        var parts = ["\(ws.files.count) 个\(noun)"]
        if ws.usesRecipe { parts.append(ws.draft.name) }
        parts.append(model.settings.outputLocation == .beside ? "存在原文件旁边" : "存在「\((model.settings.outputFolder as NSString).lastPathComponent)」")
        parts.append("行可以直接拖出，空格快速查看")
        return parts.joined(separator: " · ")
    }

    private var listSummary: String {
        var parts = ["\(ws.files.count) 个", "\(ws.files.filter { $0.state == .done }.count) 个完成"]
        if !failed.isEmpty { parts.append("\(failed.count) 个失败") }
        if !kept.isEmpty { parts.append("\(kept.count) 个保留原图") }
        return parts.joined(separator: " · ")
    }

    /// 有问题的排最前：失败 → 保留原图 → 完成。
    private var rows: [FileItem] {
        failed + kept + ws.files.filter { f in !failed.contains { $0 === f } && !kept.contains { $0 === f } }
    }

    // MARK: 右侧

    private var panel: some View {
        VStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 0) {
                Text("原来").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                Text(Format.size(before)).font(.system(size: 28, weight: .light).monospacedDigit()).foregroundStyle(Bento.secondary).padding(.top, 2)
                VStack(spacing: 0) {
                    Rectangle().fill(Bento.line).frame(width: 2).mask(DashMask())
                    Image(systemName: "arrow.down")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundStyle(Color.brand)
                        .frame(width: 30, height: 30)
                        .background(Circle().fill(Bento.card))
                    Rectangle().fill(Color.brand).frame(width: 2)
                }
                .frame(width: 30)
                .frame(minHeight: 30, maxHeight: .infinity)
                .padding(.vertical, 10)
                .accessibilityHidden(true)
                Text("现在").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                Text(Format.size(after))
                    .font(.system(size: 56, weight: .light).monospacedDigit())
                    .tracking(-1.5)
                    .lineLimit(1)
                    .minimumScaleFactor(0.5)
                    .padding(.top, 2)
                HStack(spacing: 6) {
                    Text(verbatim: "省下 \(Format.size(before > after ? before - after : 0))")
                        .foregroundStyle(Bento.alertText)
                        .padding(.horizontal, 12).frame(height: 30)
                        .background(Capsule().fill(Bento.alertFill))
                    if after > 0, before > after {
                        Text(verbatim: "小了 \(String(format: "%.1f", Double(before) / Double(after))) 倍")
                            .padding(.horizontal, 12).frame(height: 30)
                            .background(Capsule().fill(Bento.card))
                    }
                }
                .font(.system(size: 12).monospacedDigit())
                .padding(.top, 14)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 22)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Bento.fill))
            .accessibilityElement(children: .combine)

            if !failed.isEmpty {
                HStack(spacing: 10) {
                    Image(systemName: "xmark").font(.system(size: 12, weight: .bold)).foregroundStyle(.white)
                        .frame(width: 28, height: 28).background(Circle().fill(Bento.failed))
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(verbatim: "\(failed.count) 个失败").font(.system(size: 13, weight: .semibold))
                        Text(failNote).font(.system(size: 11)).lineLimit(2)
                    }
                    .foregroundStyle(Bento.alertText)
                    Spacer(minLength: 4)
                    Button("重试失败的") { model.retryFailed(ws) }
                        .buttonStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundStyle(Bento.alertText)
                        .padding(.horizontal, 12).frame(height: 30)
                        .background(Capsule().fill(Bento.card))
                        .keyboardShortcut("r", modifiers: [.command, .shift])
                        .help("按当前设置再处理一遍失败的文件（⌘⇧R）")
                }
                .padding(.leading, 14).padding(.trailing, 10).padding(.vertical, 12)
                .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Bento.alertFill))
            }
            if !kept.isEmpty {
                Text(kept.count == 1 ? "\(kept[0].name) 压完会变大，保留了原图。" : "\(kept.count) 个文件压完会变大，保留了原图。")
                    .font(.system(size: 11))
                    .foregroundStyle(Color.warning)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 6)
            }
            Button {
                model.reveal(ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } })
            } label: {
                Text("在访达中显示").font(.system(size: 15, weight: .semibold)).foregroundStyle(.white)
                    .frame(maxWidth: .infinity).frame(height: 56)
                    .background(Capsule().fill(Color.brand))
                    .contentShape(Capsule())
            }
            .buttonStyle(BentoPressStyle())
            .disabled(ok.isEmpty)
            HStack(spacing: 8) {
                wideButton("改设置再做一次") { ws.showResult = false }
                    .help("回到设置，这一批文件还在；改了设置后可以再开始")
                wideButton("再来一批") { model.clear(ws) }
                    .help("清空这一批，拖新的文件进来")
            }
        }
    }

    private var failNote: String {
        if failed.count == 1, case .failed(let r) = failed[0].state { return "\(failed[0].name) \(r)" }
        return "鼠标停在行上看原因"
    }

    private func wideButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title).font(.system(size: 13))
                .frame(maxWidth: .infinity).frame(height: 48)
                .background(Capsule().fill(Bento.fill))
                .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
    }
}

/// 虚线（「原来 → 现在」之间）。
private struct DashMask: View {
    var body: some View {
        GeometryReader { geo in
            VStack(spacing: 4) {
                ForEach(0..<max(1, Int(geo.size.height / 8)), id: \.self) { _ in Rectangle().frame(height: 4) }
            }
        }
    }
}

// MARK: - 刻度盘

/// 半圆刻度盘（设计稿 840 × 432 的坐标按比例缩放）：红色是这一批小掉的比例，中间大号百分比。
struct Gauge: View {
    let fraction: Double
    @State private var shown = 0.0
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        GeometryReader { geo in
            let s = min(geo.size.width / 840, geo.size.height / 432)
            let c = CGPoint(x: 412 * s, y: 432 * s)
            ZStack(alignment: .topLeading) {
                GaugeTicks(center: c, scale: s, progress: shown, outer: false)
                    .stroke(Color.primary.opacity(0.15), lineWidth: 1.2)
                GaugeTicks(center: c, scale: s, progress: shown, outer: true)
                    .stroke(Color.primary, lineWidth: 1.2)
                GaugeBand(center: c, scale: s, progress: shown)
                    .fill(Color.brand)
                Path { p in
                    p.addArc(center: c, radius: 22 * s, startAngle: .degrees(180), endAngle: .degrees(360), clockwise: false)
                    p.closeSubpath()
                }
                .fill(Color.brand)
                ForEach([25, 50, 75], id: \.self) { n in
                    let a = Double.pi * (1 + Double(n) / 100)
                    Text(verbatim: "\(n)").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                        .position(x: c.x + cos(a) * 330 * s, y: c.y + sin(a) * 330 * s)
                }
                Text("这一批小了")
                    .font(.system(size: 14))
                    .foregroundStyle(Bento.secondary)
                    .position(x: c.x, y: 254 * s)
                HStack(alignment: .firstTextBaseline, spacing: 6 * s) {
                    Text(verbatim: "\(Int((fraction * 100).rounded()))").font(.system(size: 146 * s, weight: .light).monospacedDigit()).tracking(-6 * s)
                    Text("%").font(.system(size: 48 * s))
                }
                .position(x: c.x - 10 * s, y: 340 * s)
            }
        }
        .onAppear {
            if reduceMotion { shown = fraction } else { withAnimation(.spring(response: 0.9, dampingFraction: 0.85).delay(0.15)) { shown = fraction } }
        }
        .onChange(of: fraction) { _, f in shown = f }
        .accessibilityElement(children: .ignore)
        .accessibilityAddTraits(.isImage)
        .accessibilityLabel("这一批小了 \(Int((fraction * 100).rounded()))%")
    }
}

/// 红色弧带：从左端顺时针走到 progress。
private struct GaugeBand: Shape {
    let center: CGPoint
    let scale: CGFloat
    var progress: Double

    var animatableData: Double {
        get { progress }
        set { progress = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let r1 = 232 * scale, r2 = 290 * scale
        let end = Angle.degrees(180 + 180 * progress)
        var p = Path()
        guard progress > 0.001 else { return p }
        p.addArc(center: center, radius: r2, startAngle: .degrees(180), endAngle: end, clockwise: false)
        p.addArc(center: center, radius: r1, startAngle: end, endAngle: .degrees(180), clockwise: true)
        p.closeSubpath()
        return p
    }
}

/// 刻度：红色以外的一段在弧带里画细线；红色那段在外侧画黑色刻度。
private struct GaugeTicks: Shape {
    let center: CGPoint
    let scale: CGFloat
    var progress: Double
    let outer: Bool

    var animatableData: Double {
        get { progress }
        set { progress = newValue }
    }

    func path(in rect: CGRect) -> Path {
        let r1 = 232 * scale, r2 = 290 * scale
        var p = Path()
        let ticks = 48
        for i in 0...ticks {
            let t = Double(i) / Double(ticks)
            guard outer ? t <= progress : t > progress else { continue }
            let a = Double.pi * (1 + t)
            let d = CGPoint(x: cos(a), y: sin(a))
            let (from, to): (CGFloat, CGFloat) = outer ? (r2 + 10 * scale, r2 + (i % 12 == 0 ? 24 : 18) * scale) : (r1 + 6 * scale, r2 - 2 * scale)
            p.move(to: CGPoint(x: center.x + d.x * from, y: center.y + d.y * from))
            p.addLine(to: CGPoint(x: center.x + d.x * to, y: center.y + d.y * to))
        }
        return p
    }
}

// MARK: - 逐个文件

struct ResultList: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @Binding var open: Bool
    let rows: [FileItem]
    let summary: String
    @State private var quickLook: URL?

    var body: some View {
        VStack(spacing: 2) {
            Button { open.toggle() } label: {
                HStack(spacing: 12) {
                    Text("逐个文件").font(.system(size: 15, weight: .semibold))
                    Text(summary).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                    Spacer()
                    if rows.count > 3 || open {
                        HStack(spacing: 6) {
                            Text(open ? "收起" : "展开")
                            Image(systemName: "chevron.up").rotationEffect(.degrees(open ? 180 : 0))
                        }
                        .font(.system(size: 12))
                        .padding(.horizontal, 14).frame(height: 34)
                        .background(Capsule().fill(Bento.card))
                    }
                }
                .padding(.leading, 20).padding(.trailing, 12)
                .frame(height: 56)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityAddTraits(.isHeader)
            header
            ScrollView {
                LazyVStack(spacing: 2) {
                    ForEach(Array((open ? rows : Array(rows.prefix(3))).enumerated()), id: \.element.id) { i, item in
                        ResultRow(item: item, striped: i % 2 == 0, selected: ws.selection.contains(item.id))
                            .onTapGesture { ws.selection = [item.id] }
                            .simultaneousGesture(TapGesture(count: 2).onEnded { reveal(item) })
                            .onDrag {
                                let url = item.outputs.first.map { URL(fileURLWithPath: $0.path) } ?? item.url
                                let p = NSItemProvider(object: url as NSURL)
                                p.suggestedName = url.lastPathComponent
                                return p
                            }
                            .contextMenu { Button("在访达中显示") { reveal(item) } }
                    }
                }
                .padding(.horizontal, 10)
            }
            .scrollDisabled(!open)
            if !open, rows.count > 3 {
                Button("还有 \(rows.count - 3) 个 · 展开全部") { open = true }
                    .buttonStyle(.plain)
                    .font(.system(size: 12))
                    .foregroundStyle(Bento.secondary)
                    .frame(height: 30)
            }
        }
        .padding(.bottom, 10)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.fill))
        .quickLookPreview($quickLook)
        .onReceive(NotificationCenter.default.publisher(for: .quickLookSelection)) { _ in
            guard let item = ws.selectedItems.first else { return }
            quickLook = item.outputs.first.map { URL(fileURLWithPath: $0.path) } ?? item.url
        }
        .background(SpaceKeyWatcher())
    }

    private var header: some View {
        ResultColumns {
            Text("名称").padding(.leading, 34)
        } before: { Text("原大小") } after: { Text("输出") } change: { Text("变化") } state: { Text("状态") }
            .font(.system(size: 11))
            .foregroundStyle(Bento.secondary)
            .frame(height: 22)
            .padding(.horizontal, 10)
    }

    private func reveal(_ item: FileItem) {
        model.reveal(item.outputs.isEmpty ? [item.url] : item.outputs.map { URL(fileURLWithPath: $0.path) })
    }
}

/// 名称 · 原大小 · 输出 · 变化 · 状态 五列。
struct ResultColumns<A: View, B: View, C: View, D: View, E: View>: View {
    @ViewBuilder var name: A
    @ViewBuilder var before: B
    @ViewBuilder var after: C
    @ViewBuilder var change: D
    @ViewBuilder var state: E

    var body: some View {
        HStack(spacing: 12) {
            name.frame(maxWidth: .infinity, alignment: .leading)
            before.frame(width: 80, alignment: .trailing)
            after.frame(width: 80, alignment: .trailing)
            change.frame(width: 64, alignment: .trailing)
            state.frame(width: 96, alignment: .trailing)
        }
        .padding(.leading, 6)
        .padding(.trailing, 14)
    }
}

struct ResultRow: View {
    let item: FileItem
    let striped: Bool
    let selected: Bool

    var body: some View {
        ResultColumns {
            HStack(spacing: 10) {
                Thumb(image: item.thumbnail).frame(width: 24, height: 24).clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
                Text(item.name).font(.system(size: 13)).lineLimit(1).truncationMode(.middle)
            }
        } before: {
            Text(Format.size(item.bytes)).foregroundStyle(Bento.secondary)
        } after: {
            Text(item.state == .done ? Format.size(item.afterBytes) : "—")
        } change: {
            Text(change).foregroundStyle(changeColor)
        } state: {
            stateLabel
        }
        .font(.system(size: 12).monospacedDigit())
        .frame(height: 34)
        .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(selected ? Color.brand.opacity(0.14) : (striped ? Bento.card : .clear)))
        .contentShape(Rectangle())
        .help(helpText)
        .accessibilityElement(children: .combine)
    }

    private var change: String {
        switch item.state {
        case .done: return Format.change(item.bytes, item.afterBytes)
        case .kept: return "—"
        default: return "—"
        }
    }

    private var changeColor: Color {
        item.state == .done ? .saving : (item.state == .kept ? .warning : Bento.secondary)
    }

    @ViewBuilder private var stateLabel: some View {
        switch item.state {
        case .done:
            Label("完成", systemImage: "checkmark").foregroundStyle(Bento.secondary).labelStyle(TightLabel(color: .saving))
        case .kept:
            Label("保留原图", systemImage: "exclamationmark").foregroundStyle(Color.warning).labelStyle(TightLabel(color: .warning))
        case .failed(let r):
            Label(r, systemImage: "xmark").foregroundStyle(Bento.failed).labelStyle(TightLabel(color: Bento.failed)).lineLimit(1)
        case .cancelled:
            Text("已停止").foregroundStyle(Bento.secondary)
        default:
            Text("—").foregroundStyle(Bento.secondary)
        }
    }

    private var helpText: String {
        if case .failed(let r) = item.state { return FileTableView.explain(r) }
        if item.state == .kept { return "压完会比原来大，保留原文件" }
        return item.outputs.first?.path ?? item.url.path
    }
}

private struct TightLabel: LabelStyle {
    let color: Color

    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 4) {
            configuration.icon.font(.system(size: 10, weight: .bold)).foregroundStyle(color)
            configuration.title
        }
    }
}
