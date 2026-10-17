import QuickLook
import SwiftUI

/// 前后对比：拖中间的线，左边原图、右边输出。
struct CompareView: View {
    let item: FileItem
    @State private var split: CGFloat = 0.5
    @State private var before: NSImage?
    @State private var after: NSImage?

    var body: some View {
        GeometryReader { geo in
            let size = fitted(in: geo.size)
            ZStack {
                if let before { Image(nsImage: before).resizable().scaledToFit().frame(width: size.width, height: size.height) }
                if let after {
                    Image(nsImage: after).resizable().scaledToFit().frame(width: size.width, height: size.height)
                        .mask(alignment: .trailing) { Rectangle().frame(width: size.width * (1 - split)) }
                }
                Rectangle().fill(.white).frame(width: 2, height: size.height)
                    .offset(x: size.width * (split - 0.5))
                Circle().fill(.white).frame(width: 26, height: 26).shadow(radius: 3)
                    .overlay { Image(systemName: "arrow.left.and.right").font(.system(size: 11, weight: .bold)).foregroundStyle(.black) }
                    .offset(x: size.width * (split - 0.5))
                HStack {
                    Tag(text: "原图 \(Format.size(item.bytes))")
                    Spacer()
                    Tag(text: "输出 \(Format.size(item.outputs.first?.bytes ?? 0))")
                }
                .frame(width: size.width)
                .frame(maxHeight: .infinity, alignment: .bottom)
                .padding(.bottom, 8)
            }
            .frame(width: geo.size.width, height: geo.size.height)
            .contentShape(Rectangle())
            .gesture(DragGesture(minimumDistance: 0).onChanged { v in
                let x = v.location.x - (geo.size.width - size.width) / 2
                split = min(1, max(0, x / size.width))
            })
        }
        .padding(12)
        .background(.background.secondary)
        .task(id: item.id) {
            before = NSImage(contentsOf: item.url)
            after = item.outputs.first.flatMap { NSImage(contentsOf: URL(fileURLWithPath: $0.path)) }
        }
        .accessibilityLabel("前后对比")
    }

    private func fitted(in box: CGSize) -> CGSize {
        let w = CGFloat(max(1, item.width)), h = CGFloat(max(1, item.height))
        let s = min(box.width / w, box.height / h)
        return CGSize(width: w * s, height: h * s)
    }
}
