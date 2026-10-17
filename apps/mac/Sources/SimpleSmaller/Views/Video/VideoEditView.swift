import AVFoundation
import SwiftUI

/// 视频「剪辑」（设计稿 04）：上面是画面与比例框，下面是播放、截取与时间轴。
/// 拖时间轴两端截取，单击时间轴设为封面；比例框可拖动取景。多段视频时底部可切换。
struct VideoEditView: View {
    @Bindable var ws: Workspace
    @State private var playback = VideoPlayback()
    @FocusState private var focused: Bool

    var body: some View {
        VStack(spacing: 10) {
            if let item = ws.currentVideo {
                VideoStage(ws: ws, item: item, playback: playback)
                    .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                TrimBar(ws: ws, item: item, playback: playback)
                if ws.videoItems.count > 1 {
                    VideoStrip(ws: ws)
                }
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .onAppear { load(); focused = true }
        .onChange(of: ws.currentVideo?.url) { _, _ in load() }
        .onDisappear { playback.pause() }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(.space) { playback.toggle(); return .handled }
    }

    private func load() {
        guard let item = ws.currentVideo else { return }
        playback.load(item.url, range: ws.trimRange(item))
    }
}

/// 画面：深色底上等比放视频，比例框外压暗，左上角写「9:16 · 1080 × 1920」。
struct VideoStage: View {
    @Bindable var ws: Workspace
    let item: FileItem
    let playback: VideoPlayback
    @State private var dragStart: CGRect?

    var body: some View {
        GeometryReader { geo in
            let layout = CropCanvas.Layout(canvas: geo.size, imageSize: CGSize(width: item.width, height: item.height), bottomInset: 24)
            let rect = CGRect(origin: layout.origin, size: layout.display)
            ZStack(alignment: .topLeading) {
                Group {
                    if MediaType.isAnimated(item.url) { AnimatedImageView(url: item.url) } else { PlayerLayerView(player: playback.player) }
                }
                    .frame(width: rect.width, height: rect.height)
                    .clipped()
                    .offset(x: rect.minX, y: rect.minY)
                    .accessibilityLabel("视频画面")
                if let box = ws.videoCropBox(item) {
                    let view = layout.toView(box)
                    Path { p in p.addRect(rect); p.addRect(view) }
                        .fill(Color.black.opacity(0.5), style: FillStyle(eoFill: true))
                        .allowsHitTesting(false)
                    Rectangle()
                        .strokeBorder(Color.white, lineWidth: 1)
                        .frame(width: view.width, height: view.height)
                        .contentShape(Rectangle())
                        .offset(x: view.minX, y: view.minY)
                        .gesture(moveGesture(layout: layout, box: box))
                        .modifier(CropCanvas.GrabCursor())
                        .accessibilityLabel("取景框，可拖动")
                }
                Text(verbatim: label)
                    .font(.system(size: 11).monospacedDigit())
                    .foregroundStyle(.white)
                    .padding(.horizontal, 6)
                    .padding(.vertical, 2)
                    .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                    .offset(x: rect.minX + 12, y: rect.minY + 12)
                    .allowsHitTesting(false)
            }
            .frame(width: geo.size.width, height: geo.size.height, alignment: .topLeading)
        }
        // 深色底只铺在自己的区域，不伸进工具栏下面。
        .background(Color(red: 0x1C / 255, green: 0x1C / 255, blue: 0x1E / 255), ignoresSafeAreaEdges: [])
        .clipped()
        .environment(\.colorScheme, .dark)
    }

    private var label: String {
        let size = ws.videoOutputSize(item)
        let ratio = ws.draft.video.crop?.name ?? "原比例"
        return "\(ratio) · \(Int(size.width)) × \(Int(size.height))"
    }

    private func moveGesture(layout: CropCanvas.Layout, box: CGRect) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { v in
                let start = dragStart ?? box
                if dragStart == nil { dragStart = box }
                var r = start.offsetBy(dx: v.translation.width / layout.scale, dy: v.translation.height / layout.scale)
                r.origin.x = min(max(0, r.minX), CGFloat(item.width) - r.width)
                r.origin.y = min(max(0, r.minY), CGFloat(item.height) - r.height)
                // 编码要求偶数坐标。
                r.origin = CGPoint(x: (r.minX / 2).rounded() * 2, y: (r.minY / 2).rounded() * 2)
                ws.videoCrop[item.url] = r
            }
            .onEnded { _ in dragStart = nil }
    }
}

/// 播放、截取与时间轴。
struct TrimBar: View {
    @Bindable var ws: Workspace
    let item: FileItem
    let playback: VideoPlayback
    @State private var thumbs: [CGImage] = []
    @State private var dragging: Handle?
    @State private var dragOrigin: Double = 0

    private enum Handle { case start, end }
    private let handleWidth: CGFloat = 10
    private let count = 14

    var body: some View {
        let range = ws.trimRange(item)
        VStack(spacing: 8) {
            HStack(spacing: 12) {
                Button { playback.toggle() } label: {
                    Image(systemName: playback.playing ? "pause.fill" : "play.fill")
                        .font(.system(size: 14))
                        .frame(width: 28, height: 28)
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(playback.playing ? "暂停" : "播放")
                .help("播放 / 暂停（空格）")
                Text(verbatim: Format.timecode(playback.time))
                    .font(.system(size: 12).monospacedDigit())
                Text(verbatim: "截取 \(Format.duration(range.lowerBound)) – \(Format.duration(range.upperBound < 1 ? range.upperBound : range.upperBound.rounded()))（\(Self.seconds(range.upperBound - range.lowerBound))）")
                    .font(.system(size: 12).monospacedDigit())
                    .foregroundStyle(.secondary)
                Spacer()
                Text("拖两端截取 · 单击设为封面")
                    .font(.system(size: 11))
                    .foregroundStyle(.tertiary)
            }
            timeline(range: range)
                .frame(height: 44)
        }
        .padding(.horizontal, 4)
        .task(id: item.url) {
            thumbs = []
            thumbs = await TimelineThumbnails.make(url: item.url, duration: item.duration, count: count, height: 44)
        }
    }

    static func seconds(_ s: Double) -> String {
        if s < 10 { return String(format: "%.1f 秒", s).replacingOccurrences(of: ".0 秒", with: " 秒") }
        let t = Int(s.rounded())
        return t >= 60 ? "\(t / 60) 分 \(t % 60) 秒" : "\(t) 秒"
    }

    private func timeline(range: ClosedRange<Double>) -> some View {
        GeometryReader { geo in
            let w = geo.size.width
            let d = max(0.001, item.duration)
            let x0 = CGFloat(range.lowerBound / d) * w
            let x1 = CGFloat(range.upperBound / d) * w
            let playX = CGFloat(min(max(playback.time, 0), d) / d) * w
            ZStack(alignment: .topLeading) {
                // 缩略图条：每格定宽，图片裁进格子里。
                let cell = (w - CGFloat(count - 1)) / CGFloat(count)
                HStack(spacing: 1) {
                    ForEach(0..<count, id: \.self) { k in
                        Color.clear
                            .frame(width: max(0, cell), height: geo.size.height)
                            .overlay {
                                if k < thumbs.count {
                                    Image(decorative: thumbs[k], scale: 2).resizable().scaledToFill()
                                } else {
                                    Rectangle().fill(.quaternary)
                                }
                            }
                            .clipped()
                    }
                }
                .frame(width: w, alignment: .leading)
                .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                // 截取范围外压淡。
                Rectangle().fill(Color(nsColor: .windowBackgroundColor).opacity(0.7))
                    .frame(width: max(0, x0))
                Rectangle().fill(Color(nsColor: .windowBackgroundColor).opacity(0.7))
                    .frame(width: max(0, w - x1))
                    .offset(x: x1)
                // 截取框：上下 3pt、两端 10pt 的品牌红把手。
                TrimFrame(handle: handleWidth)
                    .frame(width: max(handleWidth * 2, x1 - x0))
                    .offset(x: x0)
                    .allowsHitTesting(false)
                // 封面位置。
                if ws.draft.video.exportCover, let cover = ws.coverAt[item.url] {
                    Image(systemName: "photo.fill")
                        .font(.system(size: 8))
                        .foregroundStyle(.white)
                        .frame(width: 16, height: 14)
                        .background(Color.black.opacity(0.6), in: RoundedRectangle(cornerRadius: 3))
                        .offset(x: CGFloat(cover / d) * w - 8, y: 44 - 16)
                        .allowsHitTesting(false)
                        .accessibilityLabel("封面在 \(Format.timecode(cover))")
                }
                // 播放头。
                Rectangle().fill(.white)
                    .frame(width: 2)
                    .shadow(color: .black.opacity(0.6), radius: 1)
                    .offset(x: playX - 1)
                    .allowsHitTesting(false)
            }
            .frame(width: w, height: geo.size.height, alignment: .topLeading)
            .contentShape(Rectangle())
            .gesture(timelineGesture(width: w, range: range))
        }
        .accessibilityElement()
        .accessibilityLabel("时间轴")
        .accessibilityValue("截取 \(Format.duration(range.lowerBound)) 到 \(Format.duration(range.upperBound))")
    }

    /// 按在两端把手附近：拖动截取；其余位置：拖动预览，松手时（没怎么动）设为封面。
    private func timelineGesture(width w: CGFloat, range: ClosedRange<Double>) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { v in
                let d = item.duration
                let t = min(max(0, Double(v.location.x / w) * d), d)
                if dragging == nil, v.translation == .zero {
                    let x0 = CGFloat(range.lowerBound / d) * w, x1 = CGFloat(range.upperBound / d) * w
                    if abs(v.startLocation.x - x0) <= 14 { dragging = .start }
                    else if abs(v.startLocation.x - x1) <= 14 { dragging = .end }
                }
                let minLength = min(0.5, d)
                switch dragging {
                case .start:
                    let s = min(t, range.upperBound - minLength)
                    ws.trim[item.url] = s...range.upperBound
                    playback.range = s...range.upperBound
                    playback.seek(s)
                case .end:
                    let e = max(t, range.lowerBound + minLength)
                    ws.trim[item.url] = range.lowerBound...e
                    playback.range = range.lowerBound...e
                    playback.seek(e)
                case nil:
                    if playback.playing { playback.pause() }
                    playback.seek(t)
                }
            }
            .onEnded { v in
                if dragging == nil, abs(v.translation.width) < 3 {
                    let t = min(max(0, Double(v.location.x / w) * item.duration), item.duration)
                    ws.coverAt[item.url] = t
                    ws.draft.video.exportCover = true
                }
                // 截取全片时不记，免得「已修改」误报。
                if let r = ws.trim[item.url], r.lowerBound <= 0.01, r.upperBound >= item.duration - 0.01 { ws.trim[item.url] = nil }
                dragging = nil
            }
    }
}

/// 截取框：品牌红，两端宽把手中间一道白色握痕。
private struct TrimFrame: View {
    let handle: CGFloat

    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 4, style: .continuous)
                .strokeBorder(Color.brand, lineWidth: 3)
            HStack(spacing: 0) {
                grip
                Spacer(minLength: 0)
                grip
            }
        }
    }

    private var grip: some View {
        UnevenRoundedRectangle(cornerRadii: .init(topLeading: 4, bottomLeading: 4, bottomTrailing: 4, topTrailing: 4), style: .continuous)
            .fill(Color.brand)
            .frame(width: handle)
            .overlay { Capsule().fill(.white.opacity(0.9)).frame(width: 2, height: 14) }
    }
}

/// 多段视频时的切换条：缩略图 + 当前描红圈。
struct VideoStrip: View {
    @Bindable var ws: Workspace

    var body: some View {
        let items = ws.videoItems
        let current = ws.currentVideo?.id
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(Array(items.enumerated()), id: \.element.id) { i, item in
                    let on = item.id == current
                    Thumb(image: item.thumbnail)
                        .frame(width: 72, height: 48)
                        .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                        .overlay(alignment: .bottomTrailing) {
                            Text(verbatim: Format.duration(ws.trimRange(item).upperBound - ws.trimRange(item).lowerBound))
                                .font(.system(size: 9).monospacedDigit())
                                .foregroundStyle(.white)
                                .padding(.horizontal, 3)
                                .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 2))
                                .padding(3)
                        }
                        .opacity(on ? 1 : 0.7)
                        .padding(4)
                        .overlay {
                            if on {
                                RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Color.brand, lineWidth: 2)
                                RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.white, lineWidth: 2).padding(2)
                            }
                        }
                        .contentShape(Rectangle())
                        .onTapGesture { withAnimation(.smooth(duration: 0.25)) { ws.videoIndex = i } }
                        .accessibilityElement()
                        .accessibilityLabel(item.name)
                        .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
                        .accessibilityAction { ws.videoIndex = i }
                }
            }
            .padding(.horizontal, 2)
        }
        .frame(height: 64)
    }
}
