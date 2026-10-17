import SwiftUI

/// PDF 检查器（设计稿 05）：按分段换内容，输出一节共用。
struct PdfInspector: View {
    @Bindable var ws: Workspace

    private var p: PdfBody { ws.draft.pdf }

    var body: some View {
        InspectorScroll {
            InspectorHeader(ws: ws, description: description)
            if !ws.files.isEmpty {
                switch ws.pdfSegment {
                case .compress: compressSections
                case .merge: mergeSection
                case .split: splitSection
                case .toImages: toImagesSection
                case .fromImages: fromImagesSection
                }
            }
            OutputSection(ws: ws)
        }
    }

    private var description: String {
        switch ws.pdfSegment {
        case .compress:
            switch p.level {
            case .screen: "图片降到 110 dpi，适合屏幕上看，文字保持矢量。"
            case .email: "图片降到 150 dpi，尽量压到 10 MB 内，适合发邮件，文字保持矢量。"
            case .print: "图片保留 300 dpi，只优化文件结构，适合打印。"
            case .size: "逐档降图片质量，直到不超过指定大小。"
            }
        case .merge: "把几个 PDF 按页面顺序合成一个文件，可排序、删页。"
        case .split: "把一个 PDF 按每 N 页拆成多个文件。"
        case .toImages: p.longImage ? "所有页面从上到下拼成一张长图。" : "每页存成一张图片。"
        case .fromImages: "把图片按顺序做成一个 PDF，一张一页。"
        }
    }

    // MARK: 压缩

    @ViewBuilder private var compressSections: some View {
        InspectorSection(title: "压缩") {
            VStack(spacing: 0) {
                LevelRow(title: "屏幕阅读", subtitle: "图片 110 dpi", selected: p.level == .screen) { ws.draft.pdf.level = .screen }
                LevelRow(title: "邮件附件", subtitle: "图片 150 dpi，目标 10 MB 内", selected: p.level == .email) { ws.draft.pdf.level = .email }
                LevelRow(title: "打印保真", subtitle: "图片 300 dpi，只优化结构", selected: p.level == .print) { ws.draft.pdf.level = .print }
                LevelRow(title: "指定大小", subtitle: "逐档降图片质量", selected: p.level == .size) { ws.draft.pdf.level = .size }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("压缩档")
            if p.level == .size {
                InspectorRow(label: "不超过") {
                    TextField("", value: Binding(get: { p.targetMB }, set: { ws.draft.pdf.targetMB = min(2000, max(0.5, $0)) }),
                              format: .number.precision(.fractionLength(0...1)).grouping(.never))
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.small)
                        .multilineTextAlignment(.trailing)
                        .frame(width: 58)
                    Text("MB").foregroundStyle(.secondary)
                }
            }
        }
        InspectorSection(title: "选项") {
            InspectorToggle(label: "去掉作者与修改记录", isOn: $ws.draft.stripMetadata)
            InspectorToggle(label: "转灰度", isOn: $ws.draft.pdf.grayscale)
        }
        PasswordOption(ws: ws)
    }

    // MARK: 合并 / 拆分

    @ViewBuilder private var mergeSection: some View {
        InspectorSection(title: "合并") {
            InspectorToggle(label: "去掉作者与修改记录", isOn: $ws.draft.stripMetadata)
        }
        PasswordOption(ws: ws)
    }

    @ViewBuilder private var splitSection: some View {
        InspectorSection(title: "拆分") {
            InspectorRow(label: "每") {
                Stepper(value: $ws.draft.pdf.splitEvery, in: 1...500) {
                    Text(verbatim: "\(p.splitEvery) 页").monospacedDigit()
                }
                Text("一个文件").foregroundStyle(.secondary)
            }
            Text(verbatim: splitSummary)
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        PasswordOption(ws: ws)
    }

    private var splitSummary: String {
        let parts = ws.files.filter { $0.type == .pdf }.reduce(0) { $0 + Int((Double($1.pages) / Double(max(1, p.splitEvery))).rounded(.up)) }
        return "共拆出 \(parts) 个文件"
    }

    // MARK: 转图片 / 图片转 PDF

    @ViewBuilder private var toImagesSection: some View {
        InspectorSection(title: "转图片") {
            SlidingSegmented(selection: $ws.draft.pdf.longImage, options: [(false, "每页一张"), (true, "拼成长图")])
                .accessibilityLabel("方式")
            InspectorRow(label: "格式") {
                Picker("格式", selection: $ws.draft.pdf.imageFormat) {
                    ForEach([ImageFormatChoice.jpeg, .png, .webp]) { Text($0.title).tag($0) }
                }
                .labelsHidden().fixedSize()
            }
            InspectorRow(label: "宽") {
                Picker("宽", selection: $ws.draft.pdf.imageWidth) {
                    ForEach([750, 1080, 1242, 1600, 2400], id: \.self) { Text(verbatim: "\($0) px").tag($0) }
                }
                .labelsHidden().fixedSize()
            }
            if p.imageFormat != .png {
                QualitySlider(value: $ws.draft.pdf.imageQuality)
            }
            if p.longImage {
                Text("长图适合发朋友圈、小红书；页数很多时会按高度上限分成几张。")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    @ViewBuilder private var fromImagesSection: some View {
        InspectorSection(title: "页面") {
            SlidingSegmented(selection: $ws.draft.pdf.pageMode, options: [(.a4, "A4 纸"), (.fit, "按图片大小")])
                .accessibilityLabel("页面大小")
            Text(p.pageMode == .a4 ? "每张图等比放进 A4 页面，四周留白，适合打印。" : "每页和图片一样大，没有白边，适合屏幕看。")
                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        PasswordOption(ws: ws)
    }
}

/// 压缩档一行：圆点 + 名称 + 说明（设计稿 05，不写预估体积：点「开始」才压）。
private struct LevelRow: View {
    let title: String
    let subtitle: String
    let selected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 10) {
                ZStack {
                    Circle().strokeBorder(selected ? Color.brand : Color(nsColor: .separatorColor), lineWidth: selected ? 0 : 1)
                    if selected {
                        Circle().fill(Color.brand)
                        Circle().fill(.white).frame(width: 6, height: 6)
                    }
                }
                .frame(width: 16, height: 16)
                VStack(alignment: .leading, spacing: 1) {
                    Text(title)
                    Text(subtitle).font(.system(size: 11)).foregroundStyle(.secondary)
                }
                Spacer()
            }
            .frame(minHeight: 40)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .animation(.smooth(duration: 0.2), value: selected)
        .accessibilityLabel("\(title)，\(subtitle)")
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
    }
}

/// 输出加打开密码：发给客户的报价、合同常用。密码不写进配方、不保存。
private struct PasswordOption: View {
    @Bindable var ws: Workspace
    @FocusState private var focused: Bool

    var body: some View {
        InspectorSection(title: "打开密码") {
            InspectorToggle(label: "输出加打开密码", isOn: Binding(get: { ws.outputPasswordOn }, set: { v in
                ws.outputPasswordOn = v
                if v { DispatchQueue.main.async { focused = true } } else { ws.outputPassword = "" }
            }))
            if ws.outputPasswordOn {
                SecureField("设置密码", text: Binding(get: { ws.outputPassword }, set: { ws.outputPassword = String($0.utf8.count > 127 ? String(decoding: Array($0.utf8.prefix(127)), as: UTF8.self) : $0) }))
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                Text(ws.outputPassword.trimmingCharacters(in: .whitespaces).isEmpty ? "先设好密码才能开始。" : "只在这次处理里用，不保存。忘了密码就打不开，记得另外告诉对方。")
                    .font(.caption)
                    .foregroundStyle(ws.outputPassword.trimmingCharacters(in: .whitespaces).isEmpty ? AnyShapeStyle(Color.warning) : AnyShapeStyle(.secondary))
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}
