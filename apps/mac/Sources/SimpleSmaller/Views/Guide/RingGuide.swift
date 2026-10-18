import AppKit
import SwiftUI

/// 访达圆环教学弹窗（设计稿 V2.2 · D）：3 步——在什么场景用（访达画面 + 真实圆环）→ 三步用法 → 快捷键与辅助功能权限。
/// 宽 724、圆角 30，页面压暗；右上 × 关闭，底部左分页点、右「上一步 / 下一步」，最后一步「知道了，去试试」。
/// 「不再显示」勾选不做：第一次打开应用时出现一次，怎么关掉都记为看过（共用说明第 3 条）。
struct RingGuide: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    private var center: GuideCenter { .shared }

    var body: some View {
        GeometryReader { geo in
            ZStack {
                Color(red: 17 / 255, green: 18 / 255, blue: 20 / 255).opacity(0.36)
                    .contentShape(Rectangle())
                    .onTapGesture {}
                    .accessibilityHidden(true)
                dialog
                    .frame(width: 724, height: min(568, geo.size.height - 48))
                    .background(RoundedRectangle(cornerRadius: 30, style: .continuous).fill(Bento.card).shadow(color: .black.opacity(0.3), radius: 40, y: 30))
                    .clipShape(RoundedRectangle(cornerRadius: 30, style: .continuous))
                    .scaleEffect(center.shown || reduceMotion ? 1 : 0.94)
                    .animation(reduceMotion ? nil : .spring(response: 0.45, dampingFraction: 0.75), value: center.shown)
            }
        }
    }

    private var dialog: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            Group {
                switch center.step {
                case 0: sceneStep
                case 1: howStep
                default: keysStep
                }
            }
            .id(center.step)
            .transition(reduceMotion ? .opacity : .asymmetric(insertion: .scale(scale: 0.97, anchor: .bottom).combined(with: .opacity), removal: .opacity))
            .padding(.horizontal, 26)
            .padding(.top, 12)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .clipped()
            footer
        }
        .animation(reduceMotion ? nil : .timingCurve(0.22, 1, 0.36, 1, duration: 0.4), value: center.step)
        .accessibilityElement(children: .contain)
        .accessibilityLabel("访达圆环怎么用")
        .accessibilityAddTraits(.isModal)
    }

    private var header: some View {
        HStack(spacing: 12) {
            BrandMark(color: .white)
                .frame(width: 26, height: 24.6)
                .frame(width: 40, height: 40)
                .background(RoundedRectangle(cornerRadius: 12, style: .continuous).fill(Color.brand))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("访达圆环怎么用").font(.system(size: 15, weight: .bold)).accessibilityAddTraits(.isHeader)
                Text("设置 › 入口 里可以再看").font(.system(size: 11)).foregroundStyle(Bento.secondary)
            }
            Spacer()
            Button { center.finish() } label: {
                Text("×")
                    .font(.system(size: 16))
                    .foregroundStyle(Color.primary)
                    .frame(width: 34, height: 34)
                    .background(Circle().fill(Bento.fill))
                    .contentShape(Circle())
            }
            .buttonStyle(BentoPressStyle())
            .help("关闭")
            .accessibilityLabel("关闭")
        }
        .padding(.horizontal, 26)
        .padding(.top, 22)
        .padding(.bottom, 6)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            HStack(spacing: 6) {
                ForEach(0..<3, id: \.self) { i in
                    let on = i == center.step
                    Button { center.step = i } label: {
                        Capsule()
                            .fill(on ? Color.brand : Bento.line)
                            .frame(width: on ? 24 : 8, height: 8)
                            .frame(height: 20)
                            .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityLabel("第 \(i + 1) 步")
                    .accessibilityAddTraits(on ? [.isButton, .isSelected] : .isButton)
                }
            }
            .animation(reduceMotion ? nil : .spring(response: 0.3, dampingFraction: 0.6), value: center.step)
            .accessibilityElement(children: .contain)
            .accessibilityLabel("步骤")
            Spacer()
            if center.step > 0 {
                GuideButton(title: "上一步", style: .plain, height: 44) { center.previous() }
            }
            GuideButton(title: center.step < 2 ? "下一步" : "知道了，去试试", style: center.step < 2 ? .ink : .brand, height: 44) { center.next() }
        }
        .padding(.horizontal, 26)
        .padding(.top, 16)
        .padding(.bottom, 22)
    }

    // MARK: 第 1 步：在访达里，选中就能压

    private var sceneStep: some View {
        VStack(alignment: .leading, spacing: 14) {
            FinderScene()
                .frame(width: 672, height: 296)
                .clipShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
                .accessibilityElement()
                .accessibilityLabel("访达里按住 Option 点文件，圆环在指针旁弹出")
                .accessibilityAddTraits(.isImage)
            VStack(alignment: .leading, spacing: 6) {
                HStack(spacing: 12) {
                    Text("在访达里，选中就能压").font(.system(size: 22, weight: .bold))
                    HStack(spacing: 6) {
                        ForEach(["发群之前", "传官网之前", "交给客户之前", "整理素材时"], id: \.self) { t in
                            Text(t).font(.system(size: 12)).padding(.horizontal, 10).frame(height: 24).background(Capsule().fill(Bento.fill))
                        }
                    }
                }
                Text("不用打开应用、不用拖来拖去：选中文件按住 ⌥ 点一下，选一项就开始，结果存在原文件旁边。")
                    .font(.system(size: 13))
                    .foregroundStyle(Bento.body)
                    .lineSpacing(8)
            }
        }
    }

    // MARK: 第 2 步：三步用法

    private var howStep: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("三步用法").font(.system(size: 22, weight: .bold))
            HStack(alignment: .top, spacing: 16) {
                howColumn(1, "按住 ⌥ 点文件", "多选、整个文件夹都可以；选了几个，圆环上沿就标几个。", RingSketches.press)
                howColumn(2, "内圈选做什么", "压缩、换格式、改尺寸、按用途裁切。带 ● 的是默认项，点内圈直接用它。", RingSketches.inner)
                howColumn(3, "外圈选常用", "点一下就开始；也可以按住不放，划到外圈再松手。", RingSketches.outer)
            }
            Text("点圆环中心、点圆外或按 Esc 收起。点的是文件夹，会按所选常用处理里面所有图片和视频，结果放在旁边的新文件夹。")
                .font(.system(size: 12))
                .foregroundStyle(Bento.body)
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.fill))
        }
    }

    private func howColumn(_ n: Int, _ title: String, _ text: String, _ sketch: Sketch) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            sketch.clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
            HStack(spacing: 8) {
                Text("\(n)")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Bento.onInk)
                    .frame(width: 22, height: 22)
                    .background(Circle().fill(Bento.ink))
                    .accessibilityHidden(true)
                Text(title).font(.system(size: 15, weight: .semibold))
            }
            .accessibilityElement(children: .combine)
            .accessibilityLabel("第 \(n) 步：\(title)")
            Text(text)
                .font(.system(size: 12))
                .foregroundStyle(Bento.body)
                .lineSpacing(5)
                .fixedSize(horizontal: false, vertical: true)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    // MARK: 第 3 步：快捷键

    private var keysStep: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("快捷键").font(.system(size: 22, weight: .bold))
            HStack(alignment: .top, spacing: 16) {
                VStack(spacing: 6) {
                    keyRow([.key("⌥"), .plus, .key("点击")], "唤出圆环")
                    keyRow([.key("点内圈")], "用带 ● 的默认项")
                    keyRow([.key("按住"), .arrow, .key("划到外圈松手")], "直接执行这一项")
                    keyRow([.key("Esc")], "收起，什么都不做")
                    keyRow([.key("⌘"), .key("⇧"), .key("V")], "处理剪贴板里的图片（不用圆环）")
                }
                .frame(maxWidth: .infinity)
                .layoutPriority(1.35)
                PermissionCard()
                    .frame(width: 298)
            }
        }
    }

    private enum Cap { case key(String), plus, arrow }

    private func keyRow(_ caps: [Cap], _ text: String) -> some View {
        HStack(spacing: 12) {
            HStack(spacing: 6) {
                ForEach(Array(caps.enumerated()), id: \.offset) { _, cap in
                    switch cap {
                    case .key(let k):
                        Text(k)
                            .font(.system(size: 13, weight: .semibold))
                            .lineLimit(1)
                            .fixedSize()
                            .padding(.horizontal, 8)
                            .frame(minWidth: 30, minHeight: 30)
                            .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Bento.card)
                                .shadow(color: Bento.key, radius: 0, y: 2))
                            .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Bento.edge, lineWidth: 1))
                    case .plus:
                        Text("+").foregroundStyle(Bento.secondary)
                    case .arrow:
                        Image(systemName: "arrow.right").font(.system(size: 11, weight: .medium)).foregroundStyle(Bento.secondary)
                    }
                }
            }
            Spacer(minLength: 8)
            Text(text).font(.system(size: 13)).lineLimit(1)
        }
        .padding(.horizontal, 14)
        .frame(height: 46)
        .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.fill))
        .accessibilityElement(children: .combine)
    }
}

private extension Bento {
    /// 键帽下沿 #C9CCD1、描边 #E2E4E8。
    static let key = dynamic(0xC9CCD1, 0x141517)
    static let edge = dynamic(0xE2E4E8, 0x45474C)
}

/// 第 3 步右边：辅助功能权限卡。每秒查一次授权，开了显示「已开启」；按钮打开 系统设置 › 隐私与安全性 › 辅助功能。
private struct PermissionCard: View {
    @State private var trusted = RingController.shared.isTrusted

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("先开一个权限").font(.system(size: 14, weight: .semibold)).foregroundStyle(Bento.alertText)
            Text("圆环要知道你点的是哪个文件，需要「辅助功能」权限。只读取文件位置，不记录其他操作。")
                .font(.system(size: 12))
                .foregroundStyle(Bento.alertText)
                .lineSpacing(5)
                .fixedSize(horizontal: false, vertical: true)
            VStack(alignment: .leading, spacing: 8) {
                Text("系统设置 › 隐私与安全性 › 辅助功能").font(.system(size: 10)).foregroundStyle(Bento.secondary)
                HStack(spacing: 8) {
                    BrandMark(color: .white)
                        .frame(width: 14, height: 13.3)
                        .frame(width: 22, height: 22)
                        .background(RoundedRectangle(cornerRadius: 6, style: .continuous).fill(Color.brand))
                    Text("简单小一点").font(.system(size: 12, weight: .semibold))
                    Spacer()
                    // 系统设置里那一行开关的样子，跟着实际授权变。
                    Capsule()
                        .fill(trusted ? Color(red: 0x34 / 255, green: 0xC7 / 255, blue: 0x59 / 255) : Bento.line)
                        .frame(width: 34, height: 20)
                        .overlay(alignment: trusted ? .trailing : .leading) {
                            Circle().fill(.white).frame(width: 16, height: 16).padding(2)
                        }
                        .animation(.spring(response: 0.3, dampingFraction: 0.7), value: trusted)
                }
                .accessibilityElement(children: .ignore)
                .accessibilityLabel(trusted ? "简单小一点：辅助功能已开启" : "简单小一点：辅助功能未开启")
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(Bento.card))
            .padding(.top, 4)
            Spacer(minLength: 8)
            if trusted {
                HStack(spacing: 6) {
                    Image(systemName: "checkmark").font(.system(size: 12, weight: .bold))
                    Text("已开启").font(.system(size: 13, weight: .semibold))
                }
                .foregroundStyle(Color.saving)
                .frame(maxWidth: .infinity)
                .frame(height: 40)
                .background(Capsule().fill(Bento.card))
                .accessibilityElement(children: .combine)
                .accessibilityLabel("辅助功能已开启")
            } else {
                Button(action: openSettings) {
                    Text("去系统设置里开启")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundStyle(.white)
                        .frame(maxWidth: .infinity)
                        .frame(height: 40)
                        .background(Capsule().fill(Color.brand))
                        .contentShape(Capsule())
                }
                .buttonStyle(BentoPressStyle())
            }
        }
        .padding(16)
        // 和左边 5 行快捷键一样高（5 × 46 + 4 × 6）。
        .frame(height: 254, alignment: .topLeading)
        .background(RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Bento.alertFill))
        .onReceive(Timer.publish(every: 1, on: .main, in: .common).autoconnect()) { _ in
            trusted = RingController.shared.isTrusted
        }
    }

    private func openSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }
}

// MARK: - 访达画面

/// 第 1 步的画面：代码画的访达窗口（1280 × 800 按 0.7 缩小），里面选中 4 张图，指针处是应用里真实的圆环（静态）。
private struct FinderScene: View {
    @Environment(\.colorScheme) private var scheme

    private struct Item {
        let name: String
        let colors: (UInt32, UInt32)?
        var selected = false
        var pdf = false
    }

    private let items: [Item] = [
        Item(name: "hero-01.jpg", colors: (0xD8CFC4, 0x8C7B6B)),
        Item(name: "hero-02.jpg", colors: (0xC9D6CF, 0x4F6B5D)),
        Item(name: "detail-03.jpg", colors: (0xE6D9C0, 0xA2875A)),
        Item(name: "detail-04.jpg", colors: (0xD6D6E2, 0x5E5E7A)),
        Item(name: "teaser.mov", colors: (0x1D1D1F, 0x1D1D1F)),
        Item(name: "提案.pdf", colors: nil, pdf: true),
        Item(name: "model-05.jpg", colors: (0xEADFD3, 0x6B5444), selected: true),
        Item(name: "model-06.jpg", colors: (0xE2DED8, 0x5F574C), selected: true),
        Item(name: "model-07.jpg", colors: (0xEFE6E0, 0x7A5C4E), selected: true),
        Item(name: "scene-08.jpg", colors: (0xDCE3EA, 0x4A5A6C), selected: true),
        Item(name: "scene-09.jpg", colors: (0xE8E3D3, 0x6E6545)),
        Item(name: "scene-10.jpg", colors: (0xE6E0EA, 0x5F4E6B)),
    ]

    private var dark: Bool { scheme == .dark }
    private var finderBG: Color { dark ? Color(white: 0.12) : .white }
    private var sidebarBG: Color { dark ? Color(white: 0.17) : Color(red: 0xF3 / 255, green: 0xF3 / 255, blue: 0xF5 / 255) }
    private var divider: Color { dark ? Color(white: 0.24) : Color(red: 0xE2 / 255, green: 0xE2 / 255, blue: 0xE5 / 255) }
    private var text: Color { dark ? Color(white: 0.92) : Color(red: 0x1D / 255, green: 0x1D / 255, blue: 0x1F / 255) }

    var body: some View {
        // 外框按给定大小（672 × 296），里面的访达画面按 0.7 缩小后裁掉超出的部分。
        Color(red: 0x8E / 255, green: 0x99 / 255, blue: 0xA8 / 255).overlay(alignment: .topLeading) {
            scene
                .frame(width: 1280, height: 800)
                .scaleEffect(0.7, anchor: .topLeading)
                .offset(x: -24 * 0.7, y: -24 * 0.7)
                .frame(width: 1, height: 1, alignment: .topLeading)
        }
        .clipped()
        .overlay(alignment: .topLeading) { ZStack(alignment: .topLeading) {
            // 指针 +「⌥ 按住点一下」、左下场景标签（不缩放，照稿）。
            Cursor()
                .frame(width: 22, height: 30)
                .shadow(color: .black.opacity(0.3), radius: 1.5, y: 2)
                .offset(x: 299, y: 197)
            HStack(spacing: 6) {
                Text("⌥")
                    .font(.system(size: 12))
                    .foregroundStyle(Color(white: 0.07))
                    .frame(minWidth: 18, minHeight: 18)
                    .background(RoundedRectangle(cornerRadius: 5).fill(.white))
                Text("按住点一下").font(.system(size: 11, weight: .semibold)).foregroundStyle(.white)
            }
            .padding(.leading, 4)
            .padding(.trailing, 9)
            .frame(height: 24)
            .background(Capsule().fill(Color(white: 0.07)))
            .shadow(color: .black.opacity(0.25), radius: 6, y: 4)
            .offset(x: 321, y: 219)
            VStack {
                Spacer()
                Text("访达 · 选中了 4 张图")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(Color(white: 0.07))
                    .padding(.horizontal, 10)
                    .frame(height: 24)
                    .background(Capsule().fill(Color.white.opacity(0.92)))
                    .shadow(color: .black.opacity(0.12), radius: 4, y: 2)
                    .padding(.leading, 14)
                    .padding(.bottom, 12)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading) }
        .allowsHitTesting(false)
    }

    private var scene: some View {
        ZStack(alignment: .topLeading) {
            LinearGradient(colors: [Color(hex: 0xB9C3CF), Color(hex: 0x8E99A8)], startPoint: .topLeading, endPoint: .bottomTrailing)
            HStack(spacing: 0) {
                sidebar
                Rectangle().fill(divider).frame(width: 1)
                VStack(spacing: 0) {
                    Text("品牌素材 / 2026 秋季新品")
                        .font(.system(size: 13, weight: .bold))
                        .foregroundStyle(text)
                        .padding(.horizontal, 16)
                        .frame(maxWidth: .infinity, minHeight: 52, maxHeight: 52, alignment: .leading)
                    Rectangle().fill(divider).frame(height: 1)
                    grid
                        .padding(28)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
                .background(finderBG)
            }
            .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
            .shadow(color: .black.opacity(0.25), radius: 25, y: 18)
            .padding(24)
            // 圆环：应用里的真实圆环视图，示例为 4 张图、外圈停在第一个常用上。
            RingView(slots: RingMenu.slots(for: .image, store: AppModel.shared.store), badge: "4 张",
                     onChoose: { _ in }, onDismiss: {}, still: true, initialHover: 0)
                .allowsHitTesting(false)
                .shadow(color: .black.opacity(0.28), radius: 12, y: 10)
                .offset(x: 222, y: 76)
        }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 8) {
                Circle().fill(Color(hex: 0xFF5F57)).frame(width: 12, height: 12)
                Circle().fill(Color(hex: 0xFEBC2E)).frame(width: 12, height: 12)
                Circle().fill(Color(hex: 0x28C840)).frame(width: 12, height: 12)
            }
            .padding(.horizontal, 8)
            .frame(height: 52)
            Text("个人收藏").font(.system(size: 11, weight: .semibold)).foregroundStyle(Color(hex: 0x8E8E93)).padding(.horizontal, 8).padding(.vertical, 4)
            ForEach(["桌面", "下载", "品牌素材", "文稿"], id: \.self) { name in
                Text(name)
                    .font(.system(size: 13))
                    .foregroundStyle(text)
                    .padding(.horizontal, 8)
                    .frame(maxWidth: .infinity, minHeight: 28, alignment: .leading)
                    .background(RoundedRectangle(cornerRadius: 6).fill(name == "品牌素材" ? (dark ? Color(white: 0.27) : Color(hex: 0xDCDCE0)) : .clear))
            }
            Spacer()
        }
        .padding(.horizontal, 10)
        .frame(width: 180)
        .background(sidebarBG)
    }

    private var grid: some View {
        let columns = Array(repeating: GridItem(.fixed(120), spacing: 30), count: 6)
        return LazyVGrid(columns: columns, alignment: .leading, spacing: 18) {
            ForEach(items, id: \.name) { item in
                VStack(spacing: 6) {
                    if item.pdf {
                        Rectangle().fill(.white).frame(width: 60, height: 76).shadow(color: .black.opacity(0.25), radius: 1.5, y: 1)
                    } else if let c = item.colors {
                        Rectangle()
                            .fill(LinearGradient(colors: [Color(hex: c.0), Color(hex: c.1)], startPoint: .topLeading, endPoint: .bottomTrailing))
                            .frame(width: 96, height: 72)
                            .shadow(color: .black.opacity(0.2), radius: 1.5, y: 1)
                    }
                    Text(item.name)
                        .font(.system(size: 12))
                        .foregroundStyle(item.selected ? .white : text)
                        .padding(.horizontal, item.selected ? 6 : 0)
                        .background(RoundedRectangle(cornerRadius: 4).fill(item.selected ? Color.brand : .clear))
                }
                .padding(.top, item.selected ? 2 : 0)
                .frame(width: 120, height: 130, alignment: .top)
                .background(RoundedRectangle(cornerRadius: 6).fill(item.selected ? (dark ? Color(white: 0.24) : Color(hex: 0xE3E3E8)) : .clear))
            }
        }
    }
}

/// 指针（照稿的箭头路径）。
private struct Cursor: View {
    var body: some View {
        Sketch(width: 22, height: 30, svg: ##"<path d="M3 2l0 22l5.5 -5.5l4.5 10l3.6 -1.7l-4.5 -10l7.6 0z" fill="#111214" stroke="#FFFFFF" stroke-width="1.6" stroke-linejoin="round"/>"##)
    }
}

extension Color {
    init(hex: UInt32) {
        self.init(red: Double(hex >> 16 & 0xFF) / 255, green: Double(hex >> 8 & 0xFF) / 255, blue: Double(hex & 0xFF) / 255)
    }
}

// MARK: - 第 2 步的三张插图（照稿 196 × 150）

@MainActor
private enum RingSketches {
    private static func make(_ svg: String, _ texts: [SketchText] = []) -> Sketch {
        Sketch(width: 196, height: 150, svg: #"<rect width="196" height="150" rx="16" fill="{panel}"/>"# + svg, texts: texts)
    }

    static var press: Sketch {
        make("""
        <rect x="98" y="38" width="70" height="50" rx="6" fill="#BFA48C"/>
        <rect x="106" y="94" width="54" height="12" rx="4" fill="#EF3922"/>
        <rect x="22" y="55" width="54" height="46" rx="8" fill="{key}"/>
        <rect x="22" y="52" width="54" height="46" rx="8" fill="{card}"/>
        <path d="M128 60l0 24l6 -6l5 11l4 -2l-5 -11l8 0z" fill="#111214" stroke="#FFFFFF" stroke-width="1.6" stroke-linejoin="round"/>
        <circle cx="128" cy="60" r="12" fill="none" stroke="#EF3922" stroke-width="2" opacity="0.6"/>
        """, [
            SketchText(49, 76, "⌥", size: 22, weight: .semibold),
            SketchText(86, 74, "+", size: 18, weight: .semibold),
        ])
    }

    static var inner: Sketch {
        make("""
        <path d="M75.2 17.2 A62.1 62.1 0 0 1 120.8 17.2 L107.9 49.9 A27.0 27.0 0 0 0 88.1 49.9 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M122.8 18.1 A62.1 62.1 0 0 1 154.9 50.2 L122.8 64.2 A27.0 27.0 0 0 0 108.8 50.2 Z" fill="#EF3922" stroke="rgba(0,0,0,0.06)"/>
        <path d="M155.8 52.2 A62.1 62.1 0 0 1 155.8 97.8 L123.1 84.9 A27.0 27.0 0 0 0 123.1 65.1 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M154.9 99.8 A62.1 62.1 0 0 1 122.8 131.9 L108.8 99.8 A27.0 27.0 0 0 0 122.8 85.8 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M120.8 132.8 A62.1 62.1 0 0 1 75.2 132.8 L88.1 100.1 A27.0 27.0 0 0 0 107.9 100.1 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M73.2 131.9 A62.1 62.1 0 0 1 41.1 99.8 L73.2 85.8 A27.0 27.0 0 0 0 87.2 99.8 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M40.2 97.8 A62.1 62.1 0 0 1 40.2 52.2 L72.9 65.1 A27.0 27.0 0 0 0 72.9 84.9 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M41.1 50.2 A62.1 62.1 0 0 1 73.2 18.1 L87.2 50.2 A27.0 27.0 0 0 0 73.2 64.2 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <circle cx="98" cy="75" r="24.3" fill="none" stroke="#EF3922" stroke-width="2.2"/>
        """, [
            SketchText(98, 30.5, "压缩", size: 11.5, weight: .semibold),
            SketchText(129.5, 43.5, "WebP", size: 11.5, weight: .semibold, color: .white),
            SketchText(142.6, 75, "AVIF", size: 11.5, weight: .semibold),
            SketchText(129.5, 106.5, "JPG", size: 11.5, weight: .semibold),
            SketchText(98, 119.5, "PNG", size: 11.5, weight: .semibold),
            SketchText(66.5, 106.5, "尺寸", size: 11.5, weight: .semibold),
            SketchText(53.5, 75, "用途", size: 11.5, weight: .semibold),
            SketchText(66.5, 43.5, "自定义", size: 11.5, weight: .semibold),
        ])
    }

    static var outer: Sketch {
        make("""
        <path d="M81.1 32.2 A46.0 46.0 0 0 1 114.9 32.2 L105.3 56.4 A20.0 20.0 0 0 0 90.7 56.4 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M116.3 32.8 A46.0 46.0 0 0 1 140.2 56.7 L116.3 67 A20.0 20.0 0 0 0 106 56.7 Z" fill="#EF3922" stroke="rgba(0,0,0,0.06)"/>
        <path d="M140.8 58.1 A46.0 46.0 0 0 1 140.8 91.9 L116.6 82.3 A20.0 20.0 0 0 0 116.6 67.7 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M140.2 93.3 A46.0 46.0 0 0 1 116.3 117.2 L106 93.3 A20.0 20.0 0 0 0 116.3 83 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M114.9 117.8 A46.0 46.0 0 0 1 81.1 117.8 L90.7 93.6 A20.0 20.0 0 0 0 105.3 93.6 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M79.7 117.2 A46.0 46.0 0 0 1 55.8 93.3 L79.7 83 A20.0 20.0 0 0 0 90 93.3 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M55.2 91.9 A46.0 46.0 0 0 1 55.2 58.1 L79.4 67.7 A20.0 20.0 0 0 0 79.4 82.3 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M55.8 56.7 A46.0 46.0 0 0 1 79.7 32.8 L90 56.7 A20.0 20.0 0 0 0 79.7 67 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M104.7 5.3 A70.0 70.0 0 0 1 133.5 14.7 L123.4 31.9 A50.0 50.0 0 0 0 102.8 25.2 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M135.6 16 A70.0 70.0 0 0 1 157 37.4 L140.2 48.1 A50.0 50.0 0 0 0 124.9 32.8 Z" fill="{ink}" stroke="rgba(0,0,0,0.06)"/>
        <path d="M158.3 39.5 A70.0 70.0 0 0 1 167.7 68.3 L147.8 70.2 A50.0 50.0 0 0 0 141.1 49.6 Z" fill="{card}" stroke="rgba(0,0,0,0.06)"/>
        <circle cx="98" cy="75" r="18" fill="none" stroke="#EF3922" stroke-width="1.6"/>
        <path d="M122 34l0 21.6l5.4 -5.4l4.5 9.9l3.6 -1.8l-4.5 -9.9l7.2 0z" fill="#111214" stroke="#FFFFFF" stroke-width="1.4" stroke-linejoin="round"/>
        <circle cx="160" cy="118" r="14" fill="#EF3922"/>
        <path d="M153 118l5 5 9-10" fill="none" stroke="#FFFFFF" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"/>
        """)
    }
}
