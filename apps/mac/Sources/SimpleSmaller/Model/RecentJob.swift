import AppKit

/// 最近完成的一批（菜单栏小窗的「最近」）。
struct RecentJob: Identifiable {
    var id = UUID()
    let title: String
    let recipe: String
    let before: UInt64
    let after: UInt64
    let date: Date
    let outputs: [URL]
    var thumbnail: NSImage?
}
