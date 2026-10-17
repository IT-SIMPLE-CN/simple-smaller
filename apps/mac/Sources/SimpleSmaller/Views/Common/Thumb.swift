import QuickLook
import SwiftUI

struct Thumb: View {
    let image: NSImage?

    var body: some View {
        // 先撑满外部给的方框再裁：横图、竖图都裁成方块，不会溢出。
        Color.clear
            .overlay {
                if let image {
                    Image(nsImage: image).resizable().scaledToFill()
                } else {
                    Rectangle().fill(.quaternary)
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
    }
}

struct Tag: View {
    let text: String
    var body: some View {
        Text(text).font(.caption2).foregroundStyle(.white)
            .padding(.horizontal, 6).padding(.vertical, 2)
            .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 4))
            .padding(.horizontal, 8)
    }
}
