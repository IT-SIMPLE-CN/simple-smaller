import SwiftUI
import UniformTypeIdentifiers

extension Workspace {
    /// 现在能不能开始（PDF 加密码没填、合并没有页面时不能）。
    var canStart: Bool { blockReason == nil }

    var blockReason: String? {
        if type == .pdf, pdfSegment != .toImages, outputPasswordOn, outputPassword.trimmingCharacters(in: .whitespaces).isEmpty {
            return "先填打开密码"
        }
        switch type {
        case .pdf where pdfSegment == .fromImages:
            return files.contains { $0.type == .image } ? nil : "先把图片拖进来"
        case .pdf where pdfSegment == .merge:
            return files.contains { $0.type == .pdf } && !pageOrder.isEmpty ? nil : "先把 PDF 拖进来"
        default:
            return nil
        }
    }

    /// 「开始」下面写这次会做什么，只写确定的信息，不写预估体积。
    var startSummary: String {
        let n = files.count
        switch type {
        case .image:
            if isCropping {
                let items = cropItems
                return "\(items.count) 张 · \(cropTitle) · 宽 \(cropOutputWidth)"
            }
            let b = draft.image
            var parts = ["\(n) 个文件", b.format.title]
            parts.append(b.size.map { "\(b.sizeAxis.title) \($0)" } ?? "原尺寸")
            return parts.joined(separator: " · ")
        case .video:
            if videoSegment == .frames { return "生成 \(videoItems.reduce(0) { $0 + frameCount($1) }) 张" }
            if videoSegment == .live {
                let kind = switch draft.video.liveKind { case .apple: "苹果实况"; case .android: "安卓动态照片"; case .both: "两种都要" }
                return "\(n) 个视频 · \(kind)"
            }
            let v = draft.video
            if v.muteOnly { return "\(n) 个视频 · 只去声音" }
            if v.codec == .gif { return "\(n) 个视频 · GIF" }
            return "\(n) 个视频 · \(v.shortEdge.map { "\($0)p" } ?? "原分辨率")"
        case .pdf:
            switch pdfSegment {
            case .compress: return "\(n) 个文件 · \(base.name)"
            case .merge: return "合并成 1 个 · \(pageOrder.count) 页"
            case .split: return "\(n) 个文件 · 每 \(draft.pdf.splitEvery) 页一份"
            case .toImages: return draft.pdf.longImage ? "\(n) 个文件 · 拼成长图" : "\(n) 个文件 · 每页一张"
            case .fromImages: return "\(files.filter { $0.type == .image }.count) 张图 · 做成 1 个 PDF"
            }
        }
    }
}

// MARK: - 配方条
