import SwiftUI

/// 文件列表（设计稿 02 / 05）。图片、视频、PDF 共用，按类型换列。
struct FileTableView: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        VStack(spacing: 0) {
            table
            if let item = compareItem {
                Divider()
                CompareView(item: item)
                    .frame(height: 280)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.3), value: compareItem?.id)
    }

    /// 选中一张已完成的图时显示前后对比。
    private var compareItem: FileItem? {
        guard ws.type == .image, ws.selection.count == 1, let item = ws.selectedItems.first, item.state == .done, !item.outputs.isEmpty else { return nil }
        return item
    }

    private var table: some View {
        Table(ws.files, selection: $ws.selection) {
            TableColumn("名称") { item in
                HStack(spacing: 8) {
                    Thumb(image: item.thumbnail).frame(width: 22, height: 22)
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                }
                .help(item.url.path)
            }
            .width(min: 160, ideal: 220)
            TableColumn(ws.type == .pdf ? "页数" : ws.type == .video ? "时长 · 尺寸" : "尺寸") { item in
                Text(detail(item)).foregroundStyle(.secondary).monospacedDigit()
            }
            .width(min: 70, ideal: ws.type == .video ? 130 : 90)
            TableColumn("原大小") { item in
                Text(Format.size(item.bytes)).foregroundStyle(.secondary).monospacedDigit()
            }
            .width(min: 60, ideal: 72)
            // 处理前是试压预估（只在内存里压、不写文件），写出后才叫「输出」。
            TableColumn(ws.files.contains { $0.state == .done || $0.state == .kept } ? "输出" : "预估") { item in output(item) }
                .width(min: 60, ideal: 72)
            TableColumn("变化") { item in change(item) }
                .width(min: 48, ideal: 56)
            TableColumn("状态") { item in StateLabel(item: item) }
                .width(min: 64, ideal: 84)
        }
        .contextMenu(forSelectionType: FileItem.ID.self) { ids in
            let items = ws.files.filter { ids.contains($0.id) }
            Button("在访达中显示") { model.reveal(items.flatMap(outputsOrSource)) }
            Button("从列表移除") { model.remove(ids, from: ws) }.disabled(ws.running)
        } primaryAction: { ids in
            model.reveal(ws.files.filter { ids.contains($0.id) }.flatMap(outputsOrSource))
        }
        .onDeleteCommand { if !ws.running { model.remove(ws.selection, from: ws) } }
        // 只给有内容的行做底色，下面空白处保持干净；行边距与表头对齐。
        .tableStyle(.inset)
        .alternatingRowBackgrounds(.disabled)
    }

    private func outputsOrSource(_ item: FileItem) -> [URL] {
        item.outputs.isEmpty ? [item.url] : item.outputs.map { URL(fileURLWithPath: $0.path) }
    }

    private func detail(_ item: FileItem) -> String {
        if let e = item.metaError { return e }
        switch ws.type {
        case .image: return item.dimensions
        case .video: return item.duration > 0 ? "\(Format.duration(item.duration)) · \(item.width)×\(item.height)" : "—"
        case .pdf: return item.type == .image ? item.dimensions : (item.pages > 0 ? "\(item.pages)" : "—")
        }
    }

    @ViewBuilder private func output(_ item: FileItem) -> some View {
        switch item.state {
        case .done:
            Text(Format.size(item.afterBytes)).monospacedDigit()
        case .kept:
            Text("—").foregroundStyle(.secondary)
        default:
            if item.estimating {
                ProgressView().controlSize(.mini)
            } else if item.estimateKept {
                Text("—").foregroundStyle(.secondary)
            } else if let e = item.estimate, e > 0 {
                Text((ws.type == .video ? "≤ " : "≈ ") + Format.size(e)).foregroundStyle(.secondary).monospacedDigit()
                    .help("试压预估：点「开始」后才写出文件")
            } else {
                Text("—").foregroundStyle(.tertiary)
            }
        }
    }

    @ViewBuilder private func change(_ item: FileItem) -> some View {
        let after: UInt64? = item.state == .done ? item.afterBytes : (item.estimateKept ? nil : item.estimate)
        if item.state == .kept || item.estimateKept {
            Text(ws.type == .pdf ? "已很小" : "+0%").foregroundStyle(.secondary)
        } else if let after, after > 0, item.bytes > 0 {
            // 预估用浅一档的颜色，写出后才用实色。
            Text(Format.change(item.bytes, after))
                .foregroundStyle(after < item.bytes ? Color.saving : Color.warning)
                .opacity(item.state == .done ? 1 : 0.7)
                .monospacedDigit()
        } else {
            Text("—").foregroundStyle(.tertiary)
        }
    }
}

struct StateLabel: View {
    let item: FileItem

    var body: some View {
        switch item.state {
        case .waiting:
            Text("等待").foregroundStyle(.secondary)
        case .running(let p):
            HStack(spacing: 6) {
                ProgressView(value: p).progressViewStyle(.linear).tint(.brand).frame(width: 40)
                Text("\(Int(p * 100))%").monospacedDigit().foregroundStyle(Color.brand)
            }
        case .done:
            Text(item.outputs.count > 1 ? "\(item.outputs.count) 个文件" : "完成").foregroundStyle(Color.saving)
        case .kept:
            Text(item.type == .image ? "保留原图" : "保留原文件").foregroundStyle(Color.warning)
                .help("压完没有变小，原文件保持不动")
        case .failed(let message):
            Text("失败").foregroundStyle(Color.warning).help(message)
        case .cancelled:
            Text("已取消").foregroundStyle(.secondary)
        }
    }
}

struct Thumb: View {
    let image: NSImage?

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image).resizable().scaledToFill()
            } else {
                Rectangle().fill(.quaternary)
            }
        }
        .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

/// 前后对比：拖中间的线，左边原图、右边输出。
struct CompareView: View {
    let item: FileItem
    @State private var split: CGFloat = 0.5
    @State private var before: NSImage?
    @State private var after: NSImage?

    var body: some View {
        GeometryReader { geo in
            let size = fitted(in: geo.size)
            ZStack {
                if let before { Image(nsImage: before).resizable().scaledToFit().frame(width: size.width, height: size.height) }
                if let after {
                    Image(nsImage: after).resizable().scaledToFit().frame(width: size.width, height: size.height)
                        .mask(alignment: .trailing) { Rectangle().frame(width: size.width * (1 - split)) }
                }
                Rectangle().fill(.white).frame(width: 2, height: size.height)
                    .offset(x: size.width * (split - 0.5))
                Circle().fill(.white).frame(width: 26, height: 26).shadow(radius: 3)
                    .overlay { Image(systemName: "arrow.left.and.right").font(.system(size: 11, weight: .bold)).foregroundStyle(.black) }
                    .offset(x: size.width * (split - 0.5))
                HStack {
                    Tag(text: "原图 \(Format.size(item.bytes))")
                    Spacer()
                    Tag(text: "输出 \(Format.size(item.outputs.first?.bytes ?? 0))")
                }
                .frame(width: size.width)
                .frame(maxHeight: .infinity, alignment: .bottom)
                .padding(.bottom, 8)
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { v in
                let x = v.location.x - (geo.size.width - size.width) / 2
                split = min(1, max(0, x / size.width))
            })
        }
        .padding(12)
        .background(.background.secondary)
        .task(id: item.id) {
            before = NSImage(contentsOf: item.url)
            after = item.outputs.first.flatMap { NSImage(contentsOf: URL(fileURLWithPath: $0.path)) }
        }
        .accessibilityLabel("前后对比")
    }

    private func fitted(in box: CGSize) -> CGSize {
        let w = CGFloat(max(1, item.width)), h = CGFloat(max(1, item.height))
        let s = min(box.width / w, box.height / h)
        return CGSize(width: w * s, height: h * s)
    }
}

struct Tag: View {
    let text: String
    var body: some View {
        Text(text).font(.caption2).foregroundStyle(.white)
            .padding(.horizontal, 6).padding(.vertical, 2)
            .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 4))
            .padding(.horizontal, 8)
    }
}
