import AppKit
import SwiftUI

/// 教学里的小插图：图形照搬设计稿的 SVG（系统直接画），文字用 SwiftUI 叠上去（系统的 SVG 不画 `<text>`）。
/// SVG 里的颜色写成色板记号（{card} {ink} {fill} …），按浅色 / 深色换成对应的值，深色下不会是一块白。
struct Sketch: View {
    let width: CGFloat
    let height: CGFloat
    let svg: String
    var texts: [SketchText] = []
    /// 显示宽度（按比例缩放）；nil 为原尺寸。
    var fitWidth: CGFloat? = nil
    @Environment(\.colorScheme) private var scheme

    var body: some View {
        let scale = (fitWidth ?? width) / width
        ZStack(alignment: .topLeading) {
            Image(nsImage: Self.image(svg, width: width, height: height, dark: scheme == .dark))
                .resizable()
                .frame(width: width, height: height)
            ForEach(Array(texts.enumerated()), id: \.offset) { _, t in
                Text(t.text)
                    .font(.system(size: t.size, weight: t.weight))
                    .foregroundStyle(t.color)
                    .fixedSize()
                    .frame(width: 400, height: 40, alignment: t.anchor.alignment)
                    .position(x: t.x + t.anchor.shift, y: t.y)
            }
        }
        .frame(width: width, height: height, alignment: .topLeading)
        .scaleEffect(scale, anchor: .topLeading)
        .frame(width: width * scale, height: height * scale, alignment: .topLeading)
        .accessibilityHidden(true)
    }

    // MARK: 色板记号

    private static let tokens: [(String, UInt32, UInt32)] = [
        ("{card}", 0xFFFFFF, 0x3A3B40),
        ("{ink}", 0x111214, 0xF2F3F5),
        ("{onInk}", 0xFFFFFF, 0x111214),
        ("{fill}", 0xF2F3F5, 0x303135),
        ("{panel}", 0xE4E6E9, 0x2C2D31),
        ("{line}", 0xD9DCE0, 0x4A4D52),
        ("{key}", 0xC9CCD1, 0x1C1D20),
        ("{thumb}", 0xC9CCD1, 0x4A4D52),
        ("{edge}", 0xE2E4E8, 0x45474C),
        ("{alertFill}", 0xFDE8E4, 0x3D201C),
        ("{alertText}", 0xA8240F, 0xFF9B8A),
        ("{brand}", 0xEF3922, 0xEF3922),
    ]

    @MainActor private static var cache: [String: NSImage] = [:]

    @MainActor static func image(_ body: String, width: CGFloat, height: CGFloat, dark: Bool) -> NSImage {
        let key = "\(dark)|\(width)x\(height)|\(body)"
        if let hit = cache[key] { return hit }
        var s = body
        for (token, light, darkHex) in tokens {
            s = s.replacingOccurrences(of: token, with: String(format: "#%06X", dark ? darkHex : light))
        }
        let svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="\(Int(width))" height="\(Int(height))" viewBox="0 0 \(Int(width)) \(Int(height))">\(s)</svg>
        """
        let image = NSImage(data: Data(svg.utf8)) ?? NSImage(size: NSSize(width: width, height: height))
        cache[key] = image
        return image
    }
}

/// 插图上的一行字：坐标照 SVG（x 按对齐方式、y 是文字竖直中线）。
struct SketchText {
    enum Anchor {
        case start, middle, end
        var alignment: Alignment {
            switch self {
            case .start: .leading
            case .middle: .center
            case .end: .trailing
            }
        }
        /// 400 宽的框放到哪，让文字的起点 / 中点 / 终点落在 x 上。
        var shift: CGFloat {
            switch self {
            case .start: 200
            case .middle: 0
            case .end: -200
            }
        }
    }

    let x: CGFloat
    let y: CGFloat
    let text: String
    var size: CGFloat = 11
    var weight: Font.Weight = .regular
    var color: Color = .primary
    var anchor: Anchor = .middle

    init(_ x: CGFloat, _ y: CGFloat, _ text: String, size: CGFloat = 11, weight: Font.Weight = .regular, color: Color = .primary, anchor: Anchor = .middle) {
        self.x = x; self.y = y; self.text = text; self.size = size; self.weight = weight; self.color = color; self.anchor = anchor
    }
}
