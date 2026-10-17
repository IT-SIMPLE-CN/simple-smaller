import AppKit
import SwiftUI

/// 便当盒界面（设计稿 V2.1）的共用色板、尺寸与组件。
/// 浅灰窗口底上放白色圆角卡片；墨黑用于文字与选中胶囊，品牌红只用于主按钮、选中标记与进度。
enum Bento {
    static func dynamic(_ light: UInt32, _ dark: UInt32) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            let hex = appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light
            return NSColor(srgbRed: CGFloat(hex >> 16 & 0xFF) / 255, green: CGFloat(hex >> 8 & 0xFF) / 255, blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
        })
    }

    /// 窗口底 #EEF0F2。
    static let window = dynamic(0xEEF0F2, 0x17181A)
    /// 卡片白。
    static let card = dynamic(0xFFFFFF, 0x242528)
    /// 胶囊底、行底 #F2F3F5。
    static let fill = dynamic(0xF2F3F5, 0x303135)
    /// 墨黑：选中胶囊的底。
    static let ink = dynamic(0x111214, 0xF2F3F5)
    /// 选中胶囊上的字（与墨黑相反）。
    static let onInk = dynamic(0xFFFFFF, 0x111214)
    /// 次要文字 #63676E。
    static let secondary = dynamic(0x63676E, 0x9DA1A8)
    /// 未选开关、描边 #D9DCE0。
    static let line = dynamic(0xD9DCE0, 0x45474C)
    /// 提示红：底 #FDE8E4、字 #A8240F。
    static let alertFill = dynamic(0xFDE8E4, 0x3D201C)
    static let alertText = dynamic(0xA8240F, 0xFF9B8A)
    /// 失败。
    static let failed = dynamic(0xC81E1E, 0xFF6B6B)

    static let gap: CGFloat = 14
    static let pad: CGFloat = 16
    static let cardRadius: CGFloat = 22
    static let rowRadius: CGFloat = 15

    /// 大数字（体积、像素、进度）：系统字体等宽数字。
    static func number(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight).monospacedDigit()
    }
}

// MARK: - 卡片

/// 白色圆角卡片。「增强对比度」打开时加一圈描边。
struct BentoCard<Content: View>: View {
    var padding = EdgeInsets(top: 18, leading: 20, bottom: 18, trailing: 20)
    var radius = Bento.cardRadius
    var fill: Color = Bento.card
    @ViewBuilder var content: Content
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: radius, style: .continuous).fill(fill))
            .overlay {
                if contrast == .increased {
                    RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(Color.primary.opacity(0.35), lineWidth: 1)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
    }
}

/// 卡片头：圆形图标 + 标题 + 副标题，右边放操作。
struct BentoHeader<Trailing: View>: View {
    let symbol: String
    let title: String
    var subtitle: String? = nil
    @ViewBuilder var trailing: Trailing

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: symbol)
                .font(.system(size: 14, weight: .medium))
                .frame(width: 34, height: 34)
                .background(Circle().fill(Bento.fill))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(title).font(.system(size: 15, weight: .semibold)).lineLimit(1)
                if let subtitle {
                    Text(subtitle).font(.system(size: 11)).foregroundStyle(Bento.secondary).lineLimit(1)
                }
            }
            .accessibilityElement(children: .combine)
            .accessibilityAddTraits(.isHeader)
            .layoutPriority(1)
            Spacer(minLength: 6)
            trailing
        }
    }
}

extension BentoHeader where Trailing == EmptyView {
    init(symbol: String, title: String, subtitle: String? = nil) {
        self.init(symbol: symbol, title: title, subtitle: subtitle) { EmptyView() }
    }
}

/// 灰底圆角行：左名称，右值或控件。
struct BentoRow<Trailing: View>: View {
    let label: String
    var height: CGFloat = 44
    @ViewBuilder var trailing: Trailing

    var body: some View {
        HStack(spacing: 10) {
            Text(label).lineLimit(1)
            Spacer(minLength: 8)
            trailing
        }
        .padding(.leading, 16)
        .padding(.trailing, 12)
        .frame(height: height)
        .background(RoundedRectangle(cornerRadius: Bento.rowRadius, style: .continuous).fill(Bento.fill))
    }
}

/// 开关行：系统开关，开着是品牌红。
struct BentoToggleRow: View {
    let label: String
    @Binding var isOn: Bool
    var height: CGFloat = 44

    var body: some View {
        BentoRow(label: label, height: height) {
            Toggle(label, isOn: $isOn).labelsHidden().toggleStyle(.switch).tint(.brand).controlSize(.small)
        }
    }
}

// MARK: - 胶囊选择

/// 一排胶囊单选：选中墨黑底白字，其余灰底。
struct PillChoice<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    var height: CGFloat = 36
    var label = ""

    var body: some View {
        FlowLayout(spacing: 6) {
            ForEach(options, id: \.0) { value, title in
                PillButton(title: title, on: selection == value, height: height) { selection = value }
            }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}

struct PillButton: View {
    let title: String
    let on: Bool
    var height: CGFloat = 36
    var dot = false
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Text(title).lineLimit(1)
                if dot {
                    Circle().fill(Color.brand).frame(width: 6, height: 6).accessibilityLabel("已修改")
                }
            }
            .font(.system(size: 13, weight: on ? .semibold : .regular))
            .foregroundStyle(on ? Bento.onInk : Color.primary)
            .padding(.horizontal, height >= 34 ? 16 : 12)
            .frame(height: height)
            .background(Capsule().fill(on ? Bento.ink : (hovering ? Bento.line.opacity(0.7) : Bento.fill)))
            .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
        .onHover { hovering = $0 }
        .animation(.easeOut(duration: 0.15), value: hovering)
        .animation(.smooth(duration: 0.25), value: on)
        .accessibilityAddTraits(on ? .isSelected : [])
    }
}

/// 小号分段（白底槽里白色滑块）：卡片头右侧用，如「原尺寸 / 自定义」「宽 / 高 / 长边」。
struct MiniSegmented<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    var label = ""
    @Namespace private var ns

    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { value, title in
                let on = selection == value
                Button { selection = value } label: {
                    Text(title)
                        .font(.system(size: 12, weight: on ? .semibold : .regular))
                        .foregroundStyle(on ? Color.primary : Bento.secondary)
                        .padding(.horizontal, 12)
                        .frame(height: 28)
                        .background {
                            if on {
                                Capsule().fill(Bento.card)
                                    .shadow(color: .black.opacity(0.08), radius: 1, y: 1)
                                    .matchedGeometryEffect(id: "thumb", in: ns)
                            }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(3)
        .background(Capsule().fill(Bento.fill))
        .fixedSize()
        .animation(.spring(response: 0.35, dampingFraction: 0.8), value: selection)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}

/// 图标小分段（缩略图 / 列表）。
struct IconSegmented<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String, String)]
    var label = ""
    @Namespace private var ns

    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { value, symbol, title in
                let on = selection == value
                Button { selection = value } label: {
                    Image(systemName: symbol)
                        .font(.system(size: 13, weight: .medium))
                        .foregroundStyle(on ? Color.primary : Bento.secondary)
                        .frame(width: 32, height: 28)
                        .background {
                            if on {
                                Capsule().fill(Bento.card)
                                    .shadow(color: .black.opacity(0.08), radius: 1, y: 1)
                                    .matchedGeometryEffect(id: "thumb", in: ns)
                            }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .help(title)
                .accessibilityLabel(title)
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(3)
        .background(Capsule().fill(Bento.fill))
        .fixedSize()
        .animation(.spring(response: 0.35, dampingFraction: 0.8), value: selection)
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}

/// 顶栏居中的分段（白底胶囊，选中墨黑）：压缩 / 裁切 等。
struct TopTabs<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    @Namespace private var ns

    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { value, title in
                let on = selection == value
                Button { selection = value } label: {
                    Text(title)
                        .font(.system(size: 13, weight: on ? .semibold : .regular))
                        .foregroundStyle(on ? Bento.onInk : Color.primary.opacity(0.8))
                        .padding(.horizontal, 20)
                        .frame(height: 30)
                        .background {
                            if on { Capsule().fill(Bento.ink).matchedGeometryEffect(id: "tab", in: ns) }
                        }
                        .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityAddTraits(on ? [.isSelected, .isButton] : .isButton)
            }
        }
        .padding(4)
        .background(Capsule().fill(Bento.card))
        .animation(.spring(response: 0.35, dampingFraction: 0.82), value: selection)
    }
}

/// 圆形图标按钮（灰底或白底）。
struct CircleIconButton: View {
    let symbol: String
    let label: String
    var size: CGFloat = 34
    var fill: Color = Bento.fill
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: size * 0.42, weight: .medium))
                .foregroundStyle(Color.primary)
                .frame(width: size, height: size)
                .background(Circle().fill(fill).brightness(hovering ? -0.03 : 0))
                .contentShape(Circle())
        }
        .buttonStyle(BentoPressStyle())
        .onHover { hovering = $0 }
        .help(label)
        .accessibilityLabel(label)
    }
}

/// 灰底胶囊文字按钮。
struct CapsuleButton: View {
    let title: String
    var height: CGFloat = 34
    var fill: Color = Bento.fill
    var foreground: Color = .primary
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 13))
                .foregroundStyle(foreground)
                .padding(.horizontal, 14)
                .frame(height: height)
                .background(Capsule().fill(fill))
                .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
    }
}

/// 按下缩到 96%，松手回弹（设计稿：所有按钮、卡片）。「减弱动态效果」时只变暗。
struct BentoPressStyle: ButtonStyle {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed && !reduceMotion ? 0.96 : 1)
            .opacity(configuration.isPressed && reduceMotion ? 0.8 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.6), value: configuration.isPressed)
    }
}

// MARK: - 动效

/// 卡片出现：从 94% 弹到 100% 并淡入，按序号错开 50 毫秒。「减弱动态效果」时只淡入。
struct BentoAppear: ViewModifier {
    let index: Int
    let trigger: AnyHashable
    @State private var shown = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .scaleEffect(shown || reduceMotion ? 1 : 0.94)
            .opacity(shown ? 1 : 0)
            .onAppear { play() }
            .onChange(of: trigger) { _, _ in
                shown = false
                play()
            }
    }

    private func play() {
        let delay = Double(index) * 0.05
        if reduceMotion {
            withAnimation(.easeOut(duration: 0.2).delay(delay)) { shown = true }
        } else {
            withAnimation(.spring(response: 0.5, dampingFraction: 0.72).delay(delay)) { shown = true }
        }
    }
}

extension View {
    func bentoAppear(_ index: Int, trigger: AnyHashable = 0) -> some View {
        modifier(BentoAppear(index: index, trigger: trigger))
    }
}

/// 卡片里内容替换时「从底部长出来」。
extension AnyTransition {
    static var bentoGrow: AnyTransition {
        .asymmetric(insertion: .scale(scale: 0.96, anchor: .bottom).combined(with: .opacity), removal: .opacity)
    }
}

// MARK: - 自动换行

/// 胶囊一排放不下就换行。
struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let rows = arrange(width: proposal.width ?? .infinity, subviews: subviews)
        let w = rows.map { $0.width }.max() ?? 0
        let h = rows.reduce(0) { $0 + $1.height } + spacing * CGFloat(max(0, rows.count - 1))
        return CGSize(width: proposal.width ?? w, height: h)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var y = bounds.minY
        for row in arrange(width: bounds.width, subviews: subviews) {
            var x = bounds.minX
            for i in row.indices {
                let s = subviews[i].sizeThatFits(.unspecified)
                subviews[i].place(at: CGPoint(x: x, y: y + (row.height - s.height) / 2), proposal: ProposedViewSize(s))
                x += s.width + spacing
            }
            y += row.height + spacing
        }
    }

    private struct Row { var indices: [Int] = []; var width: CGFloat = 0; var height: CGFloat = 0 }

    private func arrange(width: CGFloat, subviews: Subviews) -> [Row] {
        var rows: [Row] = [Row()]
        for (i, v) in subviews.enumerated() {
            let s = v.sizeThatFits(.unspecified)
            if !rows[rows.count - 1].indices.isEmpty, rows[rows.count - 1].width + spacing + s.width > width {
                rows.append(Row())
            }
            var r = rows[rows.count - 1]
            r.width += (r.indices.isEmpty ? 0 : spacing) + s.width
            r.height = max(r.height, s.height)
            r.indices.append(i)
            rows[rows.count - 1] = r
        }
        return rows
    }
}
