import AppKit
import SmallerCore
import SwiftUI

/// 剪贴板自动压缩（默认关，设置 → 自动）：复制了图片（截图、网页里的图），自动换成压缩版，
/// 粘贴到公众号、飞书、微信就是小图。只处理图片数据，不碰复制的文件、文字。
/// 换过的内容带一个本应用的标记，不会被自己再压一遍。
@MainActor
final class ClipboardWatcher {
    static let shared = ClipboardWatcher()
    static let marker = NSPasteboard.PasteboardType("cn.it-simple.smaller.compressed")

    private var timer: Timer?
    private var lastChange = NSPasteboard.general.changeCount
    private var working = false

    func refresh() {
        if AppSettings.shared.clipboardAuto {
            guard timer == nil else { return }
            lastChange = NSPasteboard.general.changeCount
            timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.check() }
            }
        } else {
            timer?.invalidate()
            timer = nil
        }
    }

    private func check() {
        let pb = NSPasteboard.general
        guard pb.changeCount != lastChange, !working else { return }
        lastChange = pb.changeCount
        let types = pb.types ?? []
        // 复制的是文件（访达里 ⌘C）不动；只认图片数据。
        guard !types.contains(Self.marker), !types.contains(.fileURL),
              let (data, ext) = Self.imageData(pb), data.count < 80_000_000 else { return }
        working = true
        let change = pb.changeCount
        let maxEdge = AppSettings.shared.clipboardMaxEdge
        let engine = AppModel.shared.engine
        Task.detached(priority: .userInitiated) {
            let tmp = FileManager.default.temporaryDirectory.appendingPathComponent("smaller-clip-\(UUID().uuidString).\(ext)")
            defer { try? FileManager.default.removeItem(at: tmp) }
            let result: ClipboardImage?? = (try? data.write(to: tmp)).flatMap { _ in try? engine.compressForClipboard(path: tmp.path, maxLongEdge: UInt32(maxEdge)) }
            await MainActor.run {
                defer { self.working = false }
                // 这期间用户又复制了别的，就不覆盖。
                guard let out = result ?? nil, NSPasteboard.general.changeCount == change else { return }
                let pb = NSPasteboard.general
                let type: NSPasteboard.PasteboardType = out.png ? .png : NSPasteboard.PasteboardType("public.jpeg")
                pb.declareTypes([type, Self.marker], owner: nil)
                pb.setData(out.bytes, forType: type)
                pb.setData(Data(), forType: Self.marker)
                self.lastChange = pb.changeCount
                HUD.show("剪贴板图片 \(Format.size(out.beforeBytes)) → \(Format.size(UInt64(out.bytes.count)))")
            }
        }
    }

    /// 剪贴板里的原始图片数据与扩展名（PNG 优先，其次 TIFF、JPEG）。
    private static func imageData(_ pb: NSPasteboard) -> (Data, String)? {
        if let d = pb.data(forType: .png) { return (d, "png") }
        if let d = pb.data(forType: NSPasteboard.PasteboardType("public.jpeg")) { return (d, "jpg") }
        if let d = pb.data(forType: .tiff) { return (d, "tiff") }
        return nil
    }
}

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
