import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    override init() {
        // 系统的自动填充启发式会在输入框获得焦点时建一个浮窗，没有可填的内容时就空着露在屏幕边上。
        // 本应用的输入框（文件名、尺寸）都用不到自动填充，在应用初始化之前整体关掉。
        UserDefaults.standard.set(false, forKey: "NSAutoFillHeuristicControllerEnabled")
        super.init()
    }

    /// `--reset-guides`：清掉「看过教学」的记录（验证用），要在主窗口出来、教学判断之前做。
    func applicationWillFinishLaunching(_ notification: Notification) {
        if CommandLine.arguments.contains("--reset-guides") {
            MainActor.assumeIsolated { AppSettings.shared.resetGuides() }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        AppModel.clearUnlocked()
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        AppModel.shared.clearUnseen()
    }

    /// 还有文件在处理时退出先确认，免得丢掉做到一半的视频。
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard AppModel.shared.isBusy else { return .terminateNow }
        let alert = NSAlert()
        alert.messageText = "还有文件在处理"
        alert.informativeText = "现在退出会停止处理，做到一半的文件不会留下，原文件不受影响。"
        alert.addButton(withTitle: "继续处理")
        alert.addButton(withTitle: "停止并退出")
        guard alert.runModal() == .alertSecondButtonReturn else { return .terminateCancel }
        for t in MediaType.allCases { AppModel.shared.cancel(AppModel.shared.workspace(t)) }
        RingController.shared.cancelAll()
        return .terminateNow
    }

    /// macOS 27 的自动填充会给输入框建一个「SafariPlatformSupportAutoCompleteWindow」浮窗，
    /// 本应用没有可填的内容，它就空着挂在屏幕边上。出现就收起。
    private var autoFillObserver: NSObjectProtocol?

    /// 系统会在之后往「编辑」菜单里补「自动填充」，往「窗口」菜单里留连续的分隔线：每次菜单有变化都收拾一遍。
    private static func tidyMenus() {
        for top in NSApp.mainMenu?.items ?? [] {
            guard let menu = top.submenu else { continue }
            for item in menu.items where item.title == "自动填充" || item.title == "AutoFill" { menu.removeItem(item) }
            var lastWasSeparator = true
            for item in menu.items {
                if item.isSeparatorItem, lastWasSeparator { menu.removeItem(item); continue }
                lastWasSeparator = item.isSeparatorItem
            }
            if let last = menu.items.last, last.isSeparatorItem { menu.removeItem(last) }
        }
    }

    private var menuObserver: NSObjectProtocol?

    /// 系统自动填充建的浮窗：标识或类名里带 AutoComplete / AutoFill。
    private static func isAutoFillWindow(_ w: NSWindow) -> Bool {
        let names = [w.identifier?.rawValue ?? "", String(describing: type(of: w)), w.accessibilityIdentifier()]
        return names.contains { $0.contains("AutoComplete") || $0.contains("AutoFill") || $0.contains("Autofill") }
    }

    private func hideAutoFillWindows() {
        // didUpdate 每处理一个事件就来一次：只在可见窗口数变了时才逐个看类名，平时只数一下。
        var lastCount = -1
        autoFillObserver = NotificationCenter.default.addObserver(forName: NSApplication.didUpdateNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated {
                // 按可见窗口数判断：自动填充浮窗可能被复用、只是重新显示，总数不变。
                let visible = NSApp.windows.filter(\.isVisible)
                guard visible.count != lastCount else { return }
                lastCount = visible.count
                for w in visible where Self.isAutoFillWindow(w) {
                    w.orderOut(nil)
                }
            }
        }
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        hideAutoFillWindows()
        // 启动更新器：按 Info.plist 的间隔在后台检查。
        _ = Updates.shared
        // 系统往「编辑」菜单里加的「自动填充」，本应用没有可填的内容，拿掉。
        menuObserver = NotificationCenter.default.addObserver(forName: NSMenu.didAddItemNotification, object: nil, queue: .main) { _ in
            MainActor.assumeIsolated { DispatchQueue.main.async { Self.tidyMenus() } }
        }
        DispatchQueue.main.async { Self.tidyMenus() }
        // 验证用：`SMALLER_APPEARANCE=light|dark` 强制外观，不动系统设置。
        switch ProcessInfo.processInfo.environment["SMALLER_APPEARANCE"] {
        case "light": NSApp.appearance = NSAppearance(named: .aqua)
        case "dark": NSApp.appearance = NSAppearance(named: .darkAqua)
        default: break
        }
        Notifier.setup()
        AppModel.clearUnlocked()
        Task { @MainActor in
            RingController.shared.refresh()
            ClipboardWatcher.shared.refresh()
            FolderWatcher.shared.refresh()
            ServiceProvider.register()
            #if DEBUG
            ScriptHooks.install()
            #endif
            // 命令行 `--open <路径>...` 等同拖入，供脚本化验证。
            let args = CommandLine.arguments
            if let i = args.firstIndex(of: "--open") {
                let urls = args[(i + 1)...].prefix { !$0.hasPrefix("--") }.map { URL(fileURLWithPath: $0) }
                AppModel.shared.add(urls)
            }
            if let i = args.firstIndex(of: "--type"), i + 1 < args.count, let t = MediaType(rawValue: args[i + 1]) {
                AppModel.shared.selectedType = t
            }
            // `--ring <路径>...`：在屏幕中间为这些文件弹圆环，供脚本化验证（访达里需要辅助功能授权）。
            if let i = args.firstIndex(of: "--ring") {
                let urls = args[(i + 1)...].prefix { !$0.hasPrefix("--") }.map { URL(fileURLWithPath: $0) }
                let mouse = NSEvent.mouseLocation
                if let screen = NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? NSScreen.main {
                    RingController.shared.open(urls: urls, at: NSPoint(x: screen.frame.midX, y: screen.frame.midY))
                }
                return
            }
            // `--recipe <配方 id>`：选中某个配方，供脚本化验证。
            if let i = args.firstIndex(of: "--recipe"), i + 1 < args.count, let r = AppModel.shared.store.recipe(id: args[i + 1]) {
                AppModel.shared.selectedType = r.type
                AppModel.shared.current.select(recipe: r)
            }
            // `--segment <分段名>`（如「裁切」）：直接进某个分段，供脚本化验证。
            if let i = args.firstIndex(of: "--segment"), i + 1 < args.count {
                AppModel.shared.openSegment(args[i + 1])
            }
            // `--start`：读完文件信息后直接开始，供脚本化验证。
            if args.contains("--start") {
                try? await Task.sleep(for: .seconds(2))
                AppModel.shared.start(AppModel.shared.current)
            }
            // 带文件启动时 AppKit 按「打开文件」处理，SwiftUI 不会自动开主窗口。
            try? await Task.sleep(for: .milliseconds(300))
            if !NSApp.windows.contains(where: { $0.isVisible && $0.canBecomeMain }) { WindowOpener.showMain() }
        }
    }

    func application(_ application: NSApplication, open urls: [URL]) {
        Task { @MainActor in
            AppModel.shared.add(urls)
            WindowOpener.showMain()
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag { Task { @MainActor in WindowOpener.showMain() } }
        return true
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}
