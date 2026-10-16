import AppKit
import Foundation
import Observation
import OSLog
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
    static let log = Logger(subsystem: "cn.it-simple.smaller", category: "job")

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

    // MARK: 加密的 PDF

    /// 解锁副本放这里；启动与退出时清空。
    static var unlockedDir: URL {
        FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("cn.it-simple.smaller/unlocked", isDirectory: true)
    }

    /// 用密码解出一份不加密的副本，处理时代替原文件；输出照样放在原文件旁边。密码不对返回提示文字。
    func unlock(_ item: FileItem, password: String) -> String? {
        let out = Self.unlockedDir.appendingPathComponent(UUID().uuidString, isDirectory: true).appendingPathComponent(item.url.lastPathComponent)
        do {
            try engine.unlockPdf(path: item.url.path, password: password, output: out.path)
        } catch {
            return (error as? EngineError)?.userMessage ?? "解不开这个文件"
        }
        item.unlockedURL = out
        item.metaError = nil
        if case .failed = item.state { item.state = .waiting }
        loadMeta(item)
        return nil
    }

    static func clearUnlocked() {
        try? FileManager.default.removeItem(at: unlockedDir)
    }

    func remove(_ ids: Set<FileItem.ID>, from ws: Workspace) {
        // 解锁出的临时副本跟着删掉。
        for f in ws.files where ids.contains(f.id) {
            if let u = f.unlockedURL { try? FileManager.default.removeItem(at: u.deletingLastPathComponent()) }
        }
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
        guard let urls = clipboardFiles() else { return }
        if urls.count == 1, urls[0].path.contains("/简单小一点/剪贴板-") { selectedType = .image }
        add(urls, preferred: urls.count == 1 && urls[0].path.contains("/简单小一点/剪贴板-") ? .image : nil)
    }

    /// 剪贴板里的文件；是图片数据时先存成「图片/简单小一点/剪贴板-时间.png」。没有可处理的返回 nil 并提示。
    func clipboardFiles() -> [URL]? {
        let pb = NSPasteboard.general
        if let urls = pb.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL], !urls.isEmpty {
            return urls
        }
        guard let image = NSImage(pasteboard: pb), let tiff = image.tiffRepresentation,
              let rep = NSBitmapImageRep(data: tiff), let png = rep.representation(using: .png, properties: [:]) else {
            flash("剪贴板里没有图片")
            return nil
        }
        let dir = FileManager.default.urls(for: .picturesDirectory, in: .userDomainMask)[0].appendingPathComponent("简单小一点", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd-HHmmss"
        let url = dir.appendingPathComponent("剪贴板-\(f.string(from: Date())).png")
        do {
            try png.write(to: url)
            return [url]
        } catch {
            flash("剪贴板图片存不下来：\(error.localizedDescription)")
            return nil
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
                // 只读文件头拿尺寸，不整张解码（30 张 6000 万像素的图同时整张解码要 6 GB 内存）。
                let header = ImageHeader.read(path)
                await MainActor.run {
                    switch header {
                    case .success(let h):
                        item.width = h.width; item.height = h.height; item.hasAlpha = h.hasAlpha; item.bytes = h.bytes
                    case .failure(let reason):
                        item.metaError = reason.message
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
                let unlocked = await MainActor.run { item.unlockedURL?.path }
                let meta = try? engine.pdfMeta(path: unlocked ?? path)
                await MainActor.run {
                    if let m = meta {
                        item.pages = Int(m.pages)
                        item.bytes = unlocked == nil ? m.bytes : ((try? FileManager.default.attributesOfItem(atPath: path)[.size] as? UInt64) ?? m.bytes)
                        item.metaError = m.needsPassword ? "有打开密码" : nil
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

    /// 开始处理这一批；`retrying` 给出时只重做这几个（失败重试），其余结果保留。
    func start(_ ws: Workspace, retrying: [FileItem]? = nil) {
        guard !ws.running, !ws.files.isEmpty else { return }
        Self.log.info("开始 \(ws.type.title, privacy: .public)：\(ws.files.count) 个文件，配方「\(ws.draft.name, privacy: .public)」")
        let items = retrying ?? ws.files
        for f in items { f.state = .waiting; f.outputs = [] }
        ws.running = true
        // 每一轮一个编号：取消后马上重开，上一轮的后台循环与回调都认得出自己已经过期。
        ws.runGeneration += 1
        let gen = ws.runGeneration
        ws.runItemIDs = Set(items.map(\.id))
        beginActivity()
        if retrying == nil {
            ws.finishedCount = 0
            ws.batchJobs = []
        } else {
            ws.finishedCount = ws.files.filter { [.done, .kept].contains($0.state) }.count
        }
        var namingSettings = ws.draft.naming
        // 序列帧按「{name}_frames/{name}_0001」命名（设计稿 04b），不套用压缩时的后缀。
        if ws.type == .video, ws.draft.video.outputFrames { namingSettings.template = "{name}" }
        // 拆分放进「原名-拆分」文件夹，里面按「原名-01」编号。
        if ws.type == .pdf, ws.pdfSegment == .split { namingSettings.template = "{name}" }
        // 转图片：逐页放进「原名-图片」，长图叫「原名-长图」。
        if ws.type == .pdf, ws.pdfSegment == .toImages { namingSettings.template = ws.draft.pdf.longImage ? "{name}-长图" : "{name}" }
        let naming = namingSettings.ffi(date: Format.today)
        let place = settings.place
        let started = Date()
        let listener = ListenerBridge(
            progress: { [weak self] index, fraction in
                guard ws.runGeneration == gen else { return }
                self?.progress(ws, index: Int(index), fraction: fraction)
            },
            item: { [weak self] r in
                guard ws.runGeneration == gen else { return }
                self?.finish(ws, result: r)
            },
            finished: { [weak self] in
                guard ws.runGeneration == gen else { return }
                self?.finished(ws, started: started)
            }
        )
        let recipe = ws.draft
        let merging = ws.type == .pdf && [.merge, .fromImages].contains(ws.pdfSegment)
        // 每个文件该去的输出目录（拖入的文件夹照样进「文件夹-small」）。重试和视频逐段都靠它。
        if retrying == nil, !merging {
            let kind: Kind = ws.type == .image ? .image : ws.type == .video ? .video : .pdf
            let targets = engine.planTargets(inputs: ws.engineInputs, kind: kind, place: place)
            let dirs = Dictionary(targets.items.map { ($0.source, URL(fileURLWithPath: $0.outDir)) }, uniquingKeysWith: { a, _ in a })
            for f in ws.files { f.outDir = dirs[f.url.path] }
            ws.createdRoots = targets.createdRoots
        }

        // 失败重试：按原本的输出目录分组，逐组跑，结果落回同一个地方。
        if let retrying, ws.type != .video, !merging {
            let groups = Dictionary(grouping: retrying) { $0.outDir?.path ?? "" }
            runGroups(ws, groups: groups.map { (paths: $0.value.map { ($0.unlockedURL ?? $0.url).path }, place: $0.key.isEmpty ? place : Place.folder(path: $0.key)) },
                      naming: naming, recipe: recipe, listener: listener)
            return
        }

        switch ws.type {
        case .image:
            ws.planSources = engine.listFiles(inputs: ws.engineInputs, kind: .image)
            ws.job = engine.runImages(inputs: ws.engineInputs, place: place, naming: naming, settings: recipe.imageSettings(cropBoxes: ws.cropBoxesForEngine), listener: listener)
            ws.batchJobs = ws.job.map { [$0] } ?? []
        case .video:
            // 剪辑里设的截取、裁切与封面按每段视频各自生效：逐段起任务，各自送到原本的输出目录。
            runVideos(ws, items: items, recipe: recipe, place: place, naming: naming, listener: listener)
        case .pdf:
            let pdfs = ws.files.filter { $0.type == .pdf }
            // 解锁过的文件用副本代替；输出照样回到原文件该去的地方。
            if !merging, pdfs.contains(where: { $0.unlockedURL != nil }) {
                let groups = Dictionary(grouping: items.filter { $0.type == .pdf }) { $0.outDir?.path ?? "" }
                runGroups(ws, groups: groups.map { (paths: $0.value.map { ($0.unlockedURL ?? $0.url).path },
                                                    place: $0.key.isEmpty ? place : Place.folder(path: $0.key)) },
                          naming: naming, recipe: recipe, listener: listener)
                return
            }
            let inputs: [String]
            switch ws.pdfSegment {
            case .fromImages: inputs = ws.files.filter { $0.type == .image }.map(\.url.path)
            // 合并按页面顺序引用「第几个 PDF」，输入必须正好是列表里的 PDF 文件。
            case .merge: inputs = pdfs.map { ($0.unlockedURL ?? $0.url).path }
            default: inputs = ws.engineInputs
            }
            ws.planSources = merging ? inputs : engine.listFiles(inputs: inputs, kind: .pdf)
            // 合并、图片转 PDF 的结果放在第一个文件的位置；第一个可能是解锁出的临时副本，按原文件的位置算。
            var target = place
            if merging, settings.outputLocation == .beside, let first = (ws.pdfSegment == .fromImages ? ws.files.first { $0.type == .image } : pdfs.first) {
                target = .folder(path: first.url.deletingLastPathComponent().path)
            }
            ws.job = engine.runPdfs(inputs: inputs, place: target, naming: naming, settings: pdfSettings(ws, recipe: recipe), listener: listener)
            ws.batchJobs = ws.job.map { [$0] } ?? []
        }
    }

    func pdfSettings(_ ws: Workspace, recipe: Recipe) -> PdfSettings {
        let operation: PdfOperation
        switch ws.pdfSegment {
        case .compress: operation = .compress(level: recipe.pdfLevel, targetBytes: recipe.pdfTargetBytes, subsetFonts: recipe.pdf.subsetFonts, grayscale: recipe.pdf.grayscale)
        case .merge: operation = .assemble(pages: ws.pageOrder)
        case .split: operation = .split(every: UInt32(max(1, recipe.pdf.splitEvery)))
        case .toImages: operation = .toImages(width: UInt32(recipe.pdf.imageWidth), format: recipe.pdf.imageFormat.ffi, quality: UInt8(clamping: recipe.pdf.imageQuality), longImage: recipe.pdf.longImage)
        case .fromImages: operation = .fromImages(pageMode: recipe.pdf.pageMode == .a4 ? .a4 : .fitImage)
        }
        // 转图片不出 PDF，不加密码。
        let pw = ws.pdfSegment == .toImages || ws.outputPassword.isEmpty ? nil : ws.outputPassword
        return PdfSettings(operation: operation, stripMetadata: recipe.stripMetadata, password: pw)
    }

    /// 失败的重试一遍（配方可以先改）。
    func retryFailed(_ ws: Workspace) {
        let failed = ws.files.filter { if case .failed = $0.state { return true }; return false }
        guard !failed.isEmpty else { return }
        // 文件可能已经换好了：重读一次尺寸与错误。
        for f in failed { f.metaError = nil; loadMeta(f) }
        start(ws, retrying: failed)
    }

    /// 按组逐个起任务（图片、PDF 的重试）；每组的进度按它自己的文件顺序对回列表。
    private func runGroups(_ ws: Workspace, groups: [(paths: [String], place: Place)], naming: Naming, recipe: Recipe, listener: ListenerBridge) {
        let gen = ws.runGeneration
        let engine = engine
        let imageSettings = recipe.imageSettings(cropBoxes: ws.cropBoxesForEngine)
        let pdf = pdfSettings(ws, recipe: recipe)
        let type = ws.type
        Task.detached {
            for group in groups {
                let done = DispatchSemaphore(value: 0)
                let bridge = ListenerBridge(
                    progress: { i, f in
                        if Int(i) < group.paths.count, let item = ws.item(for: group.paths[Int(i)]), item.state != .done { item.state = .running(f) }
                    },
                    item: { r in listener.item(r) },
                    finished: { done.signal() }
                )
                let job = type == .image
                    ? engine.runImages(inputs: group.paths, place: group.place, naming: naming, settings: imageSettings, listener: bridge)
                    : engine.runPdfs(inputs: group.paths, place: group.place, naming: naming, settings: pdf, listener: bridge)
                await MainActor.run { ws.job = job; ws.batchJobs.append(job) }
                await withCheckedContinuation { c in DispatchQueue.global().async { done.wait(); c.resume() } }
                if await MainActor.run(body: { !ws.running || ws.runGeneration != gen }) { break }
            }
            await MainActor.run { listener.finished() }
        }
    }

    private func runVideos(_ ws: Workspace, items: [FileItem], recipe: Recipe, place: Place, naming: Naming, listener: ListenerBridge) {
        let gen = ws.runGeneration
        let engine = engine
        // 视频逐段跑；每段一个任务，用同一个回调汇总。
        let jobs = items.map { item in
            // 封面没单击指定时，取截取开头往后 1 秒（片头常是黑场）。
            (path: item.url.path,
             index: ws.files.firstIndex { $0.id == item.id } ?? 0,
             place: item.outDir.map { Place.folder(path: $0.path) } ?? place,
             settings: recipe.videoSettings(trim: ws.trim[item.url], crop: ws.videoCropBox(item),
                                            coverAt: ws.coverAt[item.url] ?? (ws.trimRange(item).lowerBound + min(1, item.duration / 2))))
        }
        Task.detached {
            for j in jobs {
                let done = DispatchSemaphore(value: 0)
                let bridge = ListenerBridge(
                    progress: { _, f in listener.progress(UInt32(j.index), f) },
                    item: { r in
                        var r = r
                        r.index = UInt32(j.index)
                        listener.item(r)
                    },
                    finished: { done.signal() }
                )
                let job = engine.runVideos(inputs: [j.path], place: j.place, naming: naming, settings: j.settings, listener: bridge)
                await MainActor.run { ws.job = job; ws.batchJobs.append(job) }
                await withCheckedContinuation { c in DispatchQueue.global().async { done.wait(); c.resume() } }
                if await MainActor.run(body: { !ws.running || ws.runGeneration != gen }) { break }
            }
            await MainActor.run { listener.finished() }
        }
    }

    func cancel(_ ws: Workspace) {
        ws.job?.cancel()
        ws.batchJobs.forEach { $0.cancel() }
        ws.running = false
        endActivityIfIdle()
    }

    /// 最近记录的缩略图：用系统 Quick Look 生成（视频、PDF 也有）。
    func loadRecentThumbnail(_ id: UUID, from url: URL?) {
        guard let url else { return }
        let request = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 64, height: 64), scale: 2, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { rep, _ in
            guard let image = rep?.nsImage else { return }
            Task { @MainActor in
                if let i = self.recent.firstIndex(where: { $0.id == id }) { self.recent[i].thumbnail = image }
            }
        }
    }

    // MARK: 处理中：不睡眠、Dock 显示进度

    private var activity: NSObjectProtocol?
    /// 后台完成、还没回到应用时 Dock 上的角标数。
    private var unseen = 0

    /// 有任务在跑时，不让系统闲置睡眠（屏幕可以照常关）。
    func beginActivity() {
        if activity == nil {
            activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiated, .idleSystemSleepDisabled], reason: "正在压缩文件")
        }
        updateDock()
    }

    func endActivityIfIdle() {
        guard !isBusy, let a = activity else { updateDock(); return }
        ProcessInfo.processInfo.endActivity(a)
        activity = nil
        updateDock()
        // 大图处理完，分配器会把释放的大块内存留着备用（几百 MB）；空闲下来就还给系统。
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 1) { malloc_zone_pressure_relief(nil, 0) }
    }

    /// 主窗口任意一类在跑，或圆环 / 小窗直接处理的任务没完。
    var isBusy: Bool { MediaType.allCases.contains { workspace($0).running } || RingController.shared.hasRunning || FolderWatcher.shared.hasRunning }

    /// Dock：处理中写「3/9」；后台做完写完成个数，回到应用后清掉。
    func updateDock() {
        let running = MediaType.allCases.map(workspace).filter(\.running)
        if let ws = running.first {
            NSApp.dockTile.badgeLabel = "\(ws.finishedCount)/\(ws.files.count)"
        } else {
            NSApp.dockTile.badgeLabel = unseen > 0 ? "\(unseen)" : nil
        }
    }

    func noteFinishedInBackground(_ n: Int) {
        if !NSApp.isActive { unseen += n }
        updateDock()
    }

    func clearUnseen() {
        unseen = 0
        updateDock()
    }

    func undo(_ ws: Workspace) {
        let files = ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }
        do {
            // 视频逐段各起一个任务，撤销要把这一批的每个任务都撤掉。
            for job in ws.batchJobs.isEmpty ? (ws.job.map { [$0] } ?? []) : ws.batchJobs { try job.undo() }
            // 视频逐段按文件夹送出，新建的「文件夹-small」要另外清掉。
            if ws.type == .video { try engine.trash(paths: ws.createdRoots) }
            for f in ws.files where f.state == .done || f.state == .kept { f.state = .waiting; f.outputs = [] }
            // 回到处理前：底栏不再显示上一批的结果。
            ws.finishedCount = 0
            ws.job = nil
            ws.batchJobs = []
            ws.createdRoots = []
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
        case .pdf: return index < ws.planSources.count ? ws.item(for: ws.planSources[index]) : nil
        case .image: return index < ws.planSources.count ? ws.item(for: ws.planSources[index]) : nil
        }
    }

    private func finish(_ ws: Workspace, result r: JobItemResult) {
        let item = ws.type == .video ? indexItem(ws, Int(r.index)) : (ws.item(for: r.source) ?? indexItem(ws, Int(r.index)))
        guard let item else { return }
        item.outputs = r.outputs
        if r.state != .failed {
            Self.log.info("\(item.name, privacy: .public)：\(String(describing: r.state), privacy: .public)，\(r.outputs.count) 个输出")
        }
        switch r.state {
        case .done: item.state = .done
        case .keptOriginal: item.state = .kept
        case .failed:
            item.state = .failed(r.message ?? "失败")
            Self.log.error("处理失败 \(item.name, privacy: .public)：\(r.message ?? "", privacy: .public)")
        case .cancelled: item.state = .cancelled
        }
        // 合并、图片转 PDF 只出一个结果：其余文件也算完成。
        if ws.type == .pdf, [.merge, .fromImages].contains(ws.pdfSegment) {
            for f in ws.files where f.id != item.id { f.state = item.state }
        }
        ws.finishedCount = ws.files.filter { [.done, .kept].contains($0.state) || { if case .failed = $0.state { return true }; return false }($0) }.count
        updateDock()
    }

    private func finished(_ ws: Workspace, started: Date) {
        ws.running = false
        noteFinishedInBackground(ws.files.filter { ws.runItemIDs.contains($0.id) && $0.state == .done }.count)
        endActivityIfIdle()
        // 只统计处理成功的（失败的不算进前后体积）。
        let ok = ws.files.filter { [.done, .kept].contains($0.state) }
        let before = ok.reduce(0) { $0 + $1.bytes }
        let after = ok.reduce(UInt64(0)) { $0 + ($1.state == .done ? $1.afterBytes : $1.bytes) }
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
    var id = UUID()
    let title: String
    let recipe: String
    let before: UInt64
    let after: UInt64
    let date: Date
    let outputs: [URL]
    var thumbnail: NSImage?
}

extension EngineError {
    /// 给用户看的一句话（引擎已写成中文）。
    var userMessage: String {
        switch self {
        case .Io(let m), .Unsupported(let m), .Failed(let m): m
        }
    }
}
