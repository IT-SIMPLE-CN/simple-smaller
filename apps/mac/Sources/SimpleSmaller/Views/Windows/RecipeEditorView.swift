import SwiftUI
import UniformTypeIdentifiers

/// 新建 / 编辑配方（设计稿 V2 · 09）：浮层，灰底分组；配方会出现在顶部配方条、访达圆环和右键菜单里。
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
            HStack(alignment: .top, spacing: 14) {
                Image(systemName: "slider.horizontal.3")
                    .font(.system(size: 18, weight: .medium))
                    .foregroundStyle(.white)
                    .frame(width: 44, height: 44)
                    .background(Circle().fill(Color.brand))
                    .accessibilityHidden(true)
                VStack(alignment: .leading, spacing: 4) {
                    Text(isNew ? "新建配方" : "编辑配方").font(.system(size: 22, weight: .bold))
                    Text("配方会出现在顶部配方条、访达圆环和右键菜单里。").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                }
            }
            .padding(.horizontal, 28)
            .padding(.top, 24)
            .padding(.bottom, 12)

            ScrollView {
                VStack(spacing: 12) {
                    group {
                        line("名称") {
                            TextField("", text: $r.name, prompt: Text("比如：官网三档 srcset"))
                                .textFieldStyle(.plain)
                                .font(.system(size: 15, weight: .semibold))
                                .padding(.horizontal, 12)
                                .frame(height: 38)
                                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Bento.card))
                        }
                        line("用于") {
                            MiniSegmented(selection: $r.type, options: MediaType.allCases.map { ($0, $0.title) }, label: "用于")
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
                .padding(.horizontal, 28)
                .padding(.vertical, 8)
            }

            HStack(spacing: 10) {
                CapsuleButton(title: "从文件导入…", height: 40) { importFile() }
                Text(importError ?? "可导出为 .smaller 发给同事")
                    .font(.system(size: 11))
                    .foregroundStyle(importError == nil ? Bento.secondary : Color.warning)
                Spacer()
                Button("取消") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                    .buttonStyle(.plain)
                    .padding(.horizontal, 20).frame(height: 44)
                    .background(Capsule().fill(Bento.fill))
                Button("存储") { save() }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.plain)
                    .font(.system(size: 13, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 26).frame(height: 44)
                    .background(Capsule().fill(Color.brand))
                    .opacity(r.name.trimmingCharacters(in: .whitespaces).isEmpty ? 0.4 : 1)
                    .disabled(r.name.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding(.horizontal, 28)
            .padding(.top, 14)
            .padding(.bottom, 22)
        }
        .frame(width: 720, height: 640)
        .background(Bento.card)
    }

    /// 灰底分组（设计稿：圆角 20 的浅灰块，左列 96 宽的小标签）。
    private func group<Content: View>(@ViewBuilder _ content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 2) { content() }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .bentoPanel()
    }

    private func line<Content: View>(_ label: String, @ViewBuilder _ content: () -> Content) -> some View {
        HStack(alignment: .center, spacing: 12) {
            Text(label).font(.system(size: 12)).foregroundStyle(Bento.secondary).frame(width: 84, alignment: .leading)
            content().frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(minHeight: 42)
    }

    /// 白底胶囊多选（尺寸档、命名选项）。
    private func chip(_ title: String, on: Binding<Bool>) -> some View {
        Button { on.wrappedValue.toggle() } label: {
            HStack(spacing: 6) {
                if on.wrappedValue { Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)) }
                Text(title)
            }
            .font(.system(size: 12, weight: on.wrappedValue ? .semibold : .regular))
            .foregroundStyle(on.wrappedValue ? Bento.onInk : Color.primary)
            .padding(.horizontal, 12)
            .frame(height: 32)
            .background(Capsule().fill(on.wrappedValue ? Bento.ink : Bento.card))
            .overlay(Capsule().strokeBorder(on.wrappedValue ? .clear : Bento.line, lineWidth: 1))
            .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
        .accessibilityAddTraits(on.wrappedValue ? .isSelected : [])
    }

    private func whitePills<V: Hashable>(_ selection: Binding<V>, _ options: [(V, String)]) -> some View {
        FlowLayout(spacing: 6) {
            ForEach(options, id: \.0) { v, t in
                let on = selection.wrappedValue == v
                Button { selection.wrappedValue = v } label: {
                    Text(t).font(.system(size: 13, weight: on ? .semibold : .regular))
                        .foregroundStyle(on ? Bento.onInk : Color.primary)
                        .padding(.horizontal, 16).frame(height: 34)
                        .background(Capsule().fill(on ? Bento.ink : Bento.card))
                        .contentShape(Capsule())
                }
                .buttonStyle(BentoPressStyle())
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
    }

    // MARK: 各类型

    @ViewBuilder private var imageSection: some View {
        group {
            line("格式") { whitePills($r.image.format, ImageFormatChoice.allCases.map { ($0, $0.title) }) }
            if !r.image.lossless, r.image.format != .png {
                line("质量") {
                    HStack(spacing: 12) {
                        BrandSlider(value: Binding(get: { Double(r.image.quality) }, set: { r.image.quality = Int($0.rounded()) }), range: 30...100)
                            .frame(height: 24)
                            .accessibilityLabel("质量")
                        Text(verbatim: "\(r.image.quality)").font(Bento.number(18)).frame(width: 32, alignment: .trailing)
                    }
                }
            }
            line("尺寸 · 按\(r.image.sizeAxis.title)") {
                // 勾几档就出几个文件（同一张图出多档，适合网站 srcset）。
                FlowLayout(spacing: 6) {
                    chip("原尺寸", on: Binding(get: { r.image.size == nil }, set: { on in
                        if on { r.image.size = nil; r.image.extraSizes = [] } else { r.image.size = 2560 }
                    }))
                    ForEach([3840, 2560, 1920, 1280, 640], id: \.self) { w in chip("\(w)", on: sizeBinding(w)) }
                    Menu {
                        Picker("按", selection: $r.image.sizeAxis) {
                            ForEach(SizeAxis.allCases) { Text(verbatim: "按\($0.title)").tag($0) }
                        }
                        .pickerStyle(.inline)
                    } label: { Text(verbatim: "按\(r.image.sizeAxis.title)") }
                        .menuStyle(.borderlessButton)
                        .fixedSize()
                }
            }
            line("比例") {
                Picker("比例", selection: Binding(get: { r.image.crop?.id ?? "" }, set: { id in
                    r.image.crop = CropPreset.imagePresets.first { $0.id == id }
                })) {
                    Text("原比例").tag("")
                    Divider()
                    ForEach(CropPreset.imagePresets.filter { $0.aspect != nil }) { Text(verbatim: "\($0.name) \($0.ratioLabel)").tag($0.id) }
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
        group {
            line("编码") {
                Picker("编码", selection: $r.video.codec) {
                    Text("H.264 · MP4").tag(VideoBody.Codec.h264)
                    Text("HEVC · MP4").tag(VideoBody.Codec.hevc)
                    Text("GIF").tag(VideoBody.Codec.gif)
                }
                .labelsHidden().fixedSize()
            }
            if r.video.codec != .gif {
                line("分辨率") {
                    Picker("分辨率", selection: $r.video.shortEdge) {
                        Text("1080p").tag(Int?.some(1080))
                        Text("720p").tag(Int?.some(720))
                        Text("540p").tag(Int?.some(540))
                        Text("原分辨率").tag(Int?.none)
                    }
                    .labelsHidden().fixedSize()
                }
                line("大小上限") {
                    HStack(spacing: 6) {
                        Toggle("", isOn: Binding(get: { r.video.rate == .size }, set: { r.video.rate = $0 ? .size : (initial.video.rate == .size ? .medium : initial.video.rate) }))
                            .labelsHidden().toggleStyle(.checkbox)
                        TextField("", value: $r.video.targetMB, format: .number.grouping(.never))
                            .frame(width: 52).multilineTextAlignment(.trailing)
                            .disabled(r.video.rate != .size)
                        Text("MB（按时长倒推码率）").foregroundStyle(Bento.secondary)
                    }
                }
                line("声音") { chip("保留声音", on: $r.video.keepAudio) }
            }
        }
    }

    @ViewBuilder private var pdfSection: some View {
        group {
            line("图片分辨率") {
                Picker("图片分辨率", selection: $r.pdf.level) {
                    Text("110 dpi · 屏幕阅读").tag(PdfBody.Level.screen)
                    Text("150 dpi · 邮件附件").tag(PdfBody.Level.email)
                    Text("300 dpi · 打印保真").tag(PdfBody.Level.print)
                    Text("指定大小").tag(PdfBody.Level.size)
                }
                .labelsHidden().fixedSize()
            }
            if r.pdf.level == .size {
                line("大小上限") {
                    HStack(spacing: 6) {
                        TextField("", value: $r.pdf.targetMB, format: .number.grouping(.never)).frame(width: 52).multilineTextAlignment(.trailing)
                        Text("MB").foregroundStyle(Bento.secondary)
                    }
                }
            } else if r.pdf.level == .email {
                line("大小上限") { Text("10 MB").foregroundStyle(Bento.secondary) }
            }
            line("") { chip("去掉作者与修改记录", on: $r.stripMetadata) }
        }
    }

    // MARK: 命名

    @ViewBuilder private var namingSection: some View {
        group {
            line("文件名") {
                TextField("", text: $r.naming.template)
                    .textFieldStyle(.plain)
                    .font(.system(size: 12, design: .monospaced))
                    .padding(.horizontal, 12)
                    .frame(height: 34)
                    .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Bento.card))
                    .accessibilityLabel("文件名规则")
            }
            line("") { HStack(spacing: 4) {
                ForEach(["{name}", "{w}", "{h}", "{ratio}", "{date}", "{n}"], id: \.self) { token in
                    Button(token) { r.naming.template += token }
                        .buttonStyle(.bordered)
                        .controlSize(.small)
                        .font(.system(size: 11, design: .monospaced))
                        .help(tokenHelp(token))
                }
            } }
            line("示例") {
                Text(verbatim: example).font(.system(.body, design: .monospaced)).foregroundStyle(Bento.secondary)
                    .lineLimit(1).truncationMode(.middle)
            }
            line("") {
                HStack(spacing: 6) {
                    chip("小写", on: $r.naming.lowercase)
                    chip("中文转拼音", on: $r.naming.pinyin)
                    chip("空格换连字符", on: $r.naming.hyphenate)
                }
            }
            if r.type != .pdf {
                line("") { chip("去掉位置与设备信息", on: $r.stripMetadata) }
            }
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
