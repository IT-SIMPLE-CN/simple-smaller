import AppKit
import SwiftUI

/// 脚本化验证（和 `--open`、`--segment`、`--start` 同一用途）：只有带 `--script` 启动时才听。
/// 屏幕锁着、点不了也截不了屏的时候，用分布式通知驱动应用、把主窗口离屏画成图。
/// 发送：通知名 `cn.it-simple.smaller.script`，object 是一行命令，例如
///   snapshot /路径/a.png · size 1280 800 · appearance dark · home · enter video 实况 /路径/x.mp4
///   segment 实况 · recipe vid.live.both · live 2.0 4.5 3.1（片段开始 结束 封面）· start · settings · guide · closeguide
enum ScriptHooks {
    static let name = Notification.Name("cn.it-simple.smaller.script")

    @MainActor static func install() {
        guard CommandLine.arguments.contains("--script") else { return }
        DistributedNotificationCenter.default().addObserver(forName: name, object: nil, queue: .main) { note in
            let line = note.object as? String ?? ""
            MainActor.assumeIsolated { run(line) }
        }
    }

    private static var mainWindow: NSWindow? {
        NSApp.windows.filter { $0.isVisible && $0.canBecomeMain }.max { $0.frame.width < $1.frame.width }
    }

    @MainActor private static func run(_ line: String) {
        let parts = line.split(separator: " ", maxSplits: 3).map(String.init)
        guard let cmd = parts.first else { return }
        let model = AppModel.shared
        let ws = model.current
        switch cmd {
        case "snapshot":
            guard parts.count > 1, let window = mainWindow, let view = window.contentView?.superview else { return }
            let path = parts[1...].joined(separator: " ")
            view.layoutSubtreeIfNeeded()
            guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return }
            view.cacheDisplay(in: view.bounds, to: rep)
            // 小教学这类子窗口叠在主窗口上：按位置画上去。
            let image = NSImage(size: view.bounds.size)
            image.addRepresentation(rep)
            let composed = NSImage(size: view.bounds.size, flipped: false) { _ in
                image.draw(in: view.bounds)
                for child in window.childWindows ?? [] where child.isVisible {
                    guard let cv = child.contentView?.superview ?? child.contentView, let r = cv.bitmapImageRepForCachingDisplay(in: cv.bounds) else { continue }
                    cv.cacheDisplay(in: cv.bounds, to: r)
                    let origin = NSPoint(x: child.frame.minX - window.frame.minX, y: child.frame.minY - window.frame.minY)
                    r.draw(in: NSRect(origin: origin, size: cv.bounds.size))
                }
                return true
            }
            if let tiff = composed.tiffRepresentation, let out = NSBitmapImageRep(data: tiff) {
                try? out.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path))
            }
        case "size":
            guard parts.count > 2, let w = Double(parts[1]), let h = Double(parts[2]), let window = mainWindow else { return }
            window.setFrame(NSRect(x: 100, y: 100, width: w, height: h), display: true)
        case "appearance":
            NSApp.appearance = parts.count > 1 && parts[1] == "dark" ? NSAppearance(named: .darkAqua) : NSAppearance(named: .aqua)
        case "home":
            model.showHome = true
        case "enter":
            // 等同首页点小标签（不带文件）或把文件放到卡片的某一格（带文件）。
            guard parts.count > 2, let t = MediaType(rawValue: parts[1]) else { return }
            let urls = parts.count > 3 ? [URL(fileURLWithPath: parts[3])] : []
            model.enter(t, feature: parts[2], with: urls)
            model.showHome = false
        case "segment":
            if parts.count > 1 { model.openSegment(parts[1]) }
        case "recipe":
            if parts.count > 1, let r = model.store.recipe(id: parts[1]) { ws.select(recipe: r) }
        case "live":
            guard let item = ws.currentVideo, parts.count > 1 else { return }
            let nums = line.split(separator: " ").dropFirst().compactMap { Double($0) }
            if nums.count >= 2 { ws.liveRange[item.url] = nums[0]...nums[1] }
            if nums.count >= 3 { ws.liveCover[item.url] = nums[2] }
        case "start":
            model.start(ws)
        case "back":
            ws.showResult = false
        case "guide":
            GuideCenter.shared.showFeature(ws.type)
        case "guidestep":
            if parts.count > 1, let n = Int(parts[1]) { GuideCenter.shared.step = n }
        default:
            break
        }
    }
}
