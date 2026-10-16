import AppKit
import CoreServices

/// 监视文件夹（设置 → 自动）：放进去的新文件按这个文件夹的配方自动处理，结果放在旁边。
/// 只看文件夹第一层；隐藏文件、之前的输出（-small、_frames 等）、不是这个配方那一类的都跳过。
/// 新文件要等大小稳定（截图、下载、录屏写完）再处理；处理完发系统通知。
@MainActor
final class FolderWatcher {
    static let shared = FolderWatcher()

    private var stream: FSEventStreamRef?
    /// 已经处理或排队过的文件（路径 + 修改时间），同一个文件不重复处理。
    private var seen: Set<String> = []
    private var pending: [URL: (size: Int, checks: Int)] = [:]
    private var settleTimer: Timer?

    func refresh() {
        stop()
        let folders = AppSettings.shared.watchFolders.filter { FileManager.default.fileExists(atPath: $0.path) }
        guard !folders.isEmpty else { return }
        // 开始监视时已经在里面的文件不处理，只处理之后放进来的。
        for f in folders { for url in Self.files(in: URL(fileURLWithPath: f.path)) { seen.insert(Self.key(url)) } }
        let paths = folders.map(\.path) as CFArray
        var context = FSEventStreamContext(version: 0, info: nil, retain: nil, release: nil, copyDescription: nil)
        let callback: FSEventStreamCallback = { _, _, count, rawPaths, _, _ in
            guard let paths = unsafeBitCast(rawPaths, to: NSArray.self) as? [String] else { return }
            let urls = paths.prefix(count).map { URL(fileURLWithPath: $0) }
            DispatchQueue.main.async { MainActor.assumeIsolated { FolderWatcher.shared.changed(urls) } }
        }
        let flags = UInt32(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagNoDefer | kFSEventStreamCreateFlagUseCFTypes)
        guard let s = FSEventStreamCreate(nil, callback, &context, paths, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.5, flags) else { return }
        FSEventStreamSetDispatchQueue(s, .main)
        FSEventStreamStart(s)
        stream = s
    }

    private func stop() {
        if let s = stream {
            FSEventStreamStop(s)
            FSEventStreamInvalidate(s)
            FSEventStreamRelease(s)
        }
        stream = nil
        settleTimer?.invalidate()
        settleTimer = nil
        pending = [:]
    }

    private func changed(_ urls: [URL]) {
        let folders = Set(AppSettings.shared.watchFolders.map { URL(fileURLWithPath: $0.path).standardizedFileURL.path })
        for url in urls {
            // 只看第一层的文件。
            guard folders.contains(url.deletingLastPathComponent().standardizedFileURL.path),
                  Self.eligible(url), !seen.contains(Self.key(url)) else { continue }
            pending[url] = (size: -1, checks: 0)
        }
        if !pending.isEmpty, settleTimer == nil {
            settleTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { _ in
                MainActor.assumeIsolated { FolderWatcher.shared.settle() }
            }
        }
    }

    var hasRunning: Bool { runs.contains { !$0.isFinished } }

    /// 这个文件夹有任务在跑：期间新出现的文件先不认（可能是自己的输出），等任务结束再判断。
    private func busy(_ folder: String) -> Bool {
        runs.contains { !$0.isFinished && $0.urls.first?.deletingLastPathComponent().standardizedFileURL.path == folder }
    }

    /// 每秒看一次大小，连续两次不变才算写完。
    private func settle() {
        var ready: [URL] = []
        for (url, state) in pending {
            if busy(url.deletingLastPathComponent().standardizedFileURL.path) { continue }
            guard let size = (try? url.resourceValues(forKeys: [.fileSizeKey]))?.fileSize else { pending[url] = nil; continue }
            if size == state.size, size > 0 { ready.append(url); pending[url] = nil }
            else if state.checks > 120 { pending[url] = nil }
            else { pending[url] = (size, state.checks + 1) }
        }
        if pending.isEmpty { settleTimer?.invalidate(); settleTimer = nil }
        // 等待期间可能已经认出是自己的输出（处理完会记进 seen）。
        ready.removeAll { seen.contains(Self.key($0)) }
        guard !ready.isEmpty else { return }
        for url in ready { seen.insert(Self.key(url)) }
        let byFolder = Dictionary(grouping: ready) { $0.deletingLastPathComponent().standardizedFileURL.path }
        for (folder, files) in byFolder {
            guard let watch = AppSettings.shared.watchFolders.first(where: { URL(fileURLWithPath: $0.path).standardizedFileURL.path == folder }),
                  let recipe = AppModel.shared.store.recipe(id: watch.recipeID) else { continue }
            let matching = files.filter { MediaType.of($0) == recipe.type }
            guard !matching.isEmpty else { continue }
            let run = QuickRun(recipe: recipe, urls: matching, type: recipe.type)
            // 每写出一个结果就记下，哪怕命名里没有 -small 也不会被当成新文件。
            run.onOutput = { url in FolderWatcher.shared.seen.insert(Self.key(url)) }
            run.onFinished = { [weak run] in
                guard let run else { return }
                // 结果写在同一个文件夹里，记下来不再当新文件处理。
                for o in run.outputs { FolderWatcher.shared.seen.insert(Self.key(o)) }
                AppModel.shared.noteFinishedInBackground(run.outputs.isEmpty ? 0 : matching.count)
                AppModel.shared.endActivityIfIdle()
                Notifier.watchFinished(folder: URL(fileURLWithPath: folder).lastPathComponent, run: run)
            }
            AppModel.shared.beginActivity()
            run.start()
            runs.append(run)
            runs.removeAll { $0.isFinished && $0 !== run }
        }
    }

    private var runs: [QuickRun] = []

    private static func key(_ url: URL) -> String {
        let m = (try? url.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate?.timeIntervalSince1970 ?? 0
        return "\(url.standardizedFileURL.path)|\(Int(m))"
    }

    /// 是图片、视频或 PDF，不是隐藏文件、不是之前的输出、不是正在下载的临时文件。
    private static func eligible(_ url: URL) -> Bool {
        let name = url.lastPathComponent
        guard !name.hasPrefix("."), MediaType.of(url) != nil else { return false }
        let stem = url.deletingPathExtension().lastPathComponent
        if ["-small", "-长图", "-cover"].contains(where: { stem.hasSuffix($0) }) { return false }
        var isDir: ObjCBool = false
        return FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir) && !isDir.boolValue
    }

    private static func files(in dir: URL) -> [URL] {
        (try? FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil, options: [.skipsHiddenFiles])) ?? []
    }
}
