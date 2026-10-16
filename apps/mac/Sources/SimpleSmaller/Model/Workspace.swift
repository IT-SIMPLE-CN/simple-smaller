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
    var hasAlpha = false
    var graphic = false
    var duration: Double = 0
    var fps: Double = 0
    var bitrate: UInt64 = 0
    var codec = ""
    var hasAudio = false
    var hdr = false
    var pages = 0
    var metaError: String?
    var thumbnail: NSImage?

    /// 试压 / 估算的输出体积；`estimateKept` 表示会保留原文件。
    var estimate: UInt64?
    var estimateKept = false
    var estimating = false

    var state: State = .waiting
    var outputs: [Output] = []

    init(url: URL, root: URL?, type: MediaType) {
        self.url = url
        self.root = root
        self.type = type
    }

    var name: String { url.lastPathComponent }
    var afterBytes: UInt64 { outputs.reduce(0) { $0 + $1.bytes } }
    var dimensions: String { width > 0 ? "\(width) × \(height)" : "—" }
}

@Observable
@MainActor
final class Workspace {
    enum ImageSegment: String, CaseIterable { case compress = "压缩", crop = "裁切" }
    enum VideoSegment: String, CaseIterable { case compress = "压缩", edit = "剪辑", frames = "序列帧" }
    enum PdfSegment: String, CaseIterable { case compress = "压缩", merge = "合并", split = "拆分", toImages = "转图片", fromImages = "图片转 PDF" }

    let type: MediaType
    var files: [FileItem] = []
    var selection: Set<FileItem.ID> = []

    var imageSegment: ImageSegment = .compress
    var videoSegment: VideoSegment = .compress
    var pdfSegment: PdfSegment = .compress

    /// 当前选中的配方与它的可改副本；两者不同即「已修改」。
    var recipeID: String
    var base: Recipe
    var draft: Recipe

    var job: Job?
    var running = false
    var finishedCount = 0
    var lastRunRoots: [URL] = []

    // 图片裁切：每张手调过的框（原图像素坐标）、已确认的、当前第几张、没调过的框放在哪。
    var cropBoxes: [URL: CGRect] = [:]
    var cropConfirmed: Set<URL> = []
    var cropIndex = 0
    var cropFollowLast = false

    // 视频剪辑：当前这段的截取、裁切框、封面时间。
    var trim: [URL: ClosedRange<Double>] = [:]
    var videoCrop: [URL: CGRect] = [:]
    var coverAt: [URL: Double] = [:]

    // PDF 合并：页面顺序（第几个文件, 第几页）。
    var pageOrder: [PdfPageRef] = []

    init(type: MediaType, recipe: Recipe) {
        self.type = type
        recipeID = recipe.id
        base = recipe
        draft = recipe
    }

    var modified: Bool { draft != base }
    var totalBytes: UInt64 { files.reduce(0) { $0 + $1.bytes } }
    var selectedItems: [FileItem] { files.filter { selection.contains($0.id) } }

    func select(recipe: Recipe) {
        recipeID = recipe.id
        base = recipe
        draft = recipe
        if type == .image { imageSegment = recipe.image.crop == nil ? .compress : .crop }
        if type == .video {
            if recipe.video.outputFrames { videoSegment = .frames }
            else if videoSegment == .frames { videoSegment = .compress }
        }
        if type == .pdf, recipe.pdf.longImage { pdfSegment = .toImages }
    }

    /// 交给引擎的输入：拖入的文件夹整个传（引擎照搬结构），其余逐个文件。
    var engineInputs: [String] {
        var seen = Set<String>()
        var out: [String] = []
        for f in files {
            let p = (f.root ?? f.url).path
            if seen.insert(p).inserted { out.append(p) }
        }
        return out
    }

    func item(for path: String) -> FileItem? { files.first { $0.url.path == path } }
}
