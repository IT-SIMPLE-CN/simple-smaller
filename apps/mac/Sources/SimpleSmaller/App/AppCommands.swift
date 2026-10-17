import AppKit
import SwiftUI

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
            Button("检查更新…") { Updates.shared.check() }
        }
        CommandGroup(before: .sidebar) {
            Button("首页") { model.showHome = true }
                .keyboardShortcut("0", modifiers: .command)
            ForEach(Array(MediaType.allCases.enumerated()), id: \.element) { i, type in
                Button(type.title) { model.selectedType = type }
                    .keyboardShortcut(KeyEquivalent(Character("\(i + 1)")), modifiers: .command)
            }
            Divider()
        }
        CommandGroup(replacing: .newItem) {
            Button("选取文件…") { FilePicker.pick(into: model) }
                .keyboardShortcut("o")
            Button("粘贴剪贴板") { model.pasteFromClipboard() }
                .keyboardShortcut("v", modifiers: [.command, .shift])
            Divider()
            Button("快速查看") { NotificationCenter.default.post(name: .quickLookSelection, object: nil) }
                .keyboardShortcut("y")
                .disabled(model.current.selection.isEmpty)
        }
        CommandGroup(replacing: .help) {
            Link("访问官网", destination: URL(string: "https://www.it-simple.cn/")!)
        }
        CommandMenu("处理") {
            Button("开始") { model.start(model.current) }
                .keyboardShortcut(.return, modifiers: .command)
                .disabled(model.current.files.isEmpty || model.current.running || model.current.isUpToDate)
            Button("停止") { model.cancel(model.current) }
                .keyboardShortcut(".", modifiers: .command)
                .disabled(!model.current.running)
            Button("重试失败的") { model.retryFailed(model.current) }
                .keyboardShortcut("r", modifiers: [.command, .shift])
                .disabled(model.current.running || !model.current.files.contains { if case .failed = $0.state { return true }; return false })
            Button("撤销这一批") { model.undo(model.current) }
                .disabled(model.current.job == nil || model.current.running)
            Divider()
            // ⌘⌫：不带修饰键的 Delete 做成菜单快捷键会抢走输入框里的退格。列表里直接按 Delete 由列表自己处理。
            Button("从列表移除所选") { model.remove(model.current.selection, from: model.current) }
                .keyboardShortcut(.delete, modifiers: .command)
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
