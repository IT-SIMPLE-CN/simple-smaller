import SwiftUI

/// 设置窗口：系统设置风格的分组表单，两页。
struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralSettings()
                .tabItem { Label("通用", systemImage: "gearshape") }
            FinderSettings()
                .tabItem { Label("访达", systemImage: "folder") }
        }
        .frame(width: 520)
        .fixedSize(horizontal: false, vertical: true)
    }
}

private struct GeneralSettings: View {
    @Bindable private var settings = AppSettings.shared

    var body: some View {
        Form {
            Section {
                Picker("存到", selection: $settings.outputLocation) {
                    Text("原文件旁边").tag(AppSettings.OutputLocation.beside)
                    Text("指定文件夹").tag(AppSettings.OutputLocation.folder)
                }
                if settings.outputLocation == .folder {
                    LabeledContent("文件夹") {
                        HStack(spacing: 8) {
                            Text(settings.outputFolder.isEmpty ? "未选择" : (settings.outputFolder as NSString).abbreviatingWithTildeInPath)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .truncationMode(.middle)
                            Button("选择…") { chooseFolder() }
                        }
                    }
                }
            } footer: {
                Text("原文件从不改动。拖入文件夹时，结构照搬到旁边的「文件夹名-small」里。")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                Toggle("在菜单栏显示图标", isOn: $settings.menuBarIcon)
            } footer: {
                Text("菜单栏小窗里可以拖入文件、换配方、看最近的结果。")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .scrollDisabled(true)
    }

    private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "选择"
        if panel.runModal() == .OK, let url = panel.url {
            settings.outputFolder = url.path
        }
    }
}

private struct FinderSettings: View {
    @Bindable private var settings = AppSettings.shared
    @State private var trusted = RingController.shared.isTrusted

    var body: some View {
        Form {
            Section {
                Toggle("按住 ⌥ 点文件，弹出圆环", isOn: $settings.ringEnabled)
                if settings.ringEnabled, !trusted {
                    LabeledContent {
                        Button("打开系统设置…") { RingController.shared.requestTrust() }
                    } label: {
                        VStack(alignment: .leading, spacing: 2) {
                            Text("需要「辅助功能」权限")
                            Text("用来读出指针下的是哪个文件，不会读取其他内容。")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
            } footer: {
                Text("在访达里按住 ⌥ 点一下图片、视频或 PDF，圆环外圈是配方，点一下直接处理。")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section {
                LabeledContent("右键菜单") { Text("服务 → 用简单小一点处理").foregroundStyle(.secondary) }
            } footer: {
                Text("在访达里选中文件后右键，按各类型当前选中的配方直接处理。不想显示可以在「系统设置 → 键盘 → 键盘快捷键 → 服务」里关掉。")
                    .font(.caption).foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .scrollDisabled(true)
        .onReceive(Timer.publish(every: 1, on: .main, in: .common).autoconnect()) { _ in
            trusted = RingController.shared.isTrusted
        }
    }
}
