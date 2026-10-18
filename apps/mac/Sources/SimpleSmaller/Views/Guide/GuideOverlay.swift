import SwiftUI

/// 盖在主窗口上的教学浮层：功能小教学是「压暗 + 聚光框 + 小卡片」，圆环教学是居中弹窗。
struct GuideOverlay: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private var center: GuideCenter { .shared }

    var body: some View {
        ZStack {
            switch center.active {
            case .feature(let type): Spotlight(type: type)
            case .ring: RingGuide()
            case nil: Color.clear
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .opacity(center.shown ? 1 : 0)
        .animation(reduceMotion ? .easeOut(duration: 0.12) : .easeOut(duration: 0.22), value: center.shown)
    }
}

// MARK: - 聚光灯

/// 页面压暗（rgba(17,18,20,.52)），聚光框品牌红 3 px、圆角 30、比目标外扩 6；旁边一张白色小卡片。
/// 翻页时框伸缩移过去（0.5 秒，cubic-bezier(.22,1,.36,1)），卡片跟着挪；「减弱动态效果」时直接跳。
private struct Spotlight: View {
    let type: MediaType
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var cardHeight: CGFloat = 240
    private var center: GuideCenter { .shared }

    private static let pad: CGFloat = 6
    private static let cardWidth: CGFloat = 330

    var body: some View {
        GeometryReader { geo in
            let _ = center.layoutTick
            let pages = FeatureGuides.pages(type, videoSegment: center.videoSegment)
            let steps = GuideCenter.steps(type)
            let step = min(center.step, steps.count - 1)
            // 外扩后贴着窗口边的（标题栏里的大标签）收回到窗口里，红边不被窗口裁掉。
            let target = (center.rect(of: steps[step]) ?? fallback(geo.size)).insetBy(dx: -Self.pad, dy: -Self.pad)
                .intersection(CGRect(origin: .zero, size: geo.size).insetBy(dx: 4, dy: 4))
            let radius = min(30, target.height / 2)
            let card = cardOrigin(for: target, in: geo.size)
            ZStack(alignment: .topLeading) {
                SpotDim(rect: target, radius: radius)
                    .fill(Color(red: 17 / 255, green: 18 / 255, blue: 20 / 255).opacity(0.52), style: FillStyle(eoFill: true))
                    .contentShape(Rectangle())
                    .onTapGesture {}
                SpotRing(rect: target, radius: radius)
                    .stroke(Color.brand, lineWidth: 3)
                    .allowsHitTesting(false)
                coach(pages[step], step: step, count: pages.count)
                    .frame(width: Self.cardWidth)
                    .onGeometryChange(for: CGFloat.self) { $0.size.height } action: { cardHeight = $0 }
                    .offset(x: card.x, y: card.y)
            }
            .animation(reduceMotion ? nil : .timingCurve(0.22, 1, 0.36, 1, duration: 0.5), value: step)
        }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(type.title)怎么用")
        .accessibilityAddTraits(.isModal)
    }

    /// 目标没登记上（比如窗口很窄时那张卡在滚动区外）：框在窗口中间，不至于框空。
    private func fallback(_ size: CGSize) -> CGRect {
        CGRect(x: size.width / 2 - 60, y: size.height / 2 - 30, width: 120, height: 60)
    }

    /// 小卡片放哪：扁长的目标（大标签、常用条）放在下面；高的卡放右边，右边放不下放左边。都夹在窗口里。
    private func cardOrigin(for t: CGRect, in size: CGSize) -> CGPoint {
        let w = Self.cardWidth, h = cardHeight, margin: CGFloat = 16
        var x: CGFloat, y: CGFloat
        if t.height < 120 {
            y = t.maxY + 14
            x = t.width > 2 * w ? t.maxX - w - 10 : t.midX - w / 2
        } else if size.width - t.maxX - 12 >= w + margin {
            x = t.maxX + 12
            y = t.minY + 58
        } else if t.minX - 12 - w >= margin {
            x = t.minX - 12 - w
            y = t.minY + 58
        } else {
            x = t.midX - w / 2
            y = t.midY - h / 2
        }
        x = min(max(x, margin), size.width - w - margin)
        y = min(max(y, margin), size.height - h - margin)
        return CGPoint(x: x, y: y)
    }

    private func coach(_ page: GuidePage, step: Int, count: Int) -> some View {
        let last = step == count - 1
        return VStack(alignment: .leading, spacing: 10) {
            VStack(alignment: .leading, spacing: 10) {
                page.sketch
                    .frame(maxWidth: .infinity)
                    .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.fill))
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("\(step + 1) / \(count)")
                        .font(.system(size: 11, weight: .bold).monospacedDigit())
                        .foregroundStyle(Color.brand)
                        .accessibilityLabel("第 \(step + 1) 步，共 \(count) 步")
                    Text(page.title)
                        .font(.system(size: 16, weight: .bold))
                        .accessibilityAddTraits(.isHeader)
                }
                Text(page.text)
                    .font(.system(size: 12.5))
                    .foregroundStyle(Bento.body)
                    .lineSpacing(5)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .id(step)
            .transition(reduceMotion ? .opacity : .asymmetric(insertion: .scale(scale: 0.97, anchor: .bottom).combined(with: .opacity), removal: .opacity))
            HStack(spacing: 6) {
                if last {
                    Text("右上角 ? 随时再看").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                } else {
                    Button("跳过") { center.finish() }
                        .buttonStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundStyle(Bento.secondary)
                        .padding(.horizontal, 4)
                        .frame(height: 34)
                        .contentShape(Rectangle())
                }
                Spacer(minLength: 0)
                if step > 0 {
                    GuideButton(title: "上一步", style: .plain) { center.previous() }
                }
                GuideButton(title: last ? "知道了" : "下一步", style: last ? .brand : .ink) { center.next() }
            }
            .padding(.top, 2)
        }
        .padding(14)
        .background(RoundedRectangle(cornerRadius: 22, style: .continuous).fill(Bento.card).shadow(color: .black.opacity(0.3), radius: 30, y: 20))
        .accessibilityElement(children: .contain)
    }
}

/// 压暗层：整块减去聚光框（偶奇填充）。框的位置可动画。
private struct SpotDim: Shape {
    var rect: CGRect
    var radius: CGFloat

    var animatableData: AnimatablePair<AnimatablePair<CGFloat, CGFloat>, AnimatablePair<AnimatablePair<CGFloat, CGFloat>, CGFloat>> {
        get { AnimatablePair(AnimatablePair(rect.minX, rect.minY), AnimatablePair(AnimatablePair(rect.width, rect.height), radius)) }
        set {
            rect = CGRect(x: newValue.first.first, y: newValue.first.second, width: newValue.second.first.first, height: newValue.second.first.second)
            radius = newValue.second.second
        }
    }

    func path(in bounds: CGRect) -> Path {
        var p = Path(bounds.insetBy(dx: -40, dy: -40))
        p.addRoundedRect(in: rect, cornerSize: CGSize(width: radius, height: radius), style: .continuous)
        return p
    }
}

/// 聚光框的红边（画在框外 1.5，3 px 宽正好贴着框外沿）。
private struct SpotRing: Shape {
    var rect: CGRect
    var radius: CGFloat

    var animatableData: AnimatablePair<AnimatablePair<CGFloat, CGFloat>, AnimatablePair<AnimatablePair<CGFloat, CGFloat>, CGFloat>> {
        get { AnimatablePair(AnimatablePair(rect.minX, rect.minY), AnimatablePair(AnimatablePair(rect.width, rect.height), radius)) }
        set {
            rect = CGRect(x: newValue.first.first, y: newValue.first.second, width: newValue.second.first.first, height: newValue.second.first.second)
            radius = newValue.second.second
        }
    }

    func path(in bounds: CGRect) -> Path {
        Path(roundedRect: rect.insetBy(dx: -1.5, dy: -1.5), cornerSize: CGSize(width: radius + 1.5, height: radius + 1.5), style: .continuous)
    }
}

/// 教学里的胶囊按钮：上一步（灰）、下一步（墨黑）、知道了（品牌红）。
struct GuideButton: View {
    enum Style { case plain, ink, brand }
    let title: String
    let style: Style
    var height: CGFloat = 34
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: height > 40 ? 13 : 13, weight: style == .plain ? .regular : .semibold))
                .foregroundStyle(style == .plain ? Color.primary : style == .ink ? Bento.onInk : Color.white)
                .padding(.horizontal, style == .plain ? (height > 40 ? 20 : 14) : (height > 40 ? 26 : 18))
                .frame(height: height)
                .background(Capsule().fill(fill).brightness(hovering ? -0.04 : 0))
                .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
        .onHover { hovering = $0 }
    }

    private var fill: Color {
        switch style {
        case .plain: Bento.fill
        case .ink: Bento.ink
        case .brand: Color.brand
        }
    }
}
