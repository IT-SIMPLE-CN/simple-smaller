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
        return .centeredMax(width: Double(item.width), height: Double(item.height), aspect: a, round: CGRect.even)
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

/// 实况：每段取一小段（不超过 3 秒）做成会动的部分，再选一帧当封面（照片）。
extension Workspace {
    static let liveMaxSeconds = 3.0

    /// 这一段用的片段：拖过的 > 默认（从开头取最多 3 秒）。
    func liveWindow(_ item: FileItem) -> ClosedRange<Double> {
        if let r = liveRange[item.url] { return r }
        return 0...min(Self.liveMaxSeconds, max(0, item.duration))
    }

    /// 封面：选过的（落在片段里）> 片段正中。
    func liveCoverTime(_ item: FileItem) -> Double {
        let w = liveWindow(item)
        guard let t = liveCover[item.url] else { return (w.lowerBound + w.upperBound) / 2 }
        return min(max(t, w.lowerBound), w.upperBound)
    }

    /// 片段整体挪到从 `start` 开始（长度不变，不出界）；封面跟着挪。
    func moveLiveWindow(_ item: FileItem, to start: Double) {
        let w = liveWindow(item)
        let length = w.upperBound - w.lowerBound
        let s = min(max(0, start), max(0, item.duration - length))
        let shift = s - w.lowerBound
        liveRange[item.url] = s...(s + length)
        if let c = liveCover[item.url] { liveCover[item.url] = c + shift }
    }

    /// 实况视频的画面尺寸：裁切后按最长边缩小（不放大），取偶数。
    func liveOutputSize(_ item: FileItem) -> CGSize {
        var size = videoCropBox(item)?.size ?? CGSize(width: item.width, height: item.height)
        let edge = CGFloat(draft.video.liveLongEdge)
        if edge > 0, edge < max(size.width, size.height) {
            let s = edge / max(size.width, size.height)
            size = CGSize(width: size.width * s, height: size.height * s)
        }
        let even = { (v: CGFloat) in max(2, (v / 2).rounded() * 2) }
        return CGSize(width: even(size.width), height: even(size.height))
    }
}
