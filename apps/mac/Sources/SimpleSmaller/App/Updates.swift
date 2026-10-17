import AppKit
import Observation
import Sparkle

/// 自动更新（Sparkle，做法与 dotnfilm-bots 一致）：每 6 小时在后台查一次，
/// 发现新版本先弹系统样式的提示，用户同意才下载安装；更新包用 EdDSA 验签。
@Observable
@MainActor
final class Updates {
    static let shared = Updates()

    @ObservationIgnored private let controller = SPUStandardUpdaterController(startingUpdater: true, updaterDelegate: nil, userDriverDelegate: nil)

    /// 设置里的「自动检查更新」。
    var automaticallyChecks: Bool {
        didSet { controller.updater.automaticallyChecksForUpdates = automaticallyChecks }
    }

    private init() {
        automaticallyChecks = controller.updater.automaticallyChecksForUpdates
    }

    var canCheck: Bool { controller.updater.canCheckForUpdates }

    func check() { controller.checkForUpdates(nil) }
}
