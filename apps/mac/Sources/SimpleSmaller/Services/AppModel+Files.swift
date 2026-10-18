import AppKit
import Foundation
import Observation
import OSLog
import QuickLookThumbnailing
import SmallerCore

/// 文件进出：拖入与分类、元信息与缩略图、加密 PDF 解锁、剪贴板、移除与清空、合并页序。
extension AppModel {
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
            if !fresh.isEmpty { ws.showResult = false }
            for item in fresh { loadMeta(item) }
            if type == .pdf { rebuildPageOrder(ws) }
        }
        if byType[selectedType] == nil, let first = byType.keys.first { selectedType = first }
        showHome = false
    }

    /// 按分段名切到当前类型的某个分段（与顶部大标签切换效果相同）。
    func openSegment(_ name: String) {
        let ws = current
        switch ws.type {
        case .image:
            // 图片不分页：「压缩」= 原比例，「裁切」= 上次用过的比例。
            openImage(crop: name == "裁切")
        case .video:
            guard let s = Workspace.VideoSegment(rawValue: name) else { return }
            ws.videoSegment = s
            ws.enterVideoSegment(s)
        case .pdf:
            if let s = Workspace.PdfSegment(rawValue: name) { ws.pdfSegment = s; ws.enterPdfSegment(s) }
        }
    }

    /// 图片的「压缩 / 裁切」入口（首页标签、拖到格子上）：都进同一个图片页，只是比例不同。
    /// 「压缩」把比例设成原比例；「裁切」设成上次用过的比例（没用过是小红书 3:4）。
    /// 有对得上的常用就换成它（按设置里的顺序找第一个），没有就只改比例。
    func openImage(crop: Bool) {
        let ws = workspace(.image)
        let preset: CropPreset? = crop
            ? (CropPreset.imagePresets.first { $0.id == settings.lastImageRatio } ?? CropPreset.imagePresets[0])
            : nil
        guard ws.draft.image.crop?.id != preset?.id else { return }
        if let r = store.ordered(.image).first(where: { $0.image.crop?.id == preset?.id }) {
            ws.select(recipe: r)
        } else {
            ws.draft.image.crop = preset
            ws.draft.image.cropWidth = nil
            ws.resetCrop()
            ws.cropEditing = false
        }
    }

    /// 首页：点卡片上的小标签，或把文件放到卡片分出来的某一格，直接进这个功能（带着这个分段）。
    /// 拖进来的文件里这一类进这个功能，其余类型照旧按类型分到各自的工作区。
    func enter(_ type: MediaType, feature: String, with urls: [URL] = []) {
        selectedType = type
        openSegment(feature)
        guard !urls.isEmpty else { return }
        add(urls, preferred: type)
        selectedType = type
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
        item.thumbnail = nil
        if case .failed = item.state { item.state = .waiting }
        loadMeta(item)
        return nil
    }

    static func clearUnlocked() {
        try? FileManager.default.removeItem(at: unlockedDir)
    }

    func remove(_ ids: Set<FileItem.ID>, from ws: Workspace) {
        // 处理中不动列表：引擎按开始时的顺序回报结果。
        guard !ws.running else { return }
        // 解锁出的临时副本跟着删掉。
        for f in ws.files where ids.contains(f.id) {
            if let u = f.unlockedURL { try? FileManager.default.removeItem(at: u.deletingLastPathComponent()) }
        }
        ws.files.removeAll { ids.contains($0.id) }
        ws.selection.subtract(ids)
        if ws.type == .pdf { rebuildPageOrder(ws) }
    }

    /// 再来一批：清空这一批的文件和所有跟文件相关的状态（与逐个移除一致）。
    func clear(_ ws: Workspace) {
        guard !ws.running else { return }
        for f in ws.files { if let u = f.unlockedURL { try? FileManager.default.removeItem(at: u.deletingLastPathComponent()) } }
        ws.files.removeAll()
        ws.selection.removeAll()
        ws.resetCrop()
        ws.videoIndex = 0
        ws.trim.removeAll()
        ws.videoCrop.removeAll()
        ws.coverAt.removeAll()
        ws.pageOrder.removeAll()
        ws.pageOrderFiles.removeAll()
        ws.finishedCount = 0
        ws.lastRun = nil
        ws.job = nil
        ws.batchJobs = []
        ws.createdRoots = []
        ws.showResult = false
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

    func loadMeta(_ item: FileItem) {
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
                        item.width = h.width; item.height = h.height; item.bytes = h.bytes
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
                        item.fps = m.fps; item.codec = m.codec; item.hasAudio = m.hasAudio
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
        let request = QLThumbnailGenerator.Request(fileAt: item.previewURL, size: CGSize(width: 96, height: 96), scale: 2, representationTypes: .thumbnail)
        QLThumbnailGenerator.shared.generateBestRepresentation(for: request) { rep, _ in
            guard let image = rep?.nsImage else { return }
            Task { @MainActor in item.thumbnail = image }
        }
    }

    /// 合并的页面顺序跟着文件变：已经手排过的页保持原样，移除的文件去掉它的页，新文件的页接在后面。
    func rebuildPageOrder(_ ws: Workspace) {
        let pdfs = ws.files.filter { $0.type == .pdf }
        let urls = pdfs.map(\.url)
        let newIndex = Dictionary(urls.enumerated().map { ($1, UInt32($0)) }, uniquingKeysWith: { a, _ in a })
        var order: [PdfPageRef] = []
        var seen = Set<URL>()
        for ref in ws.pageOrder where Int(ref.file) < ws.pageOrderFiles.count {
            let url = ws.pageOrderFiles[Int(ref.file)]
            guard let i = newIndex[url], let f = pdfs.first(where: { $0.url == url }), Int(ref.page) < f.pages else { continue }
            order.append(PdfPageRef(file: i, page: ref.page))
            seen.insert(url)
        }
        // 还没排进去的文件（新加的，或刚读出页数的）整份接在后面。
        for (i, f) in pdfs.enumerated() where !seen.contains(f.url) && f.pages > 0 {
            order += (0..<f.pages).map { PdfPageRef(file: UInt32(i), page: UInt32($0)) }
        }
        ws.pageOrder = order
        ws.pageOrderFiles = urls
    }
}
