import AppKit
import Foundation
import Observation
import OSLog
import QuickLookThumbnailing
import SmallerCore

/// 处理中：不让系统睡眠，Dock 显示进度，后台完成时给角标。
extension AppModel {
    /// 有任务在跑时，不让系统闲置睡眠（屏幕可以照常关）。
    func beginActivity() {
        if activity == nil {
            activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiated, .idleSystemSleepDisabled], reason: "正在压缩文件")
        }
        updateDock()
    }

    func endActivityIfIdle() {
        guard !isBusy, let a = activity else { updateDock(); return }
        ProcessInfo.processInfo.endActivity(a)
        activity = nil
        updateDock()
        // 大图处理完，分配器会把释放的大块内存留着备用（几百 MB）；空闲下来就还给系统。
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 1) { malloc_zone_pressure_relief(nil, 0) }
    }

    /// 主窗口任意一类在跑，或圆环 / 小窗直接处理的任务没完。
    var isBusy: Bool { MediaType.allCases.contains { workspace($0).running } || RingController.shared.hasRunning || FolderWatcher.shared.hasRunning }

    /// Dock：处理中写「3/9」；后台做完写完成个数，回到应用后清掉。
    func updateDock() {
        let running = MediaType.allCases.map(workspace).filter(\.running)
        if let ws = running.first {
            NSApp.dockTile.badgeLabel = "\(ws.finishedCount)/\(ws.files.count)"
        } else {
            NSApp.dockTile.badgeLabel = unseen > 0 ? "\(unseen)" : nil
        }
    }

    func noteFinishedInBackground(_ n: Int) {
        if !NSApp.isActive { unseen += n }
        updateDock()
    }

    func clearUnseen() {
        unseen = 0
        updateDock()
    }
}
