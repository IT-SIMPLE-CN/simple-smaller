import SwiftUI
import UniformTypeIdentifiers

/// 官网标（随包的 SVG）。
struct BrandMark: View {
    var body: some View {
        if let url = Bundle.main.url(forResource: "logo-mark", withExtension: "svg"), let image = NSImage(contentsOf: url) {
            // 模板图：跟随文字颜色，深色模式下不会是一块黑。
            Image(nsImage: { image.isTemplate = true; return image }()).resizable().scaledToFit().foregroundStyle(.secondary)
        } else {
            Image(systemName: "circle.hexagongrid").resizable().scaledToFit().foregroundStyle(.secondary)
        }
    }
}

/// 应用的红色标（首页、关于用）。
struct BrandLogo: View {
    var body: some View {
        if let url = Bundle.main.url(forResource: "logo-mark", withExtension: "svg"), let image = NSImage(contentsOf: url) {
            Image(nsImage: { image.isTemplate = true; return image }()).resizable().scaledToFit().foregroundStyle(Color.brand)
        } else {
            Circle().fill(Color.brand)
        }
    }
}
