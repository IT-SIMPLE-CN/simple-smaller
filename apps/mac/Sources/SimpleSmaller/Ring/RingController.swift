import AppKit
import SwiftUI

/// 访达圆环的总调度（移植 dotnfilm-bots 后按本项目重写）：
/// 全局监听 ⌥+点击 → 找出指针下的图片 / 视频 / PDF / 目录 → 在点击处弹圆环 → 选中即直接处理 → 指针旁给结果卡。
///
/// 只监听、不拦截：访达照样收到这次点击，⌥ 单击在访达里约等于普通单击。按下后拖动超过 4 点当作访达的
/// ⌥ 拖动（复制文件），不弹圆环。识别文件要用辅助功能接口，未授权时不装监听。
@MainActor
final class RingController {
    static let shared = RingController()

    private var clickMonitor: Any?
    private var dismissMonitor: Any?
    private var escMonitors: [Any] = []
    private var trustObserver: NSObjectProtocol?
    private var press: (location: NSPoint, hit: FileHit.Hit, dragged: Bool)?

    private var ringPanel: NSPanel?
    private var cardPanel: NSPanel?
    private var cardTimer: Timer?
    private var runs: [QuickRun] = []

    var isTrusted: Bool { FileHit.isTrusted }
    var hasRunning: Bool { runs.contains { !$0.isFinished } }
    func requestTrust() { FileHit.requestTrust() }

    /// 按开关与授权装卸监听。用户在系统设置里改了辅助功能授权时，系统会发分布式通知，稍等再查。
    func refresh() {
        if trustObserver == nil {
            trustObserver = DistributedNotificationCenter.default().addObserver(
                forName: NSNotification.Name("com.apple.accessibility.api"), object: nil, queue: .main
            ) { [weak self] _ in
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) { MainActor.assumeIsolated { self?.refresh() } }
            }
        }
        let active = AppSettings.shared.ringEnabled && isTrusted
        if active, clickMonitor == nil {
            clickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .leftMouseDragged, .leftMouseUp]) { [weak self] event in
                MainActor.assumeIsolated { self?.handle(event) }
            }
        } else if !active, let monitor = clickMonitor {
            NSEvent.removeMonitor(monitor)
            clickMonitor = nil
            dismissRing()
        }
    }

    private func handle(_ event: NSEvent) {
        let location = NSEvent.mouseLocation
        switch event.type {
        case .leftMouseDown:
            // 按下时就认文件与多选：访达松手那一刻会进入改名、多选也会收成一张，松手再认就晚了。
            press = nil
            let mods = event.modifierFlags.intersection([.option, .command, .control, .shift])
            if mods == [.option], let hit = FileHit.hit(at: location) { press = (location, hit, false) }
        case .leftMouseDragged:
            if let start = press?.location, hypot(location.x - start.x, location.y - start.y) > 4 { press?.dragged = true }
        case .leftMouseUp:
            defer { press = nil }
            guard let press, !press.dragged, event.modifierFlags.contains(.option) else { return }
            open(press.hit, at: press.location)
        default:
            break
        }
    }

    // MARK: 圆环

    /// 从外部（如测试、菜单）直接在某处为一批文件弹圆环。
    func open(urls: [URL], at point: NSPoint) {
        guard let first = urls.first, let kind = FileHit.kind(of: first) else { return }
        open(FileHit.Hit(file: first, kind: kind, batch: urls), at: point)
    }

    private func open(_ hit: FileHit.Hit, at point: NSPoint) {
        dismissRing()
        let type: MediaType = switch hit.kind {
        case .video: .video
        case .pdf: .pdf
        case .image, .folder: .image   // 目录按图片配方处理里面的图片，结构照搬到旁边的新文件夹
        }
        let urls = [hit.file] + hit.batch.filter { $0 != hit.file }
        let unit = hit.kind == .folder ? "个目录" : type == .image ? "张" : type == .video ? "段" : "个"
        let badge = urls.count > 1 || hit.kind == .folder ? "\(urls.count) \(unit)" : ""
        let slots = RingMenu.slots(for: type, store: AppModel.shared.store)

        let view = RingView(slots: slots, badge: badge,
                            onChoose: { [weak self] choice in self?.choose(choice, urls: urls, type: type, at: point) },
                            onDismiss: { [weak self] in self?.dismissRing() })
        let panel = Self.panel(size: CGSize(width: RingView.size, height: RingView.size), content: view)
        // 圆心落在点击处；贴近屏幕边缘时整体挪进可见区域。
        var origin = NSPoint(x: point.x - RingView.size / 2, y: point.y - RingView.size / 2)
        if let screen = NSScreen.screens.first(where: { NSMouseInRect(point, $0.frame, false) }) {
            let v = screen.visibleFrame
            origin.x = min(max(origin.x, v.minX), v.maxX - RingView.size)
            origin.y = min(max(origin.y, v.minY), v.maxY - RingView.size)
        }
        panel.setFrameOrigin(origin)
        panel.orderFrontRegardless()
        ringPanel = panel
        installDismissMonitors()
        // 对已选中的文件点名字，访达松手后会进入改名；弹出后查两次，正在改的是这个文件才取消。
        for delay in [0.25, 0.8] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { [weak self] in
                MainActor.assumeIsolated {
                    guard self?.ringPanel === panel else { return }
                    FileHit.cancelFinderRename(of: hit.file)
                }
            }
        }
    }

    private func choose(_ choice: RingChoice, urls: [URL], type: MediaType, at point: NSPoint) {
        dismissRing()
        switch choice.action {
        case .openMain:
            let model = AppModel.shared
            model.selectedType = type
            model.add(urls, preferred: type)
            WindowOpener.showMain()
        case .run(let recipe):
            let run = QuickRun(recipe: recipe, urls: urls, type: type)
            track(run, at: point)
            run.start()
        }
    }

    /// 给一个直接处理的任务配上结果卡（圆环与「服务」共用）。
    func track(_ run: QuickRun, at point: NSPoint) {
        runs.append(run)
        showCard(for: run, at: point)
        AppModel.shared.beginActivity()
        run.onFinished = { [weak self] in
            self?.scheduleCardClose()
            AppModel.shared.noteFinishedInBackground(run.outputs.isEmpty ? 0 : run.urls.count)
            AppModel.shared.endActivityIfIdle()
        }
    }

    func cancelAll() { runs.forEach { $0.cancel() } }

    func dismissRing() {
        guard let panel = ringPanel else { return }
        ringPanel = nil
        removeDismissMonitors()
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.15
            panel.animator().alphaValue = 0
        }, completionHandler: { panel.orderOut(nil) })
    }

    private func installDismissMonitors() {
        removeDismissMonitors()
        // 全局监听看不到落在本应用浮窗上的点击，所以这里收到的一定在圆环外。
        dismissMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown, .otherMouseDown, .scrollWheel]) { [weak self] _ in
            MainActor.assumeIsolated { self?.dismissRing() }
        }
        let esc: (NSEvent) -> Void = { [weak self] e in
            if e.keyCode == 53 { MainActor.assumeIsolated { self?.dismissRing() } }
        }
        if let g = NSEvent.addGlobalMonitorForEvents(matching: .keyDown, handler: esc) { escMonitors.append(g) }
        if let l = NSEvent.addLocalMonitorForEvents(matching: .keyDown, handler: { e in esc(e); return e }) { escMonitors.append(l) }
    }

    private func removeDismissMonitors() {
        if let m = dismissMonitor { NSEvent.removeMonitor(m) }
        dismissMonitor = nil
        escMonitors.forEach(NSEvent.removeMonitor)
        escMonitors = []
    }

    // MARK: 结果卡

    func showCard(for run: QuickRun, at point: NSPoint) {
        closeCard()
        let view = ResultCard(run: run, onClose: { [weak self] in self?.closeCard() })
            .onHover { [weak self] inside in
                // 指针在卡上时不收；离开 6 秒后收起。
                if inside { self?.cardTimer?.invalidate() } else { self?.scheduleCardClose() }
            }
        let size = CGSize(width: 340, height: 210)
        let panel = Self.panel(size: size, content: view)
        var origin = NSPoint(x: point.x + 12, y: point.y - size.height + 20)
        if let screen = NSScreen.screens.first(where: { NSMouseInRect(point, $0.frame, false) }) {
            let v = screen.visibleFrame
            origin.x = min(max(origin.x, v.minX), v.maxX - size.width)
            origin.y = min(max(origin.y, v.minY), v.maxY - size.height)
        }
        panel.setFrameOrigin(origin)
        panel.alphaValue = 0
        panel.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? 0 : 0.2
            panel.animator().alphaValue = 1
        }
        cardPanel = panel
    }

    private func scheduleCardClose() {
        cardTimer?.invalidate()
        cardTimer = Timer.scheduledTimer(withTimeInterval: 6, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated { self?.closeCard() }
        }
    }

    func closeCard() {
        cardTimer?.invalidate()
        guard let panel = cardPanel else { return }
        cardPanel = nil
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.2
            panel.animator().alphaValue = 0
        }, completionHandler: { panel.orderOut(nil) })
        runs.removeAll { $0.isFinished }
    }

    /// 不抢焦点的透明浮窗：访达保持在前台，点击直接落到圆环上。
    private static func panel<V: View>(size: CGSize, content: V) -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(origin: .zero, size: size), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = .popUpMenu
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.becomesKeyOnlyIfNeeded = true
        let host = FirstMouseHostingView(rootView: content)
        // 浮窗大小由这里定，不让 SwiftUI 按内容改窗口尺寸（会把窗口挪走）。
        host.sizingOptions = []
        host.frame = NSRect(origin: .zero, size: size)
        panel.contentView = host
        return panel
    }
}

/// 第一次点击就生效（不用先点一下激活浮窗）。
final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}
