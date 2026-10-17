import AppKit
import Foundation
import Observation
import SmallerCore

/// 列表里的一个文件。
@Observable
@MainActor
final class FileItem: Identifiable {
    enum State: Equatable {
        case waiting
        case running(Double)
        case done
        case kept
        case failed(String)
        case cancelled
    }

    let id = UUID()
    let url: URL
    /// 从哪个拖入的文件夹来的（交给引擎时传文件夹本身，输出照搬目录结构）。
    let root: URL?
    let type: MediaType

    var bytes: UInt64 = 0
    var width = 0
    var height = 0
    var duration: Double = 0
    var fps: Double = 0
    var codec = ""
    var hasAudio = false
    var hdr = false
    var pages = 0
    var metaError: String?
    var thumbnail: NSImage?


    var state: State = .waiting
    /// 这个文件的输出目录（开始时按引擎的展开规则算好，失败重试与视频逐段用）。
    var outDir: URL?
    /// 加密 PDF 输入密码后解出的临时副本，处理时代替原文件。
    var unlockedURL: URL?
    /// 预览用的文件：解锁过的 PDF 用副本（加密的原文件渲染不出来）。
    var previewURL: URL { unlockedURL ?? url }
    var outputs: [Output] = []

    init(url: URL, root: URL?, type: MediaType) {
        self.url = url
        self.root = root
        self.type = type
    }

    var name: String { url.lastPathComponent }
    /// 「保留原图」/「保留原文件」：压完没变小、原样保留时的说法，各处统一。
    var keptText: String { type == .image ? "保留原图" : "保留原文件" }
    var afterBytes: UInt64 { outputs.reduce(0) { $0 + $1.bytes } }
    var dimensions: String { width > 0 ? "\(width) × \(height)" : "—" }
}

extension FileItem {
    /// 处理完用第一个输出，没处理用原文件：快速查看、拖出都用它。
    var primaryURL: URL { outputs.first.map { URL(fileURLWithPath: $0.path) } ?? url }
    /// 在访达中显示：全部输出；还没处理就是原文件。
    var revealURLs: [URL] { outputs.isEmpty ? [url] : outputs.map { URL(fileURLWithPath: $0.path) } }

    /// 拖出：传文件地址（不是文件内容），拖到访达、微信里保留原文件名。
    func dragProvider() -> NSItemProvider {
        let url = primaryURL
        let p = NSItemProvider(object: url as NSURL)
        p.suggestedName = url.lastPathComponent
        return p
    }
}
