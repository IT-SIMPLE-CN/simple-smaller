import AppKit
import Foundation
import Observation
import SmallerCore
import UserNotifications

/// 设置窗口里的选项，存在 UserDefaults。
@Observable
@MainActor
final class AppSettings {
    static let shared = AppSettings()
    private let d = UserDefaults.standard

    enum OutputLocation: String, CaseIterable { case beside, folder }

    var outputLocation: OutputLocation { didSet { d.set(outputLocation.rawValue, forKey: "outputLocation") } }
    var outputFolder: String { didSet { d.set(outputFolder, forKey: "outputFolder") } }
    var ringEnabled: Bool { didSet { d.set(ringEnabled, forKey: "ringEnabled"); Self.later { RingController.shared.refresh() } } }
    var menuBarIcon: Bool { didSet { d.set(menuBarIcon, forKey: "menuBarIcon") } }
    /// 复制图片时自动换成压缩版（默认关）。
    var clipboardAuto: Bool { didSet { d.set(clipboardAuto, forKey: "clipboardAuto"); Self.later { ClipboardWatcher.shared.refresh() } } }
    /// 剪贴板图片的长边上限；0 为不缩小。
    var clipboardMaxEdge: Int { didSet { d.set(clipboardMaxEdge, forKey: "clipboardMaxEdge") } }
    /// 监视的文件夹与各自的配方。
    var watchFolders: [WatchFolder] {
        didSet {
            d.set(try? JSONEncoder().encode(watchFolders), forKey: "watchFolders")
            Self.later { FolderWatcher.shared.refresh() }
        }
    }

    /// 设置值改完之后再刷新监视器：在 didSet 里马上回头读同一个值会触发「同时访问」崩溃。
    private static func later(_ work: @escaping @MainActor () -> Void) {
        DispatchQueue.main.async { MainActor.assumeIsolated(work) }
    }

    private init() {
        d.register(defaults: [
            "outputLocation": OutputLocation.beside.rawValue,
            "outputFolder": "",
            "ringEnabled": true,
            "menuBarIcon": true,
            "clipboardAuto": false,
            "clipboardMaxEdge": 0,
        ])
        outputLocation = OutputLocation(rawValue: d.string(forKey: "outputLocation") ?? "") ?? .beside
        outputFolder = d.string(forKey: "outputFolder") ?? ""
        ringEnabled = d.bool(forKey: "ringEnabled")
        menuBarIcon = d.bool(forKey: "menuBarIcon")
        clipboardAuto = d.bool(forKey: "clipboardAuto")
        clipboardMaxEdge = d.integer(forKey: "clipboardMaxEdge")
        watchFolders = d.data(forKey: "watchFolders").flatMap { try? JSONDecoder().decode([WatchFolder].self, from: $0) } ?? []
    }

    var place: Place {
        if outputLocation == .folder, !outputFolder.isEmpty { return .folder(path: outputFolder) }
        return .besideSource(folderSuffix: "-small")
    }

}

struct WatchFolder: Codable, Hashable, Identifiable {
    var id = UUID()
    var path: String
    var recipeID: String
}

/// 后台任务完成且应用不在前台时发系统通知，点按在访达中显示。
enum Notifier {
    private final class Delegate: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {
        func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
            let paths = response.notification.request.content.userInfo["paths"] as? [String] ?? []
            await MainActor.run { NSWorkspace.shared.activateFileViewerSelecting(paths.map { URL(fileURLWithPath: $0) }) }
        }
    }

    private static let delegate = Delegate()

    static func setup() {
        // 只有装成 .app 才能用通知中心。
        guard Bundle.main.bundleIdentifier != nil else { return }
        let center = UNUserNotificationCenter.current()
        center.delegate = delegate
        center.requestAuthorization(options: [.alert, .sound]) { _, _ in }
    }

    /// 监视文件夹处理完：不管应用在不在前台都发，因为用户通常不看本应用。
    @MainActor
    static func watchFinished(folder: String, run: QuickRun) {
        guard Bundle.main.bundleIdentifier != nil, !run.outputs.isEmpty else { return }
        let content = UNMutableNotificationContent()
        content.title = "简单小一点 · \(folder)"
        content.body = "\(run.title) 已按「\(run.recipe.name)」处理，\(Format.size(run.before)) → \(Format.size(run.after))"
        content.subtitle = "点按在访达中显示"
        content.userInfo = ["paths": run.outputs.map(\.path)]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }

    @MainActor
    static func jobFinished(title: String, after: UInt64, outputs: [URL]) {
        guard Bundle.main.bundleIdentifier != nil, !NSApp.isActive, !outputs.isEmpty else { return }
        let content = UNMutableNotificationContent()
        content.title = "简单小一点"
        content.body = "\(title) 已完成，\(Format.size(after))"
        content.subtitle = "点按在访达中显示"
        content.userInfo = ["paths": outputs.map(\.path)]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }
}
