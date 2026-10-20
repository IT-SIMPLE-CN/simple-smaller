import AppKit
import Foundation
import Observation
import OSLog
import QuickLookThumbnailing
import SmallerCore

/// 一批的处理：开始、按组 / 逐段跑引擎、进度与结果、完成、取消、重试、撤销。
extension AppModel {
    // MARK: 开始、取消、撤销

    /// 开始处理这一批；`retrying` 给出时只重做这几个（失败重试），其余结果保留。
    func start(_ ws: Workspace, retrying: [FileItem]? = nil) {
        guard !ws.running, !ws.files.isEmpty else { return }
        if ws.type == .pdf, ws.pdfSegment != .toImages, ws.outputPasswordOn, ws.outputPassword.trimmingCharacters(in: .whitespaces).isEmpty {
            flash("先设好打开密码，或者关掉「输出加打开密码」")
            return
        }
        ws.lastRun = (ws.draft, ws.segmentKey)
        Self.log.info("开始 \(ws.type.title, privacy: .public)：\(ws.files.count) 个文件，配方「\(ws.draft.name, privacy: .public)」")
        let items = retrying ?? ws.files
        for f in items { f.state = .waiting; f.outputs = [] }
        ws.running = true
        ws.showResult = false
        // 每一轮一个编号：取消后马上重开，上一轮的后台循环与回调都认得出自己已经过期。
        ws.runGeneration += 1
        let gen = ws.runGeneration
        ws.runItemIDs = Set(items.map(\.id))
        ws.rebuildPathIndex()
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
        // 实况：苹果实况一对同名（{name}-live.jpg / .mov），动态照片叫 {name}-live.MP.jpg。
        if ws.type == .video, ws.draft.video.outputLive { namingSettings.template = "{name}-live" }
        // 拆分放进「原名-拆分」文件夹，里面按「原名-01」编号。
        if ws.type == .pdf, ws.pdfSegment == .split { namingSettings.template = "{name}" }
        // 转图片：逐页放进「原名-图片」，长图叫「原名-长图」。
        if ws.type == .pdf, ws.pdfSegment == .toImages { namingSettings.template = ws.draft.pdf.longImage ? "{name}-长图" : "{name}" }
        let naming = namingSettings.ffi(date: Format.today)
        let place = settings.place
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
                self?.finished(ws)
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
        case .compress: operation = .compress(level: recipe.pdfLevel, targetBytes: recipe.pdfTargetBytes, subsetFonts: true, grayscale: recipe.pdf.grayscale)
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
    func runGroups(_ ws: Workspace, groups: [(paths: [String], place: Place)], naming: Naming, recipe: Recipe, listener: ListenerBridge) {
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
                await MainActor.run {
                    ws.job = job
                    ws.batchJobs.append(job)
                    // 起任务的这一会儿用户可能已经点了停止。
                    if !ws.running || ws.runGeneration != gen { job.cancel() }
                }
                await withCheckedContinuation { c in DispatchQueue.global().async { done.wait(); c.resume() } }
                if await MainActor.run(body: { !ws.running || ws.runGeneration != gen }) { break }
            }
            await MainActor.run { listener.finished() }
        }
    }

    func runVideos(_ ws: Workspace, items: [FileItem], recipe: Recipe, place: Place, naming: Naming, listener: ListenerBridge) {
        let gen = ws.runGeneration
        let engine = engine
        // 视频逐段跑；每段一个任务，用同一个回调汇总。
        let live = recipe.video.outputLive
        let jobs = items.map { item in
            // 封面没单击指定时，取截取开头往后 1 秒（片头常是黑场）。实况用自己的片段与封面。
            (path: item.url.path,
             index: ws.files.firstIndex { $0.id == item.id } ?? 0,
             place: item.outDir.map { Place.folder(path: $0.path) } ?? place,
             settings: live
                ? recipe.videoSettings(trim: ws.liveWindow(item), crop: ws.videoCropBox(item), coverAt: ws.liveCoverTime(item))
                : recipe.videoSettings(trim: ws.trim[item.url], crop: ws.videoCropBox(item),
                                            coverAt: ws.coverAt[item.url] ?? (ws.trimRange(item).lowerBound + min(1, item.duration / 2)),
                                            long4K: min(item.width, item.height) >= 2000 && (ws.trimRange(item).upperBound - ws.trimRange(item).lowerBound) > 180))
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
                await MainActor.run {
                    ws.job = job
                    ws.batchJobs.append(job)
                    // 起任务的这一会儿用户可能已经点了停止。
                    if !ws.running || ws.runGeneration != gen { job.cancel() }
                }
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
        // 换一个编号：引擎之后迟到的回调（含「完成」）都作废，停掉的一批不写进最近记录。
        ws.runGeneration += 1
        for f in ws.files { if case .running = f.state { f.state = .cancelled } }
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

    func undo(_ ws: Workspace) {
        let files = ws.files.flatMap { $0.outputs.map { URL(fileURLWithPath: $0.path) } }
        do {
            // 视频逐段各起一个任务，撤销要把这一批的每个任务都撤掉。
            for job in ws.batchJobs.isEmpty ? (ws.job.map { [$0] } ?? []) : ws.batchJobs { try job.undo() }
            // 视频逐段按文件夹送出，新建的「文件夹-small」要另外清掉。
            if ws.type == .video { try engine.trash(paths: ws.createdRoots) }
            for f in ws.files where f.state == .done || f.state == .kept { f.state = .waiting; f.outputs = [] }
            // 回到处理前：不再显示上一批的结果。
            ws.finishedCount = 0
            ws.job = nil
            ws.batchJobs = []
            ws.createdRoots = []
            ws.showResult = false
            flash("已撤销，\(files.count) 个新文件移到了废纸篓")
        } catch {
            flash("撤销失败：\(error.localizedDescription)")
        }
    }

    func progress(_ ws: Workspace, index: Int, fraction: Double) {
        guard index < ws.files.count else { return }
        let item = indexItem(ws, index)
        if case .done = item?.state { return }
        item?.state = .running(fraction)
    }

    /// 引擎按输入展开后的顺序回报；图片与 PDF 用源路径对回列表。
    func indexItem(_ ws: Workspace, _ index: Int) -> FileItem? {
        switch ws.type {
        case .video: return index < ws.files.count ? ws.files[index] : nil
        case .pdf: return index < ws.planSources.count ? ws.item(for: ws.planSources[index]) : nil
        case .image: return index < ws.planSources.count ? ws.item(for: ws.planSources[index]) : nil
        }
    }

    func finish(_ ws: Workspace, result r: JobItemResult) {
        let item = ws.type == .video ? indexItem(ws, Int(r.index)) : (ws.item(for: r.source) ?? indexItem(ws, Int(r.index)))
        guard let item else { return }
        let wasFinished = item.state.isFinished
        item.outputs = r.outputs
        if r.state != .failed {
            Self.log.info("\(item.name, privacy: .private)：\(String(describing: r.state), privacy: .public)，\(r.outputs.count) 个输出")
        }
        switch r.state {
        case .done: item.state = .done
        case .keptOriginal: item.state = .kept
        case .failed:
            item.state = .failed(r.message ?? "失败")
            Self.log.error("处理失败 \(item.name, privacy: .private)：\(r.message ?? "", privacy: .public)")
        case .cancelled: item.state = .cancelled
        }
        // 合并、图片转 PDF 只出一个结果：其余文件也算完成。
        if ws.type == .pdf, [.merge, .fromImages].contains(ws.pdfSegment) {
            for f in ws.files where f.id != item.id { f.state = item.state }
            ws.finishedCount = ws.files.filter(\.state.isFinished).count
        } else if !wasFinished, item.state.isFinished {
            // 逐个累加，不每次全表重数（几百个文件时不再是 n²）。
            ws.finishedCount += 1
        }
        updateDock()
    }

    func finished(_ ws: Workspace) {
        ws.running = false
        // 有结果（完成、保留原图或失败）就换到完成页；全部停掉的不换。
        if ws.files.contains(where: { $0.state != .cancelled && $0.state != .waiting }) { ws.showResult = true }
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
        Notifier.jobFinished(title: title, after: after, outputs: outputs)
    }
}
