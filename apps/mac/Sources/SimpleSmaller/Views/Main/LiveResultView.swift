import Photos
import PhotosUI
import SwiftUI

/// 实况的完成页：左边直接看做出来的实况（悬停或点播放会动），右边是文件与去处：
/// 存进「照片」、隔空投送、共享、打开、在访达中显示；改设置再做一次、再来一批。
/// 实况不是为了变小，所以不放「小了多少」的刻度盘。
struct LiveResultView: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var index = 0
    @State private var saving = false
    @State private var shareAnchor: NSView?

    private var done: [FileItem] { ws.files.filter { $0.state == .done } }
    private var failed: [FileItem] { ws.files.filter { if case .failed = $0.state { return true }; return false } }
    private var current: FileItem? { done.isEmpty ? nil : done[min(index, done.count - 1)] }
    private var currentFiles: LiveLibrary.Files { LiveLibrary.files(current?.outputs ?? []) }
    private var allPairs: [(photo: URL, video: URL)] { done.compactMap { LiveLibrary.files($0.outputs).pair } }
    private var allURLs: [URL] { done.flatMap { LiveLibrary.files($0.outputs).all } }

    var body: some View {
        BentoCard(padding: EdgeInsets(top: 26, leading: 28, bottom: 24, trailing: 28), radius: 26) {
            GeometryReader { geo in
                let panelW: CGFloat = min(336, geo.size.width * 0.36)
                HStack(alignment: .top, spacing: 24) {
                    VStack(alignment: .leading, spacing: 14) {
                        header
                        if let item = current {
                            LivePreview(files: currentFiles)
                                .id(item.id)
                                .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                            if done.count > 1 {
                                FilmStrip(items: done, index: min(index, done.count - 1), select: { index = $0 }) { _ in EmptyView() }
                            }
                        } else {
                            Spacer()
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                    panel.frame(width: panelW)
                }
            }
        }
        .bentoAppear(0)
    }

    private var header: some View {
        HStack(spacing: 12) {
            Image(systemName: done.isEmpty ? "exclamationmark" : "checkmark")
                .font(.system(size: 18, weight: .bold))
                .foregroundStyle(.white)
                .frame(width: 40, height: 40)
                .background(Circle().fill(done.isEmpty ? Bento.failed : Color.brand))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(done.isEmpty ? "没有做成" : "实况做好了").font(.system(size: 20, weight: .semibold))
                Text(subtitle).font(.system(size: 12)).foregroundStyle(Bento.secondary).lineLimit(1)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isHeader)
    }

    private var subtitle: String {
        var parts = ["\(done.count) 个实况"]
        if !failed.isEmpty { parts.append("\(failed.count) 个失败") }
        parts.append(ws.draft.name)
        parts.append(model.settings.outputLocation == .beside ? "存在原视频旁边" : "存在「\((model.settings.outputFolder as NSString).lastPathComponent)」")
        return parts.joined(separator: " · ")
    }

    // MARK: 右侧

    private var panel: some View {
        VStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 8) {
                Text(current?.name ?? "").font(.system(size: 13, weight: .semibold)).lineLimit(1).truncationMode(.middle)
                ForEach(currentFiles.all, id: \.self) { url in fileRow(url) }
                Spacer(minLength: 4)
                Text(hint).font(.system(size: 11)).foregroundStyle(Bento.secondary).fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 16)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .bentoPanel()

            if !failed.isEmpty {
                HStack(spacing: 10) {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(verbatim: "\(failed.count) 个失败").font(.system(size: 13, weight: .semibold))
                        Text(failNote).font(.system(size: 11)).lineLimit(2)
                    }
                    .foregroundStyle(Bento.alertText)
                    Spacer(minLength: 4)
                    Button("重试失败的") { model.retryFailed(ws) }
                        .buttonStyle(.plain)
                        .font(.system(size: 12))
                        .foregroundStyle(Bento.alertText)
                        .padding(.horizontal, 12).frame(height: 30)
                        .background(Capsule().fill(Bento.card))
                }
                .padding(.leading, 14).padding(.trailing, 10).padding(.vertical, 12)
                .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Bento.alertFill))
            }

            if !allPairs.isEmpty {
                Button(action: saveToPhotos) {
                    HStack(spacing: 8) {
                        if saving { ProgressView().controlSize(.small).tint(.white) }
                        Text(allPairs.count > 1 ? "全部存进照片（\(allPairs.count) 个）" : "存进照片")
                    }
                    .font(.system(size: 15, weight: .semibold)).foregroundStyle(.white)
                    .frame(maxWidth: .infinity).frame(height: 56)
                    .background(Capsule().fill(Color.brand))
                    .contentShape(Capsule())
                }
                .buttonStyle(BentoPressStyle())
                .disabled(saving)
                .help("存进「照片」里就是一张实况；开了 iCloud 照片会同步到 iPhone")
            }
            HStack(spacing: 8) {
                wideButton("隔空投送", symbol: "dot.radiowaves.left.and.right") {
                    if !LiveLibrary.airDrop(currentFiles.all) { model.flash("这台 Mac 现在不能隔空投送") }
                }
                .help("把这一个实况的文件一起发到 iPhone 或别的 Mac")
                wideButton("共享…", symbol: "square.and.arrow.up") {
                    guard let anchor = shareAnchor else { return }
                    NSSharingServicePicker(items: currentFiles.all).show(relativeTo: anchor.bounds, of: anchor, preferredEdge: .minY)
                }
                .background(ViewAnchor(view: $shareAnchor))
            }
            HStack(spacing: 8) {
                wideButton("打开", symbol: nil) {
                    if let url = currentFiles.photo ?? currentFiles.motion { NSWorkspace.shared.open(url) }
                }
                wideButton("在访达中显示", symbol: nil) { model.reveal(allURLs) }
            }
            HStack(spacing: 8) {
                wideButton("改设置再做一次", symbol: nil) { ws.showResult = false }
                    .help("回到设置，这一批视频还在")
                wideButton("再来一批", symbol: nil) { model.clear(ws) }
                    .help("清空这一批，拖新的视频进来")
            }
        }
        .disabled(done.isEmpty && failed.isEmpty)
    }

    private var hint: String {
        let f = currentFiles
        if f.pair != nil, f.motion != nil { return "照片和视频要放在一起：存进「照片」或一起隔空投送。.MP.jpg 是给安卓手机的，传过去在相册里会动。" }
        if f.pair != nil { return "照片和视频要放在一起：存进「照片」或一起隔空投送，在 iPhone 上就是实况。" }
        return "传到安卓手机（数据线、网盘、聊天软件发原图）后，在相册里会动。"
    }

    private var failNote: String {
        if failed.count == 1, case .failed(let r) = failed[0].state { return "\(failed[0].name) \(r)" }
        return "回到设置看每个视频的原因"
    }

    private func fileRow(_ url: URL) -> some View {
        let bytes = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize).map { UInt64($0) } ?? 0
        let kind = url.lastPathComponent.lowercased().hasSuffix(".mp.jpg") ? "安卓动态照片" : url.pathExtension.lowercased() == "mov" ? "实况视频" : "实况照片"
        return HStack(spacing: 10) {
            Image(systemName: url.pathExtension.lowercased() == "mov" ? "film" : "photo")
                .font(.system(size: 13))
                .frame(width: 30, height: 30)
                .background(Circle().fill(Bento.card))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 1) {
                Text(url.lastPathComponent).font(.system(size: 12)).lineLimit(1).truncationMode(.middle)
                Text(verbatim: "\(kind) · \(Format.size(bytes))").font(.system(size: 11)).foregroundStyle(Bento.secondary).monospacedDigit()
            }
            Spacer(minLength: 0)
        }
        .contentShape(Rectangle())
        .onDrag {
            let p = NSItemProvider(object: url as NSURL)
            p.suggestedName = url.lastPathComponent
            return p
        }
        .onTapGesture(count: 2) { model.reveal([url]) }
        .help("可以直接拖出；双击在访达中显示")
        .accessibilityElement(children: .combine)
    }

    private func wideButton(_ title: String, symbol: String?, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 6) {
                if let symbol { Image(systemName: symbol).font(.system(size: 12)) }
                Text(title).font(.system(size: 13))
            }
            .frame(maxWidth: .infinity).frame(height: 44)
            .background(Capsule().fill(Bento.fill))
            .contentShape(Capsule())
        }
        .buttonStyle(BentoPressStyle())
    }

    private func saveToPhotos() {
        let pairs = allPairs
        saving = true
        Task {
            let error = await LiveLibrary.saveToPhotos(pairs)
            saving = false
            model.flash(error ?? (pairs.count > 1 ? "已存进照片，\(pairs.count) 个实况" : "已存进照片"))
        }
    }
}

/// 左边的预览：苹果实况用系统的实况视图（悬停或点播放会动）；只有安卓动态照片时显示照片。
private struct LivePreview: View {
    let files: LiveLibrary.Files
    @State private var livePhoto: PHLivePhoto?
    @State private var still: NSImage?
    @State private var playToken = 0

    var body: some View {
        ZStack(alignment: .topLeading) {
            Color(red: 0x1C / 255, green: 0x1C / 255, blue: 0x1E / 255)
            if let livePhoto {
                LivePhotoNSView(livePhoto: livePhoto, playToken: playToken)
                    .padding(12)
            } else if let still {
                Image(nsImage: still).resizable().aspectRatio(contentMode: .fit).padding(12)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            // 系统的实况视图自带「实况」标；只有安卓动态照片时自己标一下。
            if livePhoto == nil, still != nil {
                HStack(spacing: 6) {
                    LineIcon(glyph: .live, size: 13, weight: 1.8)
                    Text("动态照片").font(.system(size: 11, weight: .semibold))
                }
                .foregroundStyle(.white)
                .padding(.horizontal, 8)
                .frame(height: 24)
                .background(Color.black.opacity(0.55), in: Capsule())
                .padding(20)
                .accessibilityHidden(true)
            }
        }
        .overlay(alignment: .bottomTrailing) {
            if livePhoto != nil {
                Button { playToken += 1 } label: {
                    Image(systemName: "play.fill").font(.system(size: 13)).foregroundStyle(.white)
                        .frame(width: 36, height: 36)
                        .background(Circle().fill(Color.black.opacity(0.55)))
                        .contentShape(Circle())
                }
                .buttonStyle(BentoPressStyle())
                .padding(20)
                .help("播放实况")
                .accessibilityLabel("播放实况")
            }
        }
        .environment(\.colorScheme, .dark)
        .task(id: files.all) { await load() }
        .accessibilityElement(children: .contain)
        .accessibilityLabel(files.pair != nil ? "实况预览" : "动态照片预览")
    }

    private func load() async {
        if let pair = files.pair {
            livePhoto = await withCheckedContinuation { (c: CheckedContinuation<PHLivePhoto?, Never>) in
                var resumed = false
                PHLivePhoto.request(withResourceFileURLs: [pair.photo, pair.video], placeholderImage: nil, targetSize: .zero, contentMode: .aspectFit) { photo, info in
                    let degraded = (info[PHLivePhotoInfoIsDegradedKey] as? Bool) ?? false
                    let failed = info[PHLivePhotoInfoErrorKey] != nil || (info[PHLivePhotoInfoCancelledKey] as? Bool) == true
                    guard !resumed, !degraded || failed else { return }
                    resumed = true
                    c.resume(returning: failed ? nil : photo)
                }
            }
        }
        if livePhoto == nil, let url = files.photo ?? files.motion {
            still = NSImage(contentsOf: url)
        }
    }
}

/// 系统的实况视图（PhotosUI）。`playToken` 变了就从头播一遍。
private struct LivePhotoNSView: NSViewRepresentable {
    let livePhoto: PHLivePhoto
    let playToken: Int

    func makeNSView(context: Context) -> PHLivePhotoView {
        let v = PHLivePhotoView()
        v.contentMode = .aspectFit
        v.livePhoto = livePhoto
        context.coordinator.token = playToken
        return v
    }

    func updateNSView(_ v: PHLivePhotoView, context: Context) {
        if v.livePhoto !== livePhoto { v.livePhoto = livePhoto }
        if context.coordinator.token != playToken {
            context.coordinator.token = playToken
            v.startPlayback(with: .full)
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator() }
    final class Coordinator { var token = 0 }
}

/// 取到 SwiftUI 按钮背后的 NSView，给系统共享菜单定位用。
struct ViewAnchor: NSViewRepresentable {
    @Binding var view: NSView?

    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { view = v }
        return v
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}
