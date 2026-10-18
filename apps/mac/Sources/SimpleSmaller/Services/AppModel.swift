import AppKit
import Foundation
import Observation
import OSLog
import QuickLookThumbnailing
import SmallerCore

/// 应用状态的入口：当前类型、首页、三个工作区、提示条、最近记录。
/// 具体职责分在几个扩展里：
/// - AppModel+Files：文件进出（拖入分类、元信息、解锁、剪贴板、移除清空、合并页序）
/// - AppModel+Batch：一批的处理（开始、跑引擎、进度结果、完成、取消、重试、撤销）
/// - AppModel+Activity：处理中不睡眠、Dock 进度与角标
@Observable
@MainActor
final class AppModel {
    static let shared = AppModel()
    static let log = Logger(subsystem: "cn.it-simple.smaller", category: "job")

    let engine = Engine(decoder: ImageIODecoder())
    let store = RecipeStore()
    let settings = AppSettings.shared

    /// 切到某一类就离开首页（⌘1/2/3、拖入文件、启动参数都走这里）。
    var selectedType: MediaType = .image { didSet { showHome = false } }
    /// 首页：打开应用时显示；左上返回回到这里，各类的文件保留。
    var showHome = true
    private(set) var workspaces: [MediaType: Workspace] = [:]
    var toast: String?
    /// 最近完成的任务（菜单栏小窗）。
    var recent: [RecentJob] = []

    // 处理中（见 AppModel+Activity）。
    var activity: NSObjectProtocol?
    /// 后台完成、还没回到应用时 Dock 上的角标数。
    var unseen = 0

    init() {
        for type in MediaType.allCases {
            // 按设置里的顺序取第一个（序列帧、转长图属于另一个分段，跳过）。
            let first = store.ordered(type).first { !$0.video.outputFrames && !$0.pdf.longImage } ?? store.builtIns(for: type).first!
            workspaces[type] = Workspace(type: type, recipe: first)
        }
    }

    var current: Workspace { workspaces[selectedType]! }
    func workspace(_ type: MediaType) -> Workspace { workspaces[type]! }

    func reveal(_ urls: [URL]) {
        guard !urls.isEmpty else { return }
        NSWorkspace.shared.activateFileViewerSelecting(urls)
    }

    func flash(_ text: String) {
        toast = text
        Task { @MainActor in
            try? await Task.sleep(for: .seconds(3))
            if self.toast == text { self.toast = nil }
        }
    }
}
