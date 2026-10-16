import CoreGraphics
import Foundation

/// 图片裁切：框一律用原图像素坐标（已按 EXIF 转正），与引擎一致。
/// 没手调过的图：「居中，取最大」或「沿用上一张的位置」；自由比例默认整张。
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
        guard let a = cropAspect.map({ CGFloat($0) }) else { return CGRect(x: 0, y: 0, width: w, height: h) }
        let (cw, ch) = w / h > a ? (h * a, h) : (w, w / a)
        return CGRect(x: ((w - cw) / 2).rounded(), y: ((h - ch) / 2).rounded(), width: cw.rounded(), height: ch.rounded())
    }

    /// 第 i 张实际用的框：手调过的 > 沿用上一张 > 默认。
    func effectiveCropBox(_ index: Int) -> CGRect {
        let items = cropItems
        guard items.indices.contains(index) else { return .zero }
        if let box = cropBoxes[items[index].url] { return box }
        guard cropFollowLast else { return defaultCropBox(items[index]) }
        // 往前找最近一张手调过的，再一张张往后搬。
        var start = index
        while start > 0, cropBoxes[items[start].url] == nil { start -= 1 }
        var box = cropBoxes[items[start].url] ?? defaultCropBox(items[start])
        if start < index {
            for k in (start + 1)...index { box = Self.carry(box, from: items[k - 1], to: items[k], aspect: cropAspect) }
        }
        return box
    }

    /// 把上一张的框按相对位置搬到这一张：宽按比例换算，比例保持，收进画面。
    static func carry(_ box: CGRect, from: FileItem, to: FileItem, aspect: Double?) -> CGRect {
        let sx = CGFloat(to.width) / CGFloat(max(1, from.width))
        let sy = CGFloat(to.height) / CGFloat(max(1, from.height))
        var r = CGRect(x: box.minX * sx, y: box.minY * sy, width: box.width * sx, height: box.height * sy)
        if let a = aspect.map({ CGFloat($0) }) {
            let cx = r.midX, cy = r.midY
            r.size.height = r.width / a
            r.origin = CGPoint(x: cx - r.width / 2, y: cy - r.height / 2)
        }
        return clamp(r, width: CGFloat(to.width), height: CGFloat(to.height), aspect: aspect)
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
        cropConfirmed.remove(item.url)
    }

    /// 换比例后全部回到默认，已确认的也清掉（设计稿 03）。
    func resetCrop() {
        cropBoxes = [:]
        cropHistory = [:]
        cropConfirmed = []
        cropIndex = 0
    }

    /// 交给引擎的框：手调过的照传；「沿用上一张」时每张都传算好的框。
    var cropBoxesForEngine: [URL: CGRect] {
        guard draft.image.crop != nil else { return [:] }
        if !cropFollowLast { return cropBoxes }
        var out: [URL: CGRect] = [:]
        for (i, item) in cropItems.enumerated() { out[item.url] = effectiveCropBox(i) }
        return out
    }
}
