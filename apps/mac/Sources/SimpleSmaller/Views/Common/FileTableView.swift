import QuickLook
import SwiftUI

/// 文件列表（设计稿 02 / 05）。图片、视频、PDF 共用，按类型换列。
struct FileTableView: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var quickLook: URL?
    @State private var unlocking: FileItem?

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
        Table(of: FileItem.self, selection: $ws.selection) {
            TableColumn("名称") { item in
                HStack(spacing: 8) {
                    Thumb(image: item.thumbnail).frame(width: 22, height: 22).accessibilityHidden(true)
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                }
                .help(item.url.path)
            }
            .width(min: 110)
            TableColumn(ws.type == .pdf ? "页数" : ws.type == .video ? "时长 · 尺寸" : "尺寸") { item in
                Text(detail(item))
                    .foregroundStyle(item.metaError == nil ? AnyShapeStyle(.secondary) : AnyShapeStyle(Color.warning))
                    .monospacedDigit()
                    .help(Self.explain(item.metaError))
            }
            // 列宽按设计稿固定：尺寸 104、原大小 80、输出 80、变化 64、状态 92；名称列吃掉其余宽度。
            .width(min: ws.type == .video ? 128 : 88, ideal: ws.type == .video ? 150 : 104)
            TableColumn("原大小") { item in
                Text(Format.size(item.bytes)).foregroundStyle(.secondary).monospacedDigit()
            }
            .width(min: 64, ideal: 80)
            // 不做试压：点「开始」写出后才有输出体积。
            TableColumn("输出") { item in output(item) }
                .width(min: 64, ideal: 80)
            TableColumn("变化") { item in change(item) }
                .width(min: 52, ideal: 64)
            TableColumn("状态") { item in StateLabel(item: item, queued: ws.running) }
                .width(min: 76, ideal: 92)
        } rows: {
            ForEach(ws.files) { item in
                // 处理完的行可以直接拖出：压好的文件拖进微信、浏览器、邮件。
                TableRow(item)
                    .itemProvider {
                        // 传文件地址（不是文件内容），拖到访达、微信里保留原文件名。
                        guard let out = item.outputs.first else { return nil }
                        let url = URL(fileURLWithPath: out.path)
                        let provider = NSItemProvider(object: url as NSURL)
                        provider.suggestedName = url.lastPathComponent
                        return provider
                    }
            }
        }
        .contextMenu(forSelectionType: FileItem.ID.self) { ids in
            let items = ws.files.filter { ids.contains($0.id) }
            Button("在访达中显示") { model.reveal(items.flatMap(outputsOrSource)) }
            if let locked = items.first(where: { $0.metaError == "有打开密码" }) {
                Button("输入密码…") { unlocking = locked }
            }
            Button("从列表移除") { model.remove(ids, from: ws) }.disabled(ws.running)
        } primaryAction: { ids in
            model.reveal(ws.files.filter { ids.contains($0.id) }.flatMap(outputsOrSource))
        }
        .onDeleteCommand { if !ws.running { model.remove(ws.selection, from: ws) } }
        // 空格 / ⌘Y 快速查看：处理完看输出，没处理看原文件（和访达一致）。
        .quickLookPreview($quickLook)
        .onReceive(NotificationCenter.default.publisher(for: .quickLookSelection)) { _ in
            guard let item = ws.selectedItems.first else { return }
            quickLook = item.outputs.first.map { URL(fileURLWithPath: $0.path) } ?? item.url
        }
        .background(SpaceKeyWatcher())
        .sheet(item: $unlocking) { item in PasswordSheet(item: item) }
        // 与访达列表、设计稿一致：不画行线，用交替底色区分行。
        .tableStyle(.inset)
        .alternatingRowBackgrounds(.enabled)
        .modifier(NoTopScrollEdge())
        // 表格的滚动视图会延伸到卡片外、把表头底色画上去；裁在自己的区域内。
        .clipped()
    }

    /// 列里写短的原因，鼠标停上去给一句怎么办。
    static func explain(_ error: String?) -> String {
        switch error {
        case nil: ""
        case "文件不完整": "文件不完整，可能没下载完。重新下载后点「重试失败的」"
        case "文件损坏": "文件损坏，读不出来，换一份原文件再试"
        case "有打开密码": "右键 →「输入密码…」，密码只在这次处理里用，不保存"
        case let e?: e
        }
    }

    private func outputsOrSource(_ item: FileItem) -> [URL] {
        item.outputs.isEmpty ? [item.url] : item.outputs.map { URL(fileURLWithPath: $0.path) }
    }

    private func detail(_ item: FileItem) -> String {
        if let e = item.metaError { return e }
        switch ws.type {
        case .image: return item.dimensions
        case .video: return item.duration > 0 ? "\(Format.duration(item.duration)) · \(item.width) × \(item.height)" : "—"
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
            // 还没处理的行不写字（设计稿同）。
            EmptyView()
        }
    }

    @ViewBuilder private func change(_ item: FileItem) -> some View {
        if item.state == .kept {
            Text(ws.type == .pdf ? "已很小" : "+0%").foregroundStyle(.secondary)
        } else if item.state == .done, item.afterBytes > 0, item.bytes > 0 {
            Text(Format.change(item.bytes, item.afterBytes))
                .foregroundStyle(item.afterBytes < item.bytes ? Color.saving : Color.warning)
                .monospacedDigit()
        }
    }
}

struct StateLabel: View {
    let item: FileItem
    /// 处理中排队的行才写「等待」；还没点开始时留空。
    var queued = true

    var body: some View {
        switch item.state {
        case .waiting where item.metaError == "有打开密码":
            Text("需要密码").foregroundStyle(Color.warning).help("右键 →「输入密码…」，密码只在这次处理里用，不保存")
        case .waiting:
            if queued { Text("等待").foregroundStyle(.secondary) }
        case .running(let p):
            // 图片一张很快，没有中间进度，只标「处理中」；视频与 PDF 带百分比。
            Text(item.type == .image || p <= 0 ? "处理中" : "处理中 \(Int(p * 100))%")
                .foregroundStyle(Color.brand)
                .monospacedDigit()
        case .done:
            Text(item.outputs.count > 1 ? "\(item.outputs.count) 个文件" : "完成").foregroundStyle(Color.saving)
        case .kept:
            Text(item.keptText).foregroundStyle(Color.warning)
                .help("不用处理：压完没有变小，或已经符合配方（比如本来就没有声音），原文件保持不动")
        case .failed(let message):
            Text("失败").foregroundStyle(Bento.failed).help(FileTableView.explain(message))
        case .cancelled:
            Text("已停止").foregroundStyle(Bento.secondary)
        }
    }
}

struct Thumb: View {
    let image: NSImage?

    var body: some View {
        // 先撑满外部给的方框再裁：横图、竖图都裁成方块，不会溢出。
        Color.clear
            .overlay {
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

/// macOS 26 的滚动边缘效果会在表头上方加一层渐隐，放在卡片里不好看；关掉顶部这一侧。
struct NoTopScrollEdge: ViewModifier {
    func body(content: Content) -> some View {
        if #available(macOS 26.0, *) {
            content.scrollEdgeEffectHidden(true, for: .top)
        } else {
            content
        }
    }
}

extension Notification.Name {
    static let quickLookSelection = Notification.Name("SimpleSmaller.quickLookSelection")
}

/// 主窗口里按空格转成「快速查看」（和访达一样）；焦点在输入框、按钮上时不拦。
struct SpaceKeyWatcher: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { Probe() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    final class Probe: NSView {
        private var monitor: Any?
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let monitor { NSEvent.removeMonitor(monitor); self.monitor = nil }
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
                guard e.keyCode == 49, e.modifierFlags.intersection([.command, .option, .control, .shift]).isEmpty,
                      let w = self?.window, e.window === w else { return e }
                // 焦点在表格里，或没有控件拿着焦点（点表格行时焦点留在窗口上）才接；在输入框、按钮上按空格照常。
                let inTable = (w.firstResponder as? NSView).map { sequence(first: $0, next: { $0.superview }).contains { $0 is NSTableView } } ?? false
                guard inTable || w.firstResponder === w else { return e }
                NotificationCenter.default.post(name: .quickLookSelection, object: nil)
                return nil
            }
        }
        deinit { if let monitor { NSEvent.removeMonitor(monitor) } }
    }
}

/// 加密 PDF 输入打开密码。密码只用来解出临时副本，不保存。
struct PasswordSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let item: FileItem
    @State private var password = ""
    @State private var error: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(verbatim: "「\(item.name)」有打开密码").font(.system(size: 13, weight: .semibold))
            Text("输入密码后就能压缩、合并。密码只在这次处理里用，不会保存；输出的文件不带密码，需要的话在「选项」里另加。")
                .font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            SecureField("密码", text: $password)
                .textFieldStyle(.roundedBorder)
                .onSubmit(submit)
            if let error {
                Text(error).font(.system(size: 12)).foregroundStyle(Color.warning)
            }
            HStack {
                Spacer()
                Button("取消") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("解锁") { submit() }.keyboardShortcut(.defaultAction).disabled(password.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 380)
    }

    private func submit() {
        guard !password.isEmpty else { return }
        if let message = model.unlock(item, password: password) {
            error = message
        } else {
            dismiss()
        }
    }
}
