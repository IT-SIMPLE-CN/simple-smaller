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
    var outputPasswordOn = false
    /// 第几轮处理；取消后重开，上一轮的回调据此作废。
    var runGeneration = 0
    /// 这一轮处理的文件（重试时只是失败的那几个）。
    var runItemIDs: Set<FileItem.ID> = []
    var running = false
    /// 处理完换到完成页；「改设置再做一次」回到设置页，文件还在。
    var showResult = false
    /// 这一批任务里引擎展开后的文件顺序（进度回调按这个序号报）。
    var planSources: [String] = []
    var finishedCount = 0
    var lastRunRoots: [URL] = []

    // 图片裁切：每张手调过的框（原图像素坐标）、当前第几张。动了就按新框，不用再确认。
    var cropBoxes: [URL: CGRect] = [:]
    var cropIndex = 0
    /// 每张框的改动记录，⌘Z 撤回这一张用；nil 表示改之前是默认框。
    var cropHistory: [URL: [CGRect?]] = [:]

    // 视频剪辑：当前是第几段，以及每段的截取、裁切框（原画面像素）、封面时间。
    var videoIndex = 0
    var trim: [URL: ClosedRange<Double>] = [:]
    var videoCrop: [URL: CGRect] = [:]
    var coverAt: [URL: Double] = [:]

    // PDF 合并：页面顺序（第几个文件, 第几页）。
    var pageOrder: [PdfPageRef] = []
    /// pageOrder 里 file 序号对应的文件（排页时用来认出哪页属于哪个文件）。
    var pageOrderFiles: [URL] = []

    init(type: MediaType, recipe: Recipe) {
        self.type = type
        recipeID = recipe.id
        base = recipe
        draft = recipe
    }

    /// 改过配方参数才算「已修改」；切到「序列帧」分段带出的 outputFrames 不算。
    var modified: Bool {
        var d = draft
        d.video.outputFrames = base.video.outputFrames
        return d != base
    }

    /// 分段各用各的配方（配方条只放这一段能用的）：图片「压缩 / 裁切」、视频「压缩 · 剪辑 / 序列帧」、
    /// PDF「压缩 / 转图片」。切过去时换成那一段上次用的，没用过就用它的第一个内置配方。
    private var segmentRecipe: [String: String] = [:]

    private func switchRecipe(into group: String, from other: String, fits: (Recipe) -> Bool, fallback: String) {
        guard !fits(draft) else { return }
        segmentRecipe[other] = recipeID
        let id = segmentRecipe[group] ?? fallback
        if let r = AppModel.shared.store.recipe(id: id) ?? Recipe.builtIns.first(where: { $0.id == id }) {
            select(recipe: r)
        }
    }

    func enterImageSegment(_ s: ImageSegment) {
        let crop = s == .crop
        guard (draft.image.crop != nil) != crop else { return }
        switchRecipe(into: crop ? "crop" : "compress", from: crop ? "compress" : "crop",
                     fits: { ($0.image.crop != nil) == crop }, fallback: crop ? "img.xhs" : "img.web")
        resetCrop()
    }

    func enterVideoSegment(_ s: VideoSegment) {
        let frames = s == .frames
        switchRecipe(into: frames ? "frames" : "video", from: frames ? "video" : "frames",
                     fits: { $0.video.outputFrames == frames }, fallback: frames ? "vid.frames.web" : "vid.chat")
    }

    func enterPdfSegment(_ s: PdfSegment) {
        guard [.compress, .toImages].contains(s) else { return }
        let images = s == .toImages
        switchRecipe(into: images ? "pdfImages" : "pdf", from: images ? "pdf" : "pdfImages",
                     fits: { $0.pdf.longImage == images }, fallback: images ? "pdf.long" : "pdf.email")
    }

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
        if type == .pdf {
            if recipe.pdf.longImage { pdfSegment = .toImages }
            else if pdfSegment == .toImages { pdfSegment = .compress }
        }
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

    /// PDF 合并、拆分、图片转 PDF 用不上压缩配方：边栏不显示选中、批量头不写配方名。
    /// 上一次点「开始」时的配方与分段：都没变、文件也都处理完了，再点开始只会多出一份重复的文件。
    var lastRun: (draft: Recipe, segment: String)?
    var segmentKey: String { "\(imageSegment.rawValue)|\(videoSegment.rawValue)|\(pdfSegment.rawValue)" }

    var isUpToDate: Bool {
        guard let last = lastRun, !files.isEmpty, !running else { return false }
        let finished = files.allSatisfy { [.done, .kept].contains($0.state) }
        return finished && last.draft == draft && last.segment == segmentKey
    }

    var usesRecipe: Bool { !(type == .pdf && [.merge, .split, .fromImages].contains(pdfSegment)) }

    func item(for path: String) -> FileItem? { files.first { $0.url.path == path || $0.unlockedURL?.path == path } }
}
