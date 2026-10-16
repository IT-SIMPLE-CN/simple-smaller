import SwiftUI

struct MainView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var model = model
        let ws = model.current
        NavigationSplitView {
            SidebarView()
                .navigationSplitViewColumnWidth(min: 200, ideal: 220, max: 280)
        } detail: {
            // 检查器是详情区右侧的面板，不用系统 .inspector：后者会把工具栏切成两段，
            // 设计稿里工具栏是贯通整窗的一条（标题 · 分段 · + − | 开始 | 检查器开关）。
            HStack(spacing: 0) {
                DetailView(ws: ws)
                    .frame(minWidth: 460)
                if model.showInspector {
                    Divider()
                    InspectorView(ws: ws)
                        .frame(width: 300)
                        .background(Color(nsColor: .windowBackgroundColor))
                        .transition(.move(edge: .trailing).combined(with: .opacity))
                }
            }
            .animation(.smooth(duration: 0.3), value: model.showInspector)
        }
        // 不要工具栏下沿的硬分隔线：macOS 26 的悬浮边栏有圆角，硬线会在左上角断开成缺口。
        .modifier(HideToolbarSeparator())
        .navigationTitle(model.selectedType.title)
        .navigationSubtitle(subtitle(ws))
        .toolbar { MainToolbar(ws: ws) }
        .overlay(alignment: .bottom) {
            if let toast = model.toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(.regularMaterial, in: Capsule())
                    .padding(.bottom, 44)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.3), value: model.toast)
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls)
            return true
        }
    }

    private func subtitle(_ ws: Workspace) -> String {
        guard !ws.files.isEmpty else { return "没有文件" }
        var parts = ["\(ws.files.count) 个文件"]
        if ws.type == .pdf, ws.pdfSegment != .fromImages {
            parts.append("\(ws.files.reduce(0) { $0 + $1.pages }) 页")
        }
        parts.append(Format.size(ws.totalBytes))
        return parts.joined(separator: " · ")
    }
}

struct HideToolbarSeparator: ViewModifier {
    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.toolbarBackgroundVisibility(.hidden, for: .windowToolbar)
        } else {
            content
        }
    }
}

struct FlexibleToolbarSpace: ToolbarContent {
    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarSpacer(.flexible)
        } else {
            ToolbarItem(placement: .automatic) { Spacer() }
        }
    }
}

/// 工具栏（按 macOS 规范）：
/// 左边是边栏开关与标题（副标题写文件数与总体积）；
/// 中间是视图切换「压缩 / 裁切」——两侧各一个可伸缩空白，落在空白区正中；
/// 右边是操作，主操作在最右：添加 → 开始（处理中变停止）→ 检查器开关。
/// 「移除」不放工具栏：用 Delete 键、右键菜单与菜单栏命令。
struct MainToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some ToolbarContent {
        FlexibleToolbarSpace()
        ToolbarItem(placement: .automatic) {
            SegmentPicker(ws: ws)
        }
        FlexibleToolbarSpace()
        ToolbarItem(placement: .automatic) {
            Button { FilePicker.pick(into: model) } label: { Label("添加文件", systemImage: "plus") }
                .help("添加文件或文件夹（⌘O）")
                .disabled(ws.running)
        }
        StartToolbarItem(ws: ws)
        ToolbarItem(placement: .automatic) {
            Button { model.showInspector.toggle() } label: {
                Label(model.showInspector ? "隐藏检查器" : "显示检查器", systemImage: "sidebar.right")
            }
            .help(model.showInspector ? "隐藏检查器（⌥⌘I）" : "显示检查器（⌥⌘I）")
            .keyboardShortcut("i", modifiers: [.command, .option])
        }
    }
}

/// macOS 26 的工具栏会给每项套一层共享玻璃底，盖住按钮自己的突出样式；这一项单独关掉共享底。
struct StartToolbarItem: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .automatic) { content }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .automatic) { content }
        }
    }

    @ViewBuilder private var content: some View {
        if ws.running {
            // 处理中：变成次级样式的「停止」，左边一个小转圈，表示正在跑。
            Button { model.cancel(ws) } label: {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text("停止")
                }
            }
            .help("停止处理（⌘.）")
            .buttonStyle(QuietPillStyle())
        } else {
            StartButton(ws: ws)
        }
    }
}

struct StartButton: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        Button("开始") { model.start(ws) }
            .keyboardShortcut(.return, modifiers: .command)
            .disabled(ws.files.isEmpty || !canStart)
            .help("按当前配方处理全部文件（⌘↩）")
            .buttonStyle(BrandPillStyle())
    }

    private var canStart: Bool {
        switch ws.type {
        case .pdf where ws.pdfSegment == .fromImages: ws.files.contains { $0.type == .image }
        case .pdf where ws.pdfSegment == .merge: ws.files.filter { $0.type == .pdf }.count >= 1 && !ws.pageOrder.isEmpty
        default: true
        }
    }
}

/// 品牌红胶囊主按钮。macOS 26 起工具栏玻璃会接管按钮绘制，系统突出样式染不上品牌色，所以自绘：
/// 悬停微亮、按下加深、禁用变淡。
struct BrandPillStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled
    @State private var hovering = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 14)
            .frame(height: 28)
            .background {
                Capsule(style: .continuous)
                    .fill(Color.brand)
                    .brightness(configuration.isPressed ? -0.08 : (hovering ? 0.04 : 0))
            }
            .overlay { Capsule(style: .continuous).strokeBorder(.white.opacity(0.18), lineWidth: 0.5) }
            .shadow(color: Color.brand.opacity(isEnabled ? 0.28 : 0), radius: 3, y: 1)
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(Capsule())
            .onHover { hovering = $0 }
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
            .animation(.easeOut(duration: 0.15), value: hovering)
    }
}

/// 次级胶囊（处理中的「停止」）：与主按钮同尺寸，不抢眼。
struct QuietPillStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .medium))
            .padding(.horizontal, 12)
            .frame(height: 28)
            .background(Capsule(style: .continuous).fill(Color.primary.opacity(configuration.isPressed ? 0.16 : 0.09)))
            .contentShape(Capsule())
    }
}

/// 品牌红的主按钮：macOS 26 起工具栏是玻璃材质，要用玻璃版的突出样式才会着色。
struct ProminentStyle: ViewModifier {
    func body(content: Content) -> some View {
        if #available(macOS 26.0, *) {
            content.buttonStyle(.glassProminent).tint(Color.brand)
        } else {
            content.buttonStyle(.borderedProminent).tint(Color.brand)
        }
    }
}

struct SegmentPicker: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        @Bindable var ws = ws
        Group {
            switch ws.type {
            case .image:
                SlidingSegmented(selection: $ws.imageSegment, options: Workspace.ImageSegment.allCases.map { ($0, $0.rawValue) })
                    .frame(width: 150)
            case .video:
                SlidingSegmented(selection: $ws.videoSegment, options: Workspace.VideoSegment.allCases.map { ($0, $0.rawValue) })
                    .frame(width: 216)
            case .pdf:
                SlidingSegmented(selection: $ws.pdfSegment, options: Workspace.PdfSegment.allCases.map { ($0, $0.rawValue) })
                    .frame(width: 400)
            }
        }
        .accessibilityLabel("视图")
        .disabled(ws.running)
        .onChange(of: ws.imageSegment) { _, new in
            // 回到「压缩」不清裁切；切到「裁切」时若配方没有比例，默认用小红书 3:4。
            if new == .crop, ws.draft.image.crop == nil { ws.draft.image.crop = CropPreset.imagePresets[0] }
            model.schedulePreviews(.image)
        }
        .onChange(of: ws.videoSegment) { _, new in
            ws.draft.video.outputFrames = (new == .frames)
            model.schedulePreviews(.video)
        }
        .onChange(of: ws.pdfSegment) { _, _ in model.schedulePreviews(.pdf) }
    }
}

struct DetailView: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        VStack(spacing: 0) {
            Group {
                if ws.files.isEmpty {
                    DropZone(type: ws.type, segment: ws.pdfSegment)
                } else {
                    switch ws.type {
                    case .image:
                        if ws.imageSegment == .crop { CropView(ws: ws) } else { FileTableView(ws: ws) }
                    case .video:
                        switch ws.videoSegment {
                        case .compress: FileTableView(ws: ws)
                        case .edit: VideoEditView(ws: ws)
                        case .frames: VideoFramesView(ws: ws)
                        }
                    case .pdf:
                        PdfDetailView(ws: ws)
                    }
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            StatusBar(ws: ws)
        }
    }
}
