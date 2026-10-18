import SwiftUI

// 视频「实况」：做成哪种 · 封面 · 声音与开始（结构照「序列帧」：主参数卡、细节卡、开始卡）。

/// 做成哪种（苹果实况 / 安卓动态照片 / 两种都要）、画面比例、分辨率。
struct LiveKindCard: View {
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    private var v: VideoBody { ws.draft.video }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "livephoto", title: "做成", subtitle: "跟着常用走") {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                PillChoice(selection: $ws.draft.video.liveKind,
                           options: [(.apple, "苹果实况"), (.android, "安卓动态照片"), (.both, "两种都要")], height: 34, label: "做成哪种")
                Text(note)
                    .font(.system(size: 12))
                    .foregroundStyle(Bento.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                LabeledPills(title: "画面比例", selection: Binding(get: { v.crop?.id ?? "" }, set: { id in
                    ws.draft.video.crop = CropPreset.videoPresets.first { $0.id == id }
                    ws.resetVideoCrop()
                }), options: [("", "原比例")] + CropPreset.videoPresets.map { ($0.id, $0.name) }, trailing: "预览里的框可拖")
                LabeledPills(title: "分辨率", selection: $ws.draft.video.liveLongEdge,
                             options: [(0, "原分辨率"), (1920, "1080p"), (1280, "720p")])
                Spacer(minLength: 0)
                if let line = outputLine {
                    Text(line).font(.system(size: 11)).foregroundStyle(Bento.secondary).monospacedDigit()
                }
            }
        }
        .bentoLocked(ws.running)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    private var note: String {
        switch v.liveKind {
        case .apple: "一张照片配一段视频，同名放在一起。存进「照片」或隔空投送到 iPhone，按住就会动。"
        case .android: "一张会动的照片，小米、三星、Google 等安卓手机的相册都认。"
        case .both: "苹果实况和安卓动态照片各出一份。"
        }
    }

    /// 「1080 × 1920 · 30 fps · 3 秒」：当前这段的实际输出。
    private var outputLine: String? {
        guard let item = ws.currentVideo else { return nil }
        let size = ws.liveOutputSize(item)
        let w = ws.liveWindow(item)
        return "输出 \(Int(size.width)) × \(Int(size.height)) · \(Int(min(30, item.fps).rounded())) fps · \(TrimBar.seconds(w.upperBound - w.lowerBound))"
    }
}

/// 封面：照片用的那一帧，大图看清楚；‹ › 逐帧微调。
struct LiveCoverCard: View {
    @Bindable var ws: Workspace
    @State private var image: CGImage?

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "photo", title: "封面", subtitle: "照片用这一帧，在时间轴上单击换")
                if let item = ws.currentVideo {
                    let t = ws.liveCoverTime(item)
                    ZStack {
                        if let image {
                            Image(decorative: image, scale: 1)
                                .resizable()
                                .aspectRatio(contentMode: .fit)
                                .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                                .padding(8)
                                .transition(.opacity)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .bentoPanel()
                    .accessibilityElement()
                    .accessibilityLabel("封面预览，\(Format.timecode(t))")
                    HStack(spacing: 10) {
                        CircleIconButton(symbol: "chevron.left", label: "上一帧", size: 32) { step(item, -1) }
                            .buttonRepeatBehavior(.enabled)
                        Text(verbatim: Format.timecode(t)).font(.system(size: 13).monospacedDigit())
                        CircleIconButton(symbol: "chevron.right", label: "下一帧", size: 32) { step(item, 1) }
                            .buttonRepeatBehavior(.enabled)
                        Spacer()
                    }
                    .task(id: key(item)) { await load(item) }
                } else {
                    Spacer()
                }
            }
        }
        .bentoLocked(ws.running)
    }

    private func key(_ item: FileItem) -> String {
        "\(item.url.path)|\(ws.liveCoverTime(item))|\(ws.videoCropBox(item).map { "\($0)" } ?? "")"
    }

    private func step(_ item: FileItem, _ dir: Int) {
        let frame = 1 / max(1, min(30, item.fps))
        let w = ws.liveWindow(item)
        ws.liveCover[item.url] = min(max(ws.liveCoverTime(item) + Double(dir) * frame, w.lowerBound), w.upperBound)
    }

    private func load(_ item: FileItem) async {
        try? await Task.sleep(for: .milliseconds(80))   // 连点 ‹ › 时停手再取
        guard !Task.isCancelled, var cg = await TimelineThumbnails.frame(url: item.url, at: ws.liveCoverTime(item), maxSide: 900, duration: item.duration) else { return }
        if let box = ws.videoCropBox(item) {
            let s = CGFloat(cg.width) / CGFloat(max(1, item.width))
            cg = cg.cropping(to: CGRect(x: box.minX * s, y: box.minY * s, width: box.width * s, height: box.height * s).integral) ?? cg
        }
        withAnimation(.easeOut(duration: 0.15)) { image = cg }
    }
}

/// 声音与存放 + 开始。
struct LiveStartCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 8) {
                BentoHeader(symbol: "speaker.wave.2", title: "声音与存放", subtitle: place)
                BentoToggleRow(label: "保留声音", isOn: $ws.draft.video.keepAudio, height: 40)
                    .disabled(ws.running)
                StartBlock(ws: ws).padding(.top, 2)
            }
        }
    }

    private var place: String {
        model.settings.outputLocation == .beside ? "存在原视频旁边" : "存在「\((model.settings.outputFolder as NSString).lastPathComponent)」"
    }
}
