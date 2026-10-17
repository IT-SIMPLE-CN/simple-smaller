import QuickLook
import SwiftUI

/// 文件格子 / 文件行的通用操作：单击选中，双击在访达中显示，拖出，右键菜单。
/// 缩略图、完成页列表都用这一套，行为一致。
struct FileInteractions: ViewModifier {
    @Environment(AppModel.self) private var model
    let item: FileItem
    let ws: Workspace
    var removable = true

    func body(content: Content) -> some View {
        content
            .onTapGesture { ws.selection = [item.id] }
            .simultaneousGesture(TapGesture(count: 2).onEnded { model.reveal(item.revealURLs) })
            .onDrag { item.dragProvider() }
            .contextMenu {
                Button("在访达中显示") { model.reveal(item.revealURLs) }
                if removable {
                    Button("从列表移除") { model.remove([item.id], from: ws) }.disabled(ws.running)
                }
            }
    }
}

/// 空格 / ⌘Y 快速查看选中的那一个（处理完看输出，没处理看原文件，和访达一致）。
struct QuickLookSelection: ViewModifier {
    let ws: Workspace
    @State private var url: URL?

    func body(content: Content) -> some View {
        content
            .quickLookPreview($url)
            .onReceive(NotificationCenter.default.publisher(for: .quickLookSelection)) { _ in
                url = ws.selectedItems.first?.primaryURL
            }
            .background(SpaceKeyWatcher())
    }
}

extension View {
    func fileInteractions(_ item: FileItem, in ws: Workspace, removable: Bool = true) -> some View {
        modifier(FileInteractions(item: item, ws: ws, removable: removable))
    }

    func quickLookSelection(_ ws: Workspace) -> some View {
        modifier(QuickLookSelection(ws: ws))
    }
}
