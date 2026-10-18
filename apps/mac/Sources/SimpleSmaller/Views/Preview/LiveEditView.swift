import AVFoundation
import SwiftUI

/// 视频「实况」：上面是画面与比例框（同剪辑），下面是时间轴：红框框住会动的那一段（最长 3 秒），
/// 拖红框挪位置、拖两端改长短；单击时间轴选封面（照片用的那一帧）。多段视频时底部可切换。
struct LiveEditView: View {
    @Bindable var ws: Workspace
    @State private var playback = VideoPlayback()
    @FocusState private var focused: Bool

    var body: some View {
        VStack(spacing: 10) {
            if let item = ws.currentVideo {
                VideoStage(ws: ws, item: item, playback: playback)
                    .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
                LiveTimeline(ws: ws, item: item, playback: playback)
                if ws.videoItems.count > 1 {
                    FilmStrip(items: ws.videoItems, index: ws.videoItems.firstIndex { $0.id == ws.currentVideo?.id } ?? 0, select: { ws.videoIndex = $0 }) { item in
                        Text(verbatim: TrimBar.seconds(ws.liveWindow(item).upperBound - ws.liveWindow(item).lowerBound))
                            .font(.system(size: 9).monospacedDigit())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 4)
                            .background(Color.black.opacity(0.55), in: Capsule())
                    }
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
        playback.load(item.url, range: ws.liveWindow(item))
        playback.range = ws.liveWindow(item)
        playback.seek(ws.liveCoverTime(item))
    }
}

/// 时间轴：缩略图条上一个品牌红框（会动的那一段），框里一条白线是封面。
private struct LiveTimeline: View {
    @Bindable var ws: Workspace
    let item: FileItem
    let playback: VideoPlayback
    @State private var thumbs: [CGImage] = []
    @State private var drag: Drag?

    private enum Drag { case start, end, move(origin: Double), scrub }
    private let handleWidth: CGFloat = 10
    private let count = 14
    /// 最短 0.5 秒。
    private let minLength = 0.5

    var body: some View {
        let window = ws.liveWindow(item)
        let cover = ws.liveCoverTime(item)
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
                .help("播放这一段（空格）")
                Text(verbatim: Format.timecode(playback.time))
                    .font(.system(size: 12).monospacedDigit())
                Text(verbatim: "会动的部分 \(TrimBar.seconds(window.upperBound - window.lowerBound)) · 封面 \(Format.timecode(cover))")
                    .font(.system(size: 12).monospacedDigit())
                    .foregroundStyle(Bento.secondary)
                Spacer()
                Text("拖红框选一段（最长 3 秒）· 单击选封面")
                    .font(.system(size: 11))
                    .foregroundStyle(Bento.secondary.opacity(0.75))
                    .lineLimit(1)
            }
            timeline(window: window, cover: cover)
                .frame(height: 44)
        }
        .padding(.horizontal, 4)
        .task(id: item.url) {
            thumbs = []
            thumbs = await TimelineThumbnails.make(url: item.url, duration: item.duration, count: count, height: 44)
        }
    }

    private func timeline(window: ClosedRange<Double>, cover: Double) -> some View {
        GeometryReader { geo in
            let w = geo.size.width
            let d = max(0.001, item.duration)
            let x0 = CGFloat(window.lowerBound / d) * w
            let x1 = CGFloat(window.upperBound / d) * w
            let coverX = CGFloat(cover / d) * w
            let playX = CGFloat(min(max(playback.time, 0), d) / d) * w
            ZStack(alignment: .topLeading) {
                let cell = (w - CGFloat(count - 1)) / CGFloat(count)
                HStack(spacing: 1) {
                    ForEach(0..<count, id: \.self) { k in
                        Color.clear
                            .frame(width: max(0, cell), height: geo.size.height)
                            .overlay {
                                if k < thumbs.count {
                                    Image(decorative: thumbs[k], scale: 2).resizable().scaledToFill()
                                } else {
                                    Rectangle().fill(Bento.fill)
                                }
                            }
                            .clipped()
                    }
                }
                .frame(width: w, alignment: .leading)
                .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                // 红框外压淡。
                Rectangle().fill(Bento.card.opacity(0.7)).frame(width: max(0, x0))
                Rectangle().fill(Bento.card.opacity(0.7)).frame(width: max(0, w - x1)).offset(x: x1)
                LiveFrame(handle: handleWidth)
                    .frame(width: max(handleWidth * 2 + 4, x1 - x0))
                    .offset(x: x0)
                    .allowsHitTesting(false)
                // 封面：白线 + 顶上一个小照片标。
                Rectangle().fill(.white)
                    .frame(width: 2, height: geo.size.height - 6)
                    .shadow(color: .black.opacity(0.5), radius: 1)
                    .offset(x: coverX - 1, y: 3)
                    .allowsHitTesting(false)
                Image(systemName: "photo.fill")
                    .font(.system(size: 8))
                    .foregroundStyle(.white)
                    .frame(width: 18, height: 14)
                    .background(Color.brand, in: RoundedRectangle(cornerRadius: 3, style: .continuous))
                    .offset(x: coverX - 9, y: -16)
                    .allowsHitTesting(false)
                    .accessibilityHidden(true)
                // 播放头（播放时才显示，免得和封面线混在一起）。
                if playback.playing {
                    Rectangle().fill(Color.brand)
                        .frame(width: 2)
                        .offset(x: playX - 1)
                        .allowsHitTesting(false)
                }
            }
            .frame(width: w, height: geo.size.height, alignment: .topLeading)
            .contentShape(Rectangle())
            .gesture(gesture(width: w, window: window))
        }
        .accessibilityElement()
        .accessibilityLabel("实况时间轴")
        .accessibilityValue("会动的部分 \(Format.timecode(window.lowerBound)) 到 \(Format.timecode(window.upperBound))，封面 \(Format.timecode(cover))")
        .accessibilityAdjustableAction { direction in
            let step = 0.5
            ws.moveLiveWindow(item, to: window.lowerBound + (direction == .increment ? step : -step))
        }
    }

    /// 按在两端把手附近：改长短；按在红框里：拖动整段；松手时没怎么动：选封面（框外点一下，框跟过去）。
    private func gesture(width w: CGFloat, window: ClosedRange<Double>) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { v in
                let d = max(0.001, item.duration)
                let t = min(max(0, Double(v.location.x / w) * d), d)
                if drag == nil {
                    let x0 = CGFloat(window.lowerBound / d) * w, x1 = CGFloat(window.upperBound / d) * w
                    let sx = v.startLocation.x
                    if abs(sx - x0) <= 12 { drag = .start }
                    else if abs(sx - x1) <= 12 { drag = .end }
                    else if sx > x0, sx < x1 { drag = .move(origin: window.lowerBound) }
                    else { drag = .scrub }
                }
                let maxLength = min(Workspace.liveMaxSeconds, d)
                switch drag {
                case .start:
                    let s = min(max(t, window.upperBound - maxLength), window.upperBound - min(minLength, d))
                    ws.liveRange[item.url] = s...window.upperBound
                    playback.range = s...window.upperBound
                    playback.seek(s)
                case .end:
                    let e = max(min(t, window.lowerBound + maxLength), window.lowerBound + min(minLength, d))
                    ws.liveRange[item.url] = window.lowerBound...e
                    playback.range = window.lowerBound...e
                    playback.seek(e)
                case .move(let origin):
                    guard abs(v.translation.width) >= 3 else { return }
                    ws.moveLiveWindow(item, to: origin + Double(v.translation.width / w) * d)
                    playback.range = ws.liveWindow(item)
                    playback.seek(ws.liveCoverTime(item))
                case .scrub, nil:
                    break
                }
            }
            .onEnded { v in
                defer { drag = nil }
                let d = max(0.001, item.duration)
                let t = min(max(0, Double(v.location.x / w) * d), d)
                let still = abs(v.translation.width) < 3
                switch drag {
                case .move where still, .scrub where still:
                    // 框外点：先把框挪过去（以这一刻为中心），再把封面放在这里。
                    let current = ws.liveWindow(item)
                    if !current.contains(t) {
                        let length = current.upperBound - current.lowerBound
                        ws.moveLiveWindow(item, to: t - length / 2)
                    }
                    ws.liveCover[item.url] = t
                    playback.pause()
                    playback.range = ws.liveWindow(item)
                    playback.seek(ws.liveCoverTime(item))
                default:
                    break
                }
                // 封面挪出了框：拉回框里。
                if let c = ws.liveCover[item.url] {
                    let r = ws.liveWindow(item)
                    ws.liveCover[item.url] = min(max(c, r.lowerBound), r.upperBound)
                }
            }
    }
}

/// 红框：品牌红描边，两端宽把手中间一道白色握痕（同剪辑的截取框）。
private struct LiveFrame: View {
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
        RoundedRectangle(cornerRadius: 4, style: .continuous)
            .fill(Color.brand)
            .frame(width: handle)
            .overlay { Capsule().fill(.white.opacity(0.9)).frame(width: 2, height: 14) }
    }
}
