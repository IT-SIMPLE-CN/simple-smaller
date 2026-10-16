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


    var state: State = .waiting
    /// 这个文件的输出目录（开始时按引擎的展开规则算好，失败重试与视频逐段用）。
    var outDir: URL?
    /// 加密 PDF 输入密码后解出的临时副本，处理时代替原文件。
    var unlockedURL: URL?
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
    /// 这一批起过的全部任务（视频逐段各一个），撤销时逐个撤。
    var batchJobs: [Job] = []
    /// 这一批新建的输出根目录（如「素材-small」）。
    var createdRoots: [String] = []
    /// PDF 输出加的打开密码。只在这次处理里用，不写进配方、不保存。
    var outputPassword = ""
    var running = false
    /// 这一批任务里引擎展开后的文件顺序（进度回调按这个序号报）。
    var planSources: [String] = []
    var finishedCount = 0
    var lastRunRoots: [URL] = []

    // 图片裁切：每张手调过的框（原图像素坐标）、已确认的、当前第几张、没调过的框放在哪。
    var cropBoxes: [URL: CGRect] = [:]
    var cropConfirmed: Set<URL> = []
    var cropIndex = 0
    var cropFollowLast = false
    /// 每张框的改动记录，⌘Z 撤回这一张用；nil 表示改之前是默认框。
    var cropHistory: [URL: [CGRect?]] = [:]

    // 视频剪辑：当前是第几段，以及每段的截取、裁切框（原画面像素）、封面时间。
    var videoIndex = 0
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

    func item(for path: String) -> FileItem? { files.first { $0.url.path == path || $0.unlockedURL?.path == path } }
}
