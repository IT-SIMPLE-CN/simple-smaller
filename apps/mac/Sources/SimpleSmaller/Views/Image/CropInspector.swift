import SwiftUI

/// 检查器里的裁切设置（设计稿 03）：比例、输出尺寸。没调过的图一律居中、按比例取最大。
struct CropInspectorSections: View {
    @Bindable var ws: Workspace

    var body: some View {
        InspectorSection(title: "比例") {
            VStack(spacing: 2) {
                ForEach(CropPreset.imagePresets) { preset in
                    RatioRow(preset: preset, selected: ws.draft.image.crop?.id == preset.id) { choose(preset) }
                }
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("比例")
        }

        InspectorSection(title: "输出尺寸") {
            InspectorRow(label: "宽") {
                StepButton(symbol: "minus", label: "减小宽度") { setWidth(ws.cropOutputWidth - 10) }
                TextField("", value: Binding(get: { ws.cropOutputWidth }, set: { setWidth($0) }), format: .number.grouping(.never))
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.small)
                    .multilineTextAlignment(.trailing)
                    .monospacedDigit()
                    .frame(width: 58)
                    .accessibilityLabel("输出宽度")
                StepButton(symbol: "plus", label: "增大宽度") { setWidth(ws.cropOutputWidth + 10) }
                Text("px").foregroundStyle(.secondary)
            }
            InspectorRow(label: "高") {
                Text(verbatim: "\(outputHeight) px · 按比例").foregroundStyle(.secondary).monospacedDigit()
            }
            if let short = shortfall {
                Label(short, systemImage: "exclamationmark.triangle.fill")
                    .font(.caption)
                    .foregroundStyle(Color.warning)
                    .fixedSize(horizontal: false, vertical: true)
                    .transition(.opacity)
            }
        }
        .animation(.smooth(duration: 0.25), value: shortfall)
    }

    private var currentBox: CGRect { ws.effectiveCropBox(ws.cropCurrentIndex) }

    /// 高按比例；自由比例按当前这张框的宽高比。
    private var outputHeight: Int {
        let w = Double(ws.cropOutputWidth)
        if let a = ws.cropAspect { return Int((w / a).rounded()) }
        let b = currentBox
        return b.width > 0 ? Int((w * b.height / b.width).rounded()) : 0
    }

    /// 当前这张裁出来不够输出宽度时的提醒。
    private var shortfall: String? {
        let b = currentBox
        guard b.width > 0, Int(b.width) < ws.cropOutputWidth else { return nil }
        return "这张裁出 \(Int(b.width)) px 宽，不够 \(ws.cropOutputWidth)，按 \(Int(b.width)) px 输出，不放大"
    }

    private func setWidth(_ w: Int) {
        ws.draft.image.cropWidth = min(20000, max(16, w))
    }

    private func choose(_ preset: CropPreset) {
        ws.cropAutoSet = false
        guard ws.draft.image.crop?.id != preset.id else { return }
        withAnimation(.smooth(duration: 0.3)) {
            ws.draft.image.crop = preset
            ws.draft.image.cropWidth = nil
            ws.resetCrop()
        }
    }
}

/// 比例列表的一行：左边平台，右边比例；选中整行品牌红。
private struct RatioRow: View {
    let preset: CropPreset
    let selected: Bool
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack {
                Text(preset.name)
                Spacer(minLength: 8)
                Text(preset.ratioLabel)
                    .font(.system(size: 12).monospacedDigit())
                    .foregroundStyle(selected ? AnyShapeStyle(Color.white.opacity(0.85)) : AnyShapeStyle(.secondary))
            }
            .foregroundStyle(selected ? Color.white : Color.primary)
            .padding(.horizontal, 8)
            .frame(height: 28)
            .background {
                RoundedRectangle(cornerRadius: 5, style: .continuous)
                    .fill(selected ? Color.brand : (hovering ? Color.primary.opacity(0.06) : .clear))
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.15), value: hovering)
        .accessibilityLabel(preset.ratioLabel.isEmpty ? preset.name : "\(preset.name) \(preset.ratioLabel)")
        .accessibilityAddTraits(selected ? [.isButton, .isSelected] : .isButton)
    }
}

/// 输出宽度两侧的 − / + 小方钮。
private struct StepButton: View {
    let symbol: String
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 9, weight: .bold))
                .frame(width: 22, height: 22)
                .background(RoundedRectangle(cornerRadius: 5, style: .continuous).fill(Color(nsColor: .controlBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .buttonRepeatBehavior(.enabled)
        .accessibilityLabel(label)
    }
}
