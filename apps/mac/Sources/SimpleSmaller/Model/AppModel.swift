import AppKit
import Foundation
import Observation
import QuickLookThumbnailing
import SmallerCore

/// 引擎回调可能来自任意线程，统一转到主线程。
final class ListenerBridge: JobListener, @unchecked Sendable {
    let progress: @MainActor (UInt32, Double) -> Void
    let item: @MainActor (JobItemResult) -> Void
    let finished: @MainActor () -> Void

    init(progress: @escaping @MainActor (UInt32, Double) -> Void, item: @escaping @MainActor (JobItemResult) -> Void, finished: @escaping @MainActor () -> Void) {
        self.progress = progress
        self.item = item
        self.finished = finished
    }

    func onProgress(index: UInt32, fraction: Double) { Task { @MainActor in self.progress(index, fraction) } }
    func onItem(result: JobItemResult) { Task { @MainActor in self.item(result) } }
    func onFinished() { Task { @MainActor in self.finished() } }
}

@Observable
@MainActor
final class AppModel {
    static let shared = AppModel()

    let engine = Engine(decoder: ImageIODecoder())
    let store = RecipeStore()
    let settings = AppSettings.shared

    var selectedType: MediaType = .image
    private(set) var workspaces: [MediaType: Workspace] = [:]
    var toast: String?
    /// 最近完成的任务（菜单栏小窗）。
    var recent: [RecentJob] = []


    init() {
        for type in MediaType.allCases {
            let first = store.builtIns(for: type).first!
            workspaces[type] = Workspace(type: type, recipe: first)
        }
    }

    var current: Workspace { workspaces[selectedType]! }
    func workspace(_ type: MediaType) -> Workspace { workspaces[type]! }

    // MARK: 添加文件

    /// 拖入、选取、打开都走这里。文件夹按当前类型展开；文件按自身类型分到对应工作区。
    func add(_ urls: [URL], preferred: MediaType? = nil) {
        var byType: [MediaType: [FileItem]] = [:]
        for url in urls {
            var isDir: ObjCBool = false
            guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) else { continue }
            if isDir.boolValue {
                var type = preferred ?? selectedType
                var files = engine.listFiles(inputs: [url.path], kind: type.kind)
                if files.isEmpty {
                    // 当前类型里没有，就找文件夹里最多的那类。
                    let counts = MediaType.allCases.map { ($0, engine.listFiles(inputs: [url.path], kind: $0.kind)) }
                    if let best = counts.max(by: { $0.1.count < $1.1.count }), !best.1.isEmpty {
                        type = best.0
                        files = best.1
                    }
                }
                byType[type, default: []] += files.map { FileItem(url: URL(fileURLWithPath: $0), root: url, type: type) }
            } else if let type = MediaType.of(url) {
                // 「图片转 PDF」时，图片进 PDF 工作区。
                if type == .image, selectedType == .pdf, current.pdfSegment == .fromImages {
                    byType[.pdf, default: []].append(FileItem(url: url, root: nil, type: .image))
                } else {
                    byType[type, default: []].append(FileItem(url: url, root: nil, type: type))
                }
            }
        }
        guard !byType.isEmpty else {
            flash("没有可以处理的图片、视频或 PDF")
            return
        }
        for (type, items) in byType {
            let ws = workspace(type)
            let existing = Set(ws.files.map(\.url))
            let fresh = items.filter { !existing.contains($0.url) }
            ws.files += fresh
            for item in fresh { loadMeta(item) }
            if type == .pdf { rebuildPageOrder(ws) }
        }
        if byType[selectedType] == nil, let first = byType.keys.first { selectedType = first }
    }

    /// 按分段名切到当前类型的某个分段（与工具栏分段切换效果相同）。
    func openSegment(_ name: String) {
        let ws = current
        switch ws.type {
        case .image:
            guard let s = Workspace.ImageSegment(rawValue: name) else { return }
            if s == .crop, ws.draft.image.crop == nil { ws.draft.image.crop = CropPreset.imagePresets[0] }
            ws.imageSegment = s
        case .video:
            guard let s = Workspace.VideoSegment(rawValue: name) else { return }
            ws.draft.video.outputFrames = (s == .frames)
            ws.videoSegment = s
        case .pdf:
            if let s = Workspace.PdfSegment(rawValue: name) { ws.pdfSegment = s }
        }
    }

    func remove(_ ids: Set<FileItem.ID>, from ws: Workspace) {
        ws.files.removeAll { ids.contains($0.id) }
        ws.selection.subtract(ids)
        if ws.type == .pdf { rebuildPageOrder(ws) }
    }

    func clear(_ ws: Workspace) {
        ws.files.removeAll()
        ws.selection.removeAll()
        ws.cropBoxes.removeAll()
        ws.cropConfirmed.removeAll()
        ws.cropIndex = 0
        ws.pageOrder.removeAll()
        ws.finishedCount = 0
    }

    /// 剪贴板里的图片存成临时 PNG 再处理；剪贴板里是文件就直接加。
    func pasteFromClipboard() {
        let pb = NSPasteboard.general
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            add(urls)
            return
        }
        guard let image = NSImage(pasteboard: pb), let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff), let png = rep.representation(using: .png, properties: [:]) else {
            flash("剪贴板里没有图片")
            return
        }
        let dir = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask)[0].appendingPathComponent("简单小一点", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd-HHmmss"
        let url = dir.appendingPathComponent("剪贴板-\(f.string(from: Date())).png")
        do {
            try png.write(to: url)
            selectedType = .image
            add([url], preferred: .image)
        } catch {
            flash("剪贴板图片存不下来：\(error.localizedDescription)")
        }
    }

    // MARK: 元信息与缩略图

    private func loadMeta(_ item: FileItem) {
        let engine = engine
        let path = item.url.path
        let type = item.type
        Task.detached(priority: .userInitiated) {
            switch type {
            case .image:
                let meta = try? engine.imageMeta(path: path)
                await MainActor.run {
                    if let meta {
                        item.width = Int(meta.width); item.height = Int(meta.height)
                        item.hasAlpha = meta.hasAlpha; item.graphic = meta.graphic; item.bytes = meta.bytes
                    } else {
                        item.metaError = "读不出这张图"
                        item.bytes = (try? FileManager.default.attributesOfItem(atPath: path)[.size] as? UInt64) ?? 0
                    }
                }
            case .video:
                let meta = try? engine.videoMeta(path: path)
                await MainActor.run {
                    if let m = meta {
                        item.width = Int(m.width); item.height = Int(m.height); item.duration = m.duration
                        item.fps = m.fps; item.bitrate = m.bitrate; item.codec = m.codec; item.hasAudio = m.hasAudio
                        item.hdr = m.hdr; item.bytes = m.bytes
                    } else {
                        item.metaError = "读不出这段视频"
                    }
                }
            case .pdf:
                let meta = try? engine.pdfMeta(path: path)
                await MainActor.run {
                    if let m = meta {
                        item.pages = Int(m.pages); item.bytes = m.bytes
                        if m.needsPassword { item.metaError = "有打开密码" }
                    } else {
                        item.metaError = "读不出这个 PDF"
                    }
                    if let ws = self.workspaces[.pdf] { self.rebuildPageOrder(ws) }
                }
            }
        }
        let request = QLThumbnailGenerator.Request(fileAt: item.url, size: CGSize(width: 96, height: 96), scale: 2, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { rep, _ in
            guard let image = rep?.nsImage else { return }
            Task { @MainActor in item.thumbnail = image }
        }
    }

    func rebuildPageOrder(_ ws: Workspace) {
        let pdfs = ws.files.filter { $0.type == .pdf }
        ws.pageOrder = pdfs.enumerated().flatMap { (i, f) in (0..<max(0, f.pages)).map { PdfPageRef(file: UInt32(i), page: UInt32($0)) } }
    }

    // MARK: 开始、取消、撤销

    func start(_ ws: Workspace) {
        guard !ws.running, !ws.files.isEmpty else { return }
        for f in ws.files { f.state = .waiting; f.outputs = [] }
        ws.running = true
        ws.finishedCount = 0
        ws.batchJobs = []
        var namingSettings = ws.draft.naming
        // 序列帧按「{name}_frames/{name}_0001」命名（设计稿 04b），不套用压缩时的后缀。
        if ws.type == .video, ws.draft.video.outputFrames { namingSettings.template = "{name}" }
        let naming = namingSettings.ffi(date: Format.today)
        let place = settings.place
        let started = Date()
        let listener = ListenerBridge(
            progress: { [weak self] index, fraction in self?.progress(ws, index: Int(index), fraction: fraction) },
            item: { [weak self] r in self?.finish(ws, result: r) },
            finished: { [weak self] in self?.finished(ws, started: started) }
        )
        let recipe = ws.draft
        switch ws.type {
        case .image:
            ws.planSources = engine.listFiles(inputs: ws.engineInputs, kind: .image)
            ws.job = engine.runImages(inputs: ws.engineInputs, place: place, naming: naming, settings: recipe.imageSettings(cropBoxes: ws.cropBoxesForEngine), listener: listener)
            ws.batchJobs = ws.job.map { [$0] } ?? []
        case .video:
            // 剪辑里设的截取、裁切与封面按每段视频各自生效：逐段起任务。
            runVideos(ws, recipe: recipe, place: place, naming: naming, listener: listener)
        case .pdf:
            let operation: PdfOperation
            switch ws.pdfSegment {
            case .compress: operation = .compress(level: recipe.pdfLevel, targetBytes: recipe.pdfTargetBytes, subsetFonts: recipe.pdf.subsetFonts, grayscale: recipe.pdf.grayscale)
            case .merge: operation = .assemble(pages: ws.pageOrder)
            case .split: operation = .split(every: UInt32(max(1, recipe.pdf.splitEvery)))
            case .toImages: operation = .toImages(width: UInt32(recipe.pdf.imageWidth), format: recipe.pdf.imageFormat.ffi, quality: UInt8(clamping: recipe.pdf.imageQuality), longImage: recipe.pdf.longImage)
            case .fromImages: operation = .fromImages(pageMode: recipe.pdf.pageMode == .a4 ? .a4 : .fitImage)
            }
            let inputs = ws.pdfSegment == .fromImages ? ws.files.filter { $0.type == .image }.map(\.url.path) : ws.files.filter { $0.type == .pdf }.map(\.url.path)
            ws.job = engine.runPdfs(inputs: inputs, place: place, naming: naming, settings: PdfSettings(operation: operation, stripMetadata: recipe.stripMetadata), listener: listener)
            ws.batchJobs = ws.job.map { [$0] } ?? []
        }
    }

    private func runVideos(_ ws: Workspace, recipe: Recipe, place: Place, naming: Naming, listener: ListenerBridge) {
        let items = ws.files
        let engine = engine
        // 视频逐段跑；每段一个任务，用同一个回调汇总。
        let settingsList = items.map { item in
            // 封面没单击指定时，取截取开头往后 1 秒（片头常是黑场）。
            recipe.videoSettings(trim: ws.trim[item.url], crop: ws.videoCropBox(item),
                                 coverAt: ws.coverAt[item.url] ?? (ws.trimRange(item).lowerBound + min(1, item.duration / 2)))
        }
        let inputs = items.map(\.url.path)
        Task.detached {
            for (k, (path, settings)) in zip(inputs, settingsList).enumerated() {
                let done = DispatchSemaphore(value: 0)
                let bridge = ListenerBridge(
                    progress: { _, f in listener.progress(UInt32(k), f) },
                    item: { r in
                        var r = r
                        r.index = UInt32(k)
                        listener.item(r)
                    },
                    finished: { done.signal() }
                )
                let job = engine.runVideos(inputs: [path], place: place, naming: naming, settings: settings, listener: bridge)
                await MainActor.run { ws.job = job; ws.batchJobs.append(job) }
                await withCheckedContinuation { c in DispatchQueue.global().async { done.wait(); c.resume() } }
                if await MainActor.run(body: { ws.running == false }) { break }
            }
            await MainActor.run { listener.finished() }
        }
    }

    /// 配方带画面比例、但这段没手动框时：居中取最大。
    func cancel(_ ws: Workspace) {
        ws.job?.cancel()
        ws.running = false
    }

    func undo(_ ws: Workspace) {
        let files = ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }
        do {
            // 视频逐段各起一个任务，撤销要把这一批的每个任务都撤掉。
            for job in ws.batchJobs.isEmpty ? (ws.job.map { [$0] } ?? []) : ws.batchJobs { try job.undo() }
            for f in ws.files where f.state == .done || f.state == .kept { f.state = .waiting; f.outputs = [] }
            // 回到处理前：底栏不再显示上一批的结果。
            ws.finishedCount = 0
            ws.job = nil
            ws.batchJobs = []
            flash("已撤销，\(files.count) 个新文件移到了废纸篓")
        } catch {
            flash("撤销失败：\(error.localizedDescription)")
        }
    }

    private func progress(_ ws: Workspace, index: Int, fraction: Double) {
        guard index < ws.files.count else { return }
        let item = indexItem(ws, index)
        if case .done = item?.state { return }
        item?.state = .running(fraction)
    }

    /// 引擎按输入展开后的顺序回报；图片与 PDF 用源路径对回列表。
    private func indexItem(_ ws: Workspace, _ index: Int) -> FileItem? {
        switch ws.type {
        case .video: return index < ws.files.count ? ws.files[index] : nil
        case .pdf: return index < ws.files.count ? ws.files[index] : nil
        case .image: return index < ws.planSources.count ? ws.item(for: ws.planSources[index]) : nil
        }
    }

    private func finish(_ ws: Workspace, result r: JobItemResult) {
        let item = ws.type == .video ? indexItem(ws, Int(r.index)) : (ws.item(for: r.source) ?? indexItem(ws, Int(r.index)))
        guard let item else { return }
        item.outputs = r.outputs
        switch r.state {
        case .done: item.state = .done
        case .keptOriginal: item.state = .kept
        case .failed: item.state = .failed(r.message ?? "失败")
        case .cancelled: item.state = .cancelled
        }
        // 合并、图片转 PDF 只出一个结果：其余文件也算完成。
        if ws.type == .pdf, [.merge, .fromImages].contains(ws.pdfSegment) {
            for f in ws.files where f.id != item.id { f.state = item.state }
        }
        ws.finishedCount = ws.files.filter { [.done, .kept].contains($0.state) || { if case .failed = $0.state { return true }; return false }($0) }.count
    }

    private func finished(_ ws: Workspace, started: Date) {
        ws.running = false
        let before = ws.files.reduce(0) { $0 + $1.bytes }
        let after = ws.files.reduce(UInt64(0)) { $0 + ($1.state == .done ? $1.afterBytes : ($1.state == .kept ? $1.bytes : 0)) }
        let title = ws.files.count == 1 ? ws.files[0].name : "\(ws.files.count) \(ws.type == .image ? "张图片" : ws.type == .video ? "段视频" : "个文件")"
        let outputs = ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }
        recent.insert(RecentJob(title: title, recipe: ws.draft.name, before: before, after: after, date: Date(), outputs: outputs, thumbnail: ws.files.first?.thumbnail), at: 0)
        recent = Array(recent.prefix(10))
        Notifier.jobFinished(title: title, after: after, outputs: outputs, elapsed: Date().timeIntervalSince(started))
    }

    func reveal(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        NSWorkspace.shared.activateFileViewerSelecting(urls)
    }

    func flash(_ text: String) {
        toast = text
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(3))
            if self.toast == text { self.toast = nil }
        }
    }
}

struct RecentJob: Identifiable {
    let id = UUID()
    let title: String
    let recipe: String
    let before: UInt64
    let after: UInt64
    let date: Date
    let outputs: [URL]
    let thumbnail: NSImage?
}
