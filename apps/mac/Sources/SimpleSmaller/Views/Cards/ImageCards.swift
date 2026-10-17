import QuickLook
import SwiftUI

struct FormatQualityCard: View {
    @Environment(AppModel.self) private var model
    @Bindable var ws: Workspace
    @State private var saving: Recipe?

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 14) {
                BentoHeader(symbol: "slider.horizontal.3", title: "格式与质量", subtitle: "跟着配方走，改了会提示") {
                    SaveRecipeAccessory(ws: ws, saving: $saving)
                }
                VStack(alignment: .leading, spacing: 8) {
                    Text("格式").font(.system(size: 11)).foregroundStyle(Bento.secondary)
                    PillChoice(selection: $ws.draft.image.format, options: ImageFormatChoice.allCases.map { ($0, $0.title) }, label: "格式")
                }
                Spacer(minLength: 0)
                QualityPanel(ws: ws)
            }
        }
        .bentoLocked(ws.running)
        .sheet(item: $saving) { r in
            RecipeEditorView(initial: r) { saved in ws.select(recipe: saved) }
        }
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

/// 尺寸卡：原尺寸 / 自定义；自定义时大号像素数 + 宽 / 高 / 长边。多档尺寸在配方里加，这里只读列出。
struct SizeCard: View {
    @Bindable var ws: Workspace

    private var custom: Binding<Bool> {
        Binding(get: { ws.draft.image.size != nil }, set: { on in
            ws.draft.image.size = on ? (ws.base.image.size ?? 2560) : nil
        })
    }

    var body: some View {
        BentoCard {
            VStack(alignment: .leading, spacing: 10) {
                BentoHeader(symbol: "arrow.up.left.and.arrow.down.right", title: "尺寸",
                            subtitle: ws.draft.image.size == nil ? "保持原图像素，只压体积" : "缩放到指定尺寸") {
                    MiniSegmented(selection: custom, options: [(false, "原尺寸"), (true, "自定义")], label: "尺寸方式")
                }
                ZStack(alignment: .topLeading) {
                    if let size = ws.draft.image.size {
                        VStack(alignment: .leading, spacing: 10) {
                            HStack(alignment: .firstTextBaseline, spacing: 6) {
                                Text(ws.draft.image.sizeAxis.title).font(.system(size: 12)).foregroundStyle(Bento.secondary)
                                TextField("", value: Binding(get: { size }, set: { ws.draft.image.size = max(16, $0) }), format: .number.grouping(.never))
                                    .textFieldStyle(.plain)
                                    .font(.system(size: 44, weight: .light).monospacedDigit())
                                    .frame(width: CGFloat(max(2, String(size).count)) * 27 + 4)
                                    .accessibilityLabel("像素")
                                Text("px").font(.system(size: 13)).foregroundStyle(Bento.secondary)
                            }
                            MiniSegmented(selection: $ws.draft.image.sizeAxis, options: SizeAxis.allCases.map { ($0, $0.title) }, label: "按宽、高或长边")
                            Spacer(minLength: 0)
                            if !ws.draft.image.extraSizes.isEmpty {
                                BentoRow(label: "另出") {
                                    Text(verbatim: ws.draft.image.extraSizes.map { "\($0)" }.joined(separator: " / ") + " px")
                                        .foregroundStyle(Bento.secondary).monospacedDigit()
                                }
                                .help("这个配方每张图还会多出这几档小图，给网站按屏幕大小加载用；在配方里改")
                            } else {
                                Text("要一次出多档尺寸，在配方里加。")
                                    .font(.system(size: 11)).foregroundStyle(Bento.secondary)
                            }
                        }
                        .transition(.bentoGrow)
                    } else {
                        VStack(alignment: .leading, spacing: 6) {
                            Spacer(minLength: 0)
                            Text("原尺寸").font(.system(size: 38, weight: .light)).foregroundStyle(Bento.line)
                            Text("每张保持原来的宽高，只压体积。\n要缩小，切到「自定义」。")
                                .font(.system(size: 12)).foregroundStyle(Bento.secondary).lineSpacing(3)
                            Spacer(minLength: 0)
                        }
                        .transition(.bentoGrow)
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .animation(.spring(response: 0.45, dampingFraction: 0.82), value: ws.draft.image.size == nil)
            }
        }
        .bentoLocked(ws.running)
    }
}

// MARK: - 裁切
