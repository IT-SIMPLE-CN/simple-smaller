import SwiftUI

/// 没有文件时的拖放区（设计稿 01）。
struct DropZone: View {
    @Environment(AppModel.self) private var model
    let type: MediaType
    let segment: Workspace.PdfSegment
    @State private var targeted = false

    var body: some View {
        VStack(spacing: 14) {
            Image(systemName: "arrow.down.doc")
                .font(.system(size: 40, weight: .ultraLight))
                .foregroundStyle(targeted ? Color.brand : Color.secondary)
                .symbolEffect(.bounce, value: targeted)
            Text(title)
                .font(.system(size: 15, weight: .semibold))
            Text(formats)
                .font(.caption)
                .foregroundStyle(.secondary)
            HStack(spacing: 8) {
                Button("选取文件…") { FilePicker.pick(into: model) }
                if type == .image { Button("粘贴剪贴板") { model.pasteFromClipboard() } }
            }
            .controlSize(.regular)
            .padding(.top, 4)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            RoundedRectangle(cornerRadius: 12, style: .continuous)
                .strokeBorder(style: StrokeStyle(lineWidth: 1.5, dash: [6, 5]))
                .foregroundStyle(targeted ? Color.brand : Color.secondary.opacity(0.35))
                .background(targeted ? Color.brand.opacity(0.06) : .clear, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                .padding(20)
        }
        .animation(.smooth(duration: 0.2), value: targeted)
        .dropDestination(for: URL.self) { urls, _ in
            model.add(urls, preferred: type)
            return true
        } isTargeted: { targeted = $0 }
    }

    private var title: String {
        switch type {
        case .image: "将图片或文件夹拖到这里"
        case .video: "将视频或文件夹拖到这里"
        case .pdf: segment == .fromImages ? "将图片拖到这里" : "将 PDF 或文件夹拖到这里"
        }
    }

    private var formats: String {
        switch type {
        case .image: "JPG · PNG · HEIC · WebP · AVIF · TIFF · GIF"
        case .video: "MP4 · MOV · M4V · MKV · WebM · AVI · GIF 动图"
        case .pdf: segment == .fromImages ? "JPG · PNG · HEIC · WebP · AVIF" : "PDF"
        }
    }
}
