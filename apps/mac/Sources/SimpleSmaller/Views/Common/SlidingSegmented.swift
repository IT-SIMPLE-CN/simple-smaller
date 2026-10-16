import SwiftUI

/// 滑动分段控件：选中块带弹性滑到新位置（连续点击从当前位置接着滑），
/// 外观沿用原生分段（圆角底槽、白色选中块）。键盘左右键切换，旁白按单选组朗读；
/// 「减弱动态效果」时直接落到终点。
struct SlidingSegmented<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    var size: ControlSize = .regular

    @Namespace private var ns
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.colorScheme) private var scheme
    @FocusState private var focused: Bool

    private var height: CGFloat { size == .small ? 22 : 26 }
    private var font: Font { size == .small ? .system(size: 11) : .system(size: 12) }

    var body: some View {
        HStack(spacing: 0) {
            ForEach(Array(options.enumerated()), id: \.offset) { index, option in
                let on = option.0 == selection
                Text(option.1)
                    .font(font.weight(on ? .semibold : .regular))
                    .foregroundStyle(on ? Color.primary : Color.secondary)
                    .lineLimit(1)
                    .fixedSize()
                    .frame(maxWidth: .infinity)
                    .frame(height: height - 4)
                    .padding(.horizontal, 3)
                    .background {
                        if on {
                            RoundedRectangle(cornerRadius: 5, style: .continuous)
                                .fill(scheme == .dark ? Color.white.opacity(0.22) : Color.white)
                                .shadow(color: .black.opacity(scheme == .dark ? 0.35 : 0.14), radius: 1.5, y: 0.5)
                                .matchedGeometryEffect(id: "thumb", in: ns)
                        }
                    }
                    .overlay(alignment: .leading) {
                        // 未选中的相邻两项之间画一条细分隔线，和原生一致。
                        if index > 0, !on, options[index - 1].0 != selection {
                            Rectangle().fill(Color.secondary.opacity(0.25)).frame(width: 1, height: height * 0.45)
                        }
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { choose(option.0) }
                    .accessibilityElement()
                    .accessibilityLabel(option.1)
                    .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
                    .accessibilityAction { choose(option.0) }
            }
        }
        .padding(2)
        .background {
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(Color.primary.opacity(scheme == .dark ? 0.10 : 0.07))
        }
        .opacity(isEnabled ? 1 : 0.5)
        // 只在键盘导航（Tab）时获得焦点，焦点环交给系统画；点按不会抢焦点。
        .focusable(true, interactions: .activate)
        .focused($focused)
        .onKeyPress(.leftArrow) { step(-1); return .handled }
        .onKeyPress(.rightArrow) { step(1); return .handled }
        .accessibilityElement(children: .contain)
        .accessibilityAddTraits(.isSummaryElement)
    }

    private func choose(_ value: Value) {
        guard isEnabled, value != selection else { return }
        if reduceMotion {
            selection = value
        } else {
            withAnimation(.spring(response: 0.32, dampingFraction: 0.82)) { selection = value }
        }
    }

    private func step(_ delta: Int) {
        guard let i = options.firstIndex(where: { $0.0 == selection }) else { return }
        let j = min(options.count - 1, max(0, i + delta))
        choose(options[j].0)
    }
}
