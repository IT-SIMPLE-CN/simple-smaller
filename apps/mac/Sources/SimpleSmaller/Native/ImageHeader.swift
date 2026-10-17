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
        guard let type, type == "public.png" || type == "public.jpeg" else { return nil }
        if type == "public.png" {
            // PNG 以 IEND 块结尾，看文件尾就够。
            guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
            defer { try? handle.close() }
            let size = (try? handle.seekToEnd()) ?? 0
            let n = min(size, 4096)
            try? handle.seek(toOffset: size - n)
            return handle.readData(ofLength: Int(n)).range(of: Data("IEND".utf8)) == nil ? "文件不完整" : nil
        }
        // JPEG：在第一个扫描段之后找结束标记；不能只看文件尾，动态照片在后面接了视频。
        guard let data = try? Data(contentsOf: url, options: .mappedIfSafe) else { return nil }
        return jpegHasEnd(data) ? nil : "文件不完整"
    }

    /// 与引擎 `jpeg_has_end` 同一套规则。
    static func jpegHasEnd(_ d: Data) -> Bool {
        let b = [UInt8](d)
        var i = 2
        while i + 4 <= b.count {
            guard b[i] == 0xFF else { return false }
            let m = b[i + 1]
            if m == 0xFF { i += 1; continue }
            if m == 0xD8 || (0xD0...0xD7).contains(m) { i += 2; continue }
            let len = Int(b[i + 2]) << 8 | Int(b[i + 3])
            if m == 0xDA {
                var j = min(b.count, i + 2 + len)
                while j + 1 < b.count {
                    if b[j] == 0xFF && b[j + 1] == 0xD9 { return true }
                    j += 1
                }
                return false
            }
            i += 2 + len
        }
        return false
    }
}
