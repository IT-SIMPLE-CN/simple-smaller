import SwiftUI
import UniformTypeIdentifiers

/// 新建 / 编辑配方（设计稿 08）。配方会出现在边栏和访达圆环的「自定义」里。
struct RecipeEditorView: View {
    let initial: Recipe
    let onSave: (Recipe) -> Void
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var r: Recipe
    @State private var importError: String?

    init(initial: Recipe, onSave: @escaping (Recipe) -> Void) {
        self.initial = initial
        self.onSave = onSave
        _r = State(initialValue: initial)
    }

    private var isNew: Bool { initial.id.isEmpty || initial.builtIn }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 4) {
                Text(isNew ? "新建配方" : "编辑配方").font(.system(size: 15, weight: .semibold))
                Text("配方会出现在边栏和访达圆环的「自定义」里。").font(.system(size: 12)).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 20)
            .padding(.top, 18)
            .padding(.bottom, 6)

            Form {
                Section {
                    TextField("名称", text: $r.name, prompt: Text("比如：官网三档 srcset"))
                    LabeledContent("用于") {
                        SlidingSegmented(selection: $r.type, options: MediaType.allCases.map { ($0, $0.title) }, size: .small)
                            .frame(width: 180)
                            .disabled(!isNew)
                    }
                }
                switch r.type {
                case .image: imageSection
                case .video: videoSection
                case .pdf: pdfSection
                }
                namingSection
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)

            Divider()
            HStack(spacing: 10) {
                Button("从文件导入…") { importFile() }
                Text(importError ?? "可导出为 .smaller 发给同事")
                    .font(.system(size: 11))
                    .foregroundStyle(importError == nil ? AnyShapeStyle(.tertiary) : AnyShapeStyle(Color.warning))
                Spacer()
                Button("取消") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("存储") { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(r.name.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding(.horizontal, 20)
            .padding(.vertical, 14)
        }
        .frame(width: 520, height: 600)
    }

    // MARK: 各类型

    @ViewBuilder private var imageSection: some View {
        Section {
            LabeledContent("格式") {
                Picker("格式", selection: $r.image.format) {
                    ForEach(ImageFormatChoice.allCases) { Text($0.title).tag($0) }
                }
                .labelsHidden().fixedSize()
            }
            if !r.image.lossless, r.image.format != .png {
                QualitySlider(value: $r.image.quality)
            }
            LabeledContent("尺寸") {
                Picker("按", selection: $r.image.sizeAxis) {
                    ForEach(SizeAxis.allCases) { Text($0.title).tag($0) }
                }
                .labelsHidden().fixedSize()
            }
            // 勾几档就出几个文件（同一张图出多档，适合网站 srcset）。
            HStack(spacing: 12) {
                Toggle("原尺寸", isOn: Binding(get: { r.image.size == nil }, set: { on in
                    if on { r.image.size = nil; r.image.extraSizes = [] } else { r.image.size = 2560 }
                }))
                ForEach([3840, 2560, 1920, 1280, 640], id: \.self) { w in
                    Toggle(isOn: sizeBinding(w)) { Text(verbatim: "\(w)") }
                }
            }
            .toggleStyle(.checkbox)
            LabeledContent("比例") {
                Picker("比例", selection: Binding(get: { r.image.crop?.id ?? "" }, set: { id in
                    r.image.crop = CropPreset.imagePresets.first { $0.id == id }
                })) {
                    Text("原比例").tag("")
                    Divider()
                    ForEach(CropPreset.imagePresets.filter { $0.aspect != nil }) { Text("\($0.name) \($0.ratioLabel)").tag($0.id) }
                }
                .labelsHidden().fixedSize()
            }
        }
    }

    private var chosenSizes: [Int] { ([r.image.size].compactMap { $0 } + r.image.extraSizes).sorted(by: >) }

    private func sizeBinding(_ w: Int) -> Binding<Bool> {
        Binding(get: { chosenSizes.contains(w) }, set: { on in
            var set = Set(chosenSizes)
            if on { set.insert(w) } else { set.remove(w) }
            let list = set.sorted(by: >)
            r.image.size = list.first
            r.image.extraSizes = Array(list.dropFirst())
        })
    }

    @ViewBuilder private var videoSection: some View {
        Section {
            LabeledContent("编码") {
                Picker("编码", selection: $r.video.codec) {
                    Text("H.264 · MP4").tag(VideoBody.Codec.h264)
                    Text("HEVC · MP4").tag(VideoBody.Codec.hevc)
                    Text("GIF").tag(VideoBody.Codec.gif)
                }
                .labelsHidden().fixedSize()
            }
            if r.video.codec != .gif {
                LabeledContent("分辨率") {
                    Picker("分辨率", selection: $r.video.shortEdge) {
                        Text("1080p").tag(Int?.some(1080))
                        Text("720p").tag(Int?.some(720))
                        Text("540p").tag(Int?.some(540))
                        Text("原分辨率").tag(Int?.none)
                    }
                    .labelsHidden().fixedSize()
                }
                LabeledContent("大小上限") {
                    HStack(spacing: 6) {
                        Toggle("", isOn: Binding(get: { r.video.rate == .size }, set: { r.video.rate = $0 ? .size : .medium }))
                            .labelsHidden().toggleStyle(.checkbox)
                        TextField("", value: $r.video.targetMB, format: .number.grouping(.never))
                            .frame(width: 52).multilineTextAlignment(.trailing)
                            .disabled(r.video.rate != .size)
                        Text("MB（按时长倒推码率）").foregroundStyle(.secondary)
                    }
                }
                Toggle("保留声音", isOn: $r.video.keepAudio)
            }
        }
    }

    @ViewBuilder private var pdfSection: some View {
        Section {
            LabeledContent("图片分辨率") {
                Picker("图片分辨率", selection: $r.pdf.level) {
                    Text("110 dpi · 屏幕阅读").tag(PdfBody.Level.screen)
                    Text("150 dpi · 邮件附件").tag(PdfBody.Level.email)
                    Text("300 dpi · 打印保真").tag(PdfBody.Level.print)
                    Text("指定大小").tag(PdfBody.Level.size)
                }
                .labelsHidden().fixedSize()
            }
            if r.pdf.level == .size {
                LabeledContent("大小上限") {
                    HStack(spacing: 6) {
                        TextField("", value: $r.pdf.targetMB, format: .number.grouping(.never)).frame(width: 52).multilineTextAlignment(.trailing)
                        Text("MB").foregroundStyle(.secondary)
                    }
                }
            } else if r.pdf.level == .email {
                LabeledContent("大小上限", value: "10 MB")
            }
            Toggle("去掉作者与修改记录", isOn: $r.stripMetadata)
        }
    }

    // MARK: 命名

    @ViewBuilder private var namingSection: some View {
        Section {
            LabeledContent("文件名") {
                TextField("", text: $r.naming.template)
                    .font(.system(.body, design: .monospaced))
                    .multilineTextAlignment(.trailing)
                    .accessibilityLabel("文件名规则")
            }
            HStack(spacing: 6) {
                ForEach(["{name}", "{w}", "{h}", "{ratio}", "{date}", "{n}"], id: \.self) { token in
                    Button(token) { r.naming.template += token }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                        .font(.system(size: 11, design: .monospaced))
                        .help(tokenHelp(token))
                }
            }
            LabeledContent("示例") {
                Text(verbatim: example).font(.system(.body, design: .monospaced)).foregroundStyle(.secondary)
                    .lineLimit(1).truncationMode(.middle)
            }
            HStack(spacing: 16) {
                Toggle("小写", isOn: $r.naming.lowercase)
                Toggle("中文转拼音", isOn: $r.naming.pinyin)
                Toggle("空格换连字符", isOn: $r.naming.hyphenate)
            }
            .toggleStyle(.checkbox)
        }
    }

    private func tokenHelp(_ t: String) -> String {
        switch t {
        case "{name}": "原文件名"
        case "{w}": "输出宽"
        case "{h}": "输出高"
        case "{ratio}": "比例，如 3x4"
        case "{date}": "日期，如 20261016"
        default: "批次内序号"
        }
    }

    /// 用引擎同一套规则算示例（设计稿：新品 发布.jpg → xin-pin-fa-bu-2560.webp）。
    private var example: String {
        let (w, h, ext): (UInt32, UInt32, String) = switch r.type {
        case .image:
            (UInt32(r.image.size ?? 4000), UInt32(Double(r.image.size ?? 4000) * 0.5625),
             r.image.format == .original ? "jpg" : r.image.format == .jpeg ? "jpg" : r.image.format.rawValue)
        case .video: (UInt32(r.video.shortEdge.map { $0 * 16 / 9 } ?? 1920), UInt32(r.video.shortEdge ?? 1080), r.video.codec == .gif ? "gif" : "mp4")
        case .pdf: (0, 0, "pdf")
        }
        let source = r.type == .image ? "新品 发布.jpg" : r.type == .video ? "新品 发布.mov" : "新品 发布.pdf"
        return model.engine.previewName(naming: r.naming.ffi(date: Format.today), sourceName: source, width: w, height: h) + ".\(ext)"
    }

    private func importFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "smaller") ?? .json, .json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let n = try model.store.importRecipes(from: url)
            importError = nil
            model.flash("导入了 \(n) 个配方")
            dismiss()
        } catch {
            importError = "这个文件读不出配方"
        }
    }

    private func save() {
        var out = r
        out.name = out.name.trimmingCharacters(in: .whitespaces)
        if isNew { out.id = "" }
        let saved = model.store.saveMine(out)
        onSave(saved)
        dismiss()
    }
}
