import SwiftUI
import UniformTypeIdentifiers

/// 菜单栏小窗（设计稿 07）：拖入或 ⌘⇧V 直接按选中的配方处理；下面是最近的结果。
struct TrayView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openSettings) private var openSettings
    @State private var recipeID = "img.web"
    @State private var targeted = false

    private var recipe: Recipe? { model.store.recipe(id: recipeID) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            dropArea
                .padding(12)
            HStack {
                Text("配方").foregroundStyle(.secondary)
                Spacer()
                Picker("配方", selection: $recipeID) {
                    ForEach(MediaType.allCases) { type in
                        Section(type.title) {
                            ForEach(model.store.all.filter { $0.type == type }) { Text($0.name).tag($0.id) }
                        }
                    }
                }
                .labelsHidden()
                .frame(maxWidth: 170)
            }
            .font(.system(size: 12))
            .padding(.horizontal, 14)
            .padding(.bottom, 10)
            Divider()
            Text("最近").font(.system(size: 11, weight: .semibold)).foregroundStyle(.secondary)
                .padding(.horizontal, 14).padding(.top, 10).padding(.bottom, 4)
            if model.recent.isEmpty {
                Text("还没有处理过文件").font(.system(size: 12)).foregroundStyle(.tertiary)
                    .padding(.horizontal, 14).padding(.vertical, 10)
            } else {
                VStack(spacing: 0) {
                    ForEach(model.recent.prefix(5)) { job in RecentRow(job: job) }
                }
                .padding(.horizontal, 6)
            }
            Divider().padding(.top, 6)
            VStack(spacing: 0) {
                MenuRow(title: "打开主窗口") { WindowOpener.showMain() }
                MenuRow(title: "设置…") { NSApp.activate(); openSettings() }
            }
            .padding(6)
        }
        .frame(width: 300)
        .background(PasteShortcut { process(model.clipboardFiles()) })
    }

    private var dropArea: some View {
        VStack(spacing: 6) {
            Image(systemName: "arrow.down.doc").font(.system(size: 22, weight: .light))
                .foregroundStyle(targeted ? Color.brand : Color.secondary)
            Text("拖到这里").font(.system(size: 13, weight: .semibold))
            Text("或 ⌘⇧V 处理剪贴板里的图片").font(.system(size: 11)).foregroundStyle(.secondary)
        }
        .frame(maxWidth: .infinity)
        .frame(height: 104)
        .background {
            RoundedRectangle(cornerRadius: 10, style: .continuous)
                .strokeBorder(style: StrokeStyle(lineWidth: 1.2, dash: [5, 4]))
                .foregroundStyle(targeted ? Color.brand : Color.secondary.opacity(0.35))
                .background(targeted ? Color.brand.opacity(0.06) : .clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .animation(.smooth(duration: 0.2), value: targeted)
        .dropDestination(for: URL.self) { urls, _ in
            process(urls)
            return true
        } isTargeted: { targeted = $0 }
        .accessibilityLabel("拖入文件直接处理")
    }

    /// 选中配方那一类的文件用它，其他类型用各自当前的配方。
    private func process(_ urls: [URL]?) {
        guard let urls, !urls.isEmpty else { return }
        var byType: [MediaType: [URL]] = [:]
        for url in urls {
            var isDir: ObjCBool = false
            FileManager.default.fileExists(atPath: url.path, isDirectory: &isDir)
            if let t = isDir.boolValue ? .image : MediaType.of(url) { byType[t, default: []].append(url) }
        }
        let point = NSEvent.mouseLocation
        for (type, list) in byType {
            let r = recipe?.type == type ? recipe! : model.workspace(type).draft
            let run = QuickRun(recipe: r, urls: list, type: type)
            RingController.shared.track(run, at: point)
            run.start()
        }
    }
}

private struct RecentRow: View {
    let job: RecentJob
    @State private var hovering = false

    var body: some View {
        Button { NSWorkspace.shared.activateFileViewerSelecting(job.outputs) } label: {
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(job.title).font(.system(size: 12, weight: .medium)).lineLimit(1).truncationMode(.middle)
                    Text(verbatim: "\(job.recipe) · \(Format.size(job.before)) → \(Format.size(job.after))")
                        .font(.system(size: 11).monospacedDigit()).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer(minLength: 6)
                Text(Self.relative(job.date)).font(.system(size: 11)).foregroundStyle(.tertiary)
            }
            .padding(.horizontal, 8)
            .frame(height: 42)
            .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(hovering ? Color.primary.opacity(0.07) : .clear))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help("在访达中显示")
    }

    static func relative(_ d: Date) -> String {
        let s = Date().timeIntervalSince(d)
        if s < 60 { return "刚刚" }
        if s < 3600 { return "\(Int(s / 60)) 分钟前" }
        if Calendar.current.isDateInToday(d) { return "\(Int(s / 3600)) 小时前" }
        if Calendar.current.isDateInYesterday(d) { return "昨天" }
        let f = DateFormatter(); f.dateFormat = "M 月 d 日"
        return f.string(from: d)
    }
}

private struct MenuRow: View {
    let title: String
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            Text(title).font(.system(size: 13))
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 8)
                .frame(height: 26)
                .background(RoundedRectangle(cornerRadius: 5, style: .continuous).fill(hovering ? Color.primary.opacity(0.07) : .clear))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

/// 小窗里的 ⌘⇧V：看不见的按钮挂快捷键。
private struct PasteShortcut: View {
    let action: () -> Void
    var body: some View {
        Button("", action: action).keyboardShortcut("v", modifiers: [.command, .shift]).opacity(0).frame(width: 0, height: 0).accessibilityHidden(true)
    }
}
