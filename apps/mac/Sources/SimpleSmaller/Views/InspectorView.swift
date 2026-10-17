import SwiftUI

/// 右侧检查器：当前配方的参数。改动对这一批全部生效。
/// 版式参照 Xcode / Pages 的检查器：平铺分节，小号节标题，节与节之间一条细线，内容单独上下滚。
struct InspectorView: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        Group {
            switch ws.type {
            case .image: ImageInspector(ws: ws)
            case .video: VideoInspector(ws: ws)
            case .pdf: PdfInspector(ws: ws)
            }
        }
        .disabled(ws.running)
    }
}

enum InspectorMetrics {
    /// 左右边距，与底部「开始」按钮一致。
    static let margin: CGFloat = 16
}

/// 检查器滚动容器。
struct InspectorScroll<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 0) { content }
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}

/// 一节：节标题 + 若干行，顶上一条贯通的细线。
struct InspectorSection<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title)
                .font(.system(size: 11, weight: .semibold))
                .foregroundStyle(.secondary)
                .accessibilityAddTraits(.isHeader)
            content
        }
        .padding(.horizontal, InspectorMetrics.margin)
        .padding(.top, 12)
        .padding(.bottom, 16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .overlay(alignment: .top) { Divider() }
    }
}

/// 一行：左边名称，右边值或控件，垂直居中。
struct InspectorRow<Trailing: View>: View {
    let label: String
    @ViewBuilder var trailing: Trailing

    var body: some View {
        HStack(spacing: 8) {
            Text(label).lineLimit(1)
            Spacer(minLength: 8)
            trailing
        }
        .frame(minHeight: 22)
    }
}

/// 开关行：右边一个小号开关。
struct InspectorToggle: View {
    let label: String
    @Binding var isOn: Bool

    var body: some View {
        InspectorRow(label: label) {
            Toggle(label, isOn: $isOn).labelsHidden().toggleStyle(.switch).controlSize(.mini)
        }
    }
}

/// 检查器顶部：这一批多少文件、用的哪个配方（改过可存为新配方）、往里再加文件。
/// 还没有文件时写当前配方的一句说明。
struct InspectorHeader: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    let description: String
    @State private var saving: Recipe?

    var body: some View {
        Group {
            if ws.files.isEmpty {
                VStack(alignment: .leading, spacing: 4) {
                    // 合并、拆分、图片转 PDF 用不上配方：标题写分段名。
                    Text(ws.usesRecipe ? ws.base.name : ws.pdfSegment.rawValue).font(.system(size: 13, weight: .semibold))
                    Text(description).font(.caption).foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            } else {
                HStack(spacing: 10) {
                    Thumb(image: ws.files.first?.thumbnail)
                        .frame(width: 32, height: 32)
                        .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(.separator, lineWidth: 0.5))
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        if showsRecipe {
                            Text(ws.files.count > 1 ? "\(ws.files.count) 个文件 · 统一设置" : "1 个文件").font(.system(size: 13, weight: .semibold))
                            recipeLine
                        } else {
                            Text("\(ws.files.count) 个文件").font(.system(size: 13, weight: .semibold))
                        }
                    }
                    Spacer(minLength: 8)
                    AddFilesButton(ws: ws)
                }
            }
        }
        .padding(.horizontal, InspectorMetrics.margin)
        .padding(.vertical, 14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    private var showsRecipe: Bool { ws.usesRecipe }

    private var recipeLine: some View {
        HStack(spacing: 4) {
            Text(ws.base.name)
            if ws.modified {
                Text("· 已修改")
                Button("存为新配方") {
                    var r = ws.draft
                    r.id = ""
                    r.name = ws.base.builtIn ? "\(ws.base.name) 改" : ws.base.name
                    saving = r
                }
                .buttonStyle(.link)
                .foregroundStyle(Color.brand)
            }
        }
        .font(.caption)
        .foregroundStyle(.secondary)
        .lineLimit(1)
    }
}

struct AddFilesButton: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    @State private var hovering = false

    var body: some View {
        Button { FilePicker.pick(into: model) } label: {
            Image(systemName: "plus")
                .font(.system(size: 12, weight: .semibold))
                .foregroundStyle(.secondary)
                .frame(width: 26, height: 26)
                .background(Circle().fill(Color.primary.opacity(hovering ? 0.12 : 0.07)))
                .contentShape(Circle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.15), value: hovering)
        .help("添加文件或文件夹（⌘O）")
        .accessibilityLabel("添加文件")
    }
}

struct ImageInspector: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        InspectorScroll {
            InspectorHeader(ws: ws, description: description)
            if !ws.files.isEmpty, ws.imageSegment == .crop {
                // 裁切（设计稿 03）：比例、输出尺寸、框放在哪、格式与质量（与「压缩」共用）。
                CropInspectorSections(ws: ws)
                formatSection(formats: [.webp, .avif, .jpeg, .png] + (ws.draft.image.format == .original ? [.original] : []),
                              note: nil)
                Text("每张默认居中、按比例取最大，不调也能直接开始。要调的拖框移动、拖四角缩放；↩ 确认并到下一张，←/→ 切换，⌘Z 撤回这一张。")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, InspectorMetrics.margin)
                    .padding(.vertical, 14)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .overlay(alignment: .top) { Divider() }
            } else {
                if !ws.files.isEmpty {
                    formatSection(formats: ImageFormatChoice.allCases, note: nil)
                    sizeSection
                }
                OutputSection(ws: ws)
            }
        }
    }

    private var description: String {
        let b = ws.draft.image
        var parts = [b.format.title]
        if let c = b.crop { parts.append("\(c.name) \(c.ratioLabel)") }
        else if let s = b.size { parts.append("\(b.sizeAxis.title) \(s)") }
        else { parts.append("原尺寸") }
        if let t = b.targetKB { parts.append("不超过 \(t) KB") }
        else if b.lossless { parts.append("无损") }
        else { parts.append("质量\(QualityWord.of(b.quality))") }
        var text = parts.joined(separator: "，") + "。"
        if b.format == .webp && !b.lossless { text += "照片有损、截图无损、透明图保留透明。" }
        if b.format == .jpeg { text += "透明部分铺白底。" }
        return text
    }

    @ViewBuilder func formatSection(formats: [ImageFormatChoice], note: String?) -> some View {
        InspectorSection(title: "格式与质量") {
            SlidingSegmented(selection: $ws.draft.image.format, options: formats.map { ($0, $0.title) })
                .accessibilityLabel("格式")
            if ws.draft.image.lossless {
                InspectorRow(label: "质量") { Text("无损").foregroundStyle(.secondary) }
                InspectorToggle(label: "无损", isOn: $ws.draft.image.lossless)
            } else {
                QualitySlider(value: $ws.draft.image.quality)
            }
            if ws.draft.image.targetKB != nil {
                InspectorRow(label: "限体积") {
                    TextField("", value: Binding(get: { ws.draft.image.targetKB ?? 500 }, set: { ws.draft.image.targetKB = max(10, $0) }), format: .number.grouping(.never))
                        .textFieldStyle(.plain)
                        .multilineTextAlignment(.trailing)
                        .monospacedDigit()
                        .frame(width: 60)
                    Text("KB").foregroundStyle(.secondary)
                }
            }
            if let note {
                Text(note).font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder var sizeSection: some View {
        InspectorSection(title: "尺寸") {
            // 左边「宽 / 高 / 长边」弹出菜单，右边右对齐的数字。
            HStack(spacing: 3) {
                Picker("按", selection: Binding(get: { ws.draft.image.size == nil ? nil : ws.draft.image.sizeAxis }, set: { v in
                    if let v { ws.draft.image.sizeAxis = v; if ws.draft.image.size == nil { ws.draft.image.size = 2560 } } else { ws.draft.image.size = nil }
                })) {
                    ForEach(SizeAxis.allCases) { Text($0.title).tag(Optional($0)) }
                    Divider()
                    Text("原尺寸").tag(SizeAxis?.none)
                }
                .pickerStyle(.menu)
                .buttonStyle(.borderless)
                .labelsHidden()
                .fixedSize()
                .padding(.leading, -2)
                .help("按宽、高或长边")
                Spacer()
                if ws.draft.image.size != nil {
                    TextField("", value: Binding(get: { ws.draft.image.size ?? 2560 }, set: { ws.draft.image.size = max(16, $0) }), format: .number.grouping(.never))
                        .textFieldStyle(.plain)
                        .multilineTextAlignment(.trailing)
                        .monospacedDigit()
                        .frame(width: 60)
                        .accessibilityLabel(ws.draft.image.sizeAxis.title)
                    Text("px").foregroundStyle(.secondary)
                }
            }
            .frame(minHeight: 22)
            Button {
                ws.imageSegment = .crop
            } label: {
                InspectorRow(label: "裁切") {
                    Text(ws.draft.image.crop.map { "\($0.name) \($0.ratioLabel) · 去设置 ›" } ?? "不裁切 · 去设置 ›").foregroundStyle(.secondary)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("到「裁切」里设置比例与每张的框")
            .contextMenu {
                if ws.draft.image.crop != nil {
                    Button("不裁切") { ws.draft.image.crop = nil; ws.draft.image.cropWidth = nil; ws.resetCrop() }
                }
            }
            // 多档尺寸（网站 srcset）在配方里设；用了这类配方时只写明会多出哪几档。
            if !ws.draft.image.extraSizes.isEmpty {
                InspectorRow(label: "另出") {
                    Text(verbatim: ws.draft.image.extraSizes.map { "\($0)" }.joined(separator: " / ") + " px").foregroundStyle(.secondary).monospacedDigit()
                }
                .help("这个配方每张图还会多出这几档小图，给网站按屏幕大小加载用；在配方里改")
            }
        }
    }
}

enum QualityWord {
    static func of(_ q: Int) -> String { q >= 88 ? "高" : q >= 70 ? "均衡" : "偏小" }
}

/// 质量滑杆：两端写「更小 / 更清晰」。
struct QualitySlider: View {
    @Binding var value: Int

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack {
                Text("质量")
                Spacer()
                Text("\(value)").monospacedDigit().foregroundStyle(.secondary)
            }
            Slider(value: Binding(get: { Double(value) }, set: { value = Int($0.rounded()) }), in: 30...100)
                .labelsHidden()
                .controlSize(.small)
                .accessibilityLabel("质量")
                .accessibilityValue("\(value)")
            HStack {
                Text("更小")
                Spacer()
                Text("更清晰")
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
    }
}

/// 输出：文件名规则与去掉位置信息（三类共用）。
struct OutputSection: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    private var fixedName: String? {
        guard ws.type == .pdf else { return nil }
        switch ws.pdfSegment {
        case .split: return "{name}-拆分/{name}-01"
        case .toImages: return ws.draft.pdf.longImage ? "{name}-长图" : "{name}-图片/{name}-01"
        default: return nil
        }
    }

    var body: some View {
        InspectorSection(title: "输出") {
            InspectorRow(label: "位置") {
                Text(model.settings.outputLocation == .beside ? "原文件旁边" : (model.settings.outputFolder as NSString).lastPathComponent)
                    .foregroundStyle(.secondary)
                    .truncationMode(.middle)
            }
            if let fixed = fixedName {
                // 拆分、转图片按固定规则命名（文件夹 + 编号），不用配方里的模板。
                InspectorRow(label: "文件名") {
                    Text(verbatim: fixed).font(.system(.body, design: .monospaced)).foregroundStyle(.secondary)
                        .lineLimit(1).truncationMode(.head)
                }
            } else {
                InspectorRow(label: "文件名") {
                    TextField("", text: $ws.draft.naming.template)
                        .textFieldStyle(.plain)
                        .font(.system(.body, design: .monospaced))
                        .multilineTextAlignment(.trailing)
                }
            }
        }
    }
}
