import QuickLook
import SwiftUI

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
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
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
                Group {
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
                }
                .disabled(ws.running)
                .opacity(ws.running ? 0.45 : 1)
                StartBlock(ws: ws).padding(.top, 2)
            }
        }
    }
}
