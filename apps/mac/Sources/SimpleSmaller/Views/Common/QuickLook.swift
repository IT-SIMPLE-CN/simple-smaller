import QuickLook
import SwiftUI

extension Notification.Name {
    static let quickLookSelection = Notification.Name("SimpleSmaller.quickLookSelection")
}

/// 主窗口里按空格转成「快速查看」（和访达一样）；焦点在输入框、按钮上时不拦。
struct SpaceKeyWatcher: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { Probe() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    final class Probe: NSView {
        private var monitor: Any?
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let monitor { NSEvent.removeMonitor(monitor); self.monitor = nil }
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] e in
                guard e.keyCode == 49, e.modifierFlags.intersection([.command, .option, .control, .shift]).isEmpty,
                      let w = self?.window, e.window === w else { return e }
                // 焦点在表格里，或没有控件拿着焦点（点表格行时焦点留在窗口上）才接；在输入框、按钮上按空格照常。
                let inTable = (w.firstResponder as? NSView).map { sequence(first: $0, next: { $0.superview }).contains { $0 is NSTableView } } ?? false
                guard inTable || w.firstResponder === w else { return e }
                NotificationCenter.default.post(name: .quickLookSelection, object: nil)
                return nil
            }
        }
        deinit { if let monitor { NSEvent.removeMonitor(monitor) } }
    }
}
