import SwiftUI

/// 圆环（设计稿 06）。尺寸取自设计稿：画布 440，镂空半径 64，内圈 64–132，外圈 138–210。
/// 出现：镂空圈先出 → 内圈从 12 点顺时针逐格弹出（每格 18ms）→ 外圈展开，约 0.4 秒；换内圈时外圈重新展开。
/// 「减弱动态效果」时直接显示。
struct RingView: View {
    static let size: CGFloat = 440
    static let center = CGPoint(x: 220, y: 220)
    static let hole: CGFloat = 64, innerOuter: CGFloat = 132, outerInner: CGFloat = 138, outerOuter: CGFloat = 210

    let slots: [RingSlot]
    /// 镂空圈上沿的小标：「4 张」。
    let badge: String
    let onChoose: (RingChoice) -> Void
    let onDismiss: () -> Void
    /// 静态显示（教学里的示例）：不播出现动画、不接交互，外圈停在 `initialHover` 那一格。
    var still = false
    var initialHover: Int? = nil

    @State private var selected = 0
    @State private var hoverOuter: Int?
    @State private var revealed = false
    @State private var outerToken = 0
    @State private var outerShown = false
    @State private var pressing = false
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.colorScheme) private var scheme

    private static let ease = Animation.timingCurve(0.22, 1, 0.36, 1, duration: 0.26)

    var body: some View {
        ZStack {
            // 镂空圈：中心是空的，露出被点的文件。
            Circle()
                .strokeBorder(Color.white.opacity(scheme == .dark ? 0.35 : 0.9), lineWidth: 1.5)
                .frame(width: Self.hole * 2, height: Self.hole * 2)
                .shadow(color: .black.opacity(0.25), radius: 3)
                .scaleEffect(revealed ? 1 : 0.6)
                .opacity(revealed ? 1 : 0)
                .animation(reduceMotion ? nil : .timingCurve(0.22, 1, 0.36, 1, duration: 0.22), value: revealed)

            ForEach(Array(slots.enumerated()), id: \.element.id) { i, slot in
                innerSector(i, slot)
            }
            ForEach(Array(currentChoices.enumerated()), id: \.element.id) { j, choice in
                outerSector(j, choice)
            }
            .id(outerToken)

            if !badge.isEmpty {
                Text(verbatim: badge)
                    .font(.system(size: 11, weight: .semibold).monospacedDigit())
                    .foregroundStyle(.white)
                    .padding(.horizontal, 8)
                    .frame(minWidth: 44, minHeight: 18)
                    .background(Capsule().fill(Color.brand))
                    .shadow(color: .black.opacity(0.25), radius: 1.5, y: 1)
                    .position(x: Self.center.x, y: Self.center.y - Self.hole)
                    .opacity(revealed ? 1 : 0)
                    .animation(reduceMotion ? nil : .easeOut(duration: 0.2).delay(0.06), value: revealed)
            }
        }
        .frame(width: Self.size, height: Self.size)
        .contentShape(Circle().inset(by: Self.size / 2 - Self.outerOuter))
        .onContinuousHover(coordinateSpace: .local) { phase in
            switch phase {
            case .active(let p): hover(p)
            case .ended: hoverOuter = nil
            }
        }
        // 按下拖到外圈松手也执行；点一下等同按下后原地松手。
        .gesture(DragGesture(minimumDistance: 0)
            .onChanged { v in pressing = true; hover(v.location) }
            .onEnded { v in pressing = false; release(at: v.location) })
        .onAppear {
            if still {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) { revealed = true; outerShown = true; hoverOuter = initialHover }
                return
            }
            if reduceMotion { revealed = true; outerShown = true; return }
            revealed = true
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.18) { outerShown = true }
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("圆环")
    }

    private var currentChoices: [RingChoice] { slots.indices.contains(selected) ? slots[selected].choices : [] }
    private var innerSpan: Double { 360 / Double(max(1, slots.count)) }
    private let outerSpan: Double = 36

    // MARK: 扇区

    private func innerSector(_ i: Int, _ slot: RingSlot) -> some View {
        let mid = Double(i) * innerSpan
        let on = i == selected
        let label = Self.point(r: (Self.hole + Self.innerOuter) / 2, angle: mid)
        return ZStack {
            Annulus(r1: Self.hole, r2: Self.innerOuter, a1: mid - innerSpan / 2, a2: mid + innerSpan / 2)
                .fill(on ? AnyShapeStyle(Color.brand) : AnyShapeStyle(.regularMaterial))
            Annulus(r1: Self.hole, r2: Self.innerOuter, a1: mid - innerSpan / 2, a2: mid + innerSpan / 2)
                .stroke(Color.primary.opacity(0.08), lineWidth: 0.5)
            Text(slot.label)
                .font(.system(size: 13, weight: .semibold))
                .foregroundStyle(on ? Color.white : Color.primary)
                .position(label)
        }
        .scaleEffect(revealed ? 1 : 0.86, anchor: UnitPoint(x: 0.5, y: 0.5))
        .opacity(revealed ? 1 : 0)
        .animation(reduceMotion ? nil : Self.ease.delay(0.04 + Double(i) * 0.018), value: revealed)
        .accessibilityElement()
        .accessibilityLabel(slot.label)
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
        .accessibilityAction { choose(slot.choices[slot.defaultIndex]) }
    }

    private func outerSector(_ j: Int, _ choice: RingChoice) -> some View {
        let n = Double(currentChoices.count)
        let start = Double(selected) * innerSpan - n * outerSpan / 2
        let a1 = start + Double(j) * outerSpan
        let hovered = hoverOuter == j
        let isDefault = j == slots[selected].defaultIndex
        let label = Self.point(r: (Self.outerInner + Self.outerOuter) / 2, angle: a1 + outerSpan / 2)
        let hoverFill: Color = scheme == .dark ? .white : Color(white: 0.055)
        let hoverText: Color = scheme == .dark ? .black : .white
        return ZStack {
            Annulus(r1: Self.outerInner, r2: Self.outerOuter, a1: a1 + 0.6, a2: a1 + outerSpan - 0.6)
                .fill(hovered ? AnyShapeStyle(hoverFill) : AnyShapeStyle(.regularMaterial))
            Annulus(r1: Self.outerInner, r2: Self.outerOuter, a1: a1 + 0.6, a2: a1 + outerSpan - 0.6)
                .stroke(Color.primary.opacity(0.08), lineWidth: 0.5)
            VStack(spacing: 1) {
                Text((isDefault ? "● " : "") + choice.label)
                    .font(.system(size: 12, weight: .semibold))
                    .lineLimit(1)
                Text(choice.sub)
                    .font(.system(size: 10).monospacedDigit())
                    .lineLimit(1)
                    .opacity(0.7)
            }
            .foregroundStyle(hovered ? hoverText : Color.primary)
            .frame(width: 80)
            .position(label)
        }
        .scaleEffect(outerShown ? 1 : 0.9)
        .opacity(outerShown ? 1 : 0)
        .animation(reduceMotion ? nil : .timingCurve(0.22, 1, 0.36, 1, duration: 0.24).delay(Double(j) * 0.024), value: outerShown)
        .animation(.easeOut(duration: 0.12), value: hovered)
        .accessibilityElement()
        .accessibilityLabel("\(choice.label)，\(choice.sub)")
        .accessibilityAddTraits(.isButton)
        .accessibilityAction { choose(choice) }
    }

    // MARK: 交互

    private enum Region { case center, inner(Int), outer(Int), outside }

    private func region(_ p: CGPoint) -> Region {
        let dx = p.x - Self.center.x, dy = p.y - Self.center.y
        let r = hypot(dx, dy)
        var angle = atan2(dx, -dy) * 180 / .pi
        if angle < 0 { angle += 360 }
        if r < Self.hole { return .center }
        if r <= Self.innerOuter + 3 {
            let i = Int(((angle + innerSpan / 2).truncatingRemainder(dividingBy: 360)) / innerSpan)
            return .inner(min(slots.count - 1, i))
        }
        if r <= Self.outerOuter {
            let n = Double(currentChoices.count)
            var rel = angle - (Double(selected) * innerSpan - n * outerSpan / 2)
            rel = (rel + 720).truncatingRemainder(dividingBy: 360)
            let j = Int(rel / outerSpan)
            return j >= 0 && j < currentChoices.count ? .outer(j) : .outside
        }
        return .outside
    }

    private func hover(_ p: CGPoint) {
        switch region(p) {
        case .inner(let i):
            hoverOuter = nil
            if i != selected { select(i) }
        case .outer(let j):
            hoverOuter = j
        default:
            hoverOuter = nil
        }
    }

    /// 换内圈：外圈重新展开。
    private func select(_ i: Int) {
        selected = i
        if reduceMotion { outerToken += 1; return }
        outerShown = false
        outerToken += 1
        DispatchQueue.main.async { outerShown = true }
    }

    private func release(at p: CGPoint) {
        switch region(p) {
        case .center, .outside: onDismiss()
        case .inner(let i): choose(slots[i].choices[slots[i].defaultIndex])
        case .outer(let j): choose(currentChoices[j])
        }
    }

    private func choose(_ c: RingChoice) { onChoose(c) }

    static func point(r: CGFloat, angle: Double) -> CGPoint {
        let t = angle * .pi / 180
        return CGPoint(x: center.x + r * CGFloat(sin(t)), y: center.y - r * CGFloat(cos(t)))
    }
}

/// 环形扇区：角度从 12 点起顺时针。
struct Annulus: Shape {
    let r1: CGFloat, r2: CGFloat, a1: Double, a2: Double

    func path(in rect: CGRect) -> Path {
        let c = CGPoint(x: rect.midX, y: rect.midY)
        // SwiftUI 的角度从 3 点钟起、顺时针为正（y 向下），换算：12 点 = −90°。
        let s = Angle(degrees: a1 - 90), e = Angle(degrees: a2 - 90)
        var p = Path()
        p.addArc(center: c, radius: r2, startAngle: s, endAngle: e, clockwise: false)
        p.addArc(center: c, radius: r1, startAngle: e, endAngle: s, clockwise: true)
        p.closeSubpath()
        return p
    }
}
