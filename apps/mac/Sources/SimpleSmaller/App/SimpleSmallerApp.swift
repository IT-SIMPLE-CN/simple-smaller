import AppKit
import SwiftUI

@main
struct SimpleSmallerApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @State private var model = AppModel.shared
    @State private var settings = AppSettings.shared

    var body: some Scene {
        Window("简单小一点", id: "main") {
            MainView()
                .environment(model)
                // 便当盒三档布局的最窄一档（WorkGrid 在 1080 以下切成窄档）。
                .frame(minWidth: 1000, minHeight: 640)
        }
        .defaultSize(width: 1280, height: 800)
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
        // 不在「窗口」菜单里列出。
        .commandsRemoved()

        MenuBarExtra(isInserted: Binding(get: { settings.menuBarIcon }, set: { settings.menuBarIcon = $0 })) {
            TrayView()
                .environment(model)
        } label: {
            Label("简单小一点", systemImage: "arrow.down.right.and.arrow.up.left.square")
        }
        .menuBarExtraStyle(.window)
    }
}
