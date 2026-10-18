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
    /// 图片上次用过的裁切比例（CropPreset 的 id）。首页「裁切」标签、拖到「裁切」格时用它，没用过是小红书 3:4。
    var lastImageRatio: String { didSet { d.set(lastImageRatio, forKey: "lastImageRatio") } }
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
            "lastImageRatio": "xhs",
        ])
        outputLocation = OutputLocation(rawValue: d.string(forKey: "outputLocation") ?? "") ?? .beside
        outputFolder = d.string(forKey: "outputFolder") ?? ""
        ringEnabled = d.bool(forKey: "ringEnabled")
        menuBarIcon = d.bool(forKey: "menuBarIcon")
        clipboardAuto = d.bool(forKey: "clipboardAuto")
        clipboardMaxEdge = d.integer(forKey: "clipboardMaxEdge")
        lastImageRatio = d.string(forKey: "lastImageRatio") ?? "xhs"
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
