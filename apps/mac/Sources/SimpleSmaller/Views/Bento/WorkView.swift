import SwiftUI
import UniformTypeIdentifiers

/// 一类文件的工作区：便当盒网格；做完换到完成页。
struct WorkView: View {
    let ws: Workspace

    var body: some View {
        Group {
            if ws.showResult, !ws.running, !ws.files.isEmpty {
                ResultView(ws: ws)
                    .transition(.bentoGrow)
            } else {
                WorkGrid(ws: ws)
                    .transition(.opacity)
            }
        }
        .padding(.horizontal, Bento.pad)
        .padding(.bottom, Bento.pad)
        .padding(.top, 6)
        .animation(.spring(response: 0.45, dampingFraction: 0.82), value: ws.showResult)
    }
}

/// 网格（设计稿 03）：左边一张跨三行的大卡放文件或预览；右边上方横条是配方，下面两行参数卡，
/// 右下一格是输出与「开始」。只做三档：
/// 宽（≥ 1080）三列；窄时右侧合成一列可滚动、「开始」固定在底；很宽时右侧两列不再拉宽。
struct WorkGrid: View {
    let ws: Workspace

    private static let barHeight: CGFloat = 58
    private static let maxColumn: CGFloat = 400
    /// 换卡片内容时整组重新弹入。
    private var appearKey: String { "\(ws.type.rawValue)|\(ws.segmentKey)|\(ws.files.isEmpty)" }

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width, h = geo.size.height
            let gap = Bento.gap
            if w < 1080 {
                // 窄：左大卡占一半；右边配方条 + 可滚动的参数卡 + 固定在底的开始卡。
                let right = max(300, w * 0.46)
                HStack(spacing: gap) {
                    main.bentoRegion(5).bentoAppear(0, trigger: appearKey)
                    VStack(spacing: gap) {
                        topBar.frame(height: Self.barHeight).bentoRegion(4).bentoAppear(1, trigger: appearKey)
                        ScrollView {
                            VStack(spacing: gap) {
                                upper.frame(minHeight: 240).bentoRegion(3)
                                lowerLeft.frame(minHeight: 220).bentoRegion(2)
                            }
                        }
                        .scrollIndicators(.automatic)
                        .bentoAppear(2, trigger: appearKey)
                        lowerRight.frame(height: min(280, h * 0.42)).bentoRegion(1).bentoAppear(3, trigger: appearKey)
                    }
                    .frame(width: right)
                }
            } else {
                let unit = (w - 2 * gap) / (leftFraction + 2)
                let col = min(Self.maxColumn, unit)
                let rowH = (h - Self.barHeight - 2 * gap) / 2
                HStack(spacing: gap) {
                    main.bentoRegion(5).bentoAppear(0, trigger: appearKey)
                    VStack(spacing: gap) {
                        topBar.frame(height: Self.barHeight).bentoRegion(4).bentoAppear(1, trigger: appearKey)
                            upper.frame(height: rowH).bentoRegion(3).bentoAppear(2, trigger: appearKey)
                            HStack(spacing: gap) {
                                lowerLeft.frame(width: col).bentoRegion(2).bentoAppear(3, trigger: appearKey)
                                lowerRight.frame(width: col).bentoRegion(1).bentoAppear(4, trigger: appearKey)
                            }
                            .frame(height: rowH)
                    }
                    .frame(width: 2 * col + gap)
                }
            }
        }
        .disabled(ws.running && false)
    }

    /// 左大卡占几份（右边两列各一份）。图片压缩 1.3，有预览的 1.5。
    private var leftFraction: CGFloat {
        ws.type == .image && ws.imageSegment == .compress ? 1.3 : 1.5
    }

    private var isImageCompress: Bool { ws.type == .image && ws.imageSegment == .compress }
    private var isImageCrop: Bool { ws.type == .image && ws.imageSegment == .crop }

    // MARK: 左大卡

    @ViewBuilder private var main: some View {
        if ws.files.isEmpty {
            DropCard(ws: ws)
        } else {
            switch ws.type {
            case .image:
                if isImageCrop { PreviewCard(inset: 14) { CropView(ws: ws) } } else { FilesCard(ws: ws) }
            case .video:
                switch ws.videoSegment {
                case .compress: FilesCard(ws: ws)
                case .edit: PreviewCard(inset: 14) { VideoEditView(ws: ws) }
                case .frames: PreviewCard(inset: 14) { VideoFramesView(ws: ws) }
                }
            case .pdf:
                PreviewCard(inset: 14) { PdfDetailView(ws: ws) }
            }
        }
    }

    // MARK: 配方条

    @ViewBuilder private var topBar: some View {
        if ws.usesRecipe {
            RecipeBar(ws: ws)
        } else {
            BentoCard(padding: EdgeInsets(top: 0, leading: 20, bottom: 0, trailing: 20)) {
                HStack(spacing: 10) {
                    Text(ws.pdfSegment.rawValue).font(.system(size: 13, weight: .semibold))
                    Text(PdfNotes.segmentNote(ws.pdfSegment)).font(.system(size: 12)).foregroundStyle(Bento.secondary).lineLimit(1)
                    Spacer()
                }
                .frame(maxHeight: .infinity)
            }
        }
    }

    // MARK: 参数卡

    @ViewBuilder private var upper: some View {
        switch ws.type {
        case .image: if isImageCrop { CropRatioCard(ws: ws) } else { FormatQualityCard(ws: ws) }
        case .video: if ws.videoSegment == .frames { FramesCard(ws: ws) } else { VideoPictureCard(ws: ws) }
        case .pdf: PdfMainCard(ws: ws)
        }
    }

    @ViewBuilder private var lowerLeft: some View {
        switch ws.type {
        case .image: if isImageCrop { CropSizeCard(ws: ws) } else { SizeCard(ws: ws) }
        case .video: if ws.videoSegment == .frames { FrameEachCard(ws: ws) } else { VideoFormatCard(ws: ws) }
        case .pdf: PdfOptionsCard(ws: ws)
        }
    }

    @ViewBuilder private var lowerRight: some View {
        switch ws.type {
        case .image: if isImageCrop { CropFormatStartCard(ws: ws) } else { OutputStartCard(ws: ws) }
        case .video: if ws.videoSegment == .frames { FramesOutputStartCard(ws: ws) } else { VideoAudioStartCard(ws: ws) }
        case .pdf: if ws.pdfSegment == .compress { PdfStartCard(ws: ws) } else { OutputStartCard(ws: ws) }
        }
    }
}

/// 放预览类视图（裁切、剪辑、序列帧、PDF 页面）的大卡：内容自己铺满，只裁圆角。
struct PreviewCard<Content: View>: View {
    var inset: CGFloat = 0
    @ViewBuilder var content: Content

    var body: some View {
        BentoCard(padding: EdgeInsets(top: inset, leading: inset, bottom: inset, trailing: inset)) { content }
    }
}

// MARK: - 输出与开始

/// 右下一格：输出（文件名、去掉位置信息）与红色「开始」块。
struct OutputStartCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "folder", title: "输出", subtitle: ws.running ? "处理中，设置先锁住" : "原文件不改动")
                Group {
                    if let fixed = OutputNaming.fixedName(ws) {
                        BentoRow(label: "文件名") {
                            Text(verbatim: fixed).font(.system(size: 12, design: .monospaced)).foregroundStyle(Bento.secondary)
                                .lineLimit(1).truncationMode(.head)
                        }
                    } else {
                        BentoRow(label: "文件名") {
                            TextField("", text: $ws.draft.naming.template)
                                .textFieldStyle(.plain)
                                .font(.system(size: 12, design: .monospaced))
                                .foregroundStyle(Bento.secondary)
                                .multilineTextAlignment(.trailing)
                                .accessibilityLabel("文件名规则")
                        }
                    }
                    if ws.type != .pdf {
                        BentoToggleRow(label: "去掉位置与设备信息", isOn: $ws.draft.stripMetadata)
                    }
                }
                .disabled(ws.running)
                StartBlock(ws: ws)
                    .padding(.top, 4)
            }
        }
    }
}

enum OutputNaming {
    /// 拆分、转图片按固定规则命名（文件夹 + 编号），不用配方里的模板。
    @MainActor static func fixedName(_ ws: Workspace) -> String? {
        if ws.type == .video, ws.videoSegment == .frames { return "{name}_frames/{name}_0001" }
        guard ws.type == .pdf else { return nil }
        switch ws.pdfSegment {
        case .split: return "{name}-拆分/{name}-01"
        case .toImages: return ws.draft.pdf.longImage ? "{name}-长图" : "{name}-图片/{name}-01"
        default: return nil
        }
    }
}

/// 红色「开始」块，四种状态大小不变：
/// 没文件置灰 →「开始」→ 处理中（进度，点按停止）→「已完成」（改了设置又变回「开始」）。
struct StartBlock: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    private enum Phase { case empty, ready, running, done }

    private var phase: Phase {
        if ws.files.isEmpty || !ws.canStart { return .empty }
        if ws.running { return .running }
        if ws.isUpToDate { return .done }
        return .ready
    }

    var body: some View {
        Button(action: tap) {
            HStack(spacing: 12) {
                content
                Spacer(minLength: 0)
                knob
            }
            .padding(.leading, 22)
            .padding(.trailing, 14)
            .frame(maxWidth: .infinity, minHeight: 84, maxHeight: .infinity)
            .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(background))
            .shadow(color: Color.brand.opacity(phase == .ready || phase == .running ? 0.3 : 0), radius: 12, y: 8)
            .contentShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .keyboardShortcut(.return, modifiers: .command)
        .disabled(phase == .empty || phase == .done)
        .animation(.spring(response: 0.4, dampingFraction: 0.85), value: phase)
        .help(help)
        .accessibilityLabel(accessibility)
    }

    private func tap() {
        switch phase {
        case .ready: model.start(ws)
        case .running: model.cancel(ws)
        default: break
        }
    }

    private var background: Color {
        switch phase {
        case .empty: Bento.fill
        case .ready, .running: .brand
        case .done: Bento.ink
        }
    }

    private var foreground: Color {
        switch phase {
        case .empty: Bento.secondary
        case .ready, .running: .white
        case .done: Bento.onInk
        }
    }

    @ViewBuilder private var content: some View {
        switch phase {
        case .running:
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("处理中").font(.system(size: 24, weight: .bold))
                    Text(verbatim: "\(ws.finishedCount) / \(ws.files.count)").font(Bento.number(20))
                }
                ProgressBar(value: Double(ws.finishedCount) + runningFraction, total: Double(max(1, ws.files.count)))
                    .frame(height: 6)
                Text("停止 · ⌘.").font(.system(size: 12)).opacity(0.95)
            }
            .foregroundStyle(.white)
        default:
            VStack(alignment: .leading, spacing: 4) {
                Text(phase == .done ? "已完成" : "开始")
                    .font(.system(size: 30, weight: .bold))
                    .tracking(1)
                ViewThatFits(in: .horizontal) {
                    Text(subtitle + shortcut).lineLimit(1)
                    Text(subtitle).lineLimit(1)
                    Text(subtitle).lineLimit(2)
                }
                .font(.system(size: 12)).opacity(0.95).monospacedDigit()
            }
            .foregroundStyle(foreground)
        }
    }

    private var knob: some View {
        ZStack {
            Circle().fill(phase == .empty ? Bento.card : (phase == .done ? Bento.onInk.opacity(0.14) : .white))
            Image(systemName: phase == .running ? "stop.fill" : (phase == .done ? "checkmark" : "arrow.right"))
                .font(.system(size: phase == .running ? 18 : 22, weight: .semibold))
                .foregroundStyle(phase == .empty ? Bento.secondary : (phase == .done ? Bento.onInk : Color.brand))
        }
        .frame(width: 56, height: 56)
        .accessibilityHidden(true)
    }

    /// 正在处理的那一个走到哪了。
    private var runningFraction: Double {
        ws.files.compactMap { if case .running(let f) = $0.state { return f }; return nil }.reduce(0, +)
    }

    private var subtitle: String {
        switch phase {
        case .empty: return ws.files.isEmpty ? "先把文件拖进来" : (ws.blockReason ?? "")
        case .done: return "改了设置可以再开始"
        default: return ws.startSummary
        }
    }

    private var shortcut: String { phase == .ready ? " · ⌘↩" : "" }

    private var help: String {
        switch phase {
        case .running: "停止处理（⌘.）"
        case .done: "这一批已按当前配方处理完；改配方或加文件后可以再开始"
        default: "按当前配方处理全部文件（⌘↩）"
        }
    }

    private var accessibility: String {
        switch phase {
        case .empty: "开始，\(subtitle)"
        case .ready: "开始，\(ws.startSummary)"
        case .running: "处理中 \(ws.finishedCount) / \(ws.files.count)，按下停止"
        case .done: "已完成"
        }
    }
}

/// 白色进度条（红底块上用）。
struct ProgressBar: View {
    let value: Double
    let total: Double
    var track: Color = .white.opacity(0.32)
    var bar: Color = .white

    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(track)
                Capsule().fill(bar).frame(width: max(6, geo.size.width * min(1, value / max(1, total))))
            }
        }
        .animation(.smooth(duration: 0.3), value: value)
        .accessibilityHidden(true)
    }
}

extension Workspace {
    /// 现在能不能开始（PDF 加密码没填、合并没有页面时不能）。
    var canStart: Bool { blockReason == nil }

    var blockReason: String? {
        if type == .pdf, pdfSegment != .toImages, outputPasswordOn, outputPassword.trimmingCharacters(in: .whitespaces).isEmpty {
            return "先填打开密码"
        }
        switch type {
        case .pdf where pdfSegment == .fromImages:
            return files.contains { $0.type == .image } ? nil : "先把图片拖进来"
        case .pdf where pdfSegment == .merge:
            return files.contains { $0.type == .pdf } && !pageOrder.isEmpty ? nil : "先把 PDF 拖进来"
        default:
            return nil
        }
    }

    /// 「开始」下面写这次会做什么，只写确定的信息，不写预估体积。
    var startSummary: String {
        let n = files.count
        switch type {
        case .image:
            if imageSegment == .crop {
                let items = cropItems
                return "已确认 \(items.filter { cropConfirmed.contains($0.url) }.count) / \(items.count)"
            }
            let b = draft.image
            var parts = ["\(n) 个文件", b.format.title]
            parts.append(b.size.map { "\(b.sizeAxis.title) \($0)" } ?? "原尺寸")
            return parts.joined(separator: " · ")
        case .video:
            if videoSegment == .frames { return "生成 \(videoItems.reduce(0) { $0 + frameCount($1) }) 张" }
            let v = draft.video
            if v.muteOnly { return "\(n) 个视频 · 只去声音" }
            if v.codec == .gif { return "\(n) 个视频 · GIF" }
            return "\(n) 个视频 · \(v.shortEdge.map { "\($0)p" } ?? "原分辨率")"
        case .pdf:
            switch pdfSegment {
            case .compress: return "\(n) 个文件 · \(base.name)"
            case .merge: return "合并成 1 个 · \(pageOrder.count) 页"
            case .split: return "\(n) 个文件 · 每 \(draft.pdf.splitEvery) 页一份"
            case .toImages: return draft.pdf.longImage ? "\(n) 个文件 · 拼成长图" : "\(n) 个文件 · 每页一张"
            case .fromImages: return "\(files.filter { $0.type == .image }.count) 张图 · 做成 1 个 PDF"
            }
        }
    }
}

// MARK: - 配方条

/// 顶部配方条（设计稿 V2.1）：只放这个分段能用的配方；选中变黑，改过参数带红点；
/// 文件可以直接拖到某个配方上；「＋」新建，「管理」进设置。
struct RecipeBar: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openSettings) private var openSettings
    let ws: Workspace
    @State private var editing: Recipe?

    private var recipes: (builtIn: [Recipe], mine: [Recipe]) {
        let fits: (Recipe) -> Bool = { r in
            switch ws.type {
            case .image: return (r.image.crop != nil) == (ws.imageSegment == .crop)
            case .video: return r.video.outputFrames == (ws.videoSegment == .frames)
            case .pdf: return r.pdf.longImage == (ws.pdfSegment == .toImages)
            }
        }
        return (model.store.builtIns(for: ws.type).filter(fits), model.store.mine(for: ws.type).filter(fits))
    }

    var body: some View {
        let list = recipes
        BentoCard(padding: EdgeInsets(top: 10, leading: 20, bottom: 10, trailing: 10)) {
            HStack(spacing: 12) {
                Text("配方").font(.system(size: 13, weight: .semibold))
                ScrollView(.horizontal) {
                    HStack(spacing: 6) {
                        ForEach(list.builtIn) { pill($0) }
                        if !list.mine.isEmpty {
                            Rectangle().fill(Bento.line).frame(width: 1, height: 20).padding(.horizontal, 4)
                            ForEach(list.mine) { r in
                                pill(r).contextMenu {
                                    Button("编辑…") { editing = r }
                                    Button("导出…") { export(r) }
                                    Button("删除", role: .destructive) { model.store.delete(id: r.id) }
                                }
                            }
                        }
                    }
                    .padding(.trailing, 24)
                }
                .scrollIndicators(.never)
                .mask(LinearGradient(stops: [.init(color: .black, location: 0.9), .init(color: .clear, location: 1)], startPoint: .leading, endPoint: .trailing))
                .accessibilityElement(children: .contain)
                .accessibilityLabel("配方")
                CircleIconButton(symbol: "plus", label: "新建配方", size: 36) {
                    var r = ws.draft
                    r.id = ""
                    r.name = ""
                    r.builtIn = false
                    editing = r
                }
                CapsuleButton(title: "管理", height: 36) { openSettings() }
                    .help("在设置里排序、导入导出配方")
            }
            .frame(maxHeight: .infinity)
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
        .sheet(item: $editing) { recipe in
            RecipeEditorView(initial: recipe) { saved in ws.select(recipe: saved) }
        }
    }

    private func pill(_ r: Recipe) -> some View {
        let on = ws.recipeID == r.id
        return PillButton(title: r.name, on: on, dot: on && ws.modified) { ws.select(recipe: r) }
            .modifier(RecipeDrop(recipe: r))
            .help(r.summary)
    }

    /// 导出成 .smaller 文件，发给同事后在「新建配方 → 从文件导入」里导入。
    private func export(_ recipe: Recipe) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "\(recipe.name).smaller"
        panel.allowedContentTypes = [.init(filenameExtension: "smaller") ?? .json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try model.store.export(ids: [recipe.id], to: url)
            model.flash("已导出「\(recipe.name)」")
        } catch {
            model.flash("导出失败：\(error.localizedDescription)")
        }
    }
}

/// 把文件拖到某个配方上：加进这一批并换成这个配方（点「开始」才写出）。
struct RecipeDrop: ViewModifier {
    @Environment(AppModel.self) private var model
    let recipe: Recipe
    @State private var targeted = false

    func body(content: Content) -> some View {
        content
            .overlay {
                if targeted {
                    Capsule().strokeBorder(Color.brand, lineWidth: 2).allowsHitTesting(false)
                }
            }
            .dropDestination(for: URL.self) { urls, _ in
                model.selectedType = recipe.type
                model.add(urls, preferred: recipe.type)
                model.current.select(recipe: recipe)
                return true
            } isTargeted: { targeted = $0 }
            .animation(.easeOut(duration: 0.12), value: targeted)
    }
}

/// 官网标（随包的 SVG）。
struct BrandMark: View {
    var body: some View {
        if let url = Bundle.main.url(forResource: "logo-mark", withExtension: "svg"), let image = NSImage(contentsOf: url) {
            // 模板图：跟随文字颜色，深色模式下不会是一块黑。
            Image(nsImage: { image.isTemplate = true; return image }()).resizable().scaledToFit().foregroundStyle(.secondary)
        } else {
            Image(systemName: "circle.hexagongrid").resizable().scaledToFit().foregroundStyle(.secondary)
        }
    }
}

extension View {
    /// 便当盒里的一格：旁白当作一个分组整体读完再读下一格，顺序按优先级（大的先读）。
    func bentoRegion(_ priority: Double) -> some View {
        accessibilityElement(children: .contain).accessibilitySortPriority(priority)
    }
}
