import AppKit
import Foundation
import Observation
import SmallerCore
import UserNotifications

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
