import SwiftUI

// 视频（设计稿 V2.1 · 06 / 07）：压缩、剪辑共用「画面与画质 · 格式 · 声音与开始」；序列帧是「抽帧 · 每一帧 · 输出与开始」。

/// 一组小标题 + 胶囊。
struct LabeledPills<Value: Hashable>: View {
    let title: String
    @Binding var selection: Value
    let options: [(Value, String)]
    var trailing: String? = nil

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                Text(title).font(.system(size: 11)).foregroundStyle(Bento.secondary)
                Spacer()
                if let trailing { Text(trailing).font(.system(size: 11)).foregroundStyle(Bento.secondary).monospacedDigit() }
            }
            PillChoice(selection: $selection, options: options, height: 32, label: title)
        }
    }
}

/// 画面与画质：比例、分辨率、画质（GIF 时换成宽与帧率）。
struct VideoPictureCard: View {
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    private var v: VideoBody { ws.draft.video }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "slider.horizontal.3", title: "画面与画质", subtitle: v.muteOnly ? "只去掉声音" : "跟着配方走") {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                if v.muteOnly {
                    Text("只去掉声音，画面原样复制，不重新编码，几乎不花时间。")
                        .font(.system(size: 12)).foregroundStyle(Bento.secondary)
                } else {
                    LabeledPills(title: "画面比例", selection: Binding(get: { v.crop?.id ?? "" }, set: { id in
                        ws.draft.video.crop = CropPreset.videoPresets.first { $0.id == id }
                        ws.resetVideoCrop()
                    }), options: [("", "原比例")] + CropPreset.videoPresets.map { ($0.id, $0.name) },
                                 trailing: ws.videoSegment == .edit ? "预览里的框可拖" : nil)
                    if v.codec == .gif {
                        HStack(alignment: .top, spacing: 18) {
                            LabeledPills(title: "宽", selection: $ws.draft.video.gifWidth, options: [320, 480, 640, 800].map { ($0, "\($0)") })
                            LabeledPills(title: "帧率", selection: $ws.draft.video.gifFps, options: [10.0, 15, 24].map { ($0, "\(Int($0)) fps") })
                        }
                    } else {
                        LabeledPills(title: "分辨率", selection: $ws.draft.video.shortEdge,
                                     options: [(Int?.none, "原分辨率"), (1080, "1080p"), (720, "720p"), (540, "540p")])
                        HStack(alignment: .bottom, spacing: 12) {
                            LabeledPills(title: "画质", selection: $ws.draft.video.rate, options: VideoBody.Rate.allCases.map { ($0, VideoCards.rateTitle($0)) })
                            if v.rate == .size {
                                HStack(spacing: 4) {
                                    Text("不超过").foregroundStyle(Bento.secondary)
                                    TextField("", value: Binding(get: { v.targetMB }, set: { ws.draft.video.targetMB = min(4000, max(1, $0)) }),
                                              format: .number.precision(.fractionLength(0...1)).grouping(.never))
                                        .textFieldStyle(.plain)
                                        .multilineTextAlignment(.trailing)
                                        .frame(width: 44)
                                        .accessibilityLabel("体积上限")
                                    Text("MB").foregroundStyle(Bento.secondary)
                                }
                                .font(.system(size: 13).monospacedDigit())
                                .padding(.horizontal, 12)
                                .frame(height: 32)
                                .background(Capsule().fill(Bento.fill))
                                .transition(.opacity)
                            }
                        }
                    }
                    Spacer(minLength: 0)
                    if let line = outputLine {
                        Text(line).font(.system(size: 11)).foregroundStyle(Bento.secondary).monospacedDigit()
                    }
                }
            }
            .animation(.smooth(duration: 0.25), value: v.rate)
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    /// 「1080 × 1920 · 30 fps」：当前这段的实际输出画面。
    private var outputLine: String? {
        guard let item = ws.currentVideo, v.codec != .gif else { return nil }
        let size = ws.videoOutputSize(item)
        return "输出 \(Int(size.width)) × \(Int(size.height)) · \(Int(item.fps.rounded())) fps"
    }
}

/// 格式：H.264 / HEVC / GIF；这批里有 HDR 时多一个「HDR 转 SDR」。编码方式自动选，不摆出来。
struct VideoFormatCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 12) {
                BentoHeader(symbol: "film", title: "格式")
                PillChoice(selection: $ws.draft.video.codec, options: [(.h264, "MP4 H.264"), (.hevc, "MP4 HEVC"), (.gif, "GIF")], height: 34, label: "格式")
                Spacer(minLength: 0)
                if ws.videoItems.contains(where: \.hdr), ws.draft.video.codec != .gif {
                    BentoToggleRow(label: "HDR 转 SDR", isOn: $ws.draft.video.hdrToSdr)
                        .help("HDR 视频在多数手机网页与聊天软件里会发灰，转成普通画面更稳")
                }
            }
        }
        .disabled(ws.running || ws.draft.video.muteOnly)
        .opacity(ws.running || ws.draft.video.muteOnly ? 0.45 : 1)
    }
}

/// 声音与附带 + 开始。
struct VideoAudioStartCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 8) {
                BentoHeader(symbol: "speaker.wave.2", title: "声音与附带")
                Group {
                    if ws.draft.video.codec == .gif {
                        Text("GIF 没有声音。").font(.system(size: 12)).foregroundStyle(Bento.secondary)
                    } else if !ws.draft.video.muteOnly {
                        BentoToggleRow(label: "保留声音", isOn: $ws.draft.video.keepAudio, height: 40)
                        BentoToggleRow(label: "同时导出封面", isOn: $ws.draft.video.exportCover, height: 40)
                            .help("在「剪辑」的时间轴上单击选封面；没选时取开头往后 1 秒")
                    }
                }
                .disabled(ws.running)
                StartBlock(ws: ws).padding(.top, 2)
            }
        }
    }
}

// MARK: - 序列帧

/// 抽帧：每秒 / 间隔 / 总数 / 全部，大号数值 + 张数。
struct FramesCard: View {
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    private var v: VideoBody { ws.draft.video }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 14) {
                BentoHeader(symbol: "square.grid.3x3", title: "抽帧", subtitle: subtitle) {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                PillChoice(selection: Binding(get: { v.frameMode }, set: setMode),
                           options: [(.perSecond, "每秒"), (.every, "间隔"), (.total, "总数"), (.all, "全部")], label: "抽帧方式")
                Spacer(minLength: 0)
                HStack(alignment: .center, spacing: 12) {
                    if v.frameMode == .all {
                        Text(verbatim: "原帧率 \(sourceFps) fps").font(.system(size: 22, weight: .light))
                    } else {
                        CircleIconButton(symbol: "minus", label: "减少", size: 40) { step(-1) }.buttonRepeatBehavior(.enabled)
                        HStack(alignment: .firstTextBaseline, spacing: 6) {
                            Text(number).font(.system(size: 44, weight: .light).monospacedDigit())
                            Text(unit).font(.system(size: 13)).foregroundStyle(Bento.secondary)
                        }
                        CircleIconButton(symbol: "plus", label: "增加", size: 40) { step(1) }.buttonRepeatBehavior(.enabled)
                    }
                    Spacer()
                    VStack(alignment: .trailing, spacing: 4) {
                        Text(verbatim: "\(total) 张").font(.system(size: 22).monospacedDigit())
                        Text(verbatim: "\(v.frameFormat.title) · \(v.frameWidth.map { "宽 \($0)" } ?? "原尺寸")")
                            .font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    }
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Bento.fill))
                if total > 200 {
                    Text("张数较多，网页动画建议 60–120 张").font(.system(size: 11)).foregroundStyle(Color.warning)
                }
            }
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
    }

    private var total: Int { ws.videoItems.reduce(0) { $0 + ws.frameCount($1) } }

    private var subtitle: String {
        guard let item = ws.currentVideo else { return "" }
        return "\(item.name) · \(Format.duration(item.duration)) · \(sourceFps) fps"
    }

    private var sourceFps: Int { Int((ws.currentVideo?.fps ?? 30).rounded()) }

    private var number: String {
        let n = v.frameValue
        return n.rounded() == n ? "\(Int(n))" : String(format: "%.1f", n)
    }

    private var unit: String {
        switch v.frameMode {
        case .perSecond: "帧 / 秒"
        case .every: "秒一张"
        case .total: "张"
        case .all: ""
        }
    }

    private func setMode(_ mode: VideoBody.FrameMode) {
        ws.draft.video.frameMode = mode
        switch mode {
        case .perSecond: ws.draft.video.frameValue = Double(min(15, sourceFps))
        case .every: ws.draft.video.frameValue = 1
        case .total: ws.draft.video.frameValue = 60
        case .all: break
        }
    }

    private func step(_ dir: Int) {
        var n = v.frameValue
        switch v.frameMode {
        case .perSecond:
            // 1 → 5 → 10 …，不超过原帧率。
            n = dir > 0 ? (n < 5 ? 5 : n + 5) : (n <= 5 ? 1 : n - 5)
            n = min(Double(sourceFps), max(1, n))
        case .every:
            n = max(1, n + Double(dir))
        case .total:
            let maxFrames = Double(max(12, Int((ws.currentVideo.map { $0.duration * $0.fps } ?? 300).rounded())))
            n = min(maxFrames, max(12, n + Double(dir * 12)))
        case .all:
            return
        }
        ws.draft.video.frameValue = n
    }
}

/// 每一帧：格式、质量、宽、文件名（比例裁切沿用「剪辑」）。
struct FrameEachCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 8) {
                BentoHeader(symbol: "photo", title: "每一帧", subtitle: "比例裁切沿用「剪辑」里的设置")
                BentoRow(label: "格式", height: 40) {
                    Picker("格式", selection: $ws.draft.video.frameFormat) {
                        ForEach([ImageFormatChoice.webp, .jpeg, .png, .avif]) { Text($0.title).tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                if ws.draft.video.frameFormat != .png {
                    BentoRow(label: "质量", height: 40) {
                        Picker("质量", selection: $ws.draft.video.frameQuality) {
                            ForEach([60, 70, 80, 90, 100], id: \.self) { Text(verbatim: "\($0)").tag($0) }
                        }
                        .labelsHidden().fixedSize()
                    }
                }
                BentoRow(label: "宽", height: 40) {
                    Picker("宽", selection: $ws.draft.video.frameWidth) {
                        ForEach([3840, 2560, 1920, 1280, 960, 640], id: \.self) { Text(verbatim: "\($0) px").tag(Int?.some($0)) }
                        Divider()
                        Text("原尺寸").tag(Int?.none)
                    }
                    .labelsHidden().fixedSize()
                }
                BentoRow(label: "文件名", height: 40) {
                    Text(verbatim: "{name}_0001").font(.system(size: 12, design: .monospaced)).foregroundStyle(Bento.secondary)
                }
                Spacer(minLength: 0)
            }
        }
        .disabled(ws.running)
        .opacity(ws.running ? 0.45 : 1)
    }
}

/// 输出（存到旁边的新文件夹、zip、拼图总览）与开始。
struct FramesOutputStartCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 8) {
                BentoHeader(symbol: "folder", title: "输出", subtitle: "存到旁边的新文件夹")
                Group {
                    BentoToggleRow(label: "打包成 zip", isOn: $ws.draft.video.frameZip, height: 40)
                    BentoToggleRow(label: "另存一张拼图总览", isOn: $ws.draft.video.frameContactSheet, height: 40)
                }
                .disabled(ws.running)
                StartBlock(ws: ws).padding(.top, 2)
            }
        }
    }
}

enum VideoCards {
    static func rateTitle(_ r: VideoBody.Rate) -> String {
        switch r {
        case .high: "高"
        case .medium: "中"
        case .low: "低"
        case .size: "按大小"
        }
    }
}

extension Format {
    /// 「25 MB」「7.5 MB」。
    static func megabytes(_ mb: Double) -> String {
        mb.rounded() == mb ? "\(Int(mb)) MB" : String(format: "%.1f MB", mb)
    }
}

extension Workspace {
    /// 序列帧会生成多少张（每段按截取范围算）。
    func frameCount(_ item: FileItem) -> Int {
        let r = trimRange(item)
        let length = max(0, r.upperBound - r.lowerBound)
        let v = draft.video
        switch v.frameMode {
        case .perSecond: return max(1, Int((length * v.frameValue).rounded(.down)))
        case .every: return max(1, Int((length / max(0.01, v.frameValue)).rounded(.down)))
        case .total: return max(1, Int(v.frameValue))
        case .all: return max(1, Int((length * item.fps).rounded()))
        }
    }

    /// 第 k 张取自第几秒（与引擎按时间均匀取帧一致，用于预览）。
    func frameTime(_ item: FileItem, index k: Int) -> Double {
        let r = trimRange(item)
        let length = max(0, r.upperBound - r.lowerBound)
        let n = frameCount(item)
        let v = draft.video
        switch v.frameMode {
        case .perSecond: return r.lowerBound + Double(k) / max(0.01, v.frameValue)
        case .every: return r.lowerBound + Double(k) * v.frameValue
        case .total: return r.lowerBound + length * Double(k) / Double(max(1, n))
        case .all: return r.lowerBound + Double(k) / max(1, item.fps)
        }
    }
}
