import AppKit
import ImageIO
import SwiftUI

/// 图片「裁切」（设计稿 03）：上面一条写当前比例与这张图，中间大图拖框，底部胶片条。
/// 每张默认居中、按比例取最大；拖框移动、拖四角缩放；↩ 确认并到下一张，←/→ 切换，⌘Z 撤回这一张。
struct CropView: View {
    @Bindable var ws: Workspace
    @FocusState private var focused: Bool
    @State private var images: [URL: CGImage] = [:]

    var body: some View {
        let items = ws.cropItems
        let index = ws.cropCurrentIndex
        VStack(spacing: 0) {
            if let item = items.indices.contains(index) ? items[index] : nil {
                header(item)
                Divider()
                ZStack(alignment: .bottom) {
                    CropCanvas(ws: ws, item: item, index: index, image: images[item.url])
                    controls(count: items.count, index: index)
                        .padding(.bottom, 14)
                }
                .background(Color(nsColor: .underPageBackgroundColor))
                // 单张图片时没有胶片条。
                if items.count > 1 {
                    Divider()
                    FilmStrip(ws: ws, items: items, index: index)
                }
            } else {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .focusable()
        .focused($focused)
        .focusEffectDisabled()
        .onKeyPress(.return) { confirmNext(); return .handled }
        .onKeyPress(.leftArrow) { go(-1); return .handled }
        .onKeyPress(.rightArrow) { go(1); return .handled }
        .onKeyPress(characters: ["z"], phases: .down) { press in
            guard press.modifiers.contains(.command), let item = currentItem else { return .ignored }
            withAnimation(.smooth(duration: 0.25)) { ws.undoCropBox(for: item) }
            return .handled
        }
        .onAppear { focused = true }
        .task(id: loadKey) { await loadAround() }
    }

    private var currentItem: FileItem? {
        let items = ws.cropItems
        return items.indices.contains(ws.cropCurrentIndex) ? items[ws.cropCurrentIndex] : nil
    }

    private func header(_ item: FileItem) -> some View {
        HStack {
            Text(ws.cropTitle).fontWeight(.semibold)
            Spacer()
            Text("\(item.name) · \(item.dimensions)")
                .font(.system(size: 12))
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.middle)
        }
        .padding(.horizontal, 16)
        .frame(height: 40)
    }

    private func controls(count: Int, index: Int) -> some View {
        HStack(spacing: 8) {
            Button("← 上一张") { go(-1) }
                .buttonStyle(CropButtonStyle(primary: false))
                .disabled(index == 0)
            Button("确认，下一张 ↩") { confirmNext() }
                .buttonStyle(CropButtonStyle(primary: true))
        }
    }

    private func go(_ delta: Int) {
        let count = ws.cropItems.count
        guard count > 0 else { return }
        let next = min(count - 1, max(0, ws.cropCurrentIndex + delta))
        guard next != ws.cropCurrentIndex else { return }
        withAnimation(.smooth(duration: 0.25)) { ws.cropIndex = next }
    }

    /// 确认这张（把当前框记下来），到下一张；最后一张只确认。
    private func confirmNext() {
        guard let item = currentItem else { return }
        let i = ws.cropCurrentIndex
        if ws.cropBoxes[item.url] == nil { ws.cropBoxes[item.url] = ws.effectiveCropBox(i) }
        withAnimation(.smooth(duration: 0.25)) {
            ws.cropConfirmed.insert(item.url)
            if i + 1 < ws.cropItems.count { ws.cropIndex = i + 1 }
        }
    }

    private var loadKey: String {
        let items = ws.cropItems
        let i = ws.cropCurrentIndex
        return items.indices.contains(i) ? items[i].url.path : ""
    }

    /// 当前这张和前后各一张预先解码成屏幕尺寸，切换时不等。
    private func loadAround() async {
        let items = ws.cropItems
        let i = ws.cropCurrentIndex
        for k in [i, i + 1, i - 1] where items.indices.contains(k) {
            let url = items[k].url
            if images[url] != nil { continue }
            if let cg = await CropImageLoader.load(url) { images[url] = cg }
        }
        // 只留附近几张，避免一大批图都压在内存里。
        let keep = Set(items.indices.filter { abs($0 - i) <= 2 }.map { items[$0].url })
        images = images.filter { keep.contains($0.key) }
    }
}

/// 解码成屏幕用的尺寸（长边 2400），按 EXIF 转正，与引擎的像素坐标一致。
enum CropImageLoader {
    static func load(_ url: URL) async -> CGImage? {
        await Task.detached(priority: .userInitiated) {
            guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
            let options: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: 2400,
                kCGImageSourceShouldCacheImmediately: true,
            ]
            return CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary)
        }.value
    }
}

/// 大图与裁切框。
struct CropCanvas: View {
    @Bindable var ws: Workspace
    let item: FileItem
    let index: Int
    let image: CGImage?

    /// 一次拖动开始时的框（原图像素）。
    @State private var dragStart: CGRect?
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    enum Corner: CaseIterable { case topLeft, topRight, bottomLeft, bottomRight }

    var body: some View {
        GeometryReader { geo in
            let layout = Layout(canvas: geo.size, imageSize: CGSize(width: item.width, height: item.height))
            let box = ws.effectiveCropBox(index)
            let view = layout.toView(box)
            ZStack(alignment: .topLeading) {
                Group {
                    if let image {
                        Image(decorative: image, scale: 1).resizable().interpolation(.high)
                    } else if let thumb = item.thumbnail {
                        Image(nsImage: thumb).resizable()
                    } else {
                        Rectangle().fill(.quaternary)
                    }
                }
                .frame(width: layout.display.width, height: layout.display.height)
                .offset(x: layout.origin.x, y: layout.origin.y)
                .accessibilityHidden(true)

                // 框外压暗一半。
                Path { p in
                    p.addRect(CGRect(origin: layout.origin, size: layout.display))
                    p.addRect(view)
                }
                .fill(Color.black.opacity(0.5), style: FillStyle(eoFill: true))
                .allowsHitTesting(false)

                frame(view, box: box)

                // 拖框内部移动。
                Rectangle()
                    .fill(Color.white.opacity(0.001))
                    .frame(width: view.width, height: view.height)
                    .offset(x: view.minX, y: view.minY)
                    .gesture(moveGesture(layout: layout))
                    .modifier(GrabCursor())

                ForEach(Corner.allCases, id: \.self) { corner in
                    let p = point(of: corner, in: view)
                    Color.white.opacity(0.001)
                        .frame(width: 28, height: 28)
                        .offset(x: p.x - 14, y: p.y - 14)
                        .gesture(resizeGesture(corner, layout: layout))
                        .modifier(CornerCursor(corner: corner))
                }
            }
            .frame(width: geo.size.width, height: geo.size.height, alignment: .topLeading)
            .animation(dragStart == nil && !reduceMotion ? .smooth(duration: 0.25) : nil, value: box)
        }
        .accessibilityElement()
        .accessibilityLabel("裁切框")
        .accessibilityValue("\(Int(ws.effectiveCropBox(index).width)) × \(Int(ws.effectiveCropBox(index).height)) 像素")
        .accessibilityHint("回车确认并到下一张，左右方向键切换")
    }

    /// 框线、三分线、四角加粗的角标、左上角的像素尺寸。
    private func frame(_ view: CGRect, box: CGRect) -> some View {
        ZStack(alignment: .topLeading) {
            Path { p in
                for k in 1...2 {
                    let x = view.minX + view.width * CGFloat(k) / 3
                    let y = view.minY + view.height * CGFloat(k) / 3
                    p.move(to: CGPoint(x: x, y: view.minY)); p.addLine(to: CGPoint(x: x, y: view.maxY))
                    p.move(to: CGPoint(x: view.minX, y: y)); p.addLine(to: CGPoint(x: view.maxX, y: y))
                }
            }
            .stroke(Color.white.opacity(0.45), lineWidth: 1)

            Rectangle()
                .stroke(Color.white, lineWidth: 1)
                .frame(width: view.width, height: view.height)
                .offset(x: view.minX, y: view.minY)

            Path { p in
                // 角标长 18、粗 4，画在框内侧：框贴着图片边时也落在图上，不会白底上看不见。
                let l: CGFloat = 18, o: CGFloat = -2
                let (x0, y0, x1, y1) = (view.minX - o, view.minY - o, view.maxX + o, view.maxY + o)
                p.move(to: CGPoint(x: x0, y: y0 + l)); p.addLine(to: CGPoint(x: x0, y: y0)); p.addLine(to: CGPoint(x: x0 + l, y: y0))
                p.move(to: CGPoint(x: x1 - l, y: y0)); p.addLine(to: CGPoint(x: x1, y: y0)); p.addLine(to: CGPoint(x: x1, y: y0 + l))
                p.move(to: CGPoint(x: x0, y: y1 - l)); p.addLine(to: CGPoint(x: x0, y: y1)); p.addLine(to: CGPoint(x: x0 + l, y: y1))
                p.move(to: CGPoint(x: x1 - l, y: y1)); p.addLine(to: CGPoint(x: x1, y: y1)); p.addLine(to: CGPoint(x: x1, y: y1 - l))
            }
            .stroke(Color.white, style: StrokeStyle(lineWidth: 4, lineCap: .butt, lineJoin: .miter))
            .shadow(color: .black.opacity(0.25), radius: 1)

            Text(verbatim: "\(Int(box.width)) × \(Int(box.height))")
                .font(.system(size: 11).monospacedDigit())
                .foregroundStyle(.white)
                .padding(.horizontal, 6)
                .padding(.vertical, 2)
                .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 4, style: .continuous))
                .offset(x: view.minX + 8, y: view.minY + 8)
        }
        .allowsHitTesting(false)
    }

    private func point(of corner: Corner, in r: CGRect) -> CGPoint {
        switch corner {
        case .topLeft: CGPoint(x: r.minX, y: r.minY)
        case .topRight: CGPoint(x: r.maxX, y: r.minY)
        case .bottomLeft: CGPoint(x: r.minX, y: r.maxY)
        case .bottomRight: CGPoint(x: r.maxX, y: r.maxY)
        }
    }

    private func moveGesture(layout: Layout) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { v in
                let start = begin()
                let moved = start.offsetBy(dx: v.translation.width / layout.scale, dy: v.translation.height / layout.scale)
                ws.cropBoxes[item.url] = Workspace.clamp(moved, width: CGFloat(item.width), height: CGFloat(item.height), aspect: ws.cropAspect)
            }
            .onEnded { _ in end() }
    }

    /// 拖一个角：对角固定，按比例缩放（自由比例时宽高各自变），不超出画面。
    private func resizeGesture(_ corner: Corner, layout: Layout) -> some Gesture {
        DragGesture(minimumDistance: 1)
            .onChanged { v in
                let start = begin()
                let W = CGFloat(item.width), H = CGFloat(item.height)
                let anchor: CGPoint
                let dirX: CGFloat, dirY: CGFloat
                switch corner {
                case .topLeft: anchor = CGPoint(x: start.maxX, y: start.maxY); dirX = -1; dirY = -1
                case .topRight: anchor = CGPoint(x: start.minX, y: start.maxY); dirX = 1; dirY = -1
                case .bottomLeft: anchor = CGPoint(x: start.maxX, y: start.minY); dirX = -1; dirY = 1
                case .bottomRight: anchor = CGPoint(x: start.minX, y: start.minY); dirX = 1; dirY = 1
                }
                let dx = v.translation.width / layout.scale, dy = v.translation.height / layout.scale
                var w = start.width + dirX * dx
                var h = start.height + dirY * dy
                // 朝拖动方向还剩多少画面。
                let roomX = dirX > 0 ? W - anchor.x : anchor.x
                let roomY = dirY > 0 ? H - anchor.y : anchor.y
                let minSide = max(16, 24 / layout.scale)
                if let a = ws.cropAspect.map({ CGFloat($0) }) {
                    w = max(w, h * a)
                    w = min(max(w, minSide * max(1, a)), roomX, roomY * a)
                    h = w / a
                } else {
                    w = min(max(w, minSide), roomX)
                    h = min(max(h, minSide), roomY)
                }
                let x = dirX > 0 ? anchor.x : anchor.x - w
                let y = dirY > 0 ? anchor.y : anchor.y - h
                ws.cropBoxes[item.url] = CGRect(x: x.rounded(), y: y.rounded(), width: w.rounded(), height: h.rounded())
            }
            .onEnded { _ in end() }
    }

    /// 一次拖动开始：记下原来的框，供 ⌘Z 撤回。
    private func begin() -> CGRect {
        if let dragStart { return dragStart }
        let start = ws.effectiveCropBox(index)
        ws.cropHistory[item.url, default: []].append(ws.cropBoxes[item.url])
        dragStart = start
        return start
    }

    private func end() {
        dragStart = nil
        // 调过的这张不再算「已确认」，要再按一次确认。
        ws.cropConfirmed.remove(item.url)
    }

    /// 大图放进画布：四周留白，底部给按钮让位。
    struct Layout {
        let scale: CGFloat
        let display: CGSize
        let origin: CGPoint

        init(canvas: CGSize, imageSize: CGSize, bottomInset: CGFloat = 60) {
            let inset = EdgeInsets(top: 24, leading: 24, bottom: bottomInset, trailing: 24)
            let aw = max(1, canvas.width - inset.leading - inset.trailing)
            let ah = max(1, canvas.height - inset.top - inset.bottom)
            scale = min(aw / max(1, imageSize.width), ah / max(1, imageSize.height))
            display = CGSize(width: imageSize.width * scale, height: imageSize.height * scale)
            origin = CGPoint(x: ((canvas.width - display.width) / 2).rounded(), y: (inset.top + (ah - display.height) / 2).rounded())
        }

        func toView(_ r: CGRect) -> CGRect {
            CGRect(x: origin.x + r.minX * scale, y: origin.y + r.minY * scale, width: r.width * scale, height: r.height * scale)
        }
    }

    struct GrabCursor: ViewModifier {
        func body(content: Content) -> some View {
            if #available(macOS 15.0, *) { content.pointerStyle(.grabIdle) } else { content }
        }
    }

    struct CornerCursor: ViewModifier {
        let corner: Corner
        func body(content: Content) -> some View {
            if #available(macOS 15.0, *) {
                let position: FrameResizePosition = switch corner {
                case .topLeft: .topLeading
                case .topRight: .topTrailing
                case .bottomLeft: .bottomLeading
                case .bottomRight: .bottomTrailing
                }
                content.pointerStyle(.frameResize(position: position))
            } else {
                content
            }
        }
    }
}

/// 设计稿里浮在大图下方的两个按钮：「← 上一张」描边、「确认，下一张 ↩」实心深色（深色模式下反白）。
struct CropButtonStyle: ButtonStyle {
    let primary: Bool
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.colorScheme) private var scheme

    func makeBody(configuration: Configuration) -> some View {
        let shape = RoundedRectangle(cornerRadius: 6, style: .continuous)
        configuration.label
            .font(.system(size: 13, weight: primary ? .semibold : .regular))
            .foregroundStyle(primary ? Color(nsColor: .windowBackgroundColor) : Color.primary)
            .padding(.horizontal, primary ? 16 : 12)
            .frame(height: 28)
            .background {
                if primary {
                    shape.fill(Color.primary.opacity(configuration.isPressed ? 0.8 : 1))
                } else {
                    shape.fill(Color(nsColor: .controlBackgroundColor).opacity(configuration.isPressed ? 0.85 : 1))
                        .overlay(shape.strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                }
            }
            .shadow(color: .black.opacity(0.12), radius: 2, y: 1)
            .opacity(isEnabled ? 1 : 0.45)
            .contentShape(shape)
    }
}

/// 底部胶片条：当前这张描红圈，已确认的右下角打勾，还没看过的淡一些。
struct FilmStrip: View {
    @Bindable var ws: Workspace
    let items: [FileItem]
    let index: Int

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { i, item in
                        cell(item, current: i == index, done: ws.cropConfirmed.contains(item.url))
                            .id(i)
                            .onTapGesture { withAnimation(.smooth(duration: 0.25)) { ws.cropIndex = i } }
                            .accessibilityElement()
                            .accessibilityLabel(item.name + (ws.cropConfirmed.contains(item.url) ? "，已裁" : "，未裁"))
                            .accessibilityAddTraits(i == index ? [.isButton, .isSelected] : .isButton)
                            .accessibilityAction { ws.cropIndex = i }
                    }
                }
                .padding(.horizontal, 16)
            }
            .frame(height: 76)
            .onChange(of: index) { _, i in withAnimation(.smooth(duration: 0.3)) { proxy.scrollTo(i, anchor: .center) } }
            .onAppear { proxy.scrollTo(index, anchor: .center) }
        }
        .background(.bar)
    }

    private func cell(_ item: FileItem, current: Bool, done: Bool) -> some View {
        Thumb(image: item.thumbnail)
            .frame(width: 72, height: 48)
            .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
            .opacity(current || done ? 1 : 0.55)
            .overlay(alignment: .bottomTrailing) {
                if done {
                    Image(systemName: "checkmark")
                        .font(.system(size: 7, weight: .heavy))
                        .foregroundStyle(.white)
                        .frame(width: 14, height: 14)
                        .background(Circle().fill(Color.saving))
                        .padding(3)
                        .transition(.scale.combined(with: .opacity))
                }
            }
            // 当前这张：内圈白、外圈品牌红（设计稿 03）。
            .padding(4)
            .overlay {
                if current {
                    RoundedRectangle(cornerRadius: 7, style: .continuous).strokeBorder(Color.brand, lineWidth: 2)
                    RoundedRectangle(cornerRadius: 6, style: .continuous).strokeBorder(Color.white, lineWidth: 2).padding(2)
                }
            }
            .contentShape(Rectangle())
    }
}
