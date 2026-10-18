import QuickLook
import SwiftUI

/// 文件列表（设计稿 02 / 05）。图片、视频、PDF 共用，按类型换列。
struct FileTableView: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var unlocking: FileItem?

    var body: some View {
        VStack(spacing: 0) {
            table
            if let item = compareItem {
                Divider()
                CompareView(item: item)
                    .frame(height: 280)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.3), value: compareItem?.id)
    }

    /// 选中一张已完成的图时显示前后对比。
    private var compareItem: FileItem? {
        guard ws.type == .image, ws.selection.count == 1, let item = ws.selectedItems.first, item.state == .done, !item.outputs.isEmpty else { return nil }
        return item
    }

    private var table: some View {
        Table(of: FileItem.self, selection: $ws.selection) {
            TableColumn("名称") { item in
                HStack(spacing: 8) {
                    Thumb(image: item.thumbnail).frame(width: 22, height: 22).accessibilityHidden(true)
                    Text(item.name).lineLimit(1).truncationMode(.middle)
                }
                .help(item.url.path)
            }
            .width(min: 110)
            TableColumn(ws.type == .pdf ? "页数" : ws.type == .video ? "时长 · 尺寸" : "尺寸") { item in
                Text(detail(item))
                    .foregroundStyle(item.metaError == nil ? AnyShapeStyle(Bento.secondary) : AnyShapeStyle(Color.warning))
                    .monospacedDigit()
                    .help(Self.explain(item.metaError))
            }
            // 列宽按设计稿固定：尺寸 104、原大小 80、输出 80、变化 64、状态 92；名称列吃掉其余宽度。
            .width(min: ws.type == .video ? 128 : 88, ideal: ws.type == .video ? 150 : 104)
            TableColumn("原大小") { item in
                Text(Format.size(item.bytes)).foregroundStyle(Bento.secondary).monospacedDigit()
            }
            .width(min: 64, ideal: 80)
            // 不做试压：点「开始」写出后才有输出体积。
            TableColumn("输出") { item in output(item) }
                .width(min: 64, ideal: 80)
            TableColumn("变化") { item in change(item) }
                .width(min: 52, ideal: 64)
            TableColumn("状态") { item in StateLabel(item: item, queued: ws.running) }
                .width(min: 76, ideal: 92)
        } rows: {
            ForEach(ws.files) { item in
                // 处理完的行可以直接拖出：压好的文件拖进微信、浏览器、邮件。
                TableRow(item)
                    .itemProvider { item.dragProvider() }
            }
        }
        .contextMenu(forSelectionType: FileItem.ID.self) { ids in
            let items = ws.files.filter { ids.contains($0.id) }
            Button("在访达中显示") { model.reveal(items.flatMap(\.revealURLs)) }
            if let locked = items.first(where: { $0.metaError == "有打开密码" }) {
                Button("输入密码…") { unlocking = locked }
            }
            Button("从列表移除") { model.remove(ids, from: ws) }.disabled(ws.running)
        } primaryAction: { ids in
            model.reveal(ws.files.filter { ids.contains($0.id) }.flatMap(\.revealURLs))
        }
        .onDeleteCommand { model.remove(ws.selection, from: ws) }
        .quickLookSelection(ws)
        .sheet(item: $unlocking) { item in PasswordSheet(item: item) }
        // 与访达列表、设计稿一致：不画行线，用交替底色区分行。
        .tableStyle(.inset)
        .alternatingRowBackgrounds(.enabled)
        .modifier(NoTopScrollEdge())
        // 表格的滚动视图会延伸到卡片外、把表头底色画上去；裁在自己的区域内。
        .clipped()
    }

    /// 列里写短的原因，鼠标停上去给一句怎么办。
    static func explain(_ error: String?) -> String {
        switch error {
        case nil: ""
        case "文件不完整": "文件不完整，可能没下载完。重新下载后点「重试失败的」"
        case "文件损坏": "文件损坏，读不出来，换一份原文件再试"
        case "有打开密码": "右键 →「输入密码…」，密码只在这次处理里用，不保存"
        case let e?: e
        }
    }

    private func detail(_ item: FileItem) -> String {
        if let e = item.metaError { return e }
        switch ws.type {
        case .image: return item.dimensions
        case .video: return item.duration > 0 ? "\(Format.duration(item.duration)) · \(item.width) × \(item.height)" : "—"
        case .pdf: return item.type == .image ? item.dimensions : (item.pages > 0 ? "\(item.pages)" : "—")
        }
    }

    @ViewBuilder private func output(_ item: FileItem) -> some View {
        switch item.state {
        case .done:
            Text(Format.size(item.afterBytes)).monospacedDigit()
        case .kept:
            Text("—").foregroundStyle(Bento.secondary)
        default:
            // 还没处理的行不写字（设计稿同）。
            EmptyView()
        }
    }

    @ViewBuilder private func change(_ item: FileItem) -> some View {
        if item.state == .kept {
            Text(ws.type == .pdf ? "已很小" : "+0%").foregroundStyle(Bento.secondary)
        } else if item.state == .done, item.afterBytes > 0, item.bytes > 0 {
            Text(Format.change(item.bytes, item.afterBytes))
                .foregroundStyle(item.afterBytes < item.bytes ? Color.saving : Color.warning)
                .monospacedDigit()
        }
    }
}

struct StateLabel: View {
    let item: FileItem
    /// 处理中排队的行才写「等待」；还没点开始时留空。
    var queued = true

    var body: some View {
        switch item.state {
        case .waiting where item.metaError == "有打开密码":
            Text("需要密码").foregroundStyle(Color.warning).help("右键 →「输入密码…」，密码只在这次处理里用，不保存")
        case .waiting:
            if queued { Text("等待").foregroundStyle(Bento.secondary) }
        case .running(let p):
            // 图片一张很快，没有中间进度，只标「处理中」；视频与 PDF 带百分比。
            Text(item.type == .image || p <= 0 ? "处理中" : "处理中 \(Int(p * 100))%")
                .foregroundStyle(Color.brand)
                .monospacedDigit()
        case .done:
            Text(item.outputs.count > 1 ? "\(item.outputs.count) 个文件" : "完成").foregroundStyle(Color.saving)
        case .kept:
            Text(item.keptText).foregroundStyle(Color.warning)
                .help("不用处理：压完没有变小，或已经符合要求（比如本来就没有声音），原文件保持不动")
        case .failed(let message):
            Text("失败").foregroundStyle(Bento.failed).help(FileTableView.explain(message))
        case .cancelled:
            Text("已停止").foregroundStyle(Bento.secondary)
        }
    }
}
