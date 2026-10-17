import AppKit
import SwiftUI

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
    @State private var grown = false
    @State private var visible = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content
            .scaleEffect(grown || reduceMotion ? 1 : 0.94)
            // 不从全透明开始：旧内容一换走，新卡片马上就有，中间不会闪一下空底。
            .opacity(visible ? 1 : 0.25)
            .onAppear { play() }
            .onChange(of: trigger) { _, _ in
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { grown = false; visible = false }
                play()
            }
    }

    private func play() {
        let delay = Double(index) * 0.04
        if reduceMotion {
            withAnimation(.easeOut(duration: 0.18).delay(delay)) { visible = true; grown = true }
        } else {
            // 淡入短、缩放带一点回弹：94% → 约 101% → 100%（设计稿 V2 交接第 4 节）。
            withAnimation(.easeOut(duration: 0.16).delay(delay)) { visible = true }
            withAnimation(.spring(response: 0.48, dampingFraction: 0.68).delay(delay)) { grown = true }
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

extension View {
    /// 处理中（或不适用时）锁住一张卡：不能点，变淡。
    func bentoLocked(_ locked: Bool) -> some View {
        disabled(locked).opacity(locked ? 0.45 : 1)
    }
}
