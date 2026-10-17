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
            Text("保留原图").font(.system(size: 10, weight: .semibold)).foregroundStyle(.white)
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

struct FormatQualityCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 14) {
                BentoHeader(symbol: "slider.horizontal.3", title: "格式与质量", subtitle: "跟着配方走，改了会提示") {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                VStack(alignment: .leading, spacing: 8) {
                    Text("格式").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    PillChoice(selection: $ws.draft.image.format, options: ImageFormatChoice.allCases.map { ($0, $0.title) }, label: "格式")
                }
                Spacer(minLength: 0)
                QualityPanel(ws: ws)
            }
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }
}

/// 「已修改」+「存为新配方」。
struct SaveRecipeAccessory: View {
    let ws: Workspace
    @Binding var saving: Recipe?

    var body: some View {
        if ws.modified, ws.usesRecipe {
            HStack(spacing: 6) {
                Text("已修改")
                    .font(.system(size: 11))
                    .foregroundStyle(Bento.alertText)
                    .padding(.horizontal, 10)
                    .frame(height: 26)
                    .background(Capsule().fill(Bento.alertFill))
                CapsuleButton(title: "存为新配方") {
                    var r = ws.draft
                    r.id = ""
                    r.name = ws.base.builtIn ? "\(ws.base.name) 改" : ws.base.name
                    r.builtIn = false
                    saving = r
                }
            }
            .transition(.opacity.combined(with: .scale(scale: 0.9)))
        }
    }
}

/// 灰底质量面板：大数字 + 红色滑杆 +「更小 / 更清晰」；无损、限体积时换成对应说明。
struct QualityPanel: View {
    @Bindable var ws: Workspace

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let kb = ws.draft.image.targetKB {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("不超过")
                    TextField("", value: Binding(get: { kb }, set: { ws.draft.image.targetKB = max(10, $0) }), format: .number.grouping(.never))
                        .textFieldStyle(.plain)
                        .font(Bento.number(22, weight: .medium))
                        .frame(width: 80)
                        .accessibilityLabel("体积上限")
                    Text("KB").foregroundStyle(Bento.secondary)
                    Spacer()
                }
                Text("逐档降质量，直到每张不超过这个体积。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            } else if ws.draft.image.lossless {
                HStack {
                    Text("质量")
                    Text("无损").font(Bento.number(22, weight: .medium))
                    Spacer()
                    Toggle("无损", isOn: $ws.draft.image.lossless).labelsHidden().toggleStyle(.switch).tint(.brand).controlSize(.small)
                }
                Text("画面一点不变，只去掉多余数据。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            } else {
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    Text("质量")
                    Text(verbatim: "\(ws.draft.image.quality) · \(QualityWord.of(ws.draft.image.quality))")
                        .font(Bento.number(22, weight: .medium))
                    Spacer()
                }
                BrandSlider(value: Binding(get: { Double(ws.draft.image.quality) }, set: { ws.draft.image.quality = Int($0.rounded()) }), range: 30...100)
                    .frame(height: 24)
                    .accessibilityLabel("质量")
                    .accessibilityValue("\(ws.draft.image.quality)")
                HStack {
                    Button("更小") { ws.draft.image.quality = max(30, ws.draft.image.quality - 6) }
                    Spacer()
                    Button("更清晰") { ws.draft.image.quality = min(100, ws.draft.image.quality + 6) }
                }
                .buttonStyle(.plain)
                .font(.system(size: 11))
                .foregroundStyle(Bento.secondary)
            }
        }
        .padding(.horizontal, 18)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Bento.fill))
    }
}

/// 粗轨道品牌红滑杆（设计稿：8 px 轨道、24 px 白色圆钮）。键盘方向键可调。
struct BrandSlider: View {
    @Binding var value: Double
    let range: ClosedRange<Double>
    var step: Double = 1

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let t = (value - range.lowerBound) / (range.upperBound - range.lowerBound)
            ZStack(alignment: .leading) {
                Capsule().fill(Bento.line).frame(height: 8)
                Capsule().fill(Color.brand).frame(width: max(8, w * t), height: 8)
                Circle().fill(.white)
                    .frame(width: 24, height: 24)
                    .shadow(color: .black.opacity(0.18), radius: 3, y: 2)
                    .overlay(Circle().strokeBorder(.black.opacity(0.08), lineWidth: 0.5))
                    .offset(x: (w - 24) * t)
            }
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { g in
                let x = min(max(0, g.location.x - 12), w - 24) / max(1, w - 24)
                let v = range.lowerBound + x * (range.upperBound - range.lowerBound)
                value = (v / step).rounded() * step
            })
        }
        .focusable()
        .onKeyPress(.leftArrow) { value = max(range.lowerBound, value - step); return .handled }
        .onKeyPress(.rightArrow) { value = min(range.upperBound, value + step); return .handled }
        // 旁白按系统滑杆读：有数值，上下轻扫可调。
        .accessibilityRepresentation {
            Slider(value: $value, in: range, step: step)
        }
    }
}

// MARK: - 尺寸

/// 尺寸卡：原尺寸 / 自定义；自定义时大号像素数 + 宽 / 高 / 长边。多档尺寸在配方里加，这里只读列出。
struct SizeCard: View {
    @Bindable var ws: Workspace

    private var custom: Binding<Bool> {
        Binding(get: { ws.draft.image.size != nil }, set: { on in
            ws.draft.image.size = on ? (ws.base.image.size ?? 2560) : nil
        })
    }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "arrow.up.left.and.arrow.down.right", title: "尺寸",
                            subtitle: ws.draft.image.size == nil ? "保持原图像素，只压体积" : "缩放到指定尺寸") {
                    MiniSegmented(selection: custom, options: [(false, "原尺寸"), (true, "自定义")], label: "尺寸方式")
                }
                ZStack(alignment: .topLeading) {
                    if let size = ws.draft.image.size {
                        VStack(alignment: .leading, spacing: 10) {
                            HStack(alignment: .firstTextBaseline, spacing: 6) {
                                Text(ws.draft.image.sizeAxis.title).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                                TextField("", value: Binding(get: { size }, set: { ws.draft.image.size = max(16, $0) }), format: .number.grouping(.never))
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 44, weight: .light).monospacedDigit())
                                    .frame(width: CGFloat(max(2, String(size).count)) * 27 + 4)
                                    .accessibilityLabel("像素")
                                Text("px").font(.system(size: 13)).foregroundStyle(Bento.secondary)
                            }
                            MiniSegmented(selection: $ws.draft.image.sizeAxis, options: SizeAxis.allCases.map { ($0, $0.title) }, label: "按宽、高或长边")
                            Spacer(minLength: 0)
                            if !ws.draft.image.extraSizes.isEmpty {
                                BentoRow(label: "另出") {
                                    Text(verbatim: ws.draft.image.extraSizes.map { "\($0)" }.joined(separator: " / ") + " px")
                                        .foregroundStyle(Bento.secondary).monospacedDigit()
                                }
                                .help("这个配方每张图还会多出这几档小图，给网站按屏幕大小加载用；在配方里改")
                            } else {
                                Text("要一次出多档尺寸，在配方里加。")
                                    .font(.system(size: 11)).foregroundStyle(Bento.secondary)
                            }
                        }
                        .transition(.bentoGrow)
                    } else {
                        VStack(alignment: .leading, spacing: 6) {
                            Spacer(minLength: 0)
                            Text("原尺寸").font(.system(size: 38, weight: .light)).foregroundStyle(Bento.line)
                            Text("每张保持原来的宽高，只压体积。\n要缩小，切到「自定义」。")
                                .font(.system(size: 12)).foregroundStyle(Bento.secondary).lineSpacing(3)
                            Spacer(minLength: 0)
                        }
                        .transition(.bentoGrow)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .animation(.spring(response: 0.45, dampingFraction: 0.82), value: ws.draft.image.size == nil)
            }
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
    }
}

// MARK: - 裁切

/// 比例：八格，跟着配方选中；临时换比例会让配方显示「已修改」（设计稿 05）。
struct CropRatioCard: View {
    @Bindable var ws: Workspace

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 8), count: 4)

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "crop", title: "比例", subtitle: "跟着配方选中；没调过的图居中取最大")
                LazyVGrid(columns: columns, spacing: 8) {
                    ForEach(CropPreset.imagePresets) { preset in
                        RatioTile(preset: preset, on: ws.draft.image.crop?.id == preset.id) { choose(preset) }
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel("比例")
                Spacer(minLength: 0)
            }
        }
    }

    private func choose(_ preset: CropPreset) {
        guard ws.draft.image.crop?.id != preset.id else { return }
        withAnimation(.smooth(duration: 0.3)) {
            ws.draft.image.crop = preset
            ws.draft.image.cropWidth = nil
            ws.resetCrop()
        }
    }
}

private struct RatioTile: View {
    let preset: CropPreset
    let on: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 2) {
                Text(preset.ratioLabel.isEmpty ? "—" : preset.ratioLabel).font(.system(size: 17, weight: .semibold).monospacedDigit())
                Text(preset.ratioLabel.isEmpty ? "自由 · 不锁比例" : preset.name).font(.system(size: 11)).lineLimit(1)
                    .foregroundStyle(on ? Bento.onInk.opacity(0.75) : Bento.secondary)
            }
            .foregroundStyle(on ? Bento.onInk : Color.primary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(on ? Bento.ink : Bento.fill))
            .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .accessibilityLabel(preset.ratioLabel.isEmpty ? "自由比例" : "\(preset.name) \(preset.ratioLabel)")
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
    }
}

/// 输出尺寸：填宽，高按比例算；原图不够大时提醒、不放大。
struct CropSizeCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "arrow.left.and.right", title: "输出尺寸", subtitle: "填宽，高按比例自动算")
                HStack(spacing: 10) {
                    Text("宽").foregroundStyle(Bento.secondary)
                    CircleIconButton(symbol: "minus", label: "减小宽度", size: 32) { setWidth(ws.cropOutputWidth - 10) }
                        .buttonRepeatBehavior(.enabled)
                    TextField("", value: Binding(get: { ws.cropOutputWidth }, set: { setWidth($0) }), format: .number.grouping(.never))
                        .textFieldStyle(.plain)
                        .font(.system(size: 34, weight: .light).monospacedDigit())
                        .multilineTextAlignment(.center)
                        .frame(width: 100)
                        .accessibilityLabel("输出宽度")
                    CircleIconButton(symbol: "plus", label: "增大宽度", size: 32) { setWidth(ws.cropOutputWidth + 10) }
                        .buttonRepeatBehavior(.enabled)
                    Text("px").foregroundStyle(Bento.secondary)
                }
                BentoRow(label: "高") {
                    Text(verbatim: "\(outputHeight) px · 按比例").foregroundStyle(Bento.secondary).monospacedDigit()
                }
                Spacer(minLength: 0)
                if let short = shortfall {
                    Label(short, systemImage: "exclamationmark.triangle.fill")
                        .font(.system(size: 11))
                        .foregroundStyle(Color.warning)
                        .fixedSize(horizontal: false, vertical: true)
                        .transition(.opacity)
                } else {
                    Text("原图不够大时提醒，不放大。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                }
            }
            .animation(.smooth(duration: 0.25), value: shortfall)
        }
        .disabled(ws.running)
    }

    private var currentBox: CGRect { ws.effectiveCropBox(ws.cropCurrentIndex) }

    private var outputHeight: Int {
        let w = Double(ws.cropOutputWidth)
        if let a = ws.cropAspect { return Int((w / a).rounded()) }
        let b = currentBox
        return b.width > 0 ? Int((w * b.height / b.width).rounded()) : 0
    }

    private var shortfall: String? {
        let b = currentBox
        guard b.width > 0, Int(b.width) < ws.cropOutputWidth else { return nil }
        return "这张裁出 \(Int(b.width)) px 宽，不够 \(ws.cropOutputWidth)，按 \(Int(b.width)) px 输出"
    }

    private func setWidth(_ w: Int) {
        ws.draft.image.cropWidth = min(20000, max(16, w))
    }
}

/// 裁切的格式与开始：格式跟着配方，只用于这次裁切的输出。
struct CropFormatStartCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "slider.horizontal.3", title: "格式与质量", subtitle: "跟着配方走")
                MiniSegmented(selection: $ws.draft.image.format,
                              options: ([.webp, .jpeg, .png] + (ws.draft.image.format == .original || ws.draft.image.format == .avif ? [ws.draft.image.format] : [])).map { ($0, $0.title) },
                              label: "格式")
                if !ws.draft.image.lossless {
                    HStack {
                        Text("质量").foregroundStyle(Bento.secondary)
                        BrandSlider(value: Binding(get: { Double(ws.draft.image.quality) }, set: { ws.draft.image.quality = Int($0.rounded()) }), range: 30...100)
                            .frame(height: 22)
                            .accessibilityLabel("质量")
                        Text(verbatim: "\(ws.draft.image.quality)").monospacedDigit().frame(width: 28, alignment: .trailing)
                    }
                    .font(.system(size: 12))
                }
                StartBlock(ws: ws).padding(.top, 2)
            }
        }
    }
}
