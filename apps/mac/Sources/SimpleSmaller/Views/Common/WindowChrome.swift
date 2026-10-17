import QuickLook
import SwiftUI
import UniformTypeIdentifiers

/// macOS 26 的滚动边缘效果会在表头上方加一层渐隐，放在卡片里不好看；关掉顶部这一侧。
struct NoTopScrollEdge: ViewModifier {
    func body(content: Content) -> some View {
        if #available(macOS 26.0, *) {
            content.scrollEdgeEffectHidden(true, for: .top)
        } else {
            content
        }
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

/// 标题栏也铺窗口底色（macOS 15 起），和便当盒底连成一片。
struct WindowBackground: ViewModifier {
    func body(content: Content) -> some View {
        if #available(macOS 15.0, *) {
            content.containerBackground(Bento.window, for: .window)
        } else {
            content
        }
    }
}
