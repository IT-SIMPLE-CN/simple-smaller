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
