import SwiftUI

/// 序列帧的检查器（设计稿 04b）：抽帧、每一帧、输出。
struct FramesInspectorSections: View {
    @Bindable var ws: Workspace

    private var v: VideoBody { ws.draft.video }

    var body: some View {
        InspectorSection(title: "抽帧") {
            SlidingSegmented(selection: Binding(get: { v.frameMode }, set: setMode),
                             options: [(.perSecond, "每秒"), (.every, "间隔"), (.total, "总数"), (.all, "全部")])
                .accessibilityLabel("抽帧方式")
            InspectorRow(label: modeLabel) {
                if v.frameMode == .all {
                    Text(verbatim: "\(sourceFps) fps").foregroundStyle(.secondary).monospacedDigit()
                } else {
                    FrameStepButton(symbol: "minus", label: "减少") { step(-1) }
                    Text(verbatim: valueText)
                        .monospacedDigit()
                        .frame(minWidth: 52, alignment: .trailing)
                        .padding(.horizontal, 6)
                        .frame(height: 22)
                        .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                    FrameStepButton(symbol: "plus", label: "增加") { step(1) }
                }
            }
        }

        InspectorSection(title: "每一帧") {
            InspectorRow(label: "格式") {
                Menu {
                    Picker("格式", selection: $ws.draft.video.frameFormat) {
                        ForEach([ImageFormatChoice.webp, .jpeg, .png, .avif]) { Text($0.title).tag($0) }
                    }
                    .pickerStyle(.inline)
                    if v.frameFormat != .png {
                        Picker("质量", selection: $ws.draft.video.frameQuality) {
                            ForEach([60, 70, 80, 90, 100], id: \.self) { Text("质量 \($0)").tag($0) }
                        }
                        .pickerStyle(.inline)
                    }
                } label: {
                    Text(formatTitle)
                }
                .menuStyle(.button)
                .fixedSize()
            }
            InspectorRow(label: "宽") {
                Picker("宽", selection: $ws.draft.video.frameWidth) {
                    ForEach([3840, 2560, 1920, 1280, 960, 640], id: \.self) { Text(verbatim: "\($0) px").tag(Int?.some($0)) }
                    Divider()
                    Text("原尺寸").tag(Int?.none)
                }
                .labelsHidden()
                .fixedSize()
            }
        }

        InspectorSection(title: "输出") {
            InspectorToggle(label: "打包成 zip", isOn: $ws.draft.video.frameZip)
            InspectorToggle(label: "另存一张拼图总览", isOn: $ws.draft.video.frameContactSheet)
        }

        Text("比例裁切沿用「剪辑」里的设置。")
            .font(.caption)
            .foregroundStyle(Bento.secondary)
            .padding(.horizontal, 6)
    }

    private var sourceFps: Int { Int((ws.currentVideo?.fps ?? 30).rounded()) }

    private var modeLabel: String {
        switch v.frameMode {
        case .perSecond: "每秒导出"
        case .every: "每隔"
        case .total: "平均取"
        case .all: "原帧率"
        }
    }

    private var valueText: String {
        let n = v.frameValue
        let number = n.rounded() == n ? "\(Int(n))" : String(format: "%.1f", n)
        switch v.frameMode {
        case .perSecond: return "\(number) 帧"
        case .every: return "\(number) 秒"
        case .total: return "\(Int(n)) 张"
        case .all: return ""
        }
    }

    private var formatTitle: String {
        v.frameFormat == .png ? "PNG" : "\(v.frameFormat.title) · 质量 \(v.frameQuality)"
    }

    /// 换方式时给一个常用的起始值（设计稿：每秒 15 帧、每隔 1 秒、总共 60 张）。
    private func setMode(_ mode: VideoBody.FrameMode) {
        guard mode != v.frameMode else { return }
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

private struct FrameStepButton: View {
    let symbol: String
    let label: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 9, weight: .bold))
                .frame(width: 22, height: 22)
                .background(RoundedRectangle(cornerRadius: 5, style: .continuous).fill(Color(nsColor: .controlBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 5, style: .continuous).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .buttonRepeatBehavior(.enabled)
        .accessibilityLabel(label)
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
