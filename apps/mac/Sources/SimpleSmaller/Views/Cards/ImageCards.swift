import QuickLook
import SwiftUI

struct FormatQualityCard: View {
    @Bindable var ws: Workspace

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 14) {
                BentoHeader(symbol: "slider.horizontal.3", title: "格式与质量", subtitle: "跟着常用走")
                VStack(alignment: .leading, spacing: 8) {
                    Text("格式").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    PillChoice(selection: $ws.draft.image.format, options: ImageFormatChoice.allCases.map { ($0, $0.title) }, label: "格式")
                }
                Spacer(minLength: 0)
                QualityPanel(ws: ws)
            }
        }
        .bentoLocked(ws.running)
    }
}

/// 灰底质量面板：大数字 + 红色滑杆 +「更小 / 更清晰」；无损、限体积时换成对应说明。
struct QualityPanel: View {
    @Bindable var ws: Workspace

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if let kb = ws.draft.image.targetKB {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text("不超过")
                    TextField("", value: Binding(get: { kb }, set: { ws.draft.image.targetKB = max(10, $0) }), format: .number.grouping(.never))
                        .textFieldStyle(.plain)
                        .font(Bento.number(22, weight: .medium))
                        .frame(width: 80)
                        .accessibilityLabel("体积上限")
                    Text("KB").foregroundStyle(Bento.secondary)
                    Spacer()
                }
                Text("逐档降质量，直到每张不超过这个体积。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            } else if ws.draft.image.lossless {
                HStack {
                    Text("质量")
                    Text("无损").font(Bento.number(22, weight: .medium))
                    Spacer()
                    Toggle("无损", isOn: $ws.draft.image.lossless).labelsHidden().toggleStyle(.switch).tint(.brand).controlSize(.small)
                }
                Text("画面一点不变，只去掉多余数据。").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            } else {
                HStack(alignment: .firstTextBaseline, spacing: 10) {
                    Text("质量")
                    Text(verbatim: "\(ws.draft.image.quality) · \(QualityWord.of(ws.draft.image.quality))")
                        .font(Bento.number(22, weight: .medium))
                    Spacer()
                }
                BrandSlider(value: Binding(get: { Double(ws.draft.image.quality) }, set: { ws.draft.image.quality = Int($0.rounded()) }), range: 30...100)
                    .frame(height: 24)
                    .accessibilityLabel("质量")
                    .accessibilityValue("\(ws.draft.image.quality)")
                HStack {
                    Button("更小") { ws.draft.image.quality = max(30, ws.draft.image.quality - 6) }
                    Spacer()
                    Button("更清晰") { ws.draft.image.quality = min(100, ws.draft.image.quality + 6) }
                }
                .buttonStyle(.plain)
                .font(.system(size: 11))
                .foregroundStyle(Bento.secondary)
            }
        }
        .padding(.horizontal, 18)
        .padding(.top, 16)
        .padding(.bottom, 12)
        .bentoPanel()
    }
}

// MARK: - 裁切
