import AppKit
import SwiftUI

extension Color {
    /// 品牌红，只用于主按钮、选中与进度。
    static let brand = Color(red: 0xEF / 255, green: 0x39 / 255, blue: 0x22 / 255)
    static let saving = Color(red: 0x1B / 255, green: 0x7F / 255, blue: 0x4E / 255)
    static let warning = Color(red: 0xC2 / 255, green: 0x41 / 255, blue: 0x0C / 255)
}

extension NSColor {
    static let brand = NSColor(red: 0xEF / 255, green: 0x39 / 255, blue: 0x22 / 255, alpha: 1)
}

@main
struct SimpleSmallerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var model = AppModel.shared
    @State private var settings = AppSettings.shared

    var body: some Scene {
        Window("简单小一点", id: "main") {
            MainView()
                .environment(model)
                .frame(minWidth: 900, minHeight: 560)
        }
        .defaultSize(width: 1180, height: 760)
        .commands { AppCommands(model: model) }

        Settings {
            SettingsView()
                .environment(model)
        }

        Window("关于简单小一点", id: "about") {
            AboutView()
        }
        .windowResizability(.contentSize)
        .windowStyle(.hiddenTitleBar)

        MenuBarExtra(isInserted: Binding(get: { settings.menuBarIcon }, set: { settings.menuBarIcon = $0 })) {
            TrayView()
                .environment(model)
        } label: {
            Label("简单小一点", systemImage: "arrow.down.right.and.arrow.up.left.square")
        }
        .menuBarExtraStyle(.window)
    }
}

/// 从 AppKit 一侧打开 SwiftUI 的窗口：通过「打开文件」或 Dock 重新打开时，SwiftUI 不会自动开主窗口。
@MainActor
enum WindowOpener {
    static var action: OpenWindowAction?

    static func showMain() {
        if let w = NSApp.windows.first(where: { $0.identifier?.rawValue == "main" || $0.title == AppModel.shared.selectedType.title }), w.isVisible {
            w.makeKeyAndOrderFront(nil)
        } else {
            action?(id: "main")
        }
        NSApp.activate()
    }
}

struct AppCommands: Commands {
    let model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        let _ = { WindowOpener.action = openWindow }()
        CommandGroup(replacing: .appInfo) {
            Button("关于简单小一点") { openWindow(id: "about") }
        }
        CommandGroup(replacing: .newItem) {
            Button("选取文件…") { FilePicker.pick(into: model) }
                .keyboardShortcut("o")
            Button("粘贴剪贴板") { model.pasteFromClipboard() }
                .keyboardShortcut("v", modifiers: [.command, .shift])
        }
        CommandMenu("处理") {
            Button("开始") { model.start(model.current) }
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(model.current.files.isEmpty || model.current.running)
            Button("取消") { model.cancel(model.current) }
                .keyboardShortcut(".", modifiers: .command)
                .disabled(!model.current.running)
            Button("撤销这一批") { model.undo(model.current) }
                .disabled(model.current.job == nil || model.current.running)
            Divider()
            Button("从列表移除所选") { model.remove(model.current.selection, from: model.current) }
                .keyboardShortcut(.delete, modifiers: [])
                .disabled(model.current.selection.isEmpty || model.current.running)
            Button("清空列表") { model.clear(model.current) }
                .disabled(model.current.files.isEmpty || model.current.running)
        }
    }
}

enum FilePicker {
    @MainActor
    static func pick(into model: AppModel) {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = true
        panel.canChooseFiles = true
        panel.prompt = "添加"
        if panel.runModal() == .OK { model.add(panel.urls) }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // 验证用：`SMALLER_APPEARANCE=light|dark` 强制外观，不动系统设置。
        switch ProcessInfo.processInfo.environment["SMALLER_APPEARANCE"] {
        case "light": NSApp.appearance = NSAppearance(named: .aqua)
        case "dark": NSApp.appearance = NSAppearance(named: .darkAqua)
        default: break
        }
        Notifier.setup()
        Task { @MainActor in
            RingController.shared.refresh()
            ServiceProvider.register()
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

