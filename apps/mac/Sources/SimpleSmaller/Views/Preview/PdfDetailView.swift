import SwiftUI
import UniformTypeIdentifiers

/// PDF 详情区（设计稿 05）：
/// 压缩、拆分、转图片——上面文件列表，下面选中文件的页面条；
/// 合并——全部页面按顺序排开，拖动排序、选中按 Delete 删页；
/// 图片转 PDF——图片按顺序排开，一张一页，拖动排序。
struct PdfDetailView: View {
    @Bindable var ws: Workspace

    var body: some View {
        switch ws.pdfSegment {
        case .merge:
            PageGrid(ws: ws)
        case .fromImages:
            ImageOrderGrid(ws: ws)
        default:
            VStack(spacing: 12) {
                BentoHeader(symbol: "doc.richtext", title: "\(ws.files.count) 个文件",
                            subtitle: "\(ws.files.reduce(0) { $0 + $1.pages }) 页 · \(Format.size(ws.totalBytes))") {
                    CircleIconButton(symbol: "plus", label: "添加文件（⌘O）") { FilePicker.pick(into: AppModel.shared) }
                }
                .padding(.horizontal, 6)
                .padding(.top, 4)
                FileTableView(ws: ws)
                    .scrollContentBackground(.hidden)
                if let item = previewItem {
                    PageStrip(item: item)
                        .transition(.move(edge: .bottom).combined(with: .opacity))
                }
            }
            .animation(.smooth(duration: 0.3), value: previewItem?.id)
        }
    }

    /// 页面条显示选中的那个；没选时显示第一个。
    private var previewItem: FileItem? {
        let pdfs = ws.files.filter { $0.type == .pdf && $0.pages > 0 }
        return pdfs.first { ws.selection.contains($0.id) } ?? pdfs.first
    }
}

/// 一个文件的页面条（只看不改）。
private struct PageStrip: View {
    let item: FileItem
    private let thumbs = PdfThumbnails.shared

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(verbatim: "\(item.name) · \(item.pages) 页").font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("页面预览 · 排序和删页在「合并」里").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            }
            ScrollView(.horizontal, showsIndicators: false) {
                LazyHStack(spacing: 10) {
                    ForEach(0..<item.pages, id: \.self) { i in
                        PageThumb(image: thumbs.thumbnails(for: item.previewURL).flatMap { i < $0.count ? $0[i] : nil }, number: i + 1, height: 96)
                    }
                }
                .padding(.bottom, 2)
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .frame(height: 168)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Bento.fill))
        .task(id: item.previewURL) { thumbs.load(item.previewURL) }
    }
}

/// 页面缩略图：白纸、细边、下方页码；选中时品牌红描边。
struct PageThumb: View {
    let image: CGImage?
    let number: Int
    var height: CGFloat = 132
    var selected = false
    var badge: String?

    var body: some View {
        VStack(spacing: 4) {
            Group {
                if let image {
                    Image(decorative: image, scale: 2).resizable().scaledToFit()
                } else {
                    Rectangle().fill(Color.white).aspectRatio(0.707, contentMode: .fit)
                }
            }
            .frame(height: height)
            .background(Color.white)
            .overlay(Rectangle().strokeBorder(Color.black.opacity(0.12), lineWidth: 0.5))
            .shadow(color: .black.opacity(0.1), radius: 1.5, y: 1)
            .padding(3)
            .overlay {
                if selected {
                    RoundedRectangle(cornerRadius: 4, style: .continuous).strokeBorder(Color.brand, lineWidth: 2.5)
                }
            }
            .overlay(alignment: .topLeading) {
                if let badge {
                    Text(verbatim: badge)
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 4)
                        .padding(.vertical, 1)
                        .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 3))
                        .padding(6)
                }
            }
            Text(verbatim: "\(number)")
                .font(.system(size: 11).monospacedDigit())
                .foregroundStyle(selected ? Color.brand : .secondary)
        }
        .accessibilityElement()
        .accessibilityAddTraits(.isImage)
        .accessibilityLabel(Text(verbatim: "第 \(number) 页"))
        .accessibilityAddTraits(selected ? [.isSelected] : [])
    }
}

/// 合并：全部页面按顺序排开。点选（⌘ 多选、⇧ 连选），拖动排序，Delete 删页。
private struct PageGrid: View {
    @Bindable var ws: Workspace
    @State private var selected: Set<Int> = []
    @State private var anchor: Int?
    @State private var dragging: Int?
    private let thumbs = PdfThumbnails.shared

    private var pdfs: [FileItem] { ws.files.filter { $0.type == .pdf } }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(verbatim: "合并后 \(ws.pageOrder.count) 页 · 来自 \(pdfs.count) 个文件").font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("拖动可排序 · 选中后按 Delete 删除").font(.system(size: 11)).foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 8)
            .frame(height: 36)
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 104, maximum: 140), spacing: 14)], spacing: 16) {
                    ForEach(Array(ws.pageOrder.enumerated()), id: \.offset) { i, ref in
                        let file = Int(ref.file)
                        let item = file < pdfs.count ? pdfs[file] : nil
                        PageThumb(image: item.flatMap { thumbs.thumbnails(for: $0.previewURL) }.flatMap { Int(ref.page) < $0.count ? $0[Int(ref.page)] : nil },
                                  number: i + 1, selected: selected.contains(i),
                                  badge: pdfs.count > 1 ? "\(file + 1)·\(ref.page + 1)" : nil)
                            .opacity(dragging == i ? 0.4 : 1)
                            .contentShape(Rectangle())
                            .onTapGesture { tap(i) }
                            .onDrag {
                                dragging = i
                                return NSItemProvider(object: "\(i)" as NSString)
                            }
                            .onDrop(of: [.text], delegate: PageDrop(target: i, ws: ws, dragging: $dragging, selected: $selected))
                            .help(item.map { "\($0.name) 第 \(ref.page + 1) 页" } ?? "")
                    }
                }
                .padding(16)
                .animation(.smooth(duration: 0.25), value: ws.pageOrder)
            }
            .contentShape(Rectangle())
            .onTapGesture { selected = [] }
        }
        .focusable()
        .focusEffectDisabled()
        .onDeleteCommand { deleteSelected() }
        .onKeyPress(.delete) { deleteSelected(); return .handled }
        .task(id: pdfs.map(\.previewURL)) { for p in pdfs { thumbs.load(p.previewURL) } }
    }

    private func tap(_ i: Int) {
        let mods = NSEvent.modifierFlags
        if mods.contains(.command) {
            if selected.contains(i) { selected.remove(i) } else { selected.insert(i) }
            anchor = i
        } else if mods.contains(.shift), let a = anchor {
            selected = Set(min(a, i)...max(a, i))
        } else {
            selected = [i]
            anchor = i
        }
    }

    private func deleteSelected() {
        guard !selected.isEmpty, !ws.running else { return }
        withAnimation(.smooth(duration: 0.25)) {
            ws.pageOrder = ws.pageOrder.enumerated().filter { !selected.contains($0.offset) }.map(\.element)
        }
        selected = []
    }
}

/// 拖到某一页上：把拖着的那页（或选中的一组）挪到它的位置。
private struct PageDrop: DropDelegate {
    let target: Int
    let ws: Workspace
    @Binding var dragging: Int?
    @Binding var selected: Set<Int>

    func dropEntered(info: DropInfo) {
        guard let from = dragging, from != target, !ws.running else { return }
        withAnimation(.smooth(duration: 0.2)) {
            let page = ws.pageOrder.remove(at: from)
            ws.pageOrder.insert(page, at: target)
        }
        dragging = target
        selected = [target]
    }

    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }

    func performDrop(info: DropInfo) -> Bool {
        dragging = nil
        return true
    }
}

/// 图片转 PDF：图片按顺序排开，一张一页；拖动排序，Delete 移出。
private struct ImageOrderGrid: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var dragging: FileItem.ID?

    private var images: [FileItem] { ws.files.filter { $0.type == .image } }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(verbatim: "\(images.count) 张图片 · 一张一页").font(.system(size: 12, weight: .semibold))
                Spacer()
                Text("拖动可排序 · 选中后按 Delete 删除").font(.system(size: 11)).foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 8)
            .frame(height: 36)
            ScrollView {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 104, maximum: 140), spacing: 14)], spacing: 16) {
                    ForEach(Array(images.enumerated()), id: \.element.id) { i, item in
                        VStack(spacing: 4) {
                            Thumb(image: item.thumbnail)
                                .aspectRatio(CGFloat(max(1, item.width)) / CGFloat(max(1, item.height)), contentMode: .fit)
                                .frame(height: 132)
                                .overlay {
                                    if ws.selection.contains(item.id) {
                                        RoundedRectangle(cornerRadius: 4, style: .continuous).strokeBorder(Color.brand, lineWidth: 2.5).padding(-3)
                                    }
                                }
                            Text(verbatim: "\(i + 1)").font(.system(size: 11).monospacedDigit())
                                .foregroundStyle(ws.selection.contains(item.id) ? Color.brand : .secondary)
                        }
                        .opacity(dragging == item.id ? 0.4 : 1)
                        .contentShape(Rectangle())
                        .onTapGesture {
                            if NSEvent.modifierFlags.contains(.command) {
                                if ws.selection.contains(item.id) { ws.selection.remove(item.id) } else { ws.selection.insert(item.id) }
                            } else {
                                ws.selection = [item.id]
                            }
                        }
                        .onDrag {
                            dragging = item.id
                            return NSItemProvider(object: item.id.uuidString as NSString)
                        }
                        .onDrop(of: [.text], delegate: ImageDrop(target: item.id, ws: ws, dragging: $dragging))
                        .help(item.name)
                        .accessibilityAddTraits(.isImage)
                        .accessibilityLabel(Text(verbatim: "第 \(i + 1) 页，\(item.name)"))
                    }
                }
                .padding(16)
                .animation(.smooth(duration: 0.25), value: ws.files.map(\.id))
            }
        }
        .focusable()
        .focusEffectDisabled()
        .onDeleteCommand { model.remove(ws.selection, from: ws) }
    }
}

private struct ImageDrop: DropDelegate {
    let target: FileItem.ID
    let ws: Workspace
    @Binding var dragging: FileItem.ID?

    func dropEntered(info: DropInfo) {
        guard let from = dragging, from != target, !ws.running,
              let a = ws.files.firstIndex(where: { $0.id == from }),
              let b = ws.files.firstIndex(where: { $0.id == target }) else { return }
        withAnimation(.smooth(duration: 0.2)) {
            let item = ws.files.remove(at: a)
            ws.files.insert(item, at: b)
        }
    }

    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .move) }

    func performDrop(info: DropInfo) -> Bool {
        dragging = nil
        return true
    }
}
