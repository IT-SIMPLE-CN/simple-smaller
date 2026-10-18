import SwiftUI

/// 主窗口（设计稿 V2.1）：没有文件时是首页；选了一类就是这一类的便当盒工作区，做完换到完成页。
/// 工具栏用系统的（左边返回与标题、正中分段），浅灰窗口底上铺白色卡片。
struct MainView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var windowWidth: CGFloat = 1280

    var body: some View {
        let ws = model.current
        ZStack {
            // 首页 ↔ 工作区：整体轻微缩放 + 淡入淡出，里面的卡片各自再弹入。
            if model.showHome {
                HomeView()
                    .transition(.asymmetric(insertion: .scale(scale: 0.98).combined(with: .opacity), removal: .opacity))
            } else {
                WorkView(ws: ws)
                    .id(ws.type)
                    .transition(.asymmetric(insertion: .scale(scale: 0.98).combined(with: .opacity), removal: .opacity))
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onGeometryChange(for: CGFloat.self) { $0.size.width } action: { windowWidth = $0 }
        .background(Bento.window.ignoresSafeArea())
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.42, dampingFraction: 0.86), value: model.showHome)
        .animation(reduceMotion ? .easeOut(duration: 0.2) : .spring(response: 0.42, dampingFraction: 0.86), value: model.selectedType)
        .modifier(HideToolbarSeparator())
        .navigationTitle(model.showHome ? "简单小一点" : title(ws))
        .navigationSubtitle(model.showHome ? "" : subtitle(ws))
        .toolbar {
            if model.showHome { HomeToolbar() } else { WorkToolbar(ws: ws, compactTabs: compactTabs(ws)) }
        }
        .modifier(HideWindowTitle(hidden: model.showHome))
        .overlay(alignment: .bottom) {
            if let toast = model.toast {
                Text(toast)
                    .font(.callout)
                    .padding(.horizontal, 14)
                    .padding(.vertical, 8)
                    .background(.regularMaterial, in: Capsule())
                    .padding(.bottom, 28)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.smooth(duration: 0.3), value: model.toast)
        .background(NoInitialFocus())
        // 拖到窗口任何地方：按类型自动分到图片、视频、PDF。
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: model.showHome ? nil : model.selectedType)
            return true
        }
    }

    /// 顶部大标签居中放；左边标题、右边按钮占掉的地方估一下，放不下文字就只显示图标（不和两边重叠）。
    private func compactTabs(_ ws: Workspace) -> Bool {
        let titles = SegmentPicker.titles(ws.type)
        guard !titles.isEmpty else { return false }
        let tabs = FeatureTabs<Int>.fullWidth(titles)
        func width(_ text: String, _ font: NSFont) -> CGFloat { (text as NSString).size(withAttributes: [.font: font]).width }
        // 红绿灯与返回按钮约 150；标题 15 粗、副标题 11；右边「撤销这一批」约 110。
        let titleEnd = 150 + max(width(title(ws), .systemFont(ofSize: 15, weight: .bold)), width(subtitle(ws), .systemFont(ofSize: 11))) + 16
        let right: CGFloat = ws.showResult && !ws.running ? 130 : 20
        let left = windowWidth / 2 - tabs / 2, rightEdge = windowWidth / 2 + tabs / 2
        return left < titleEnd || rightEdge > windowWidth - right
    }

    /// 视频剪辑、序列帧一次看一段：标题写这段的文件名。
    private func singleVideo(_ ws: Workspace) -> FileItem? {
        guard ws.type == .video, ws.videoSegment != .compress else { return nil }
        return ws.currentVideo
    }

    private func title(_ ws: Workspace) -> String {
        singleVideo(ws)?.name ?? model.selectedType.title
    }

    private func subtitle(_ ws: Workspace) -> String {
        if let v = singleVideo(ws) {
            var parts: [String] = []
            if ws.videoItems.count > 1, let i = ws.videoItems.firstIndex(where: { $0.id == v.id }) {
                parts.append("第 \(i + 1) / \(ws.videoItems.count) 个")
            }
            parts += ["\(v.width) × \(v.height)", Format.duration(v.duration)]
            if ws.videoSegment == .frames {
                parts.append("\(Int(v.fps.rounded())) fps")
                parts.append("\(Int((v.duration * v.fps).rounded())) 帧")
            } else {
                if v.hdr { parts.append("HDR") }
                parts.append(Format.size(v.bytes))
            }
            return parts.joined(separator: " · ")
        }
        guard !ws.files.isEmpty else { return "没有文件" }
        if ws.running { return "处理中 \(ws.finishedCount) / \(ws.files.count)" }
        if ws.showResult { return "\(ws.files.count) 个文件 · 已完成" }
        var parts = ["\(ws.files.count) 个文件"]
        if ws.isCropping {
            let items = ws.cropItems
            parts.append(ws.cropTitle)
            if ws.cropAdjustedCount > 0 { parts.append("调过 \(ws.cropAdjustedCount) / \(items.count) 张") }
            return parts.joined(separator: " · ")
        }
        if ws.type == .pdf, ws.pdfSegment != .fromImages {
            parts.append("\(ws.pdfSegment == .merge ? ws.pageOrder.count : ws.files.reduce(0) { $0 + $1.pages }) 页")
        }
        parts.append(Format.size(ws.totalBytes))
        return parts.joined(separator: " · ")
    }
}
