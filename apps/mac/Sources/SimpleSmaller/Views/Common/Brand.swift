import AppKit
import SwiftUI

/// 随包的矢量图：只读一次，之后都用缓存。
@MainActor
enum BundledImage {
    private static var cache: [String: NSImage] = [:]

    static func svg(_ name: String, template: Bool = false) -> NSImage? {
        let key = name + (template ? "#t" : "")
        if let hit = cache[key] { return hit }
        guard let url = Bundle.main.url(forResource: name, withExtension: "svg"), let image = NSImage(contentsOf: url) else { return nil }
        image.isTemplate = template
        cache[key] = image
        return image
    }
}

/// IT-SIMPLE 的标（随包的 SVG，模板图，跟着给的颜色走，深色模式下不会是一块黑）。
/// 首页工具栏用品牌红，关于里用次要文字色。
struct BrandMark: View {
    var color: Color = Bento.secondary

    var body: some View {
        if let image = BundledImage.svg("logo-mark", template: true) {
            Image(nsImage: image).resizable().scaledToFit().foregroundStyle(color)
        } else {
            Circle().fill(color)
        }
    }
}
