import SwiftUI

/// 主窗口（设计稿 V2.1）：没有文件时是首页；选了一类就是这一类的便当盒工作区，做完换到完成页。
/// 工具栏用系统的（左边返回与标题、正中分段），浅灰窗口底上铺白色卡片。
struct MainView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let ws = model.current
        ZStack {
            // 首页 ↔ 工作区：整体轻微缩放 + 淡入淡出，里面的卡片各自再弹入。
            if model.showHome {
                HomeView()
                    .transition(.asymmetric(insertion: .scale(scale: 0.98).combined(with: .opacity), removal: .opacity))
            } else {
                WorkView(ws: ws)
                    .id(ws.type)
                    .transition(.asymmetric(insertion: .scale(scale: 0.98).combined(with: .opacity), removal: .opacity))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Bento.window.ignoresSafeArea())
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.42, dampingFraction: 0.86), value: model.showHome)
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.42, dampingFraction: 0.86), value: model.selectedType)
        .modifier(HideToolbarSeparator())
        .navigationTitle(model.showHome ? "简单小一点" : title(ws))
        .navigationSubtitle(model.showHome ? "" : subtitle(ws))
        .toolbar {
            if model.showHome { HomeToolbar() } else { WorkToolbar(ws: ws) }
        }
        .modifier(HideWindowTitle(hidden: model.showHome))
        .overlay(alignment: .bottom) {
            if let toast = model.toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(.regularMaterial, in: Capsule())
                    .padding(.bottom, 28)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.3), value: model.toast)
        .background(NoInitialFocus())
        // 拖到窗口任何地方：按类型自动分到图片、视频、PDF。
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: model.showHome ? nil : model.selectedType)
            return true
        }
    }

    /// 视频剪辑、序列帧一次看一段：标题写这段的文件名。
    private func singleVideo(_ ws: Workspace) -> FileItem? {
        guard ws.type == .video, ws.videoSegment != .compress else { return nil }
        return ws.currentVideo
    }

    private func title(_ ws: Workspace) -> String {
        singleVideo(ws)?.name ?? model.selectedType.title
    }

    private func subtitle(_ ws: Workspace) -> String {
        if let v = singleVideo(ws) {
            var parts: [String] = []
            if ws.videoItems.count > 1, let i = ws.videoItems.firstIndex(where: { $0.id == v.id }) {
                parts.append("第 \(i + 1) / \(ws.videoItems.count) 个")
            }
            parts += ["\(v.width) × \(v.height)", Format.duration(v.duration)]
            if ws.videoSegment == .frames {
                parts.append("\(Int(v.fps.rounded())) fps")
                parts.append("\(Int((v.duration * v.fps).rounded())) 帧")
            } else {
                if v.hdr { parts.append("HDR") }
                parts.append(Format.size(v.bytes))
            }
            return parts.joined(separator: " · ")
        }
        guard !ws.files.isEmpty else { return "没有文件" }
        if ws.running { return "处理中 \(ws.finishedCount) / \(ws.files.count)" }
        if ws.showResult { return "\(ws.files.count) 个文件 · 已完成" }
        var parts = ["\(ws.files.count) 个文件"]
        if ws.type == .image, ws.imageSegment == .crop {
            let items = ws.cropItems
            parts.append("已确认 \(items.filter { ws.cropConfirmed.contains($0.url) }.count) / \(items.count)")
            return parts.joined(separator: " · ")
        }
        if ws.type == .pdf, ws.pdfSegment != .fromImages {
            parts.append("\(ws.pdfSegment == .merge ? ws.pageOrder.count : ws.files.reduce(0) { $0 + $1.pages }) 页")
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

/// 首页不写窗口标题（正中是品牌）。
struct HideWindowTitle: ViewModifier {
    let hidden: Bool

    func body(content: Content) -> some View {
        if #available(macOS 15.0, *), hidden {
            content.toolbar(removing: .title)
        } else {
            content
        }
    }
}

/// 工作区工具栏：左边返回首页；正中是业务分段；完成页右边是「撤销这一批」。
struct WorkToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model
    let ws: Workspace

    var body: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            Button { model.showHome = true } label: {
                Image(systemName: "arrow.left")
            }
            .help("回首页（这一批文件保留）")
            .accessibilityLabel("回首页")
        }
        SegmentToolbarItem(ws: ws)
        if ws.showResult, !ws.running {
            ToolbarItem(placement: .primaryAction) {
                Button("撤销这一批") { model.undo(ws) }
                    .help("把这一批写出的文件移到废纸篓，原文件不动")
            }
        }
    }
}

/// 居中的业务分段。macOS 26 起工具栏会给每项套一层玻璃胶囊，分段自带白底，这里关掉。
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

struct SegmentPicker: View {
    let ws: Workspace

    var body: some View {
        @Bindable var ws = ws
        Group {
            switch ws.type {
            case .image:
                TopTabs(selection: $ws.imageSegment, options: Workspace.ImageSegment.allCases.map { ($0, $0.rawValue) })
            case .video:
                TopTabs(selection: $ws.videoSegment, options: Workspace.VideoSegment.allCases.map { ($0, $0.rawValue) })
            case .pdf:
                TopTabs(selection: $ws.pdfSegment, options: Workspace.PdfSegment.allCases.map { ($0, $0.rawValue) })
            }
        }
        .accessibilityLabel("视图")
        .disabled(ws.running)
        .onChange(of: ws.imageSegment) { _, new in
            ws.enterImageSegment(new)
            ws.showResult = false
        }
        .onChange(of: ws.videoSegment) { _, new in
            ws.enterVideoSegment(new)
            ws.showResult = false
        }
        .onChange(of: ws.pdfSegment) { _, new in
            ws.enterPdfSegment(new)
            ws.showResult = false
        }
    }
}

/// 窗口默认把第一个输入框设成初始焦点，一打开「文件名」就被选中。
/// 在用户第一次点击或按 Tab 之前，窗口每次变前台、内容换了都把这种自动焦点清掉。
struct NoInitialFocus: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { Probe() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    final class Probe: NSView {
        private var userActed = false
        private var monitor: Any?
        private var observers: [NSObjectProtocol] = []

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window, monitor == nil else { return }
            window.initialFirstResponder = nil
            monitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .keyDown]) { [weak self] e in
                if e.type == .leftMouseDown || e.keyCode == 48 { self?.userActed = true }   // 48 = Tab
                return e
            }
            for name in [NSWindow.didBecomeKeyNotification, NSWindow.didUpdateNotification] {
                observers.append(NotificationCenter.default.addObserver(forName: name, object: window, queue: .main) { [weak self] _ in
                    MainActor.assumeIsolated { self?.clear() }
                })
            }
            clear()
        }

        private func clear() {
            guard !userActed, let window, window.firstResponder is NSText else { return }
            window.makeFirstResponder(nil)
        }

        deinit {
            if let monitor { NSEvent.removeMonitor(monitor) }
            observers.forEach(NotificationCenter.default.removeObserver)
        }
    }
}
