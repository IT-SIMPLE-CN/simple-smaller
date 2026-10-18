import SwiftUI

/// 画面（设计稿 V2.2）：比例和尺寸放在一起定。图片不再分「压缩 / 裁切」——
/// 选「原比例」就是只压缩，下面是尺寸；选了比例就是同时裁切，下面是输出宽（高按比例）。
struct FrameCard: View {
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    private let columns = Array(repeating: GridItem(.flexible(), spacing: 6), count: 9)

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "crop", title: "画面", subtitle: subtitle) {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                Text("比例").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                LazyVGrid(columns: columns, spacing: 6) {
                    RatioTile(ratio: "原比例", name: "只压缩", on: ws.draft.image.crop == nil) { choose(nil) }
                    ForEach(CropPreset.imagePresets) { preset in
                        RatioTile(ratio: preset.ratioLabel.isEmpty ? "—" : preset.ratioLabel,
                                  name: preset.ratioLabel.isEmpty ? "自由" : Self.shortName(preset),
                                  on: ws.draft.image.crop?.id == preset.id) { choose(preset) }
                    }
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel("比例")
                Spacer(minLength: 0)
                ZStack(alignment: .leading) {
                    if ws.isCropping { cropSize.transition(.bentoGrow) } else { plainSize.transition(.bentoGrow) }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .bentoPanel()
                .animation(.spring(response: 0.45, dampingFraction: 0.82), value: ws.isCropping)
            }
        }
        .bentoLocked(ws.running)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    /// 比例格子里写短名（一行放得下）；完整名字在悬停和旁白里。
    static func shortName(_ p: CropPreset) -> String {
        switch p.id {
        case "dy": "抖音"
        case "gzh": "公众号"
        case "gzh2": "次图"
        case "shop": "电商"
        case "ig": "IG"
        case "banner": "横幅"
        default: p.name
        }
    }

    private var subtitle: String {
        ws.isCropping ? "每张默认居中取最大；在左边点一张可以调框" : "比例和尺寸一起定；选「原比例」就是只压缩"
    }

    private func choose(_ preset: CropPreset?) {
        guard ws.draft.image.crop?.id != preset?.id else { return }
        withAnimation(.smooth(duration: 0.3)) {
            ws.draft.image.crop = preset
            ws.draft.image.cropWidth = nil
            ws.resetCrop()
            if preset == nil { ws.cropEditing = false }
        }
    }

    // MARK: 原比例：尺寸

    private var custom: Binding<Bool> {
        Binding(get: { ws.draft.image.size != nil }, set: { on in
            ws.draft.image.size = on ? (ws.base.image.size ?? 2560) : nil
        })
    }

    private var plainSize: some View {
        HStack(alignment: .center, spacing: 12) {
            if let size = ws.draft.image.size {
                Text(ws.draft.image.sizeAxis.title).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                TextField("", value: Binding(get: { size }, set: { ws.draft.image.size = max(16, $0) }), format: .number.grouping(.never))
                    .textFieldStyle(.plain)
                    .font(.system(size: 36, weight: .light).monospacedDigit())
                    .frame(width: CGFloat(max(2, String(size).count)) * 22 + 4)
                    .accessibilityLabel("像素")
                Text("px").font(.system(size: 13)).foregroundStyle(Bento.secondary)
                if !ws.draft.image.extraSizes.isEmpty {
                    Text(verbatim: "另出 " + ws.draft.image.extraSizes.map { "\($0)" }.joined(separator: " / "))
                        .font(.system(size: 11).monospacedDigit()).foregroundStyle(Bento.secondary)
                        .help("这个常用每张图还会多出这几档小图，给网站按屏幕大小加载用；在常用里改")
                }
                Spacer(minLength: 8)
                MiniSegmented(selection: $ws.draft.image.sizeAxis, options: SizeAxis.allCases.map { ($0, $0.title) }, label: "按宽、高或长边")
            } else {
                Text("原尺寸").font(.system(size: 28, weight: .light)).foregroundStyle(Bento.secondary.opacity(0.6))
                Text("每张保持原来的宽高，只压体积").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                Spacer(minLength: 8)
            }
            MiniSegmented(selection: custom, options: [(false, "原尺寸"), (true, "自定义")], label: "尺寸方式")
        }
    }

    // MARK: 选了比例：输出宽

    private var cropSize: some View {
        HStack(spacing: 10) {
            Text("输出宽").font(.system(size: 12)).foregroundStyle(Bento.secondary)
            CircleIconButton(symbol: "minus", label: "减小宽度", size: 30, fill: Bento.card) { setWidth(ws.cropOutputWidth - 10) }
                .buttonRepeatBehavior(.enabled)
            TextField("", value: Binding(get: { ws.cropOutputWidth }, set: { setWidth($0) }), format: .number.grouping(.never))
                .textFieldStyle(.plain)
                .font(.system(size: 32, weight: .light).monospacedDigit())
                .multilineTextAlignment(.center)
                .frame(width: 92)
                .accessibilityLabel("输出宽度")
            CircleIconButton(symbol: "plus", label: "增大宽度", size: 30, fill: Bento.card) { setWidth(ws.cropOutputWidth + 10) }
                .buttonRepeatBehavior(.enabled)
            Text("px").font(.system(size: 13)).foregroundStyle(Bento.secondary)
            Spacer(minLength: 8)
            VStack(alignment: .trailing, spacing: 2) {
                Text(verbatim: "高 \(outputHeight) px · 按比例").font(.system(size: 12).monospacedDigit()).foregroundStyle(Bento.secondary)
                if let short = shortfall {
                    Label(short, systemImage: "exclamationmark.triangle.fill")
                        .font(.system(size: 11)).foregroundStyle(Color.warning).lineLimit(1)
                } else {
                    Text("原图不够大时提醒，不放大").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                }
            }
        }
    }

    private var currentBox: CGRect { ws.effectiveCropBox(ws.cropCurrentIndex) }

    /// 高按比例；自由比例按当前这张框的宽高比。
    private var outputHeight: Int {
        let w = Double(ws.cropOutputWidth)
        if let a = ws.cropAspect { return Int((w / a).rounded()) }
        let b = currentBox
        return b.width > 0 ? Int((w * b.height / b.width).rounded()) : 0
    }

    /// 有裁出来不够输出宽的：提醒按原宽输出、不放大。
    private var shortfall: String? {
        let short = ws.cropItems.indices.filter { Int(ws.effectiveCropBox($0).width) < ws.cropOutputWidth }.count
        return short > 0 ? "\(short) 张裁出来不够宽，按原宽输出" : nil
    }

    private func setWidth(_ w: Int) {
        ws.draft.image.cropWidth = min(20000, max(16, w))
    }
}

/// 比例格子：上面比例，下面平台；选中墨黑。
private struct RatioTile: View {
    let ratio: String
    let name: String
    let on: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 1) {
                Text(ratio).font(.system(size: 15, weight: .semibold).monospacedDigit()).lineLimit(1).minimumScaleFactor(0.8)
                Text(name).font(.system(size: 10)).lineLimit(1)
                    .foregroundStyle(on ? Bento.onInk.opacity(0.75) : Bento.secondary)
            }
            .foregroundStyle(on ? Bento.onInk : Color.primary)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(on ? Bento.ink : Bento.fill))
            .contentShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .accessibilityLabel(ratio == "原比例" ? "原比例，只压缩" : "\(name) \(ratio)")
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
    }
}
