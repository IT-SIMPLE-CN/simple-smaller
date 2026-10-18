import AppKit
import Observation
import SwiftUI

/// 教学的总调度（设计稿 V3）：
/// - 功能小教学：每类（图片 / 视频 / PDF）第一次拖进文件后自动出现一次；标题栏「? 再看一遍教学」随时再看。
/// - 访达圆环教学：不自动出现；点首页「访达圆环」卡片打开（每次点都开），设置 › 入口 里「再看一遍」也能开。
/// 小教学跳过、知道了、关掉（含 Esc）都记为看过（`AppSettings.guidesSeen`），之后不再自动出现。
///
/// 教学盖在整个主窗口上（含标题栏、顶部大标签）：浮层视图直接放进窗口的边框视图最上层，
/// 跟着窗口圆角裁切、跟着窗口缩放；聚光框的位置从各张卡登记的 `GuideTarget` 现场换算。
@Observable
@MainActor
final class GuideCenter {
    static let shared = GuideCenter()

    enum Kind: Equatable {
        case feature(MediaType)
        case ring
    }

    /// 正在显示的教学；nil 为没有。
    private(set) var active: Kind?
    /// 第几步（从 0 起）。
    var step = 0
    /// 浮层是否显示（淡入淡出用，和 active 分开：关掉时先淡出再拆）。
    private(set) var shown = false
    /// 视频小教学第 2 步按进入时的分段换内容（剪辑框预览，压缩 / 序列帧框文件卡）。
    private(set) var videoSegment: Workspace.VideoSegment = .edit
    /// 窗口尺寸变了、卡片挪了：浮层据此重新量聚光框。
    private(set) var layoutTick = 0

    @ObservationIgnored private var targets: [GuideTarget: WeakView] = [:]
    @ObservationIgnored private weak var mainWindow: NSWindow?
    @ObservationIgnored private var host: NSView?
    @ObservationIgnored private var panel: NSPanel?
    @ObservationIgnored private var keyMonitor: Any?
    @ObservationIgnored private var resizeObserver: NSObjectProtocol?
    @ObservationIgnored private var pendingAuto: DispatchWorkItem?

    private var settings: AppSettings { .shared }

    // MARK: 登记

    func register(_ target: GuideTarget, _ view: NSView) {
        targets[target] = WeakView(view: view)
    }

    func registerWindow(_ window: NSWindow) {
        mainWindow = window
    }

    /// 某个登记点在浮层里的位置（浮层坐标，左上为原点）。
    func rect(of target: GuideTarget) -> CGRect? {
        guard let host, let panel, let view = targets[target]?.view, let window = view.window, window === panel.parent,
              !view.isHiddenOrHasHiddenAncestor else { return nil }
        let inWindow = view.convert(view.bounds, to: nil)
        let onScreen = window.convertToScreen(inWindow)
        let r = host.convert(panel.convertFromScreen(onScreen), from: nil)
        return r.isEmpty ? nil : r
    }

    // MARK: 功能小教学

    /// 各类每一步框住哪张卡。
    static func steps(_ type: MediaType) -> [GuideTarget] {
        switch type {
        case .image: [.main, .bar, .upper, .start]
        case .video: [.tabs, .main, .bar, .start]
        case .pdf: [.tabs, .main, .upper, .start]
        }
    }

    /// 标题栏的「? 再看一遍教学」：从第 1 步再看。
    func showFeature(_ type: MediaType) {
        let ws = AppModel.shared.workspace(type)
        guard !ws.files.isEmpty else { return }
        pendingAuto?.cancel()
        videoSegment = ws.videoSegment
        step = 0
        present(.feature(type))
    }

    /// 页面状态变了就来问一下：这一类有文件、还没看过、当前没别的教学，就（稍等卡片弹完后）自动出现。
    func considerAuto() {
        pendingAuto?.cancel()
        guard active == nil, autoCandidate != nil else { return }
        let work = DispatchWorkItem { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.active == nil, let type = self.autoCandidate else { return }
                self.showFeature(type)
            }
        }
        pendingAuto = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.8, execute: work)
    }

    private var autoCandidate: MediaType? {
        let model = AppModel.shared
        guard !model.showHome else { return nil }
        let ws = model.current
        guard !ws.files.isEmpty, !ws.showResult, !ws.running, !settings.guideSeen(ws.type.rawValue) else { return nil }
        guard mainWindow?.isVisible == true else { return nil }
        return ws.type
    }

    // MARK: 圆环教学

    /// 首页圆环卡、设置里「再看一遍」：把主窗口叫到前面再弹。
    func showRing() {
        pendingAuto?.cancel()
        if mainWindow?.isVisible != true {
            WindowOpener.showMain()
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { [weak self] in
                MainActor.assumeIsolated { self?.presentRing() }
            }
        } else {
            mainWindow?.makeKeyAndOrderFront(nil)
            NSApp.activate()
            presentRing()
        }
    }

    private func presentRing() {
        if active != nil { teardown() }
        step = 0
        present(.ring)
    }

    // MARK: 翻页与关闭

    var stepCount: Int {
        switch active {
        case .feature(let t): Self.steps(t).count
        case .ring: 3
        case nil: 0
        }
    }

    func next() {
        if step < stepCount - 1 { step += 1 } else { finish() }
    }

    func previous() {
        step = max(0, step - 1)
    }

    /// 跳过、知道了、×、Esc：都记为看过并关掉。
    func finish() {
        guard let kind = active else { return }
        switch kind {
        case .feature(let t): settings.markGuideSeen(t.rawValue)
        case .ring: settings.markGuideSeen("ring")
        }
        let wasRing = kind == .ring
        shown = false
        let delay = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0.12 : 0.22
        DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
            MainActor.assumeIsolated {
                guard let self, !self.shown else { return }
                self.teardown()
                // 圆环教学关掉后，若这时功能页已经有文件，小教学接着按规则出现。
                if wasRing { self.considerAuto() }
            }
        }
    }

    // MARK: 浮层

    private func present(_ kind: Kind) {
        guard let window = mainWindow ?? NSApp.windows.first(where: { $0.identifier?.rawValue.hasPrefix("main") == true }) else { return }
        active = kind
        if panel == nil {
            // 浮层是贴在主窗口上的透明子窗口：标题栏（含顶部大标签）也一起压暗，跟着主窗口移动、缩放。
            // 主窗口保持前台（按键仍由它收，Esc 等在本地监听里处理）。
            let view = GuideHostView(rootView: AnyView(GuideOverlay().ignoresSafeArea().environment(AppModel.shared)))
            let p = GuidePanel(contentRect: window.frame, styleMask: [.borderless], backing: .buffered, defer: false)
            p.isOpaque = false
            p.backgroundColor = .clear
            p.hasShadow = false
            p.isReleasedWhenClosed = false
            p.contentView = view
            // 子窗口没有标题栏，但宿主视图仍会按主窗口算出顶部安全区：清掉，压暗铺满整窗。
            if #available(macOS 14.4, *) { view.safeAreaRegions = [] }
            view.wantsLayer = true
            view.layer?.cornerRadius = Self.cornerRadius(of: window)
            view.layer?.masksToBounds = true
            window.addChildWindow(p, ordered: .above)
            p.setFrame(window.frame, display: true)
            panel = p
            host = view
            installMonitors(window)
        }
        layoutTick += 1
        DispatchQueue.main.async { [weak self] in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.shown = true
                self.layoutTick += 1
            }
        }
        // 卡片可能还在弹入，稍后再量一次。
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { [weak self] in
            MainActor.assumeIsolated { self?.layoutTick += 1 }
        }
        NSAccessibility.post(element: host as Any, notification: .layoutChanged)
    }

    /// 主窗口的圆角（系统没公开，取不到时按 macOS 26 带工具栏窗口的 16）。
    private static func cornerRadius(of window: NSWindow) -> CGFloat {
        let sel = NSSelectorFromString("_cornerRadius")
        if window.responds(to: sel), let r = window.value(forKey: "_cornerRadius") as? CGFloat, r > 0 { return r }
        return 16
    }

    private func teardown() {
        if let panel { panel.parent?.removeChildWindow(panel); panel.orderOut(nil) }
        panel = nil
        host = nil
        active = nil
        shown = false
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        if let resizeObserver { NotificationCenter.default.removeObserver(resizeObserver) }
        resizeObserver = nil
    }

    private func installMonitors(_ window: NSWindow) {
        // Esc = 跳过；← → 翻页、↩ 下一步。带 ⌘ 的组合键照常交给菜单。
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.window === window, !event.modifierFlags.contains(.command) else { return event }
            let code = event.keyCode
            let handled = MainActor.assumeIsolated { () -> Bool in
                guard let self, self.active != nil else { return false }
                switch code {
                case 53: self.finish()
                case 124, 36, 76: self.next()
                case 123: self.previous()
                default: break
                }
                return true
            }
            return handled ? nil : event
        }
        resizeObserver = NotificationCenter.default.addObserver(forName: NSWindow.didResizeNotification, object: window, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                self?.panel?.setFrame(window.frame, display: true)
                self?.layoutTick += 1
            }
        }
    }

    struct WeakView {
        weak var view: NSView?
    }
}

/// 浮层子窗口：不抢前台（主窗口照样是 key），点击照收。
private final class GuidePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

/// 浮层的宿主：整窗接住点击（教学开着时下面不能点）。
private final class GuideHostView: NSHostingView<AnyView> {
    override var acceptsFirstResponder: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

/// 聚光框能框住的地方。
enum GuideTarget: Hashable {
    /// 顶部大标签（视频、PDF）。
    case tabs
    /// 左大卡（文件、预览）。
    case main
    /// 常用条。
    case bar
    /// 右上参数卡（图片的比例与画面、PDF 的压缩档）。
    case upper
    /// 右下开始块所在的卡。
    case start
}

extension View {
    /// 登记为教学的聚光点。
    func guideTarget(_ target: GuideTarget) -> some View {
        background(GuideProbe(target: target))
    }
}

private struct GuideProbe: NSViewRepresentable {
    let target: GuideTarget

    func makeNSView(context: Context) -> Probe {
        let v = Probe()
        v.target = target
        return v
    }

    func updateNSView(_ nsView: Probe, context: Context) {
        nsView.target = target
        nsView.register()
    }

    final class Probe: NSView {
        var target: GuideTarget = .main

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            register()
        }

        func register() {
            guard window != nil else { return }
            MainActor.assumeIsolated { GuideCenter.shared.register(target, self) }
        }

        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }
}

/// 主窗口登记（圆环教学在首页弹，要知道往哪个窗口放）。
struct GuideWindowProbe: NSViewRepresentable {
    func makeNSView(context: Context) -> Probe { Probe() }
    func updateNSView(_ nsView: Probe, context: Context) {}

    final class Probe: NSView {
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            guard let window else { return }
            MainActor.assumeIsolated { GuideCenter.shared.registerWindow(window) }
        }

        override func hitTest(_ point: NSPoint) -> NSView? { nil }
    }
}
