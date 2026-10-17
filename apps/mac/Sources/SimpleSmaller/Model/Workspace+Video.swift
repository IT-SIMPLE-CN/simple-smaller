import CoreGraphics
import Foundation
import SwiftUI

/// 视频剪辑与序列帧：当前这段、比例裁切框（原画面像素）、截取范围、输出尺寸。
extension Workspace {
    /// 已读出时长的视频。
    var videoItems: [FileItem] { files.filter { $0.type == .video && $0.duration > 0 && $0.width > 0 } }

    var currentVideo: FileItem? {
        let items = videoItems
        guard !items.isEmpty else { return nil }
        return items[min(max(0, videoIndex), items.count - 1)]
    }

    var videoAspect: Double? { draft.video.crop?.aspect }

    /// 按比例居中取最大的框；宽高取偶数（编码要求）。没设比例时为 nil（整幅）。
    func defaultVideoCrop(_ item: FileItem) -> CGRect? {
        guard let a = videoAspect, item.width > 0, item.height > 0 else { return nil }
        let (w, h) = (Double(item.width), Double(item.height))
        let (cw, ch) = w / h > a ? (h * a, h) : (w, w / a)
        let even = { (v: Double) in (v / 2).rounded(.down) * 2 }
        return CGRect(x: even((w - cw) / 2), y: even((h - ch) / 2), width: even(cw), height: even(ch))
    }

    /// 这一段实际用的框：拖过的 > 默认。
    func videoCropBox(_ item: FileItem) -> CGRect? {
        guard videoAspect != nil else { return nil }
        return videoCrop[item.url] ?? defaultVideoCrop(item)
    }

    func trimRange(_ item: FileItem) -> ClosedRange<Double> {
        trim[item.url] ?? 0...item.duration
    }

    /// 输出画面尺寸：裁切后按短边上限缩小（不放大），取偶数。
    func videoOutputSize(_ item: FileItem) -> CGSize {
        var size = videoCropBox(item)?.size ?? CGSize(width: item.width, height: item.height)
        if draft.video.codec == .gif {
            let w = CGFloat(draft.video.gifWidth)
            if w < size.width { size = CGSize(width: w, height: (size.height * w / size.width).rounded()) }
            return size
        }
        if let edge = draft.video.shortEdge.map(CGFloat.init), edge < min(size.width, size.height) {
            let s = edge / min(size.width, size.height)
            size = CGSize(width: size.width * s, height: size.height * s)
        }
        let even = { (v: CGFloat) in (v / 2).rounded() * 2 }
        return CGSize(width: even(size.width), height: even(size.height))
    }

    /// 换比例后各段的框回到默认。
    func resetVideoCrop() {
        videoCrop = [:]
    }
}

extension Workspace {
    /// 序列帧会生成多少张（每段按截取范围算）。
    func frameCount(_ item: FileItem) -> Int {
        let r = trimRange(item)
        let length = max(0, r.upperBound - r.lowerBound)
        let v = draft.video
        switch v.frameMode {
        case .perSecond: return max(1, Int((length * v.frameValue).rounded(.down)))
        case .every: return max(1, Int((length / max(0.01, v.frameValue)).rounded(.down)))
        case .total: return max(1, Int(v.frameValue))
        case .all: return max(1, Int((length * item.fps).rounded()))
        }
    }

    /// 第 k 张取自第几秒（与引擎按时间均匀取帧一致，用于预览）。
    func frameTime(_ item: FileItem, index k: Int) -> Double {
        let r = trimRange(item)
        let length = max(0, r.upperBound - r.lowerBound)
        let n = frameCount(item)
        let v = draft.video
        switch v.frameMode {
        case .perSecond: return r.lowerBound + Double(k) / max(0.01, v.frameValue)
        case .every: return r.lowerBound + Double(k) * v.frameValue
        case .total: return r.lowerBound + length * Double(k) / Double(max(1, n))
        case .all: return r.lowerBound + Double(k) / max(1, item.fps)
        }
    }
}
