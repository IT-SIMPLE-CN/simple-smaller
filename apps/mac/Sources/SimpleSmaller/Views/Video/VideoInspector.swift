import SwiftUI

/// 视频检查器（设计稿 04）：压缩、剪辑共用画面 / 格式与编码 / 声音与附带；序列帧另有一套。
struct VideoInspector: View {
    @Bindable var ws: Workspace

    var body: some View {
        InspectorScroll {
            if !ws.files.isEmpty {
                if ws.videoSegment == .frames {
                    FramesInspectorSections(ws: ws)
                } else if ws.draft.video.muteOnly {
                    InspectorSection(title: "处理方式") {
                        Text("只去掉声音，画面原样复制，不重新编码，几乎不花时间。")
                            .font(.caption).foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                } else {
                    pictureSection
                    codecSection
                    audioSection
                }
            } else {
                InspectorSection(title: ws.base.name) {
                    Text(description).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
        }
    }

    private var description: String {
        let v = ws.draft.video
        if v.muteOnly { return "只去掉声音，画面不重新编码。" }
        if v.outputFrames { return "把视频按设定抽成一张张图片，存进旁边的新文件夹。" }
        var parts: [String] = []
        switch v.codec {
        case .h264: parts.append("MP4 H.264")
        case .hevc: parts.append("MP4 HEVC")
        case .gif: parts.append("GIF \(v.gifWidth) px · \(Int(v.gifFps)) fps")
        }
        if v.codec != .gif {
            if let c = v.crop { parts.append(c.name) }
            parts.append(v.shortEdge.map { "\($0)p" } ?? "原分辨率")
            parts.append(v.rate == .size ? "不超过 \(Format.megabytes(v.targetMB))" : "画质\(Self.rateTitle(v.rate))")
            if !v.keepAudio { parts.append("去掉声音") }
        }
        return parts.joined(separator: "，") + "。"
    }

    static func rateTitle(_ r: VideoBody.Rate) -> String {
        switch r {
        case .high: "高"
        case .medium: "中"
        case .low: "低"
        case .size: "按大小"
        }
    }

    // MARK: 画面

    @ViewBuilder private var pictureSection: some View {
        InspectorSection(title: "画面") {
            InspectorRow(label: "比例") {
                Picker("比例", selection: Binding(get: { ws.draft.video.crop?.id ?? "" }, set: { id in
                    ws.draft.video.crop = CropPreset.videoPresets.first { $0.id == id }
                    ws.resetVideoCrop()
                })) {
                    Text("原比例").tag("")
                    Divider()
                    ForEach(CropPreset.videoPresets) { Text($0.name).tag($0.id) }
                }
                .labelsHidden()
                .fixedSize()
            }
            if ws.draft.video.codec != .gif {
                InspectorRow(label: "分辨率") {
                    Picker("分辨率", selection: $ws.draft.video.shortEdge) {
                        Text("1080p").tag(Int?.some(1080))
                        Text("720p").tag(Int?.some(720))
                        Text("540p").tag(Int?.some(540))
                        Divider()
                        Text("原分辨率").tag(Int?.none)
                    }
                    .labelsHidden()
                    .fixedSize()
                }
            }
            // 只有这批里有 HDR 视频时才出现。
            if ws.videoItems.contains(where: \.hdr) {
                InspectorToggle(label: "HDR 转 SDR", isOn: $ws.draft.video.hdrToSdr)
                    .help("HDR 视频在多数手机网页与聊天软件里会发灰，转成普通画面更稳")
            }
        }
    }

    // MARK: 格式与编码

    @ViewBuilder private var codecSection: some View {
        InspectorSection(title: "格式与编码") {
            SlidingSegmented(selection: $ws.draft.video.codec, options: [(.h264, "MP4 H.264"), (.hevc, "MP4 HEVC"), (.gif, "GIF")])
                .accessibilityLabel("格式")
            if ws.draft.video.codec == .gif {
                InspectorRow(label: "帧率") {
                    Picker("帧率", selection: $ws.draft.video.gifFps) {
                        ForEach([10.0, 12, 15, 20, 24], id: \.self) { Text("\(Int($0)) fps").tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
                InspectorRow(label: "宽") {
                    Picker("宽", selection: $ws.draft.video.gifWidth) {
                        ForEach([320, 480, 640, 800], id: \.self) { Text(verbatim: "\($0) px").tag($0) }
                    }
                    .labelsHidden().fixedSize()
                }
            } else {
                SlidingSegmented(selection: $ws.draft.video.rate, options: VideoBody.Rate.allCases.map { ($0, Self.rateTitle($0)) })
                    .accessibilityLabel("画质")
                if ws.draft.video.rate == .size {
                    InspectorRow(label: "不超过") {
                        TextField("", value: Binding(get: { ws.draft.video.targetMB }, set: { ws.draft.video.targetMB = min(4000, max(1, $0)) }),
                                  format: .number.precision(.fractionLength(0...1)).grouping(.never))
                            .textFieldStyle(.roundedBorder)
                            .controlSize(.small)
                            .multilineTextAlignment(.trailing)
                            .frame(width: 58)
                        Text("MB").foregroundStyle(.secondary)
                    }
                }
                Text(outputLine)
                    .monospacedDigit()
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }
        }
    }

    /// 「1080 × 1920 · 30 fps」：当前这段的实际输出画面。
    private var outputLine: String {
        guard let item = ws.currentVideo else { return "" }
        let size = ws.videoOutputSize(item)
        return "\(Int(size.width)) × \(Int(size.height)) · \(Int(item.fps.rounded())) fps"
    }

    // MARK: 声音与附带

    @ViewBuilder private var audioSection: some View {
        InspectorSection(title: "声音与附带") {
            if ws.draft.video.codec == .gif {
                Text("GIF 没有声音。").font(.caption).foregroundStyle(.secondary)
            } else {
                InspectorToggle(label: "保留声音", isOn: $ws.draft.video.keepAudio)
                InspectorToggle(label: "同时导出封面", isOn: $ws.draft.video.exportCover)
                    .help("在「剪辑」的时间轴上单击选封面；没选时取开头往后 1 秒")
            }
        }
    }
}

extension Format {
    /// 「25 MB」「7.5 MB」。
    static func megabytes(_ mb: Double) -> String {
        mb.rounded() == mb ? "\(Int(mb)) MB" : String(format: "%.1f MB", mb)
    }
}
