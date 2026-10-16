import Foundation
import ImageIO
import CoreGraphics
import SmallerCore

/// 引擎不认识的格式（HEIC 等）交给系统 ImageIO 解码：按 EXIF 方向转正、转到 sRGB，输出 RGBA8（非预乘）。
final class ImageIODecoder: PlatformDecoder, @unchecked Sendable {
    func decode(path: String) -> RawImage? {
        let url = URL(fileURLWithPath: path) as CFURL
        guard let source = CGImageSourceCreateWithURL(url, nil),
              // 文件被截断时系统照样能「解」出一张缺了下半截（或全黑）的图，必须拒绝。
              CGImageSourceGetStatus(source) == .statusComplete,
              CGImageSourceGetStatusAtIndex(source, 0) == .statusComplete,
              let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              let w = props[kCGImagePropertyPixelWidth] as? Int,
              let h = props[kCGImagePropertyPixelHeight] as? Int else { return nil }
        // 用缩略图接口取全尺寸图，同时让系统按 EXIF 方向转正。
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: max(w, h),
            // 不让系统缓存解码结果：画进缓冲区后就不需要了。
            kCGImageSourceShouldCache: false,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        let width = image.width, height = image.height
        let hasAlpha: Bool = {
            switch image.alphaInfo {
            case .none, .noneSkipFirst, .noneSkipLast: return false
            default: return true
            }
        }()
        // 直接画进最终交给引擎的缓冲区，不再多拷一份整图。
        guard let srgb = CGColorSpace(name: CGColorSpace.sRGB) else { return nil }
        var rgba = Data(count: width * height * 4)
        let drawn: Bool = rgba.withUnsafeMutableBytes { raw in
            guard let context = CGContext(data: raw.baseAddress, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4,
                                          space: srgb, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return false }
            context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
            return true
        }
        guard drawn else { return nil }
        if hasAlpha {
            // CoreGraphics 只给预乘的 RGBA，还原成引擎要的直通透明。
            rgba.withUnsafeMutableBytes { raw in
                let p = raw.bindMemory(to: UInt8.self)
                for i in stride(from: 0, to: p.count, by: 4) {
                    let a = UInt32(p[i + 3])
                    if a > 0 && a < 255 {
                        for c in 0..<3 { p[i + c] = UInt8(min(255, (UInt32(p[i + c]) * 255 + a / 2) / a)) }
                    }
                }
            }
        }
        return RawImage(width: UInt32(width), height: UInt32(height), rgba: rgba, hasAlpha: hasAlpha)
    }
}
