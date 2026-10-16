import SwiftUI

/// 右侧检查器：当前配方的参数。改动对这一批全部生效。
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
        .formStyle(.grouped)
        .disabled(ws.running)
        .onChange(of: ws.draft) { _, _ in model.schedulePreviews(ws.type) }
    }
}

/// 检查器顶部：这一批有多少文件。
struct BatchHeader: View {
    let ws: Workspace

    var body: some View {
        if !ws.files.isEmpty {
            HStack(spacing: 10) {
                // 一张缩略图，后面衬两层淡灰卡片表示「一叠」。
                ZStack {
                    if ws.files.count > 1 {
                        RoundedRectangle(cornerRadius: 5, style: .continuous).fill(.quaternary).frame(width: 30, height: 30).offset(y: -7)
                        RoundedRectangle(cornerRadius: 5, style: .continuous).fill(.tertiary).frame(width: 34, height: 34).offset(y: -3.5)
                    }
                    Thumb(image: ws.files.first?.thumbnail)
                        .frame(width: 38, height: 38)
                        .clipShape(RoundedRectangle(cornerRadius: 5, style: .continuous))
                        .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(.separator, lineWidth: 0.5))
                }
                .frame(width: 44, height: 46, alignment: .bottom)
                .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(ws.files.count) 个文件 · 统一设置").fontWeight(.semibold)
                    Text("改动对这一批全部生效").font(.caption).foregroundStyle(.secondary)
                }
            }
        }
    }
}

/// 配方一行：名称；改过后可存为新配方。
struct RecipeSection: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace
    @State private var saving: Recipe?

    var body: some View {
        Section("配方") {
            if ws.files.isEmpty {
                Picker("配方", selection: Binding(get: { ws.recipeID }, set: { id in
                    if let r = model.store.recipe(id: id) { ws.select(recipe: r) }
                })) {
                    ForEach(model.store.all.filter { $0.type == ws.type }) { Text($0.name).tag($0.id) }
                }
            } else {
                HStack {
                    Text(ws.base.name)
                    Spacer()
                    if ws.modified {
                        Button("已修改 · 存为新配方") {
                            var r = ws.draft
                            r.id = ""
                            r.name = ws.base.builtIn ? "\(ws.base.name) 改" : ws.base.name
                            saving = r
                        }
                        .buttonStyle(.link)
                        .foregroundStyle(Color.brand)
                    }
                }
            }
        }
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }
}

struct ImageInspector: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        Form {
            if !ws.files.isEmpty { Section { BatchHeader(ws: ws) } }
            RecipeSection(ws: ws)
            if ws.files.isEmpty {
                Section { Text(description).font(.caption).foregroundStyle(.secondary) }
            } else if ws.imageSegment == .crop {
                CropInspectorSections(ws: ws)
                formatSection
            } else {
                formatSection
                sizeSection
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
        Section("格式与质量") {
            SlidingSegmented(selection: $ws.draft.image.format, options: ImageFormatChoice.allCases.map { ($0, $0.title) })
                .accessibilityLabel("格式")
            if ws.draft.image.lossless {
                LabeledContent("质量", value: "无损")
                Toggle("无损", isOn: $ws.draft.image.lossless)
            } else {
                QualitySlider(value: $ws.draft.image.quality)
            }
            if ws.draft.image.targetKB != nil {
                LabeledContent("限体积") {
                    HStack(spacing: 4) {
                        TextField("", value: Binding(get: { ws.draft.image.targetKB ?? 500 }, set: { ws.draft.image.targetKB = max(10, $0) }), format: .number)
                            .multilineTextAlignment(.trailing).frame(width: 64)
                        Text("KB").foregroundStyle(.secondary)
                    }
                }
            }
        }
    }

    @ViewBuilder var sizeSection: some View {
        Section("尺寸") {
            // 一整行水平排布、垂直居中：左边「宽 / 高 / 长边」弹出菜单，右边右对齐的数字。
            HStack(alignment: .center, spacing: 3) {
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
                        .font(.body.monospacedDigit())
                        .frame(width: 60)
                        .accessibilityLabel(ws.draft.image.sizeAxis.title)
                    Text("px").foregroundStyle(.secondary)
                }
            }
            Button {
                ws.imageSegment = .crop
            } label: {
                LabeledContent("裁切") {
                    Text(ws.draft.image.crop.map { "\($0.name) \($0.ratioLabel) · 去设置 ›" } ?? "不裁切 · 去设置 ›")
                        .foregroundStyle(.secondary)
                }
            }
            .buttonStyle(.plain)
            Toggle(extraLabel, isOn: Binding(get: { !ws.draft.image.extraSizes.isEmpty }, set: { on in
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
        Section("输出") {
            if ws.files.isEmpty {
                LabeledContent("位置", value: model.settings.outputLocation == .beside ? "原文件旁边" : (model.settings.outputFolder as NSString).lastPathComponent)
            }
            LabeledContent("文件名") {
                TextField("", text: $ws.draft.naming.template)
                    .font(.system(.body, design: .monospaced))
                    .multilineTextAlignment(.trailing)
            }
            if ws.type == .image {
                LabeledContent("去掉位置与设备信息") { Text("始终").foregroundStyle(.secondary) }
                    .help("图片重新编码时一律不带原图的位置与设备信息")
            } else {
                Toggle("去掉位置与设备信息", isOn: $ws.draft.stripMetadata)
            }
        }
    }
}
