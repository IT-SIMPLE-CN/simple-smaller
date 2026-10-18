import AppKit
import QuickLook
import SwiftUI

/// 「已修改」+「存为新配方」。
struct SaveRecipeAccessory: View {
    let ws: Workspace
    @Binding var saving: Recipe?

    var body: some View {
        if ws.modified, ws.usesRecipe {
            HStack(spacing: 6) {
                Text("已修改")
                    .font(.system(size: 11))
                    .foregroundStyle(Bento.alertText)
                    .padding(.horizontal, 10)
                    .frame(height: 26)
                    .background(Capsule().fill(Bento.alertFill))
                CapsuleButton(title: "存为常用") {
                    var r = ws.draft
                    r.id = ""
                    r.name = ws.base.builtIn ? "\(ws.base.name) 改" : ws.base.name
                    r.builtIn = false
                    saving = r
                }
            }
            .transition(.opacity.combined(with: .scale(scale: 0.9)))
        }
    }
}

/// 粗轨道品牌红滑杆（设计稿：8 px 轨道、24 px 白色圆钮）。键盘方向键可调。
struct BrandSlider: View {
    @Binding var value: Double
    let range: ClosedRange<Double>
    var step: Double = 1

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let t = (value - range.lowerBound) / (range.upperBound - range.lowerBound)
            ZStack(alignment: .leading) {
                Capsule().fill(Bento.line).frame(height: 8)
                Capsule().fill(Color.brand).frame(width: max(8, w * t), height: 8)
                Circle().fill(.white)
                    .frame(width: 24, height: 24)
                    .shadow(color: .black.opacity(0.18), radius: 3, y: 2)
                    .overlay(Circle().strokeBorder(.black.opacity(0.08), lineWidth: 0.5))
                    .offset(x: (w - 24) * t)
            }
            .frame(maxHeight: .infinity)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { g in
                let x = min(max(0, g.location.x - 12), w - 24) / max(1, w - 24)
                let v = range.lowerBound + x * (range.upperBound - range.lowerBound)
                value = (v / step).rounded() * step
            })
        }
        .focusable()
        .onKeyPress(.leftArrow) { value = max(range.lowerBound, value - step); return .handled }
        .onKeyPress(.rightArrow) { value = min(range.upperBound, value + step); return .handled }
        // 旁白按系统滑杆读：有数值，上下轻扫可调。
        .accessibilityRepresentation {
            Slider(value: $value, in: range, step: step)
        }
    }
}

// MARK: - 尺寸

// 视频（设计稿 V2.1 · 06 / 07）：压缩、剪辑共用「画面与画质 · 格式 · 声音与开始」；序列帧是「抽帧 · 每一帧 · 输出与开始」。

/// 一组小标题 + 胶囊。
struct LabeledPills<Value: Hashable>: View {
    let title: String
    @Binding var selection: Value
    let options: [(Value, String)]
    var trailing: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title).font(.system(size: 11)).foregroundStyle(Bento.secondary)
                Spacer()
                if let trailing { Text(trailing).font(.system(size: 11)).foregroundStyle(Bento.secondary).monospacedDigit() }
            }
            PillChoice(selection: $selection, options: options, height: 32, label: title)
        }
    }
}

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

/// 滑块分段：选中的底块在选项之间滑过去（弹簧），顶栏分段、卡片里的小分段、图标分段共用这一个。
/// 「减弱动态效果」时只做短淡入。旁白把整组读成一个分组，选中项带「已选」。
struct SlidingSegments<Value: Hashable, Item: View>: View {
    @Binding var selection: Value
    let values: [Value]
    var inset: CGFloat = 3
    var track: Color = Bento.fill
    var thumb: Color = Bento.card
    var thumbShadow = true
    var label = ""
    var titles: (Value) -> String
    @ViewBuilder var item: (Value, Bool) -> Item
    @State private var frames: [Int: CGRect] = [:]
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        HStack(spacing: 2) {
            ForEach(Array(values.enumerated()), id: \.offset) { i, v in
                let on = v == selection
                Button { selection = v } label: { item(v, on).contentShape(Capsule()) }
                    .buttonStyle(BentoPressStyle())
                    .background(GeometryReader { g in
                        Color.clear.preference(key: SegmentFrames.self, value: [i: g.frame(in: .named("segments"))])
                    })
                    .accessibilityLabel(titles(v))
                    .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
            }
        }
        .padding(inset)
        .coordinateSpace(name: "segments")
        .onPreferenceChange(SegmentFrames.self) { frames = $0 }
        .background(alignment: .topLeading) {
            if let i = values.firstIndex(of: selection), let f = frames[i] {
                Capsule()
                    .fill(thumb)
                    .shadow(color: .black.opacity(thumbShadow ? 0.08 : 0), radius: 1, y: 1)
                    .frame(width: f.width, height: f.height)
                    .offset(x: f.minX, y: f.minY)
                    .animation(reduceMotion ? .easeOut(duration: 0.12) : .spring(response: 0.35, dampingFraction: 0.82), value: selection)
                    .animation(nil, value: frames)
            }
        }
        .background(Capsule().fill(track))
        .fixedSize()
        .accessibilityElement(children: .contain)
        .accessibilityLabel(label)
    }
}

private struct SegmentFrames: PreferenceKey {
    static let defaultValue: [Int: CGRect] = [:]
    static func reduce(value: inout [Int: CGRect], nextValue: () -> [Int: CGRect]) {
        value.merge(nextValue()) { $1 }
    }
}

/// 小号分段（灰槽里白色滑块）：卡片头右侧用，如「原尺寸 / 自定义」「宽 / 高 / 长边」。
struct MiniSegmented<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    var label = ""

    var body: some View {
        SlidingSegments(selection: $selection, values: options.map(\.0), label: label,
                        titles: { v in options.first { $0.0 == v }?.1 ?? "" }) { v, on in
            Text(options.first { $0.0 == v }?.1 ?? "")
                .font(.system(size: 12, weight: on ? .semibold : .regular))
                .foregroundStyle(on ? Color.primary : Bento.secondary)
                .padding(.horizontal, 12)
                .frame(height: 28)
                .animation(.easeOut(duration: 0.2), value: on)
        }
    }
}

/// 图标小分段（缩略图 / 列表）。
struct IconSegmented<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String, String)]
    var label = ""

    var body: some View {
        SlidingSegments(selection: $selection, values: options.map(\.0), label: label,
                        titles: { v in options.first { $0.0 == v }?.2 ?? "" }) { v, on in
            Image(systemName: options.first { $0.0 == v }?.1 ?? "")
                .font(.system(size: 13, weight: .medium))
                .foregroundStyle(on ? Color.primary : Bento.secondary)
                .frame(width: 32, height: 28)
                .help(options.first { $0.0 == v }?.2 ?? "")
        }
    }
}

/// 顶栏居中的分段（白底胶囊，选中墨黑）：压缩 / 裁切 等，设置的通用 / 配方 / 自动。
struct TopTabs<Value: Hashable>: View {
    @Binding var selection: Value
    let options: [(Value, String)]
    var label = ""

    var body: some View {
        SlidingSegments(selection: $selection, values: options.map(\.0), inset: 4, track: Bento.card, thumb: Bento.ink, thumbShadow: false, label: label,
                        titles: { v in options.first { $0.0 == v }?.1 ?? "" }) { v, on in
            Text(options.first { $0.0 == v }?.1 ?? "")
                .font(.system(size: 13, weight: on ? .semibold : .regular))
                .foregroundStyle(on ? Bento.onInk : Color.primary.opacity(0.8))
                .padding(.horizontal, 20)
                .frame(height: 30)
                .animation(.easeOut(duration: 0.2), value: on)
        }
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
