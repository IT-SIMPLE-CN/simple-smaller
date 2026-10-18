import QuickLook
import SwiftUI

// MARK: - 左大卡：拖放 / 文件

/// 没有文件时的左大卡（设计稿 V3 · 11）：斜纹底上放首页同款大插图，整块就是一个「选取文件」按钮，
/// 点哪都打开选取面板，也能拖进来、粘贴。悬停或拖着文件经过时整块变红框浅红底。
struct DropCard: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    @State private var targeted = false
    @State private var hovering = false

    private var hot: Bool { targeted || hovering }
    private static let hotFill = Bento.dynamic(0xFFF5F2, 0x3D201C)
    /// 斜纹两色（设计稿 #FAFAFB / #F4F5F7，135° 每 14 一条）。
    private static let stripeLight = Bento.dynamic(0xFAFAFB, 0x2A2B2E)
    private static let stripeDark = Bento.dynamic(0xF4F5F7, 0x26272A)
    private static let edge = Bento.dynamic(0xD5D8DC, 0x45474C)

    var body: some View {
        BentoCard(padding: EdgeInsets(top: 14, leading: 14, bottom: 14, trailing: 14)) {
            let shape = RoundedRectangle(cornerRadius: 18, style: .continuous)
            ZStack {
                // 整块是一个按钮：插图、说明文字、空白处点了都走这里。
                Button { FilePicker.pick(into: model) } label: {
                    ZStack {
                        if hot { shape.fill(Self.hotFill) } else { Stripes(light: Self.stripeLight, dark: Self.stripeDark).clipShape(shape) }
                    }
                    .contentShape(shape)
                }
                .buttonStyle(.plain)
                .accessibilityLabel("选取文件")
                .accessibilityHint(title)
                VStack(spacing: 18) {
                    Group {
                        art
                            .frame(width: 224, height: 168)
                            .clipShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
                            .shadow(color: Color.brand.opacity(0.18), radius: 20, y: 16)
                            .scaleEffect(targeted ? 1.04 : 1)
                        VStack(spacing: 6) {
                            Text(title)
                                .font(.system(size: 22, weight: .bold))
                                .multilineTextAlignment(.center)
                                .minimumScaleFactor(0.75)
                            if let formats {
                                Text(formats).font(.system(size: 12)).tracking(0.5).foregroundStyle(Bento.secondary)
                            }
                        }
                    }
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
                    HStack(spacing: 8) {
                        // 不是另一个按钮，只是提示：点它和点空白处一样（落到下面的整块按钮）。
                        Text("点任意位置选取")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundStyle(hovering ? Color.white : Bento.onInk)
                            .padding(.horizontal, 18)
                            .frame(height: 40)
                            .background(Capsule().fill(hovering ? Color.brand : Bento.ink))
                            .allowsHitTesting(false)
                            .accessibilityHidden(true)
                        Button { model.pasteFromClipboard() } label: {
                            HStack(spacing: 8) {
                                Image(systemName: "clipboard").font(.system(size: 13))
                                Text("粘贴").font(.system(size: 13))
                                Text("⌘⇧V").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                            }
                            .foregroundStyle(Color.primary)
                            .padding(.horizontal, 18)
                            .frame(height: 40)
                            .background(Capsule().fill(Bento.card))
                            .overlay(Capsule().strokeBorder(Self.edge, lineWidth: 1))
                            .contentShape(Capsule())
                        }
                        .buttonStyle(BentoPressStyle())
                        .accessibilityLabel("粘贴剪贴板")
                    }
                    Text("也可以在访达里按住 ⌥ 点文件，不用开这个窗口")
                        .font(.system(size: 11))
                        .foregroundStyle(Bento.secondary)
                        .allowsHitTesting(false)
                        .accessibilityHidden(true)
                }
                .padding(.horizontal, 12)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .overlay {
                shape.strokeBorder(hot ? Color.brand : Self.edge, lineWidth: 2).allowsHitTesting(false)
            }
            .onHover { hovering = $0 }
        }
        .animation(.easeOut(duration: 0.2), value: hot)
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: ws.type)
            return true
        } isTargeted: { targeted = $0 }
    }

    /// 首页同一类卡片的插图（PDF「图片转 PDF」收图片，用图片那张）。
    @ViewBuilder private var art: some View {
        let name = ws.type == .video ? "home-video" : ws.type == .pdf && ws.pdfSegment != .fromImages ? "home-pdf" : "home-image"
        if let image = BundledImage.svg(name) {
            Image(nsImage: image).resizable().scaledToFill()
        } else {
            Rectangle().fill(Color.brand)
        }
    }

    private var title: String {
        switch ws.type {
        case .image: "点这里，或把图片 / 文件夹拖进来"
        case .video: "点这里，或把视频拖进来"
        case .pdf: ws.pdfSegment == .fromImages ? "点这里，或把图片拖进来" : "点这里，或把 PDF 拖进来"
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
                    CapsuleButton(title: "清空") { model.clearWithUndo(ws) }
                        .disabled(ws.running)
                        .opacity(ws.running ? 0.4 : 1)
                        .help("清空这一批（⌘⇧⌫），5 秒内可撤销")
                        .accessibilityLabel("清空这一批")
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
        if ws.isCropping { return "\(ws.files.count) 个文件 · 按 \(ws.cropPreset?.ratioLabel.isEmpty == false ? ws.cropPreset!.ratioLabel : "自由比例") 裁" }
        return ws.files.count > 1 ? "\(ws.files.count) 个文件 · 统一设置" : "1 个文件"
    }

    private var subtitle: String {
        if ws.running { return "完成一个写出一个 · 按 ⌘. 停止" }
        if ws.isCropping { return "点一张可以调框" }
        return ws.files.count > 1 ? "改动对这一批全部生效 · \(Format.size(ws.totalBytes))" : Format.size(ws.totalBytes)
    }
}

/// 缩略图网格：三列方块，底部胶囊写文件名与大小；处理中、完成在角上标状态。
/// 选中后空格快速查看，处理完的可以直接拖出。
struct ThumbGrid: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    private let columns = [GridItem(.adaptive(minimum: 130, maximum: 220), spacing: 10)]

    var body: some View {
        ScrollView {
            LazyVGrid(columns: columns, spacing: 10) {
                ForEach(ws.files) { item in
                    let crop = cropIndex(item)
                    ThumbTile(item: item, selected: ws.selection.contains(item.id),
                              cropBox: crop.map { ws.effectiveCropBox($0) }, adjusted: ws.cropBoxes[item.url] != nil)
                        .fileInteractions(item, in: ws)
                        // 选了比例：点一张进大图调框。
                        .simultaneousGesture(TapGesture().onEnded {
                            guard let i = crop, !ws.running else { return }
                            withAnimation(.spring(response: 0.45, dampingFraction: 0.85)) {
                                ws.cropIndex = i
                                ws.cropEditing = true
                            }
                        })
                }
            }
            .padding(2)
        }
        .scrollIndicators(.automatic)
        .quickLookSelection(ws)
        .onDeleteCommand { model.remove(ws.selection, from: ws) }
    }

    /// 这张在裁切里是第几张（读不出尺寸的图不参与裁切）。
    private func cropIndex(_ item: FileItem) -> Int? {
        guard ws.isCropping else { return nil }
        return ws.cropItems.firstIndex { $0.id == item.id }
    }
}

struct ThumbTile: View {
    let item: FileItem
    let selected: Bool
    /// 选了比例时这张会裁出来的框（原图像素坐标）。
    var cropBox: CGRect? = nil
    var adjusted = false

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
            .overlay {
                if let cropBox, item.width > 0, item.height > 0 {
                    CropOverlay(box: cropBox, imageSize: CGSize(width: item.width, height: item.height))
                        .transition(.opacity)
                }
            }
            .overlay(alignment: .topLeading) {
                if cropBox != nil, adjusted {
                    Text("调过").font(.system(size: 10, weight: .semibold)).foregroundStyle(Bento.onInk)
                        .padding(.horizontal, 7).frame(height: 20).background(Capsule().fill(Bento.ink))
                        .padding(6)
                }
            }
            .animation(.smooth(duration: 0.3), value: cropBox)
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

/// 缩略图上的裁切框：框外压暗，框描白线。缩略图按「填满方块」显示，框按同样的缩放和偏移换算过来。
struct CropOverlay: View {
    let box: CGRect
    let imageSize: CGSize

    var body: some View {
        GeometryReader { geo in
            let t = geo.size
            let s = max(t.width / imageSize.width, t.height / imageSize.height)
            let dx = (t.width - imageSize.width * s) / 2, dy = (t.height - imageSize.height * s) / 2
            let r = CGRect(x: box.minX * s + dx, y: box.minY * s + dy, width: box.width * s, height: box.height * s)
            ZStack {
                Path { p in
                    p.addRect(CGRect(origin: .zero, size: t))
                    p.addRect(r)
                }
                .fill(Color.black.opacity(0.5), style: FillStyle(eoFill: true))
                Path { $0.addRect(r) }.stroke(Color.white.opacity(0.95), lineWidth: 1.5)
            }
        }
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }
}

/// 斜纹底：135° 方向每 28 一组（浅 14、深 14），和设计稿的 repeating-linear-gradient 一样。
struct Stripes: View {
    let light: Color
    let dark: Color

    var body: some View {
        Canvas { ctx, size in
            ctx.fill(Path(CGRect(origin: .zero, size: size)), with: .color(dark))
            let band = 14 * 2.0.squareRoot()
            var c: CGFloat = 0
            var path = Path()
            while c < size.width + size.height {
                path.move(to: CGPoint(x: c, y: 0))
                path.addLine(to: CGPoint(x: c + band, y: 0))
                path.addLine(to: CGPoint(x: c + band - size.height, y: size.height))
                path.addLine(to: CGPoint(x: c - size.height, y: size.height))
                path.closeSubpath()
                c += band * 2
            }
            ctx.fill(path, with: .color(light))
        }
        .accessibilityHidden(true)
    }
}
