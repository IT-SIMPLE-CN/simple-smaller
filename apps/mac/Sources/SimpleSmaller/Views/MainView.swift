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
            // 工具栏要贯通整窗，分段才能落在工作区正中。
            // 左边是列表与状态栏；右边是检查器，内容自己上下滚，「开始」占满宽度固定在底部。
            // 两边底栏同高，分隔线连成一条。
            HStack(spacing: 0) {
                VStack(spacing: 0) {
                    DetailView(ws: ws)
                    StatusBar(ws: ws)
                }
                .frame(minWidth: 460)
                Divider()
                VStack(spacing: 0) {
                    InspectorView(ws: ws)
                    InspectorFooter(ws: ws)
                }
                .frame(width: 300)
                .background(Color(nsColor: .windowBackgroundColor))
            }
            // 工具栏下沿的细线只画在详情区（列表 + 检查器），不伸进边栏，左上角不会断出缺口。
            .overlay(alignment: .top) { Divider() }
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
        // 窗口默认把第一个输入框设成初始焦点，一打开「文件名」就被选中；改成不聚焦任何输入框。
        .task {
            try? await Task.sleep(for: .milliseconds(80))
            NSApp.keyWindow?.makeFirstResponder(nil)
        }
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

/// 工具栏：左边是边栏开关与标题（副标题写文件数与总体积）；
/// 正中是「压缩 / 裁切」这类业务分段——它切换的是整个工作区，所以居中；
/// 添加文件在检查器顶部的批量卡片里，「开始」固定在检查器底部。
struct MainToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some ToolbarContent {
        SegmentToolbarItem(ws: ws)
    }
}

/// 居中的业务分段。macOS 26 起工具栏会给每项套一层玻璃胶囊，分段自带底槽，叠起来是两层，这里关掉。
struct SegmentToolbarItem: ToolbarContent {
    let ws: Workspace

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws) }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .principal) { SegmentPicker(ws: ws) }
        }
    }
}

struct StartButton: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        Button { model.start(ws) } label: {
            Text("开始").frame(maxWidth: .infinity)
        }
        .keyboardShortcut(.return, modifiers: .command)
        .disabled(ws.files.isEmpty || !canStart)
        .help("按当前配方处理全部文件（⌘↩）")
        .buttonStyle(BrandBlockStyle())
    }

    private var canStart: Bool {
        switch ws.type {
        case .pdf where ws.pdfSegment == .fromImages: ws.files.contains { $0.type == .image }
        case .pdf where ws.pdfSegment == .merge: ws.files.filter { $0.type == .pdf }.count >= 1 && !ws.pageOrder.isEmpty
        default: true
        }
    }
}

/// 检查器底部：占满宽度的「开始」，处理中变「停止」并带进度。
struct InspectorFooter: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
        Group {
            if ws.running {
                Button { model.cancel(ws) } label: {
                    HStack(spacing: 8) {
                        ProgressView().controlSize(.small)
                        Text("停止")
                        Text("\(ws.finishedCount) / \(ws.files.count)").monospacedDigit().foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity)
                }
                .help("停止处理（⌘.）")
                .buttonStyle(QuietBlockStyle())
            } else {
                StartButton(ws: ws)
            }
        }
        .padding(.horizontal, 16)
        .frame(height: StatusBar.height)
        .overlay(alignment: .top) { Divider() }
        .animation(.smooth(duration: 0.25), value: ws.running)
    }
}

/// 品牌红主按钮（自绘，各系统版本一致）：悬停微亮、按下加深、禁用变淡。
struct BrandBlockStyle: ButtonStyle {
    @Environment(\.isEnabled) private var isEnabled
    @State private var hovering = false

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(.white)
            .frame(height: 32)
            .background {
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(Color.brand)
                    .brightness(configuration.isPressed ? -0.08 : (hovering && isEnabled ? 0.04 : 0))
            }
            .overlay { RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(.white.opacity(0.16), lineWidth: 0.5) }
            .shadow(color: Color.brand.opacity(isEnabled ? 0.25 : 0), radius: 3, y: 1)
            .opacity(isEnabled ? 1 : 0.4)
            .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
            .onHover { hovering = $0 }
            .animation(.easeOut(duration: 0.12), value: configuration.isPressed)
            .animation(.easeOut(duration: 0.15), value: hovering)
    }
}

/// 次级按钮（处理中的「停止」）：与主按钮同尺寸，不抢眼。
struct QuietBlockStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .medium))
            .frame(height: 32)
            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Color.primary.opacity(configuration.isPressed ? 0.16 : 0.09)))
            .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
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
        }
        .onChange(of: ws.videoSegment) { _, new in
            ws.draft.video.outputFrames = (new == .frames)
        }
    }
}

struct DetailView: View {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some View {
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
    }
}
