import AppKit

/// 访达右键「服务」→「用简单小一点处理」：按各类型当前选中的配方直接处理，指针旁给结果卡。
/// 声明在 Info.plist 的 NSServices 里。
@MainActor
final class ServiceProvider: NSObject {
    static let shared = ServiceProvider()

    static func register() {
        NSApp.servicesProvider = shared
        NSUpdateDynamicServices()
    }

    @objc func processFiles(_ pboard: NSPasteboard, userData: String?, error: AutoreleasingUnsafeMutablePointer<NSString?>) {
        let urls = (pboard.readObjects(forClasses: [NSURL.self], options: [.urlReadingFileURLsOnly: true]) as? [URL]) ?? []
        guard !urls.isEmpty else { error.pointee = "没有收到文件" as NSString; return }
        let model = AppModel.shared
        var byType: [MediaType: [URL]] = [:]
        for url in urls {
            var isDir: ObjCBool = false
            FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir)
            if let t = isDir.boolValue ? .image : MediaType.of(url) { byType[t, default: []].append(url) }
        }
        let point = NSEvent.mouseLocation
        for (type, list) in byType {
            let run = QuickRun(recipe: model.workspace(type).draft, urls: list, type: type)
            RingController.shared.track(run, at: point)
            run.start()
        }
    }
}
