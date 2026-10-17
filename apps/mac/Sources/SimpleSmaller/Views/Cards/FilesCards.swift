import QuickLook
import SwiftUI

// MARK: - 左大卡：拖放 / 文件

/// 没有文件时的左大卡：整张卡就是拖放区，也可以选取或粘贴（设计稿 02）。
struct DropCard: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    @State private var targeted = false

    var body: some View {
        BentoCard {
            VStack(spacing: 14) {
                Spacer(minLength: 0)
                Image(systemName: "arrow.down.to.line")
                    .font(.system(size: 30, weight: .light))
                    .foregroundStyle(targeted ? Color.white : Color.primary)
                    .frame(width: 76, height: 76)
                    .background(Circle().fill(targeted ? Color.brand : Bento.fill))
                    .symbolEffect(.bounce, value: targeted)
                Text(title).font(.system(size: 18, weight: .semibold))
                if let formats {
                    Text(formats).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                }
                HStack(spacing: 8) {
                    CapsuleButton(title: "选取文件…", height: 40, fill: Bento.ink, foreground: Bento.onInk) { FilePicker.pick(into: model) }
                    if ws.type == .image {
                        CapsuleButton(title: "粘贴 ⌘⇧V", height: 40) { model.pasteFromClipboard() }
                    }
                }
                .padding(.top, 6)
                Spacer(minLength: 0)
                if ws.type == .image {
                    Text("也可以在访达里按住 ⌥ 点文件，不用开这个窗口")
                        .font(.system(size: 12))
                        .foregroundStyle(Bento.secondary)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background {
                RoundedRectangle(cornerRadius: 18, style: .continuous)
                    .strokeBorder(style: StrokeStyle(lineWidth: 1.5, dash: [6, 6]))
                    .foregroundStyle(targeted ? Color.brand : Bento.line)
                    .background(targeted ? Color.brand.opacity(0.05) : .clear, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
            }
        }
        .animation(.smooth(duration: 0.2), value: targeted)
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: ws.type)
            return true
        } isTargeted: { targeted = $0 }
    }

    private var title: String {
        switch ws.type {
        case .image: "把图片或文件夹拖到这里"
        case .video: "把视频或文件夹拖到这里"
        case .pdf: ws.pdfSegment == .fromImages ? "把图片拖到这里" : "把 PDF 或文件夹拖到这里"
        }
    }

    private var formats: String? {
        switch ws.type {
        case .image: "JPG · PNG · HEIC · WebP · AVIF · TIFF · GIF"
        case .video: "MP4 · MOV · M4V · MKV · WebM · AVI · GIF 动图"
        case .pdf: ws.pdfSegment == .fromImages ? "JPG · PNG · HEIC · WebP · AVIF" : nil
        }
    }
}

/// 这一批文件：缩略图 / 列表两种看法，底部一条「拖进更多」（设计稿 03）。
struct FilesCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @AppStorage("filesView") private var view = "grid"

    var body: some View {
        BentoCard {
            VStack(spacing: 14) {
                BentoHeader(symbol: "square.stack.3d.up", title: title, subtitle: subtitle) {
                    IconSegmented(selection: $view, options: [("grid", "square.grid.2x2", "缩略图"), ("list", "list.bullet", "列表")], label: "显示方式")
                    CircleIconButton(symbol: "plus", label: "添加文件（⌘O）") { FilePicker.pick(into: model) }
                    if ws.type == .image {
                        CircleIconButton(symbol: "doc.on.clipboard", label: "粘贴剪贴板（⌘⇧V）") { model.pasteFromClipboard() }
                    }
                }
                ZStack {
                    if view == "grid" {
                        ThumbGrid(ws: ws).transition(.bentoGrow)
                    } else {
                        FileTableView(ws: ws)
                            .scrollContentBackground(.hidden)
                            .transition(.bentoGrow)
                    }
                }
                .frame(maxHeight: .infinity)
                .animation(.spring(response: 0.45, dampingFraction: 0.82), value: view)
                if !ws.running {
                    Label(ws.type == .image ? "拖进更多图片或文件夹" : "拖进更多文件", systemImage: "plus")
                        .font(.system(size: 12))
                        .foregroundStyle(Bento.secondary)
                        .frame(maxWidth: .infinity)
                        .frame(height: 40)
                        .overlay(Capsule().strokeBorder(Bento.line, lineWidth: 1.2))
                        .accessibilityHidden(true)
                }
            }
        }
    }

    private var title: String {
        if ws.running { return "\(ws.files.count) 个文件 · 处理中" }
        return ws.files.count > 1 ? "\(ws.files.count) 个文件 · 统一设置" : "1 个文件"
    }

    private var subtitle: String {
        if ws.running { return "完成一个写出一个 · 按 ⌘. 停止" }
        return ws.files.count > 1 ? "改动对这一批全部生效 · \(Format.size(ws.totalBytes))" : Format.size(ws.totalBytes)
    }
}

/// 缩略图网格：三列方块，底部胶囊写文件名与大小；处理中、完成在角上标状态。
/// 选中后空格快速查看，处理完的可以直接拖出。
struct ThumbGrid: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var quickLook: URL?

    private let columns = [GridItem(.adaptive(minimum: 130, maximum: 220), spacing: 10)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: columns, spacing: 10) {
                ForEach(ws.files) { item in
                    ThumbTile(item: item, selected: ws.selection.contains(item.id))
                        .onTapGesture { ws.selection = [item.id] }
                        .simultaneousGesture(TapGesture(count: 2).onEnded { reveal(item) })
                        .onDrag {
                            // 处理完的拖出结果；没处理的拖出原文件。
                            let url = item.outputs.first.map { URL(fileURLWithPath: $0.path) } ?? item.url
                            let p = NSItemProvider(object: url as NSURL)
                            p.suggestedName = url.lastPathComponent
                            return p
                        }
                        .contextMenu {
                            Button("在访达中显示") { reveal(item) }
                            Button("从列表移除") { model.remove([item.id], from: ws) }.disabled(ws.running)
                        }
                }
            }
            .padding(2)
        }
        .scrollIndicators(.automatic)
        .quickLookPreview($quickLook)
        .onReceive(NotificationCenter.default.publisher(for: .quickLookSelection)) { _ in
            guard let item = ws.selectedItems.first else { return }
            quickLook = item.outputs.first.map { URL(fileURLWithPath: $0.path) } ?? item.url
        }
        .background(SpaceKeyWatcher())
        .onDeleteCommand { if !ws.running { model.remove(ws.selection, from: ws) } }
    }

    private func reveal(_ item: FileItem) {
        model.reveal(item.outputs.isEmpty ? [item.url] : item.outputs.map { URL(fileURLWithPath: $0.path) })
    }
}

struct ThumbTile: View {
    let item: FileItem
    let selected: Bool

    var body: some View {
        Color.clear
            .aspectRatio(1, contentMode: .fit)
            .overlay {
                if let image = item.thumbnail {
                    Image(nsImage: image).resizable().scaledToFill()
                } else {
                    Rectangle().fill(Bento.fill)
                }
            }
            .overlay(alignment: .bottom) {
                HStack(spacing: 6) {
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                    Spacer(minLength: 2)
                    Text(sizeText).foregroundStyle(Bento.secondary).monospacedDigit()
                }
                .font(.system(size: 10))
                .foregroundStyle(Color.primary)
                .padding(.horizontal, 8)
                .frame(height: 22)
                .background(Capsule().fill(Bento.card.opacity(0.92)))
                .padding(6)
            }
            .overlay(alignment: .topTrailing) { badge.padding(6) }
            .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
            .overlay {
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .strokeBorder(selected ? Color.brand : Color.primary.opacity(0.06), lineWidth: selected ? 3 : 1)
            }
            .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
            .help(item.metaError.map { FileTableView.explain($0) } ?? item.url.path)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(item.name)，\(sizeText)")
            .accessibilityAddTraits(selected ? [.isSelected, .isButton] : .isButton)
    }

    private var sizeText: String {
        item.state == .done && item.afterBytes > 0 ? Format.size(item.afterBytes) : Format.size(item.bytes)
    }

    @ViewBuilder private var badge: some View {
        switch item.state {
        case .running(let f):
            Text(f < 0.02 ? "处理中" : "\(Int(f * 100))%").font(.system(size: 10, weight: .semibold)).monospacedDigit()
                .foregroundStyle(.white).padding(.horizontal, 7).frame(height: 20).background(Capsule().fill(Color.brand))
        case .done:
            Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)).foregroundStyle(.white)
                .frame(width: 20, height: 20).background(Circle().fill(Color.saving))
        case .kept:
            Text(item.keptText).font(.system(size: 10, weight: .semibold)).foregroundStyle(.white)
                .padding(.horizontal, 7).frame(height: 20).background(Capsule().fill(Color.warning))
        case .failed(let reason):
            Text(reason).font(.system(size: 10, weight: .semibold)).foregroundStyle(.white).lineLimit(1)
                .padding(.horizontal, 7).frame(height: 20).background(Capsule().fill(Bento.failed))
        default:
            if let e = item.metaError {
                Text(e).font(.system(size: 10, weight: .semibold)).foregroundStyle(.white).lineLimit(1)
                    .padding(.horizontal, 7).frame(height: 20).background(Capsule().fill(Color.warning))
            }
        }
    }
}

// MARK: - 格式与质量
