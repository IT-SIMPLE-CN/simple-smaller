import SwiftUI

// 视频、PDF 各分段的设置节：每节是一张白卡，在右侧竖着排（便当盒）。

/// 设置节的容器：卡片之间留便当盒间距。
struct InspectorScroll<Content: View>: View {
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: Bento.gap) { content }
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

/// 一节 = 一张卡：标题 + 若干行。
struct InspectorSection<Content: View>: View {
    let title: String
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(title)
                .font(.system(size: 15, weight: .semibold))
                .accessibilityAddTraits(.isHeader)
            content
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Bento.cardRadius, style: .continuous).fill(Bento.card))
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
        .frame(minHeight: 26)
    }
}

/// 开关行：右边一个开关，开着是品牌红。
struct InspectorToggle: View {
    let label: String
    @Binding var isOn: Bool

    var body: some View {
        InspectorRow(label: label) {
            Toggle(label, isOn: $isOn).labelsHidden().toggleStyle(.switch).tint(.brand).controlSize(.small)
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
