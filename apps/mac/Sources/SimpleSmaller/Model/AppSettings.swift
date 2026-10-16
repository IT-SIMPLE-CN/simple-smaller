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
    var defaultTemplate: String { didSet { d.set(defaultTemplate, forKey: "defaultTemplate") } }
    var stripMetadata: Bool { didSet { d.set(stripMetadata, forKey: "stripMetadata") } }
    var autoUpdate: Bool { didSet { d.set(autoUpdate, forKey: "autoUpdate") } }
    var hardwareVideo: Bool { didSet { d.set(hardwareVideo, forKey: "hardwareVideo") } }
    var ringEnabled: Bool { didSet { d.set(ringEnabled, forKey: "ringEnabled"); RingController.shared.refresh() } }
    var menuBarIcon: Bool { didSet { d.set(menuBarIcon, forKey: "menuBarIcon") } }
    var quickAction: Bool { didSet { d.set(quickAction, forKey: "quickAction") } }

    private init() {
        d.register(defaults: [
            "outputLocation": OutputLocation.beside.rawValue,
            "outputFolder": "",
            "defaultTemplate": "{name}-small",
            "stripMetadata": true,
            "autoUpdate": true,
            "hardwareVideo": false,
            "ringEnabled": true,
            "menuBarIcon": true,
            "quickAction": true,
        ])
        outputLocation = OutputLocation(rawValue: d.string(forKey: "outputLocation") ?? "") ?? .beside
        outputFolder = d.string(forKey: "outputFolder") ?? ""
        defaultTemplate = d.string(forKey: "defaultTemplate") ?? "{name}-small"
        stripMetadata = d.bool(forKey: "stripMetadata")
        autoUpdate = d.bool(forKey: "autoUpdate")
        hardwareVideo = d.bool(forKey: "hardwareVideo")
        ringEnabled = d.bool(forKey: "ringEnabled")
        menuBarIcon = d.bool(forKey: "menuBarIcon")
        quickAction = d.bool(forKey: "quickAction")
    }

    var place: Place {
        if outputLocation == .folder, !outputFolder.isEmpty { return .folder(path: outputFolder) }
        return .besideSource(folderSuffix: "-small")
    }

    var imageThreads: Int { max(2, ProcessInfo.processInfo.activeProcessorCount / 2) }
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

    @MainActor
    static func jobFinished(title: String, after: UInt64, outputs: [URL], elapsed: TimeInterval) {
        guard Bundle.main.bundleIdentifier != nil, !NSApp.isActive, !outputs.isEmpty else { return }
        let content = UNMutableNotificationContent()
        content.title = "简单小一点"
        content.body = "\(title) 已完成，\(Format.size(after))"
        content.subtitle = "点按在访达中显示"
        content.userInfo = ["paths": outputs.map(\.path)]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: UUID().uuidString, content: content, trigger: nil))
    }
}
