import SwiftUI
import UniformTypeIdentifiers

/// 设置（设计稿 V2.1 · 10）：便当盒小窗，通用 / 配方 / 自动 三页。
struct SettingsView: View {
    enum Tab: String, CaseIterable { case general = "通用", recipes = "常用", auto = "自动" }
    @State private var tab: Tab = .general

    var body: some View {
        VStack(spacing: 14) {
            TopTabs(selection: $tab, options: Tab.allCases.map { ($0, $0.rawValue) }, label: "设置分类")
                .accessibilitySortPriority(10)
            Group {
                switch tab {
                case .general: GeneralSettings()
                case .recipes: RecipeSettings()
                case .auto: AutoSettings()
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .bentoAppear(0, trigger: tab)
        }
        .padding(.horizontal, 14)
        .padding(.bottom, 14)
        .padding(.top, 4)
        .frame(width: 820, height: 560)
        .background(Bento.window.ignoresSafeArea())
        .modifier(WindowBackground())
    }
}

/// 设置里的白卡：标题 + 内容 + 底部一句说明。
private struct SettingsCard<Content: View>: View {
    let symbol: String
    let title: String
    var note: String? = nil
    @ViewBuilder var content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Image(systemName: symbol)
                    .font(.system(size: 14, weight: .medium))
                    .frame(width: 34, height: 34)
                    .background(Circle().fill(Bento.fill))
                    .accessibilityHidden(true)
                Text(title).font(.system(size: 14, weight: .semibold)).accessibilityAddTraits(.isHeader)
            }
            .padding(.bottom, 4)
            content
            if let note {
                Text(note)
                    .font(.system(size: 11))
                    .foregroundStyle(Bento.secondary)
                    .lineSpacing(2)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.horizontal, 4)
                    .padding(.top, 2)
            }
        }
        .padding(.horizontal, 16)
        .padding(.top, 14)
        .padding(.bottom, 16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: Bento.cardRadius, style: .continuous).fill(Bento.card))
        .accessibilityElement(children: .contain)
    }
}

// MARK: - 通用

private struct GeneralSettings: View {
    @Bindable private var settings = AppSettings.shared
    @Bindable private var updates = Updates.shared
    @State private var trusted = RingController.shared.isTrusted

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            VStack(spacing: 12) {
                SettingsCard(symbol: "folder", title: "输出", note: "原文件从不改动；压完变大时保留原文件。撤销会把新文件移到废纸篓。") {
                    BentoRow(label: "存到", height: 40) {
                        Picker("存到", selection: $settings.outputLocation) {
                            Text("原文件旁边").tag(AppSettings.OutputLocation.beside)
                            Text("指定文件夹").tag(AppSettings.OutputLocation.folder)
                        }
                        .labelsHidden().fixedSize()
                    }
                    if settings.outputLocation == .folder {
                        BentoRow(label: "文件夹", height: 40) {
                            Text(settings.outputFolder.isEmpty ? "未选择" : (settings.outputFolder as NSString).abbreviatingWithTildeInPath)
                                .foregroundStyle(Bento.secondary).lineLimit(1).truncationMode(.middle)
                            Button("选择…") { chooseFolder() }
                        }
                    }
                }
                SettingsCard(symbol: "lock.shield", title: "隐私与更新", note: "文件只在本机处理，不上传。除检查更新外，应用不联网。") {
                    BentoToggleRow(label: "自动检查更新", isOn: $updates.automaticallyChecks, height: 40)
                    BentoRow(label: "现在检查", height: 40) {
                        Button("检查更新…") { Updates.shared.check() }
                    }
                }
            }
            .bentoRegion(2)
            SettingsCard(symbol: "cursorarrow.click.2", title: "入口",
                         note: "圆环要读出你点的是哪个文件，所以需要辅助功能权限。只读取文件位置，不记录其他操作。") {
                BentoToggleRow(label: "访达圆环", isOn: $settings.ringEnabled, height: 40)
                BentoRow(label: "唤出方式", height: 40) { Text("按住 ⌥ 点文件").foregroundStyle(Bento.secondary) }
                BentoRow(label: "辅助功能权限", height: 40) {
                    if trusted {
                        Label("已授权", systemImage: "circle.fill").labelStyle(DotLabel()).foregroundStyle(Color.saving)
                    } else {
                        Button("去授权…") { RingController.shared.requestTrust() }
                    }
                }
                BentoToggleRow(label: "菜单栏图标", isOn: $settings.menuBarIcon, height: 40)
                BentoRow(label: "访达右键", height: 40) { Text("服务 → 用简单小一点处理").foregroundStyle(Bento.secondary) }
                BentoRow(label: "处理剪贴板", height: 40) {
                    Text("⌘⇧V").font(.system(size: 12, design: .monospaced))
                        .padding(.horizontal, 10).frame(height: 26)
                        .background(Capsule().fill(Bento.card))
                }
            }
            .bentoRegion(1)
        }
        .onReceive(Timer.publish(every: 1, on: .main, in: .common).autoconnect()) { _ in
            trusted = RingController.shared.isTrusted
        }
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

private struct DotLabel: LabelStyle {
    func makeBody(configuration: Configuration) -> some View {
        HStack(spacing: 6) {
            configuration.icon.font(.system(size: 7))
            configuration.title.font(.system(size: 12))
        }
    }
}

// MARK: - 常用

/// 常用（设计稿 V2.1 · 10 → 常用）：左边按类型分开，右边只列这一类。
/// 这一类的顺序就是功能页常用条的顺序，前 5 个也在访达圆环外圈；拖动排序，导入导出，新建、编辑、删除我的常用。
private struct RecipeSettings: View {
    @Environment(AppModel.self) private var model
    @State private var kind: MediaType = AppModel.shared.selectedType
    @State private var selection: Set<String> = []
    @State private var editing: Recipe?
    @State private var message: String?

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            kinds
                .frame(width: 200)
                .frame(maxHeight: .infinity)
                .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.card))
            list
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.card))
        }
        .sheet(item: $editing) { r in RecipeEditorView(initial: r) { _ in } }
        .onChange(of: kind) { _, _ in selection = []; message = nil }
    }

    // MARK: 左栏：类型

    private var kinds: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("按类型分开管")
                .font(.system(size: 12))
                .foregroundStyle(Bento.secondary)
                .padding(.horizontal, 6)
                .padding(.top, 4)
                .padding(.bottom, 6)
            ForEach(MediaType.allCases) { t in kindRow(t) }
            Spacer(minLength: 0)
            Text("每一类的顺序，就是那个功能页「常用」条的顺序；前 5 个也出现在访达圆环外圈。")
                .font(.system(size: 11))
                .foregroundStyle(Bento.secondary)
                .lineSpacing(3)
                .fixedSize(horizontal: false, vertical: true)
                .padding(10)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.fill))
        }
        .padding(12)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("常用的类型")
    }

    private func kindRow(_ t: MediaType) -> some View {
        let on = t == kind
        let count = model.store.ordered(t).count
        return Button { withAnimation(.smooth(duration: 0.2)) { kind = t } } label: {
            HStack(spacing: 10) {
                LineIcon(glyph: t.kindGlyph, size: 17, weight: 1.9)
                    .foregroundStyle(on ? Color.white : Color.primary)
                    .frame(width: 34, height: 34)
                    .background(RoundedRectangle(cornerRadius: 11, style: .continuous).fill(on ? Color.brand : Bento.card))
                Text(t.title)
                    .font(.system(size: 14, weight: on ? .semibold : .regular))
                    .frame(maxWidth: .infinity, alignment: .leading)
                Text(verbatim: "\(count)")
                    .font(.system(size: 11).monospacedDigit())
                    .foregroundStyle(on ? Bento.onInk : Bento.secondary)
                    .padding(.horizontal, 6)
                    .frame(minWidth: 22, minHeight: 20)
                    .background(Capsule().fill(on ? Bento.onInk.opacity(0.16) : Bento.card))
            }
            .foregroundStyle(on ? Bento.onInk : Color.primary)
            .padding(.leading, 8)
            .padding(.trailing, 12)
            .frame(height: 52)
            .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(on ? Bento.ink : Bento.fill))
            .contentShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .accessibilityLabel("\(t.title)，\(count) 个")
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
    }

    // MARK: 右边：这一类的常用

    private var list: some View {
        let recipes = model.store.ordered(kind)
        return VStack(alignment: .leading, spacing: 6) {
            HStack(spacing: 10) {
                Text("\(kind.title)的常用").font(.system(size: 14, weight: .semibold)).accessibilityAddTraits(.isHeader)
                Text(message ?? "拖动排序").font(.system(size: 11)).foregroundStyle(Bento.secondary).lineLimit(1)
                Spacer(minLength: 6)
                smallButton("导入…") { importFile() }
                smallButton("导出所选…") { exportSelected() }
                    .disabled(selection.isEmpty)
            }
            .padding(.bottom, 4)
            List {
                ForEach(Array(recipes.enumerated()), id: \.element.id) { i, r in
                    VStack(spacing: 6) {
                        row(r, ring: i < 5)
                        if i == 4, recipes.count > 5 { ringDivider }
                    }
                    .padding(.vertical, 3)
                    .listRowInsets(EdgeInsets())
                    .listRowSeparator(.hidden)
                    .listRowBackground(Color.clear)
                }
                .onMove { model.store.move(kind, fromOffsets: $0, toOffset: $1) }
            }
            .listStyle(.plain)
            .scrollContentBackground(.hidden)
            .environment(\.defaultMinListRowHeight, 0)
            .modifier(NoTopScrollEdge())
            .padding(.vertical, -3)
            .accessibilityLabel("\(kind.title)的常用")
            HStack(spacing: 6) {
                Button { editing = Recipe(id: "", name: "", type: kind) } label: {
                    HStack(spacing: 6) {
                        LineIcon(glyph: .plus, size: 14, weight: 2.4)
                        Text("新建\(kind.title)常用").font(.system(size: 13, weight: .semibold))
                    }
                    .foregroundStyle(.white)
                    .padding(.horizontal, 16)
                    .frame(height: 36)
                    .background(Capsule().fill(Color.brand))
                    .contentShape(Capsule())
                }
                .buttonStyle(BentoPressStyle())
                CapsuleButton(title: "删除所选", height: 36) {
                    for id in selection where !(model.store.recipe(id: id)?.builtIn ?? true) { model.store.delete(id: id) }
                    selection = []
                }
                .disabled(!selection.contains { !(model.store.recipe(id: $0)?.builtIn ?? true) })
                Spacer()
                Text("内置的不能删").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            }
            .padding(.top, 6)
        }
        .padding(.horizontal, 16)
        .padding(.top, 14)
        .padding(.bottom, 16)
    }

    /// 一行：拖动把手、名称、摘要、「圆环外圈」小标、内置 / 我的。点一下选中（⌘ 点多选）。
    private func row(_ r: Recipe, ring: Bool) -> some View {
        let on = selection.contains(r.id)
        let sub = on ? Bento.onInkSecondary : Bento.secondary
        return HStack(spacing: 8) {
            LineIcon(glyph: .grip, size: 15, weight: 3)
                .foregroundStyle(sub)
                .frame(width: 22)
            Text(r.name)
                .font(.system(size: 13, weight: on ? .semibold : .regular))
                .lineLimit(1)
                .truncationMode(.tail)
                .frame(maxWidth: .infinity, alignment: .leading)
            Text(r.summary)
                .font(.system(size: 12).monospacedDigit())
                .foregroundStyle(sub)
                .lineLimit(1)
                .frame(width: 170, alignment: .leading)
            Text("圆环外圈")
                .font(.system(size: 10))
                .foregroundStyle(on ? Bento.onInk : Bento.alertText)
                .padding(.horizontal, 8)
                .frame(height: 20)
                .background(Capsule().fill(on ? Bento.onInk.opacity(0.14) : Bento.alertFill))
                .frame(width: 70, alignment: .leading)
                .opacity(ring ? 1 : 0)
                .accessibilityHidden(!ring)
            Text(r.builtIn ? "内置" : "我的")
                .font(.system(size: 11, weight: r.builtIn ? .regular : .semibold))
                .foregroundStyle(r.builtIn ? sub : (on ? Bento.onInk : Color.brand))
                .frame(width: 40, alignment: .trailing)
        }
        .foregroundStyle(on ? Bento.onInk : Color.primary)
        .padding(.leading, 6)
        .padding(.trailing, 14)
        .frame(height: 40)
        .background(RoundedRectangle(cornerRadius: 13, style: .continuous).fill(on ? Bento.ink : Bento.fill))
        .contentShape(RoundedRectangle(cornerRadius: 13, style: .continuous))
        .onTapGesture { toggle(r.id) }
        .contextMenu {
            if !r.builtIn {
                Button("编辑…") { editing = r }
                Button("删除", role: .destructive) { model.store.delete(id: r.id); selection.remove(r.id) }
            } else {
                Button("复制为我的常用…") { var c = r; c.id = ""; c.name = "\(r.name) 改"; c.builtIn = false; editing = c }
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
        .accessibilityAction { toggle(r.id) }
    }

    private var ringDivider: some View {
        HStack(spacing: 10) {
            DashedLine()
            Text("以上 5 个出现在圆环外圈").font(.system(size: 10)).foregroundStyle(Bento.tertiary).fixedSize()
            DashedLine()
        }
        .padding(.horizontal, 6)
        .padding(.vertical, 2)
        .accessibilityElement(children: .combine)
    }

    private func toggle(_ id: String) {
        if NSEvent.modifierFlags.contains(.command) {
            if selection.contains(id) { selection.remove(id) } else { selection.insert(id) }
        } else {
            selection = selection == [id] ? [] : [id]
        }
    }

    private func smallButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12))
                .padding(.horizontal, 12)
                .frame(height: 30)
                .background(Capsule().fill(Bento.fill))
                .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
    }

    private func importFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.init(filenameExtension: "smaller") ?? .json, .json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let n = try model.store.importRecipes(from: url)
            message = "导入了 \(n) 个常用"
        } catch {
            message = "这个文件里没有可用的常用"
        }
    }

    private func exportSelected() {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = selection.count == 1 ? "\(model.store.recipe(id: selection.first!)?.name ?? "常用").smaller" : "\(kind.title)常用.smaller"
        panel.allowedContentTypes = [.init(filenameExtension: "smaller") ?? .json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try model.store.export(ids: Array(selection), to: url)
            message = "已导出 \(selection.count) 个常用"
        } catch {
            message = "导出失败：\(error.localizedDescription)"
        }
    }
}

// MARK: - 自动

/// 自动：剪贴板自动压缩、监视文件夹。
private struct AutoSettings: View {
    @Environment(AppModel.self) private var model
    @Bindable private var settings = AppSettings.shared

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            SettingsCard(symbol: "doc.on.clipboard", title: "剪贴板",
                         note: "截图或从网页复制的图片会自动换成压缩版，粘贴到公众号、飞书、微信就是小图。复制的文件、文字不动。") {
                BentoToggleRow(label: "复制的图片自动换成压缩版", isOn: $settings.clipboardAuto, height: 40)
                if settings.clipboardAuto {
                    BentoRow(label: "最长边", height: 40) {
                        Picker("最长边", selection: $settings.clipboardMaxEdge) {
                            Text("不缩小").tag(0)
                            Text(verbatim: "2560 px").tag(2560)
                            Text(verbatim: "1920 px").tag(1920)
                            Text(verbatim: "1280 px").tag(1280)
                        }
                        .labelsHidden().fixedSize()
                    }
                }
            }
            SettingsCard(symbol: "folder.badge.gearshape", title: "监视文件夹",
                         note: "放进这些文件夹的新文件按常用自动处理，结果放在旁边，处理完发通知。只处理之后放进来的文件。") {
                ForEach($settings.watchFolders) { $watch in
                    BentoRow(label: (watch.path as NSString).lastPathComponent, height: 40) {
                        Picker("常用", selection: $watch.recipeID) {
                            ForEach(MediaType.allCases) { type in
                                Section(type.title) {
                                    ForEach(model.store.all.filter { $0.type == type }) { Text($0.name).tag($0.id) }
                                }
                            }
                        }
                        .labelsHidden()
                        .fixedSize()
                        Button {
                            // 正在遍历这一行的绑定，改数组放到下一轮。
                            let id = watch.id
                            DispatchQueue.main.async { settings.watchFolders.removeAll { $0.id == id } }
                        } label: { Image(systemName: "minus.circle") }
                            .buttonStyle(.borderless)
                            .help("不再监视")
                            .accessibilityLabel("不再监视")
                    }
                    .help((watch.path as NSString).abbreviatingWithTildeInPath)
                }
                CapsuleButton(title: "添加文件夹…", height: 34) { addFolder() }
            }
        }
    }

    private func addFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.prompt = "监视"
        guard panel.runModal() == .OK, let url = panel.url, !settings.watchFolders.contains(where: { $0.path == url.path }) else { return }
        settings.watchFolders.append(WatchFolder(path: url.path, recipeID: "img.web"))
    }
}
