import AppKit
import SwiftUI

/// 线性图标：直接用设计稿里的 SVG 路径（24×24 画布、圆头圆角描边），做成模板图，颜色跟着前景色走。
/// 系统 SF Symbols 里没有对得上的（合并、拆分、序列帧等）就用这里，和设计稿一笔不差。
struct LineIcon: View {
    let glyph: Glyph
    var size: CGFloat = 14
    var weight: CGFloat = 2

    var body: some View {
        Image(nsImage: LineIcon.image(glyph.svg, weight: weight))
            .resizable()
            .frame(width: size, height: size)
            .accessibilityHidden(true)
    }

    /// 设计稿里的图标（路径照搬）。
    enum Glyph {
        case compress, crop, scissors, frames, merge, split, toImages, fromImages
        case image, video, pdf, ring, arrowUpRight, plus, grip
        /// 设置 › 常用左栏的三类图标（与卡片角标略有不同，照稿）。
        case kindImage, kindVideo, kindPdf

        var svg: String {
            switch self {
            case .compress: #"<path d="M4 14h6v6M20 10h-6V4M14 10l7-7M3 21l7-7"/>"#
            case .crop: #"<path d="M6 2v14a2 2 0 0 0 2 2h14"/><path d="M18 22V8a2 2 0 0 0-2-2H2"/>"#
            case .scissors: #"<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M20 4L8.1 15.9M14.5 14.5L20 20M8.1 8.1L12 12"/>"#
            case .frames: #"<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M9 5v14M15 5v14"/>"#
            case .merge: #"<path d="M6 4v5a6 6 0 0 0 6 6 6 6 0 0 0 6-6V4M12 15v5"/>"#
            case .split: #"<path d="M12 4v5M6 20v-5a6 6 0 0 1 6-6 6 6 0 0 1 6 6v5"/>"#
            case .toImages, .image: #"<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 16l5-5 4 4 3-3 6 6"/>"#
            case .fromImages: #"<path d="M7 3h7l5 5v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M14 3v5h5"/>"#
            case .video: #"<rect x="3" y="6" width="13" height="12" rx="2"/><path d="M16 10l5-3v10l-5-3"/>"#
            case .pdf: #"<path d="M6 3h8l4 4v14H6z"/><path d="M14 3v4h4"/>"#
            case .ring: #"<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3"/><path d="M12 4v5M20 12h-5"/>"#
            case .arrowUpRight: #"<path d="M7 17L17 7M8 7h9v9"/>"#
            case .plus: #"<path d="M12 5v14M5 12h14"/>"#
            case .grip: #"<path d="M9 6h.01M15 6h.01M9 12h.01M15 12h.01M9 18h.01M15 18h.01"/>"#
            case .kindImage: #"<rect x="3" y="5" width="18" height="14" rx="3"/><path d="M3 16l5-5 4 4 3-3 6 6"/><circle cx="16" cy="9" r="1.5"/>"#
            case .kindVideo: #"<rect x="3" y="5" width="18" height="14" rx="3"/><path d="M10 9.5v5l4.5-2.5z"/>"#
            case .kindPdf: #"<path d="M7 3h7l5 5v11a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M14 3v5h5M9 13h6M9 17h4"/>"#
            }
        }
    }

    @MainActor private static var cache: [String: NSImage] = [:]

    @MainActor static func image(_ body: String, weight: CGFloat) -> NSImage {
        let key = "\(weight)|\(body)"
        if let hit = cache[key] { return hit }
        let svg = """
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#000" \
        stroke-width="\(weight)" stroke-linecap="round" stroke-linejoin="round">\(body)</svg>
        """
        let image = NSImage(data: Data(svg.utf8)) ?? NSImage(size: NSSize(width: 24, height: 24))
        image.isTemplate = true
        cache[key] = image
        return image
    }
}

extension MediaType {
    /// 设置 › 常用左栏用的图标。
    var kindGlyph: LineIcon.Glyph {
        switch self {
        case .image: .kindImage
        case .video: .kindVideo
        case .pdf: .kindPdf
        }
    }
}
