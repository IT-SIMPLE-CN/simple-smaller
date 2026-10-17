import AppKit
import SwiftUI

extension Color {
    /// 品牌红，只用于主按钮、选中与进度。
    static let brand = Color(red: 0xEF / 255, green: 0x39 / 255, blue: 0x22 / 255)
    /// 变小了（完成、节省）。
    static let saving = Color(red: 0x1B / 255, green: 0x7F / 255, blue: 0x4E / 255)
    /// 提醒（保留原文件、原图不够大）。
    static let warning = Color(red: 0xC2 / 255, green: 0x41 / 255, blue: 0x0C / 255)
}

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
