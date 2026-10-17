import SwiftUI

/// 首页（设计稿 V2.1 · 01）：打开应用、还没选类型时显示。
/// 文件拖到哪张卡就进哪个功能；拖到窗口别处按类型自动分流。
struct HomeView: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        GeometryReader { geo in
            let cardW = min(260, max(200, (geo.size.width - 40 - 176 - 3 * 16 - 40) / 4))
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 14) {
                    Text("拖进来，选一下")
                        .font(.system(size: 46, weight: .semibold))
                        .tracking(-1)
                    Image(systemName: "arrow.turn.down.right")
                        .font(.system(size: 30, weight: .medium))
                        .accessibilityHidden(true)
                }
                .padding(.top, max(20, (geo.size.height - 640) / 2))
                Spacer(minLength: 24).frame(maxHeight: 56)
                HStack(alignment: .top, spacing: 16) {
                    VStack(alignment: .leading, spacing: 0) {
                        Button { openWindow(id: "about") } label: {
                            HStack(spacing: 12) {
                                Text("关于")
                                Image(systemName: "arrow.up.right").font(.system(size: 11, weight: .semibold))
                            }
                            .font(.system(size: 12))
                            .padding(.leading, 16)
                            .padding(.trailing, 12)
                            .frame(height: 38)
                            .overlay(Capsule().strokeBorder(Color.primary, lineWidth: 1.2))
                            .contentShape(Capsule())
                        }
                        .buttonStyle(BentoPressStyle())
                        Spacer()
                        Text("图片、视频、PDF\n都在本机处理，不上传。")
                            .font(.system(size: 12))
                            .foregroundStyle(Bento.secondary)
                            .lineSpacing(4)
                    }
                    .frame(width: 160, height: 392, alignment: .leading)
                    ForEach(Array(HomeCard.Kind.allCases.enumerated()), id: \.element) { i, kind in
                        HomeCard(kind: kind)
                            .frame(width: cardW, height: 392)
                            .bentoAppear(i)
                    }
                }
                Text("把文件拖到卡片上就进对应的功能；拖到窗口别处按类型自动分流。")
                    .font(.system(size: 12))
                    .foregroundStyle(Bento.secondary)
                    .padding(.leading, 176)
                    .padding(.top, 28)
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 40)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        }
    }
}

struct HomeCard: View {
    enum Kind: CaseIterable, Hashable {
        case image, video, pdf, ring

        var title: String {
            switch self {
            case .image: "图片"
            case .video: "视频"
            case .pdf: "PDF"
            case .ring: "访达圆环"
            }
        }

        var subtitle: String {
            switch self {
            case .image: "压缩 · 裁切"
            case .video: "压缩 · 剪辑 · 序列帧"
            case .pdf: "压缩 · 合并 · 拆分"
            case .ring: "不开窗口也能压"
            }
        }

        var text: String {
            switch self {
            case .image: "WebP、AVIF、JPG 一键变小，一次出多档尺寸；按比例批量裁切。"
            case .video: "按体积压到 25 MB，裁成 9:16，或者拆成一张张序列帧。"
            case .pdf: "邮件附件、屏幕阅读、打印保真三档；合并、拆分、转图片。"
            case .ring: "在访达里按住 ⌥ 点文件，圆环里点一下就好。第一次用要在设置里开启。"
            }
        }

        var formats: String {
            switch self {
            case .image: "JPG · PNG · HEIC"
            case .video: "MP4 · MOV · GIF"
            case .pdf: "PDF"
            case .ring: "macOS"
            }
        }

        var art: String {
            switch self {
            case .image: "home-image"
            case .video: "home-video"
            case .pdf: "home-pdf"
            case .ring: "home-ring"
            }
        }

        var symbol: String {
            switch self {
            case .image: "photo"
            case .video: "film"
            case .pdf: "doc.richtext"
            case .ring: "circle.circle"
            }
        }

        var type: MediaType? {
            switch self {
            case .image: .image
            case .video: .video
            case .pdf: .pdf
            case .ring: nil
            }
        }
    }

    @Environment(AppModel.self) private var model
    @Environment(\.openSettings) private var openSettings
    let kind: Kind
    @State private var targeted = false
    @State private var hovering = false

    var body: some View {
        Button(action: open) {
            VStack(alignment: .leading, spacing: 0) {
                art
                    .aspectRatio(4 / 3, contentMode: .fit)
                    .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                    .overlay(alignment: .topTrailing) {
                        // 右上角挖出一个白色缺口放图标（设计稿）。
                        Image(systemName: kind.symbol)
                            .font(.system(size: 14, weight: .medium))
                            .foregroundStyle(Color.primary)
                            .frame(width: 34, height: 34)
                            .background(Circle().fill(Bento.fill))
                            .padding([.leading, .bottom], 6)
                            .background(UnevenRoundedRectangle(bottomLeadingRadius: 16, style: .continuous).fill(Bento.card))
                    }
                Text(kind.title)
                    .font(.system(size: 24, weight: .semibold))
                    .padding(.top, 18)
                Text(kind.subtitle)
                    .font(.system(size: 13))
                    .foregroundStyle(Bento.secondary)
                    .padding(.top, 4)
                Rectangle().fill(Color.primary).frame(width: 18, height: 1.5).padding(.top, 16)
                Text(kind.text)
                    .font(.system(size: 12))
                    .foregroundStyle(Color.primary.opacity(0.78))
                    .lineSpacing(3)
                    .fixedSize(horizontal: false, vertical: true)
                    .padding(.top, 12)
                Spacer(minLength: 8)
                HStack {
                    Image(systemName: "arrow.right")
                        .font(.system(size: 15, weight: .semibold))
                        .foregroundStyle(kind == .image || targeted ? Color.white : Color.primary)
                        .frame(width: 36, height: 36)
                        .background(Circle().fill(kind == .image || targeted ? Color.brand : Bento.fill))
                    Spacer()
                    Text(kind.formats).font(.system(size: 10)).tracking(0.4).foregroundStyle(Bento.secondary)
                }
            }
            .padding(14)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Bento.card))
            .overlay {
                RoundedRectangle(cornerRadius: 20, style: .continuous)
                    .strokeBorder(Color.brand, lineWidth: targeted ? 2.5 : 0)
            }
            .shadow(color: .black.opacity(hovering ? 0.08 : 0.03), radius: hovering ? 12 : 2, y: hovering ? 6 : 1)
            .contentShape(RoundedRectangle(cornerRadius: 20, style: .continuous))
        }
        .buttonStyle(BentoPressStyle())
        .onHover { hovering = $0 }
        .animation(.smooth(duration: 0.2), value: hovering)
        .animation(.smooth(duration: 0.15), value: targeted)
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: kind.type)
            return true
        } isTargeted: { targeted = $0 }
        .accessibilityLabel("\(kind.title)：\(kind.subtitle)")
        .accessibilityHint(kind.text)
    }

    @ViewBuilder private var art: some View {
        if let url = Bundle.main.url(forResource: kind.art, withExtension: "svg"), let image = NSImage(contentsOf: url) {
            Image(nsImage: image).resizable().scaledToFill()
        } else {
            Rectangle().fill(Bento.fill)
        }
    }

    private func open() {
        if let type = kind.type {
            model.selectedType = type
        } else {
            openSettings()
        }
    }
}

/// 首页工具栏：正中品牌，右边粘贴剪贴板与设置。
struct HomeToolbar: ToolbarContent {
    @Environment(AppModel.self) private var model

    var body: some ToolbarContent {
        if #available(macOS 26.0, *) {
            ToolbarItem(placement: .principal) { brand }
                .sharedBackgroundVisibility(.hidden)
        } else {
            ToolbarItem(placement: .principal) { brand }
        }
        ToolbarItem(placement: .primaryAction) {
            Button { model.pasteFromClipboard() } label: {
                Label("粘贴剪贴板", systemImage: "doc.on.clipboard")
            }
            .help("处理剪贴板里的图片或文件（⌘⇧V）")
        }
        ToolbarItem(placement: .primaryAction) {
            SettingsLink { Label("设置", systemImage: "gearshape") }
                .help("设置（⌘,）")
        }
    }
}

extension HomeToolbar {
    var brand: some View {
        HStack(spacing: 8) {
            BrandLogo().frame(width: 22, height: 21)
            Text("简单小一点").font(.system(size: 15, weight: .bold))
            Text("SIMPLE SMALLER").font(.system(size: 11)).tracking(1.5).foregroundStyle(Bento.secondary)
        }
        .accessibilityElement(children: .combine)
    }
}

/// 应用的红色标（首页、关于用）。
struct BrandLogo: View {
    var body: some View {
        if let url = Bundle.main.url(forResource: "logo-mark", withExtension: "svg"), let image = NSImage(contentsOf: url) {
            Image(nsImage: { image.isTemplate = true; return image }()).resizable().scaledToFit().foregroundStyle(Color.brand)
        } else {
            Circle().fill(Color.brand)
        }
    }
}
