import AppKit
import SmallerCore
import SwiftUI

/// 屏幕右上角的轻提示，2.5 秒后淡出；不抢焦点。
@MainActor
enum HUD {
    private static var panel: NSPanel?
    private static var timer: Timer?

    static func show(_ text: String) {
        timer?.invalidate()
        panel?.orderOut(nil)
        let view = HStack(spacing: 8) {
            Image(systemName: "checkmark.circle.fill").foregroundStyle(Color.saving)
            Text(text).font(.system(size: 12, weight: .medium)).monospacedDigit()
        }
        .padding(.horizontal, 14)
        .frame(height: 34)
        .background(.regularMaterial, in: Capsule())
        .overlay(Capsule().strokeBorder(Color.primary.opacity(0.08), lineWidth: 0.5))
        .shadow(color: .black.opacity(0.15), radius: 8, y: 3)
        .padding(10)
        let host = NSHostingView(rootView: view)
        host.sizingOptions = []
        let size = host.fittingSize
        host.frame = NSRect(origin: .zero, size: size)
        let p = NSPanel(contentRect: host.frame, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.level = .statusBar
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        p.ignoresMouseEvents = true
        p.contentView = host
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        if let v = screen?.visibleFrame { p.setFrameOrigin(NSPoint(x: v.maxX - size.width - 8, y: v.maxY - size.height - 8)) }
        p.alphaValue = 0
        p.orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { $0.duration = 0.2; p.animator().alphaValue = 1 }
        panel = p
        timer = Timer.scheduledTimer(withTimeInterval: 2.5, repeats: false) { _ in
            MainActor.assumeIsolated {
                NSAnimationContext.runAnimationGroup({ $0.duration = 0.3; p.animator().alphaValue = 0 }, completionHandler: { p.orderOut(nil) })
            }
        }
    }
}
