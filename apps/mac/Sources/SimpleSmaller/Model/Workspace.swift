import AppKit
import Foundation
import Observation
import SmallerCore

@Observable
@MainActor
final class Workspace {
    enum VideoSegment: String, CaseIterable { case compress = "压缩", edit = "剪辑", frames = "序列帧" }
    enum PdfSegment: String, CaseIterable { case compress = "压缩", merge = "合并", split = "拆分", toImages = "转图片", fromImages = "图片转 PDF" }

    let type: MediaType
    var files: [FileItem] = []
    var selection: Set<FileItem.ID> = []

    /// 图片不分「压缩 / 裁切」：比例是配方里的一个参数，选「原比例」就是只压缩。
    /// 选了比例时，左大卡默认是缩略图（每张画出裁切框）；点一张进大图调框，这时为 true。
    var cropEditing = false
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
        // 换了比例（或换成不裁的配方）：每张的框回到居中取最大，回到缩略图。
        if type == .image, recipe.image.crop != draft.image.crop {
            resetCrop()
            cropEditing = false
        }
        recipeID = recipe.id
        base = recipe
        draft = recipe
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

    /// 上一次点「开始」时的配方与分段：都没变、文件也都处理完了，再点开始只会多出一份重复的文件。
    var lastRun: (draft: Recipe, segment: String)?
    var segmentKey: String { "\(videoSegment.rawValue)|\(pdfSegment.rawValue)" }

    /// 图片这一批要裁（配方里选了比例）。
    var isCropping: Bool { type == .image && draft.image.crop != nil }

    var isUpToDate: Bool {
        guard let last = lastRun, !files.isEmpty, !running else { return false }
        let finished = files.allSatisfy { [.done, .kept].contains($0.state) }
        return finished && last.draft == draft && last.segment == segmentKey
    }

    /// PDF 合并、拆分、图片转 PDF 用不上配方：不显示配方条，换成一句说明。
    var usesRecipe: Bool { !(type == .pdf && [.merge, .split, .fromImages].contains(pdfSegment)) }

    /// 路径 → 文件（含解锁副本的路径），开始处理时建好，进度与结果回调按路径找文件不用逐个比。
    var pathIndex: [String: FileItem] = [:]

    func rebuildPathIndex() {
        var index: [String: FileItem] = [:]
        for f in files {
            index[f.url.path] = f
            if let u = f.unlockedURL { index[u.path] = f }
        }
        pathIndex = index
    }

    func item(for path: String) -> FileItem? {
        pathIndex[path] ?? files.first { $0.url.path == path || $0.unlockedURL?.path == path }
    }
}
