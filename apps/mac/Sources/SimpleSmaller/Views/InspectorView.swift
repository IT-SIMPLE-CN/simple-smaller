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
                    Text(ws.base.name).font(.system(size: 13, weight: .semibold))
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
                        Text("\(ws.files.count) 个文件 · 统一设置").font(.system(size: 13, weight: .semibold))
                        recipeLine
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
            if !ws.files.isEmpty {
                if ws.imageSegment == .crop {
                    CropInspectorSections(ws: ws)
                    formatSection
                } else {
                    formatSection
                    sizeSection
                }
            }
            OutputSection(ws: ws)
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

    @ViewBuilder var formatSection: some View {
        InspectorSection(title: "格式与质量") {
            SlidingSegmented(selection: $ws.draft.image.format, options: ImageFormatChoice.allCases.map { ($0, $0.title) })
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
            InspectorToggle(label: extraLabel, isOn: Binding(get: { !ws.draft.image.extraSizes.isEmpty }, set: { on in
                ws.draft.image.extraSizes = on ? extraSizes : []
            }))
        }
    }

    private var extraSizes: [Int] {
        let base = ws.draft.image.size ?? 2560
        return [base / 2, base / 4].filter { $0 >= 64 }
    }

    private var extraLabel: String { "同时出 " + extraSizes.map(String.init).joined(separator: " / ") }
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

    var body: some View {
        InspectorSection(title: "输出") {
            InspectorRow(label: "位置") {
                Text(model.settings.outputLocation == .beside ? "原文件旁边" : (model.settings.outputFolder as NSString).lastPathComponent)
                    .foregroundStyle(.secondary)
                    .truncationMode(.middle)
            }
            InspectorRow(label: "文件名") {
                TextField("", text: $ws.draft.naming.template)
                    .textFieldStyle(.plain)
                    .font(.system(.body, design: .monospaced))
                    .multilineTextAlignment(.trailing)
            }
            if ws.type == .image {
                InspectorRow(label: "去掉位置与设备信息") { Text("始终").foregroundStyle(.secondary) }
                    .help("图片重新编码时一律不带原图的位置与设备信息")
            } else {
                InspectorToggle(label: "去掉位置与设备信息", isOn: $ws.draft.stripMetadata)
            }
        }
    }
}
