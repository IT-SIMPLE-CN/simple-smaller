import SwiftUI
import UniformTypeIdentifiers

/// 首页（设计稿 V2.2 · H1）：打开应用、还没选类型时显示。
/// 卡片标题下一排小标签，点了直接进那个功能；文件拖到能接它的卡片上，卡片分成几格，放到哪格进哪个；
/// 拖到卡片外、接不了的卡片或窗口别处，照旧按文件类型分流。
struct HomeView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openWindow) private var openWindow
    @State private var drag: HomeDrag?
    @State private var frames: [HomeSlot: CGRect] = [:]

    var body: some View {
        GeometryReader { geo in
            let cardW = min(260, max(200, (geo.size.width - 40 - 176 - 3 * 16 - 40) / 4))
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 14) {
                    Text("拖进来，选一下")
                        .font(.system(size: 46, weight: .semibold))
                        .tracking(-1)
                    Image(systemName: "arrow.turn.down.right")
                        .font(.system(size: 30, weight: .medium))
                        .accessibilityHidden(true)
                }
                .padding(.top, max(20, (geo.size.height - 640) / 2))
                Spacer(minLength: 24).frame(maxHeight: 56)
                HStack(alignment: .top, spacing: 16) {
                    VStack(alignment: .leading, spacing: 0) {
                        Button { openWindow(id: "about") } label: {
                            HStack(spacing: 12) {
                                Text("关于")
                                Image(systemName: "arrow.up.right").font(.system(size: 11, weight: .semibold))
                            }
                            .font(.system(size: 12))
                            .padding(.leading, 16)
                            .padding(.trailing, 12)
                            .frame(height: 38)
                            .overlay(Capsule().strokeBorder(Color.primary, lineWidth: 1.2))
                            .contentShape(Capsule())
                        }
                        .buttonStyle(BentoPressStyle())
                        Spacer()
                        Text("图片、视频、PDF\n都在本机处理，不上传。")
                            .font(.system(size: 12))
                            .foregroundStyle(Bento.secondary)
                            .lineSpacing(4)
                    }
                    .frame(width: 160, height: 392, alignment: .leading)
                    ForEach(Array(HomeCard.Kind.allCases.enumerated()), id: \.element) { i, kind in
                        let hot = hotCard == kind
                        HomeCard(kind: kind, split: hot, hotCell: hot ? hotCell : nil,
                                 blocked: hot && drag != nil ? drag!.blockedCells(kind) : [])
                            .frame(width: cardW, height: 392)
                            .background(frameReader(.card(kind)))
                            .zIndex(hot ? 1 : 0)
                            .bentoAppear(i)
                    }
                }
                Text("点标签直接进；拖文件到卡片上，卡片会分成几格，放到哪格进哪个。")
                    .font(.system(size: 12))
                    .foregroundStyle(Bento.secondary)
                    .padding(.leading, 176)
                    .padding(.top, 28)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 40)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .coordinateSpace(name: HomeSlot.space)
            .onPreferenceChange(HomeFrames.self) { frames = $0 }
            .onDrop(of: [.fileURL], delegate: HomeDropDelegate(model: model, drag: $drag, frames: frames))
        }
    }

    // MARK: 拖放落点

    /// 指针下面、能接这批文件的卡片（只有它分格）。
    private var hotCard: HomeCard.Kind? {
        guard let drag else { return nil }
        return HomeDropDelegate.card(at: drag.location, frames: frames, accepting: drag)
    }

    private var hotCell: Int? {
        guard let drag, let card = hotCard else { return nil }
        return HomeDropDelegate.cell(at: drag.location, in: card, frames: frames, drag: drag)
    }

    private func frameReader(_ slot: HomeSlot) -> some View {
        GeometryReader { g in Color.clear.preference(key: HomeFrames.self, value: [slot: g.frame(in: .named(HomeSlot.space))]) }
    }
}

/// 首页里要记位置的地方：四张卡、分出来的每一格。
enum HomeSlot: Hashable {
    case card(HomeCard.Kind)
    case cell(HomeCard.Kind, Int)
    static let space = "home"
}

struct HomeFrames: PreferenceKey {
    static let defaultValue: [HomeSlot: CGRect] = [:]
    static func reduce(value: inout [HomeSlot: CGRect], nextValue: () -> [HomeSlot: CGRect]) {
        value.merge(nextValue()) { $1 }
    }
}

/// 正在拖进来的这批文件：有哪些类型、有没有文件夹，以及指针位置。
struct HomeDrag: Equatable {
    var urls: [URL]
    var types: Set<MediaType>
    var hasFolder: Bool
    var location: CGPoint

    /// 读拖拽剪贴板（拖的过程中就能同步读到文件路径）。
    @MainActor static func current(at location: CGPoint) -> HomeDrag {
        let urls = NSPasteboard(name: .drag).readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL] ?? []
        return make(urls, at: location)
    }

    @MainActor static func make(_ urls: [URL], at location: CGPoint) -> HomeDrag {
        var types = Set<MediaType>()
        var folder = false
        for url in urls {
            if (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true { folder = true }
            else if let t = MediaType.of(url) { types.insert(t) }
        }
        return HomeDrag(urls: urls, types: types, hasFolder: folder, location: location)
    }

    /// 这张卡能接：拖的文件里至少有一个是它的类型（文件夹按卡片的类型展开，都能接）。
    /// PDF 卡的「图片转 PDF」格接图片，所以拖的文件里有图片时 PDF 卡也分格。
    func fits(_ kind: HomeCard.Kind) -> Bool {
        kind.features.indices.contains { accepts(kind, cell: $0) }
    }

    /// 某一格接不接这批文件：PDF 卡「图片转 PDF」接图片，其余 4 格接 PDF；别的卡按卡片类型。
    func accepts(_ kind: HomeCard.Kind, cell: Int) -> Bool {
        guard let t = kind.type, kind.features.indices.contains(cell) else { return false }
        if hasFolder { return true }
        if kind == .pdf, kind.features[cell].title == Workspace.PdfSegment.fromImages.rawValue { return types.contains(.image) }
        return types.contains(t)
    }

    /// 分格时不能放的格子（变淡、不高亮），如只拖图片时 PDF 卡除「图片转 PDF」外的 4 格。
    func blockedCells(_ kind: HomeCard.Kind) -> Set<Int> {
        Set(kind.features.indices.filter { !accepts(kind, cell: $0) })
    }
}

/// 首页整块的拖放：悬停时算出落在哪张卡、哪一格；松手时按格子进功能，否则按类型分流。
struct HomeDropDelegate: DropDelegate {
    let model: AppModel
    @Binding var drag: HomeDrag?
    let frames: [HomeSlot: CGRect]

    static func card(at p: CGPoint, frames: [HomeSlot: CGRect], accepting drag: HomeDrag) -> HomeCard.Kind? {
        HomeCard.Kind.allCases.first { frames[.card($0)]?.contains(p) == true && drag.fits($0) }
    }

    /// 指针在哪一格；在卡片里但不在格子上（标题、间隙）时取最近的一格能放的。
    /// 指在不能放的格子上时为 nil（松手按类型分流）。
    static func cell(at p: CGPoint, in card: HomeCard.Kind, frames: [HomeSlot: CGRect], drag: HomeDrag) -> Int? {
        let cells = card.features.indices.compactMap { i in frames[.cell(card, i)].map { (i, $0) } }
        if let hit = cells.first(where: { $0.1.contains(p) }) { return drag.accepts(card, cell: hit.0) ? hit.0 : nil }
        return cells.filter { drag.accepts(card, cell: $0.0) }.min { abs($0.1.midY - p.y) < abs($1.1.midY - p.y) }?.0
    }

    func validateDrop(info: DropInfo) -> Bool { info.hasItemsConforming(to: [.fileURL]) }

    func dropEntered(info: DropInfo) {
        MainActor.assumeIsolated { drag = HomeDrag.current(at: info.location) }
    }

    func dropUpdated(info: DropInfo) -> DropProposal? {
        MainActor.assumeIsolated { () -> Void in
            // 只更新位置：松手之后系统还会再报一次 update，不能在这里重建拖拽状态，否则卡片一直分着格。
            drag?.location = info.location
        }
        return DropProposal(operation: .copy)
    }

    func dropExited(info: DropInfo) {
        MainActor.assumeIsolated { drag = nil }
    }

    func performDrop(info: DropInfo) -> Bool {
        let handled = MainActor.assumeIsolated { () -> Bool in
            var state = drag ?? HomeDrag.current(at: info.location)
            drag = nil
            guard !state.urls.isEmpty else { return false }
            state.location = info.location
            route(state)
            return true
        }
        if handled { return true }
        // 拖拽剪贴板读不到时，从 item provider 取路径再分流。
        let providers = info.itemProviders(for: [.fileURL])
        let location = info.location
        Task { @MainActor in
            var urls: [URL] = []
            for p in providers {
                if let url = try? await p.loadItem(forTypeIdentifier: UTType.fileURL.identifier) as? Data,
                   let u = URL(dataRepresentation: url, relativeTo: nil) { urls.append(u) }
            }
            route(HomeDrag.make(urls, at: location))
        }
        return true
    }

    /// 落在能接的卡片上：这类文件进那一格的功能，其余按类型分；否则整批按类型分流（同 V2.1）。
    @MainActor func route(_ d: HomeDrag) {
        guard !d.urls.isEmpty else { return }
        if let card = Self.card(at: d.location, frames: frames, accepting: d), let type = card.type,
           let i = Self.cell(at: d.location, in: card, frames: frames, drag: d) {
            // 放进 PDF 卡「图片转 PDF」：图片进 PDF 的「图片转 PDF」（add 在这个分段下把图片收进 PDF 工作区）。
            model.enter(type, feature: card.features[i].title, with: d.urls)
        } else {
            model.add(d.urls)
        }
    }
}

struct HomeCard: View {
    enum Kind: CaseIterable, Hashable {
        case image, video, pdf, ring

        var title: String {
            switch self {
            case .image: "图片"
            case .video: "视频"
            case .pdf: "PDF"
            case .ring: "访达圆环"
            }
        }

        /// 卡片上的小标签，也是拖进来时分出的格子（顺序照稿）。
        var features: [(title: String, glyph: LineIcon.Glyph)] {
            switch self {
            case .image: [("压缩", .compress), ("裁切", .crop)]
            case .video: Workspace.VideoSegment.allCases.map { ($0.rawValue, $0.glyph) }
            case .pdf: Workspace.PdfSegment.allCases.map { ($0.rawValue, $0.glyph) }
            case .ring: []
            }
        }

        var text: String {
            switch self {
            case .image: "WebP、AVIF、JPG 一键变小，一次出多档尺寸；按比例批量裁切。"
            case .video: "按体积压到 25 MB，裁成 9:16，或者拆成一张张序列帧。"
            case .pdf: "邮件、阅读、打印三档。"
            case .ring: "在访达里按住 ⌥ 点文件，圆环里点一下就好。第一次用要在设置里开启。"
            }
        }

        var formats: String {
            switch self {
            case .image: "JPG · PNG · HEIC"
            case .video: "MP4 · MOV · GIF"
            case .pdf: "PDF"
            case .ring: "macOS · Windows"
            }
        }

        var art: String {
            switch self {
            case .image: "home-image"
            case .video: "home-video"
            case .pdf: "home-pdf"
            case .ring: "home-ring"
            }
        }

        var glyph: LineIcon.Glyph {
            switch self {
            case .image: .image
            case .video: .video
            case .pdf: .pdf
            case .ring: .ring
            }
        }

        var type: MediaType? {
            switch self {
            case .image: .image
            case .video: .video
            case .pdf: .pdf
            case .ring: nil
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.openSettings) private var openSettings
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    let kind: Kind
    /// 拖进来、指针在这张卡上：分成几格。
    var split = false
    var hotCell: Int?
    /// 这批文件放不进去的格子：变淡、不高亮。
    var blocked: Set<Int> = []
    @State private var hovering = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            art
                .aspectRatio(4 / 3, contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(alignment: .topTrailing) {
                    // 右上角挖出一个白色缺口放图标（设计稿）。
                    LineIcon(glyph: kind.glyph, size: 16, weight: 1.8)
                        .foregroundStyle(Color.primary)
                        .frame(width: 34, height: 34)
                        .background(Circle().fill(Bento.fill))
                        .padding([.leading, .bottom], 12)
                        .background(UnevenRoundedRectangle(bottomLeadingRadius: 16, style: .continuous).fill(Bento.card))
                }
                .accessibilityHidden(true)
            Text(kind.title)
                .font(.system(size: 24, weight: .semibold))
                .padding(.top, 18)
                .accessibilityAddTraits(.isHeader)
            if kind == .ring {
                Text("不开窗口也能压")
                    .font(.system(size: 13))
                    .foregroundStyle(Bento.secondary)
                    .padding(.top, 4)
            } else {
                FlowLayout(spacing: 4) {
                    ForEach(Array(kind.features.enumerated()), id: \.offset) { _, f in tag(f.title, f.glyph) }
                }
                .padding(.top, 8)
            }
            Rectangle().fill(Color.primary).frame(width: 18, height: 1.5).padding(.top, 16).accessibilityHidden(true)
            Text(kind.text)
                .font(.system(size: 12))
                .foregroundStyle(Bento.body)
                .lineSpacing(3)
                .fixedSize(horizontal: false, vertical: true)
                .padding(.top, 12)
            Spacer(minLength: 8)
            HStack {
                Button(action: open) {
                    LineIcon(glyph: .arrowUpRight, size: 16)
                        .foregroundStyle(kind == .image ? Color.white : Color.primary)
                        .frame(width: 36, height: 36)
                        .background(Circle().fill(kind == .image ? Color.brand : Bento.fill))
                        .contentShape(Circle())
                }
                .buttonStyle(BentoPressStyle())
                .accessibilityLabel(kind.type == nil ? "打开设置" : "进入\(kind.title)")
                Spacer()
                Text(kind.formats).font(.system(size: 10)).tracking(0.4).foregroundStyle(Bento.secondary)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.card))
        .shadow(color: .black.opacity(hovering ? 0.08 : 0.03), radius: hovering ? 12 : 2, y: hovering ? 6 : 1)
        .contentShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        .onTapGesture(perform: open)
        .onHover { hovering = $0 }
        .overlay {
            if split {
                cells.transition(reduceMotion ? .opacity : .scale(scale: 0.94).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.2), value: hovering)
        .animation(reduceMotion ? .easeOut(duration: 0.15) : .spring(response: 0.32, dampingFraction: 0.8), value: split)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(kind.features.isEmpty ? kind.title : "\(kind.title)：\(kind.features.map(\.title).joined(separator: " · "))")
    }

    /// 小标签：图标 + 名称，点了直接进这个功能。
    private func tag(_ title: String, _ glyph: LineIcon.Glyph) -> some View {
        Button { if let t = kind.type { model.enter(t, feature: title) } } label: {
            HStack(spacing: 4) {
                LineIcon(glyph: glyph, size: 11)
                Text(title).font(.system(size: 11.5, weight: .semibold)).lineLimit(1).fixedSize()
            }
            .foregroundStyle(Color.primary)
            .padding(.leading, 6)
            .padding(.trailing, 9)
            .frame(height: 24)
            .background(Capsule().fill(Bento.fill))
            .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
        .help("进入\(kind.title) · \(title)")
        .accessibilityLabel(title)
        .accessibilityHint("进入\(kind.title)的\(title)")
    }

    /// 拖进来时分出的格子：红框 +「放到哪一格？」，悬停的那格品牌红底 +「松手 → 剪辑」。
    private var cells: some View {
        let many = kind.features.count > 3
        return VStack(alignment: .leading, spacing: 8) {
            Text("放到哪一格？")
                .font(.system(size: 12))
                .foregroundStyle(Bento.secondary)
                .padding(.horizontal, 4)
                .padding(.vertical, 2)
            ForEach(Array(kind.features.enumerated()), id: \.offset) { i, f in
                let hot = hotCell == i && !blocked.contains(i)
                HStack(spacing: 8) {
                    LineIcon(glyph: f.glyph, size: many ? 16 : 18)
                    VStack(alignment: .leading, spacing: 1) {
                        Text(f.title).font(.system(size: many ? 14 : 15, weight: .bold))
                        if hot { Text("松手 → \(f.title)").font(.system(size: 11)).opacity(0.85) }
                    }
                }
                .foregroundStyle(hot ? Color.white : Color.primary)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background {
                    let shape = RoundedRectangle(cornerRadius: 14, style: .continuous)
                    if hot {
                        shape.fill(Color.brand)
                    } else {
                        shape.fill(Bento.card).overlay(shape.strokeBorder(Bento.dash, style: StrokeStyle(lineWidth: 2, dash: [5, 4])))
                    }
                }
                .opacity(blocked.contains(i) ? 0.35 : 1)
                .background(GeometryReader { g in
                    Color.clear.preference(key: HomeFrames.self, value: [.cell(kind, i): g.frame(in: .named(HomeSlot.space))])
                })
                .animation(.easeOut(duration: 0.15), value: hot)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.card))
        .overlay(RoundedRectangle(cornerRadius: 21.5, style: .continuous).stroke(Color.brand, lineWidth: 3).padding(-1.5))
        .shadow(color: Color.brand.opacity(0.22), radius: 20, y: 16)
        .accessibilityHidden(true)
    }

    @ViewBuilder private var art: some View {
        if let image = BundledImage.svg(kind.art) {
            Image(nsImage: image).resizable().scaledToFill()
        } else {
            Rectangle().fill(Bento.fill)
        }
    }

    private func open() {
        if let type = kind.type {
            model.selectedType = type
        } else {
            openSettings()
        }
    }
}

/// 首页工具栏：正中品牌，右边粘贴剪贴板与设置。
struct HomeToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .principal) { brand }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .principal) { brand }
        }
        ToolbarItem(placement: .primaryAction) {
            Button { model.pasteFromClipboard() } label: {
                Label("粘贴剪贴板", systemImage: "doc.on.clipboard")
            }
            .help("处理剪贴板里的图片或文件（⌘⇧V）")
        }
        ToolbarItem(placement: .primaryAction) {
            SettingsLink { Label("设置", systemImage: "gearshape") }
                .help("设置（⌘,）")
        }
    }
}

extension HomeToolbar {
    var brand: some View {
        HStack(spacing: 8) {
            BrandMark(color: .brand).frame(width: 22, height: 21)
            Text("简单小一点").font(.system(size: 15, weight: .bold))
            Text("SIMPLE SMALLER").font(.system(size: 11)).tracking(1.5).foregroundStyle(Bento.secondary)
        }
        .accessibilityElement(children: .combine)
    }
}

