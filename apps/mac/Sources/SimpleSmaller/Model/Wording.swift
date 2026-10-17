import AppKit
import SwiftUI

enum VideoCards {
    static func rateTitle(_ r: VideoBody.Rate) -> String {
        switch r {
        case .high: "高"
        case .medium: "中"
        case .low: "低"
        case .size: "按大小"
        }
    }
}

enum PdfNotes {
    /// 合并、拆分、图片转 PDF 没有配方：顶部横条写一句这个分段做什么。
    static func segmentNote(_ s: Workspace.PdfSegment) -> String {
        switch s {
        case .merge: "把几个 PDF 按页面顺序合成一个文件，可排序、删页。"
        case .split: "把一个 PDF 按每 N 页拆成多个文件。"
        case .fromImages: "把图片按顺序做成一个 PDF，一张一页。"
        default: ""
        }
    }
}

/// 质量数值对应的说法：高 / 均衡 / 偏小。
enum QualityWord {
    static func of(_ q: Int) -> String { q >= 88 ? "高" : q >= 70 ? "均衡" : "偏小" }
}
