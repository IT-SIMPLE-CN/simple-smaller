import SwiftUI

// PDF（设计稿 V2.1 · 08）：上方一张宽卡放这个分段的主设置，左下「选项」，右下「开始」。

/// 上方宽卡：按分段换内容。
struct PdfMainCard: View {
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    private var p: PdfBody { ws.draft.pdf }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                switch ws.pdfSegment {
                case .compress: compress
                case .merge: merge
                case .split: split
                case .toImages: toImages
                case .fromImages: fromImages
                }
            }
            .animation(.smooth(duration: 0.25), value: p)
        }
        .bentoLocked(ws.running)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    // MARK: 压缩：四档大格子

    @ViewBuilder private var compress: some View {
        BentoHeader(symbol: "arrow.down.right.and.arrow.up.left", title: "压缩档", subtitle: "跟着常用走；文字一直保持矢量") {
            SaveRecipeAccessory(ws: ws, saving: $saving)
        }
        Grid(horizontalSpacing: 10, verticalSpacing: 10) {
            GridRow {
                LevelTile(title: "屏幕阅读", detail: "图片 110 dpi", on: p.level == .screen) { ws.draft.pdf.level = .screen }
                LevelTile(title: "邮件附件", detail: "图片 150 dpi，目标 10 MB 内", on: p.level == .email) { ws.draft.pdf.level = .email }
            }
            GridRow {
                LevelTile(title: "打印保真", detail: "图片 300 dpi，只优化结构", on: p.level == .print) { ws.draft.pdf.level = .print }
                LevelTile(title: "指定大小", detail: "逐档降图片质量", on: p.level == .size) { ws.draft.pdf.level = .size } accessory: {
                    if p.level == .size {
                        HStack(spacing: 4) {
                            TextField("", value: Binding(get: { p.targetMB }, set: { ws.draft.pdf.targetMB = min(2000, max(0.5, $0)) }),
                                      format: .number.precision(.fractionLength(0...1)).grouping(.never))
                                .textFieldStyle(.plain)
                                .multilineTextAlignment(.trailing)
                                .frame(width: 36)
                                .accessibilityLabel("目标大小")
                            Text("MB")
                        }
                        .font(.system(size: 13).monospacedDigit())
                        .foregroundStyle(Color.primary)
                        .padding(.horizontal, 10)
                        .frame(height: 30)
                        .background(RoundedRectangle(cornerRadius: 10, style: .continuous).fill(Bento.card))
                    }
                }
            }
        }
        .frame(maxHeight: .infinity)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("压缩档")
    }

    // MARK: 合并 / 拆分

    @ViewBuilder private var merge: some View {
        BentoHeader(symbol: "doc.on.doc", title: "页面顺序", subtitle: "在左边拖动排序；选中按 Delete 删页")
        Spacer(minLength: 0)
        bigLine(number: "\(ws.pageOrder.count)", unit: "页", note: "合成 1 个文件")
    }

    @ViewBuilder private var split: some View {
        BentoHeader(symbol: "scissors", title: "拆分方式", subtitle: "每个文件分别拆")
        Spacer(minLength: 0)
        HStack(spacing: 12) {
            Text("每").foregroundStyle(Bento.secondary)
            CircleIconButton(symbol: "minus", label: "减少", size: 40) { ws.draft.pdf.splitEvery = max(1, p.splitEvery - 1) }
                .buttonRepeatBehavior(.enabled)
            Text(verbatim: "\(p.splitEvery)").font(.system(size: 44, weight: .light).monospacedDigit()).frame(minWidth: 60)
            CircleIconButton(symbol: "plus", label: "增加", size: 40) { ws.draft.pdf.splitEvery = min(500, p.splitEvery + 1) }
                .buttonRepeatBehavior(.enabled)
            Text("页一个文件").foregroundStyle(Bento.secondary)
            Spacer()
            Text(verbatim: "共拆出 \(splitCount) 个").font(.system(size: 20).monospacedDigit())
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .bentoPanel()
        .accessibilityElement(children: .combine)
    }

    private var splitCount: Int {
        ws.files.filter { $0.type == .pdf }.reduce(0) { $0 + Int((Double($1.pages) / Double(max(1, p.splitEvery))).rounded(.up)) }
    }

    // MARK: 转图片 / 图片转 PDF

    @ViewBuilder private var toImages: some View {
        BentoHeader(symbol: "photo.on.rectangle", title: "转图片", subtitle: p.longImage ? "所有页面从上到下拼成一张长图" : "每页存成一张图片") {
            SaveRecipeAccessory(ws: ws, saving: $saving)
        }
        MiniSegmented(selection: $ws.draft.pdf.longImage, options: [(false, "每页一张"), (true, "拼成长图")], label: "方式")
        HStack(alignment: .top, spacing: 18) {
            LabeledPills(title: "格式", selection: $ws.draft.pdf.imageFormat, options: [ImageFormatChoice.jpeg, .png, .webp].map { ($0, $0.title) })
            LabeledPills(title: "宽", selection: $ws.draft.pdf.imageWidth, options: [750, 1080, 1242, 1600, 2400].map { ($0, "\($0)") })
        }
        Spacer(minLength: 0)
        if p.longImage {
            Text("页数很多时会按高度上限分成几张。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
        }
    }

    @ViewBuilder private var fromImages: some View {
        BentoHeader(symbol: "doc.badge.plus", title: "页面", subtitle: "图片按左边的顺序，一张一页")
        MiniSegmented(selection: $ws.draft.pdf.pageMode, options: [(.a4, "A4 纸"), (.fit, "按图片大小")], label: "页面大小")
        Spacer(minLength: 0)
        Text(p.pageMode == .a4 ? "每张图等比放进 A4 页面，四周留白，适合打印。" : "每页和图片一样大，没有白边，适合屏幕看。")
            .font(.system(size: 12)).foregroundStyle(Bento.secondary)
    }

    private func bigLine(number: String, unit: String, note: String) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(number).font(.system(size: 44, weight: .light).monospacedDigit())
            Text(unit).foregroundStyle(Bento.secondary)
            Spacer()
            Text(note).font(.system(size: 16))
        }
        .padding(.horizontal, 16).padding(.vertical, 10)
        .bentoPanel()
    }
}

/// 压缩档大格子：名称 + 说明（不写预估体积：点「开始」才压）。
private struct LevelTile<Accessory: View>: View {
    let title: String
    let detail: String
    let on: Bool
    let action: () -> Void
    @ViewBuilder var accessory: Accessory

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text(title).font(.system(size: 16, weight: .semibold))
                    Spacer()
                    if on {
                        Image(systemName: "checkmark").font(.system(size: 10, weight: .bold)).foregroundStyle(.white)
                            .frame(width: 22, height: 22).background(Circle().fill(Color.brand))
                    }
                }
                Spacer(minLength: 6)
                HStack(alignment: .bottom) {
                    Text(detail).font(.system(size: 11)).foregroundStyle(on ? Bento.onInk.opacity(0.7) : Bento.secondary)
                        .lineLimit(2)
                    Spacer(minLength: 4)
                    accessory
                }
            }
            .foregroundStyle(on ? Bento.onInk : Color.primary)
            .padding(.horizontal, 16).padding(.vertical, 14)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(on ? Bento.ink : Bento.fill))
            .contentShape(RoundedRectangle(cornerRadius: 18, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .accessibilityLabel("\(title)，\(detail)")
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
    }
}

extension LevelTile where Accessory == EmptyView {
    init(title: String, detail: String, on: Bool, action: @escaping () -> Void) {
        self.init(title: title, detail: detail, on: on, action: action) { EmptyView() }
    }
}

/// 左下：选项（去掉作者、转灰度、打开密码）；转图片时是质量。
struct PdfOptionsCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 8) {
                if ws.pdfSegment == .toImages {
                    BentoHeader(symbol: "dial.medium", title: "质量")
                    Spacer(minLength: 0)
                    if ws.draft.pdf.imageFormat == .png {
                        Text("PNG 无损，不用调质量。").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                    } else {
                        HStack(alignment: .firstTextBaseline) {
                            Text(verbatim: "\(ws.draft.pdf.imageQuality) · \(QualityWord.of(ws.draft.pdf.imageQuality))").font(Bento.number(22, weight: .medium))
                            Spacer()
                        }
                        BrandSlider(value: Binding(get: { Double(ws.draft.pdf.imageQuality) }, set: { ws.draft.pdf.imageQuality = Int($0.rounded()) }), range: 30...100)
                            .frame(height: 24)
                            .accessibilityLabel("质量")
                    }
                } else {
                    BentoHeader(symbol: "checklist", title: "选项")
                    Spacer(minLength: 0)
                    if [.compress, .merge].contains(ws.pdfSegment) {
                        BentoToggleRow(label: "去掉作者与修改记录", isOn: $ws.draft.stripMetadata, height: 40)
                    }
                    if ws.pdfSegment == .compress {
                        BentoToggleRow(label: "转灰度", isOn: $ws.draft.pdf.grayscale, height: 40)
                    }
                    PasswordRows(ws: ws)
                }
            }
        }
        .bentoLocked(ws.running)
    }
}

/// 输出加打开密码：发给客户的报价、合同常用。密码不写进配方、不保存。
struct PasswordRows: View {
    @Bindable var ws: Workspace
    @FocusState private var focused: Bool

    var body: some View {
        BentoToggleRow(label: "输出加打开密码", isOn: Binding(get: { ws.outputPasswordOn }, set: { v in
            ws.outputPasswordOn = v
            if v { DispatchQueue.main.async { focused = true } } else { ws.outputPassword = "" }
        }), height: 40)
        if ws.outputPasswordOn {
            SecureField("设置密码", text: Binding(get: { ws.outputPassword }, set: { ws.outputPassword = String($0.utf8.count > 127 ? String(decoding: Array($0.utf8.prefix(127)), as: UTF8.self) : $0) }))
                .textFieldStyle(.plain)
                .padding(.horizontal, 16)
                .frame(height: 40)
                .background(RoundedRectangle(cornerRadius: Bento.rowRadius, style: .continuous).strokeBorder(focused ? Color.brand : Bento.line, lineWidth: 1.2))
                .focused($focused)
            Text(ws.outputPassword.trimmingCharacters(in: .whitespaces).isEmpty ? "先设好密码才能开始。" : "只在这次用，不保存。记得另外告诉对方。")
                .font(.system(size: 11))
                .foregroundStyle(ws.outputPassword.trimmingCharacters(in: .whitespaces).isEmpty ? Color.warning : Bento.secondary)
        }
    }
}

/// PDF 压缩的右下格：这一批多少页 + 开始。
struct PdfStartCard: View {
    let ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text("这一批").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    Text(verbatim: "\(ws.files.count) 个 · \(ws.files.reduce(0) { $0 + $1.pages }) 页").font(.system(size: 24, weight: .light).monospacedDigit())
                }
                Text("压完变大的文件会保留原文件")
                    .font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    .padding(.horizontal, 12).padding(.vertical, 9)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.fill))
                StartBlock(ws: ws)
            }
        }
    }
}
