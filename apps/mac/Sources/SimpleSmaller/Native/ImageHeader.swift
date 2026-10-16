import Foundation
import ImageIO

/// 只读图片文件头：显示尺寸（按 EXIF 方向转正）、是否带透明。不解码像素，几毫秒一张。
/// 顺带看一眼文件尾：PNG 没有结束块、JPEG 没有结束标记，就是没下载完。
struct ImageHeader {
    let width: Int
    let height: Int
    let hasAlpha: Bool
    let bytes: UInt64

    struct Problem: Error { let message: String }

    static func read(_ path: String) -> Result<ImageHeader, Problem> {
        let url = URL(fileURLWithPath: path)
        let bytes = UInt64((try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0)
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let props = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any],
              var w = props[kCGImagePropertyPixelWidth] as? Int,
              var h = props[kCGImagePropertyPixelHeight] as? Int else {
            return .failure(Problem(message: "读不出这张图"))
        }
        if let problem = tailProblem(url, type: CGImageSourceGetType(source) as String?) {
            return .failure(Problem(message: problem))
        }
        // EXIF 方向 5–8 是转了 90°，宽高对调。
        if let o = props[kCGImagePropertyOrientation] as? Int, (5...8).contains(o) { swap(&w, &h) }
        let alpha = props[kCGImagePropertyHasAlpha] as? Bool ?? false
        return .success(ImageHeader(width: w, height: h, hasAlpha: alpha, bytes: bytes))
    }

    private static func tailProblem(_ url: URL, type: String?) -> String? {
        guard let type, type == "public.png" || type == "public.jpeg",
              let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        let size = (try? handle.seekToEnd()) ?? 0
        let n = min(size, 4096)
        try? handle.seek(toOffset: size - n)
        let tail = handle.readData(ofLength: Int(n))
        let complete: Bool
        if type == "public.png" {
            complete = tail.range(of: Data("IEND".utf8)) != nil
        } else {
            complete = tail.range(of: Data([0xFF, 0xD9])) != nil
        }
        return complete ? nil : "文件不完整，可能没下载完"
    }
}
