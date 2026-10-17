import CoreGraphics
import Foundation

/// 图片裁切：框一律用原图像素坐标（已按 EXIF 转正），与引擎一致。
/// 没手调过的图居中、按比例取最大；自由比例默认整张。
extension Workspace {
    var cropPreset: CropPreset? { draft.image.crop }
    var cropAspect: Double? { draft.image.crop?.aspect }
    var cropOutputWidth: Int { draft.image.cropWidth ?? draft.image.crop?.width ?? 1920 }

    /// 能裁的图：已读出尺寸的图片。
    var cropItems: [FileItem] { files.filter { $0.type == .image && $0.width > 0 && $0.height > 0 } }

    var cropCurrentIndex: Int { min(max(0, cropIndex), max(0, cropItems.count - 1)) }

    /// 「小红书 3:4」；自由比例写「自由」。
    var cropTitle: String {
        guard let p = cropPreset else { return "不裁切" }
        return p.ratioLabel.isEmpty ? p.name : "\(p.name) \(p.ratioLabel)"
    }

    func defaultCropBox(_ item: FileItem) -> CGRect {
        let w = CGFloat(item.width), h = CGFloat(item.height)
        guard let a = cropAspect else { return CGRect(x: 0, y: 0, width: w, height: h) }
        return .centeredMax(width: Double(w), height: Double(h), aspect: a, round: { $0.rounded() })
    }

    /// 第 i 张实际用的框：手调过的，否则居中取最大。
    func effectiveCropBox(_ index: Int) -> CGRect {
        let items = cropItems
        guard items.indices.contains(index) else { return .zero }
        return cropBoxes[items[index].url] ?? defaultCropBox(items[index])
    }

    /// 收进画面：先缩到放得下（保持比例），再把位置推回画面里，坐标取整。
    static func clamp(_ rect: CGRect, width: CGFloat, height: CGFloat, aspect: Double?) -> CGRect {
        var r = rect
        if let a = aspect.map({ CGFloat($0) }) {
            if r.width > width { r.size = CGSize(width: width, height: width / a) }
            if r.height > height { r.size = CGSize(width: height * a, height: height) }
        } else {
            r.size = CGSize(width: min(r.width, width), height: min(r.height, height))
        }
        r.origin.x = min(max(0, r.minX), width - r.width)
        r.origin.y = min(max(0, r.minY), height - r.height)
        return CGRect(x: r.minX.rounded(), y: r.minY.rounded(), width: max(1, r.width.rounded()), height: max(1, r.height.rounded()))
    }

    /// ⌘Z：撤回这一张的上一次改动；没有记录时回到默认。
    func undoCropBox(for item: FileItem) {
        if var stack = cropHistory[item.url], let last = stack.popLast() {
            cropHistory[item.url] = stack
            cropBoxes[item.url] = last
        } else {
            cropBoxes[item.url] = nil
        }
    }

    /// 换比例后全部回到默认（设计稿 03）。
    func resetCrop() {
        cropBoxes = [:]
        cropHistory = [:]
        cropIndex = 0
    }

    /// 手调过框的张数（标题栏、开始块写）。
    var cropAdjustedCount: Int { cropItems.filter { cropBoxes[$0.url] != nil }.count }

    /// 交给引擎的框：只传手调过的，其余由引擎按居中取最大处理。
    var cropBoxesForEngine: [URL: CGRect] {
        draft.image.crop == nil ? [:] : cropBoxes
    }
}

extension CGRect {
    /// 画面里按比例取最大、居中的框（图片裁切、视频取景的默认框都用它）。
    /// `round` 决定坐标怎么取整：图片四舍五入，视频取偶数（编码器要求）。
    static func centeredMax(width w: Double, height h: Double, aspect a: Double, round: (Double) -> Double) -> CGRect {
        let (cw, ch) = w / h > a ? (h * a, h) : (w, w / a)
        return CGRect(x: round((w - cw) / 2), y: round((h - ch) / 2), width: round(cw), height: round(ch))
    }

    /// 视频用：向下取到偶数。
    static let even: (Double) -> Double = { ($0 / 2).rounded(.down) * 2 }
}
