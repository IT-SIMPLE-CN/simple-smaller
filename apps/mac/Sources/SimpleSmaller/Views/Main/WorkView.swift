import SwiftUI
import UniformTypeIdentifiers

/// 一类文件的工作区：便当盒网格；做完换到完成页。
struct WorkView: View {
    let ws: Workspace

    var body: some View {
        Group {
            if ws.showResult, !ws.running, !ws.files.isEmpty {
                ResultView(ws: ws)
                    .transition(.bentoGrow)
            } else {
                WorkGrid(ws: ws)
                    .transition(.opacity)
            }
        }
        .padding(.horizontal, Bento.pad)
        .padding(.bottom, Bento.pad)
        .padding(.top, 6)
        .animation(.spring(response: 0.45, dampingFraction: 0.82), value: ws.showResult)
    }
}

/// 网格（设计稿 03）：左边一张跨三行的大卡放文件或预览；右边上方横条是配方，下面两行参数卡，
/// 右下一格是输出与「开始」。只做三档：
/// 宽（≥ 1080）三列；窄时右侧合成一列可滚动、「开始」固定在底；很宽时右侧两列不再拉宽。
struct WorkGrid: View {
    let ws: Workspace
    @Namespace private var slots
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    private static let barHeight: CGFloat = 58
    private static let maxColumn: CGFloat = 400
    /// 换卡片内容时整组重新弹入。
    private var appearKey: String { "\(ws.type.rawValue)|\(ws.segmentKey)|\(ws.files.isEmpty)" }

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width, h = geo.size.height
            let gap = Bento.gap
            let narrow = w < 1080
            Group {
            if narrow {
                // 窄：左大卡占一半；右边配方条 + 可滚动的参数卡 + 固定在底的开始卡。
                let right = max(300, w * 0.46)
                HStack(spacing: gap) {
                    main.matchedGeometryEffect(id: "main", in: slots).bentoRegion(5).bentoAppear(0, trigger: appearKey)
                    VStack(spacing: gap) {
                        topBar.frame(height: Self.barHeight).matchedGeometryEffect(id: "bar", in: slots).bentoRegion(4).bentoAppear(1, trigger: appearKey)
                        ScrollView {
                            VStack(spacing: gap) {
                                upper.frame(minHeight: 240).bentoRegion(3)
                                lowerLeft.frame(minHeight: 220).bentoRegion(2)
                            }
                        }
                        .scrollIndicators(.automatic)
                        .bentoAppear(2, trigger: appearKey)
                        lowerRight.frame(height: min(280, h * 0.42)).matchedGeometryEffect(id: "start", in: slots).bentoRegion(1).bentoAppear(3, trigger: appearKey)
                    }
                    .frame(width: right)
                }
            } else {
                let unit = (w - 2 * gap) / (leftFraction + 2)
                let col = min(Self.maxColumn, unit)
                let rowH = (h - Self.barHeight - 2 * gap) / 2
                HStack(spacing: gap) {
                    main.matchedGeometryEffect(id: "main", in: slots).bentoRegion(5).bentoAppear(0, trigger: appearKey)
                    VStack(spacing: gap) {
                        topBar.frame(height: Self.barHeight).matchedGeometryEffect(id: "bar", in: slots).bentoRegion(4).bentoAppear(1, trigger: appearKey)
                            upper.frame(height: rowH).bentoRegion(3).bentoAppear(2, trigger: appearKey)
                            HStack(spacing: gap) {
                                lowerLeft.frame(width: col).bentoRegion(2).bentoAppear(3, trigger: appearKey)
                                lowerRight.frame(width: col).matchedGeometryEffect(id: "start", in: slots).bentoRegion(1).bentoAppear(4, trigger: appearKey)
                            }
                            .frame(height: rowH)
                    }
                    .frame(width: 2 * col + gap)
                }
            }
            }
            // 窗口缩到窄档 / 放回宽档时，每格按同一身份弹到新位置，不是重建。
            .animation(reduceMotion ? nil : .spring(response: 0.45, dampingFraction: 0.82), value: narrow)
        }
    }

    /// 左大卡占几份（右边两列各一份）。图片压缩 1.3，有预览的 1.5。
    private var leftFraction: CGFloat {
        ws.type == .image && !(ws.isCropping && ws.cropEditing) ? 1.3 : 1.5
    }

    /// 选了比例、点了一张：左大卡换成大图调框。
    private var isImageCrop: Bool { ws.isCropping && ws.cropEditing }

    // MARK: 左大卡

    @ViewBuilder private var main: some View {
        if ws.files.isEmpty {
            DropCard(ws: ws)
        } else {
            switch ws.type {
            case .image:
                if isImageCrop { PreviewCard(inset: 14) { CropView(ws: ws) } } else { FilesCard(ws: ws) }
            case .video:
                switch ws.videoSegment {
                case .compress: FilesCard(ws: ws)
                case .edit: PreviewCard(inset: 14) { VideoEditView(ws: ws) }
                case .frames: PreviewCard(inset: 14) { VideoFramesView(ws: ws) }
                }
            case .pdf:
                PreviewCard(inset: 14) { PdfDetailView(ws: ws) }
            }
        }
    }

    // MARK: 配方条

    @ViewBuilder private var topBar: some View {
        if ws.usesRecipe {
            RecipeBar(ws: ws)
        } else {
            BentoCard(padding: EdgeInsets(top: 0, leading: 20, bottom: 0, trailing: 20)) {
                HStack(spacing: 10) {
                    Text(ws.pdfSegment.rawValue).font(.system(size: 13, weight: .semibold))
                    Text(PdfNotes.segmentNote(ws.pdfSegment)).font(.system(size: 12)).foregroundStyle(Bento.secondary).lineLimit(1)
                    Spacer()
                }
                .frame(maxHeight: .infinity)
            }
        }
    }

    // MARK: 参数卡

    @ViewBuilder private var upper: some View {
        switch ws.type {
        case .image: FrameCard(ws: ws)
        case .video: if ws.videoSegment == .frames { FramesCard(ws: ws) } else { VideoPictureCard(ws: ws) }
        case .pdf: PdfMainCard(ws: ws)
        }
    }

    @ViewBuilder private var lowerLeft: some View {
        switch ws.type {
        case .image: FormatQualityCard(ws: ws)
        case .video: if ws.videoSegment == .frames { FrameEachCard(ws: ws) } else { VideoFormatCard(ws: ws) }
        case .pdf: PdfOptionsCard(ws: ws)
        }
    }

    @ViewBuilder private var lowerRight: some View {
        switch ws.type {
        case .image: OutputStartCard(ws: ws)
        case .video: if ws.videoSegment == .frames { FramesOutputStartCard(ws: ws) } else { VideoAudioStartCard(ws: ws) }
        case .pdf: if ws.pdfSegment == .compress { PdfStartCard(ws: ws) } else { OutputStartCard(ws: ws) }
        }
    }
}

/// 放预览类视图（裁切、剪辑、序列帧、PDF 页面）的大卡：内容自己铺满，只裁圆角。
struct PreviewCard<Content: View>: View {
    var inset: CGFloat = 0
    @ViewBuilder var content: Content

    var body: some View {
        BentoCard(padding: EdgeInsets(top: inset, leading: inset, bottom: inset, trailing: inset)) { content }
    }
}

// MARK: - 输出与开始

extension View {
    /// 便当盒里的一格：旁白当作一个分组整体读完再读下一格，顺序按优先级（大的先读）。
    func bentoRegion(_ priority: Double) -> some View {
        accessibilityElement(children: .contain).accessibilitySortPriority(priority)
    }
}
