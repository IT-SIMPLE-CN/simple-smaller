import AppKit
import Foundation
import Observation
import SmallerCore

/// 不进主窗口、直接按一个配方处理一批文件（圆环与访达「服务」共用）。
/// 图片、PDF 一个任务跑完；视频逐段跑。结果写出后可在访达中显示、拷贝、撤销。
@Observable
@MainActor
final class QuickRun {
    enum Phase: Equatable { case running(done: Int, total: Int), finished, failed(String) }

    let recipe: Recipe
    let urls: [URL]
    let type: MediaType
    private(set) var phase: Phase
    private(set) var before: UInt64 = 0
    private(set) var after: UInt64 = 0
    private(set) var outputs: [URL] = []
    private(set) var kept = 0
    private(set) var failures: [String] = []
    private var jobs: [Job] = []
    private var cancelled = false
    var onFinished: (() -> Void)?

    init(recipe: Recipe, urls: [URL], type: MediaType) {
        self.recipe = recipe
        self.urls = urls
        self.type = type
        phase = .running(done: 0, total: urls.count)
    }

    /// 「4 张图片」「teaser.mov」。
    var title: String {
        if urls.count == 1 { return urls[0].lastPathComponent }
        return "\(urls.count) \(type == .image ? "张图片" : type == .video ? "段视频" : "个 PDF")"
    }

    var isFinished: Bool { if case .running = phase { false } else { true } }

    func start() {
        let model = AppModel.shared
        let engine = model.engine
        let place = model.settings.place
        var naming = recipe.naming
        if type == .video, recipe.video.outputFrames { naming.template = "{name}" }
        if type == .pdf, recipe.pdf.longImage { naming.template = "{name}-长图" }
        let ffiNaming = naming.ffi(date: Format.today)
        let inputs = urls.map(\.path)
        before = urls.reduce(0) { $0 + Self.size(of: $1) }
        let bridge = ListenerBridge(progress: { _, _ in }, item: { [weak self] r in self?.record(r) }, finished: { [weak self] in self?.finish() })
        switch type {
        case .image:
            jobs = [engine.runImages(inputs: inputs, place: place, naming: ffiNaming, settings: recipe.imageSettings(), listener: bridge)]
        case .pdf:
            let operation: PdfOperation = recipe.pdf.longImage
                ? .toImages(width: UInt32(recipe.pdf.imageWidth), format: recipe.pdf.imageFormat.ffi, quality: UInt8(clamping: recipe.pdf.imageQuality), longImage: true)
                : .compress(level: recipe.pdfLevel, targetBytes: recipe.pdfTargetBytes, subsetFonts: recipe.pdf.subsetFonts, grayscale: recipe.pdf.grayscale)
            jobs = [engine.runPdfs(inputs: inputs, place: place, naming: ffiNaming, settings: PdfSettings(operation: operation, stripMetadata: recipe.stripMetadata), listener: bridge)]
        case .video:
            runVideos(inputs, place: place, naming: ffiNaming)
        }
    }

    /// 视频逐段：先读尺寸算比例框（居中取最大），再起任务；一段完成再起下一段。
    private func runVideos(_ inputs: [String], place: Place, naming: Naming) {
        let engine = AppModel.shared.engine
        let recipe = recipe
        Task.detached { [weak self] in
            for path in inputs {
                if await MainActor.run(body: { self?.cancelled ?? true }) { break }
                let meta = try? engine.videoMeta(path: path)
                var crop: CGRect?
                if let m = meta, let a = recipe.video.crop?.aspect {
                    let (w, h) = (Double(m.width), Double(m.height))
                    let (cw, ch) = w / h > a ? (h * a, h) : (w, w / a)
                    let even = { (v: Double) in (v / 2).rounded(.down) * 2 }
                    crop = CGRect(x: even((w - cw) / 2), y: even((h - ch) / 2), width: even(cw), height: even(ch))
                }
                let settings = recipe.videoSettings(trim: nil, crop: crop, coverAt: recipe.video.exportCover ? min(1, (meta?.duration ?? 2) / 2) : nil)
                let done = DispatchSemaphore(value: 0)
                let bridge = ListenerBridge(progress: { _, _ in }, item: { r in self?.record(r) }, finished: { done.signal() })
                let job = engine.runVideos(inputs: [path], place: place, naming: naming, settings: settings, listener: bridge)
                await MainActor.run { self?.jobs.append(job) }
                await withCheckedContinuation { c in DispatchQueue.global().async { done.wait(); c.resume() } }
            }
            await MainActor.run { self?.finish() }
        }
    }

    private func record(_ r: JobItemResult) {
        switch r.state {
        case .done:
            outputs += r.outputs.map { URL(fileURLWithPath: $0.path) }
            after += r.outputs.reduce(0) { $0 + $1.bytes }
        case .keptOriginal:
            kept += 1
            after += r.beforeBytes
        case .failed:
            failures.append(r.message ?? "失败")
        case .cancelled:
            break
        }
        if case .running(let done, let total) = phase { phase = .running(done: min(total, done + 1), total: total) }
    }

    private func finish() {
        guard !isFinished else { return }
        if outputs.isEmpty, kept == 0, let first = failures.first {
            phase = .failed(first)
        } else {
            phase = .finished
            let model = AppModel.shared
            model.recent.insert(RecentJob(title: title, recipe: recipe.name, before: before, after: after, date: Date(), outputs: outputs,
                                          thumbnail: nil), at: 0)
            model.recent = Array(model.recent.prefix(10))
            Notifier.jobFinished(title: title, after: after, outputs: outputs, elapsed: 0)
        }
        onFinished?()
    }

    func cancel() {
        cancelled = true
        jobs.forEach { $0.cancel() }
    }

    /// 撤销：新文件移到废纸篓（原文件从不改动）。
    func undo() throws {
        for job in jobs { try job.undo() }
        outputs = []
    }

    func reveal() { NSWorkspace.shared.activateFileViewerSelecting(outputs) }

    /// 拷贝新文件，可以直接粘贴进聊天窗口或访达。
    func copy() {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.writeObjects(outputs as [NSURL])
    }

    private static func size(of url: URL) -> UInt64 {
        var isDir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) else { return 0 }
        if !isDir.boolValue { return UInt64((try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
        let e = FileManager.default.enumerator(at: url, includingPropertiesForKeys: [.fileSizeKey, .isRegularFileKey], options: [.skipsHiddenFiles])
        var total: UInt64 = 0
        while let f = e?.nextObject() as? URL {
            if MediaType.of(f) != nil { total += UInt64((try? f.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0) }
        }
        return total
    }
}
