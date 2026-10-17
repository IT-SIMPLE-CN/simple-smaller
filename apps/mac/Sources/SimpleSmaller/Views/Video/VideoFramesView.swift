import SwiftUI

/// 视频「序列帧」（设计稿 04b）：上面是范围与要导出的帧（竖线），下面预览前 12 帧；底栏写将生成多少张。
struct VideoFramesView: View {
    @Bindable var ws: Workspace

    var body: some View {
        VStack(spacing: 10) {
            if let item = ws.currentVideo {
                FrameRangeBar(ws: ws, item: item)
                FramePreviewGrid(ws: ws, item: item)
                if ws.videoItems.count > 1 {
                    VideoStrip(ws: ws)
                }
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }
}

/// 范围条：缩略图底上画出每一帧的位置（最多 60 条竖线），两端可拖，只导出一段。
private struct FrameRangeBar: View {
    @Bindable var ws: Workspace
    let item: FileItem
    @State private var thumbs: [CGImage] = []
    @State private var dragging: Bool?   // true = 开头，false = 结尾

    private let count = 14

    var body: some View {
        let range = ws.trimRange(item)
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(verbatim: range.lowerBound <= 0.01 && range.upperBound >= item.duration - 0.01
                     ? "范围：全片 \(Format.duration(0)) – \(Format.duration(item.duration < 1 ? item.duration : item.duration.rounded()))"
                     : "范围：\(Format.duration(range.lowerBound)) – \(Format.duration(range.upperBound < 1 ? range.upperBound : range.upperBound.rounded()))")
                    .font(.system(size: 12, weight: .semibold).monospacedDigit())
                Spacer()
                Text("竖线是要导出的帧，拖两端只导出一段")
                    .font(.system(size: 11))
                    .foregroundStyle(.tertiary)
            }
            GeometryReader { geo in
                let w = geo.size.width
                let d = max(0.001, item.duration)
                let x0 = CGFloat(range.lowerBound / d) * w
                let x1 = CGFloat(range.upperBound / d) * w
                let n = ws.frameCount(item)
                let ticks = min(n, 30)
                let cell = (w - CGFloat(count - 1)) / CGFloat(count)
                ZStack(alignment: .topLeading) {
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
                    .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                    Rectangle().fill(Color(nsColor: .windowBackgroundColor).opacity(0.7)).frame(width: max(0, x0))
                    Rectangle().fill(Color(nsColor: .windowBackgroundColor).opacity(0.7)).frame(width: max(0, w - x1)).offset(x: x1)
                    // 要导出的帧：张数多时按 60 条均匀示意。
                    Path { p in
                        for k in 0..<ticks {
                            let t = ws.frameTime(item, index: ticks == n ? k : k * n / max(1, ticks))
                            let x = CGFloat(t / d) * w
                            p.move(to: CGPoint(x: x, y: 6)); p.addLine(to: CGPoint(x: x, y: geo.size.height - 6))
                        }
                    }
                    .stroke(Color.brand.opacity(0.9), lineWidth: 1)
                    .allowsHitTesting(false)
                    RoundedRectangle(cornerRadius: 4, style: .continuous)
                        .strokeBorder(Color.brand, lineWidth: 2)
                        .frame(width: max(8, x1 - x0))
                        .offset(x: x0)
                        .allowsHitTesting(false)
                }
                .contentShape(Rectangle())
                .gesture(DragGesture(minimumDistance: 1)
                    .onChanged { v in
                        let t = min(max(0, Double(v.location.x / w) * d), d)
                        if dragging == nil { dragging = abs(v.startLocation.x - x0) <= abs(v.startLocation.x - x1) }
                        let minLength = min(0.5, d)
                        if dragging == true {
                            ws.trim[item.url] = min(t, range.upperBound - minLength)...range.upperBound
                        } else {
                            ws.trim[item.url] = range.lowerBound...max(t, range.lowerBound + minLength)
                        }
                    }
                    .onEnded { _ in
                        dragging = nil
                        if let r = ws.trim[item.url], r.lowerBound <= 0.01, r.upperBound >= item.duration - 0.01 { ws.trim[item.url] = nil }
                    })
            }
            .frame(height: 52)
            .accessibilityElement()
            .accessibilityLabel("导出范围")
            .accessibilityValue("\(Format.duration(range.lowerBound)) 到 \(Format.duration(range.upperBound))，\(ws.frameCount(item)) 张")
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 14)
        .task(id: item.url) {
            thumbs = await TimelineThumbnails.make(url: item.url, duration: item.duration, count: count, height: 52)
        }
    }
}

/// 预览前 12 帧，文件名按实际输出写。
private struct FramePreviewGrid: View {
    @Bindable var ws: Workspace
    let item: FileItem
    @State private var images: [Int: CGImage] = [:]

    private var stem: String { item.url.deletingPathExtension().lastPathComponent }
    private var ext: String {
        switch ws.draft.video.frameFormat {
        case .jpeg, .original: "jpg"
        case .png: "png"
        case .webp: "webp"
        case .avif: "avif"
        }
    }

    var body: some View {
        let n = min(12, ws.frameCount(item))
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Text("预览前 \(n) 帧").font(.system(size: 12, weight: .semibold))
                    Spacer()
                    Text(verbatim: "\(stem)_frames/").font(.system(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                }
                LazyVGrid(columns: [GridItem(.adaptive(minimum: 132, maximum: 220), spacing: 10)], spacing: 12) {
                    ForEach(0..<n, id: \.self) { k in
                        VStack(alignment: .leading, spacing: 4) {
                            Color.clear
                                .aspectRatio(aspect, contentMode: .fit)
                                .overlay {
                                    if let cg = images[k] {
                                        Image(decorative: cg, scale: 1).resizable().scaledToFill()
                                    } else {
                                        Rectangle().fill(.quaternary)
                                    }
                                }
                                .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 4, style: .continuous).strokeBorder(.separator, lineWidth: 0.5))
                            Text(verbatim: String(format: "_%04d.%@", k + 1, ext))
                                .font(.system(size: 11).monospacedDigit())
                                .foregroundStyle(.secondary)
                        }
                    }
                }
            }
            .padding(16)
        }
        .task(id: previewKey) { await load(count: n) }
    }

    /// 裁切后的画面比例（沿用「剪辑」）。
    private var aspect: CGFloat {
        if let box = ws.videoCropBox(item), box.height > 0 { return box.width / box.height }
        return CGFloat(item.width) / CGFloat(max(1, item.height))
    }

    private var previewKey: String {
        let v = ws.draft.video
        let r = ws.trimRange(item)
        return "\(item.url.path)|\(v.frameMode)|\(v.frameValue)|\(r.lowerBound)|\(r.upperBound)|\(ws.videoCropBox(item).map { "\($0)" } ?? "")"
    }

    private func load(count n: Int) async {
        try? await Task.sleep(for: .milliseconds(150))   // 连点 +/− 时停手再取
        var fresh: [Int: CGImage] = [:]
        for k in 0..<n {
            if Task.isCancelled { return }
            let t = ws.frameTime(item, index: k)
            guard var cg = await TimelineThumbnails.frame(url: item.url, at: t, maxSide: 480, duration: item.duration) else { continue }
            if let box = ws.videoCropBox(item) {
                let s = CGFloat(cg.width) / CGFloat(item.width)
                let r = CGRect(x: box.minX * s, y: box.minY * s, width: box.width * s, height: box.height * s).integral
                cg = cg.cropping(to: r) ?? cg
            }
            fresh[k] = cg
            images = images.merging(fresh) { $1 }
        }
        images = fresh
    }
}
