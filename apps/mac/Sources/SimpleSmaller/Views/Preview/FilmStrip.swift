import SwiftUI

/// 底部胶片条（裁切的图、剪辑与序列帧的视频共用）：当前这张外圈品牌红、内圈卡片色，
/// 右下角放一个小标（调过的裁切、截取后的时长）；切换时自动滚到当前这张。
struct FilmStrip<Badge: View>: View {
    let items: [FileItem]
    let index: Int
    let select: (Int) -> Void
    var label: (FileItem) -> String = { $0.name }
    @ViewBuilder var badge: (FileItem) -> Badge

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { i, item in
                        cell(item, current: i == index)
                            .id(i)
                            .onTapGesture { withAnimation(.smooth(duration: 0.25)) { select(i) } }
                            .accessibilityElement()
                            .accessibilityLabel(label(item))
                            .accessibilityAddTraits(i == index ? [.isButton, .isSelected] : .isButton)
                            .accessibilityAction { select(i) }
                    }
                }
                .padding(.horizontal, 2)
            }
            .frame(height: 64)
            .onChange(of: index) { _, i in withAnimation(.smooth(duration: 0.3)) { proxy.scrollTo(i, anchor: .center) } }
            .onAppear { proxy.scrollTo(index, anchor: .center) }
        }
    }

    private func cell(_ item: FileItem, current: Bool) -> some View {
        Thumb(image: item.thumbnail)
            .frame(width: 72, height: 48)
            .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
            .overlay(alignment: .bottomTrailing) { badge(item).padding(3) }
            .padding(4)
            .overlay {
                if current {
                    RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.brand, lineWidth: 2)
                    RoundedRectangle(cornerRadius: 11, style: .continuous).strokeBorder(Bento.card, lineWidth: 2).padding(2)
                }
            }
            .contentShape(Rectangle())
    }
}
