import AppKit
import PDFKit

/// PDF 页面缩略图：用系统 PDFKit 在后台一次渲染一个文件的全部页，按文件缓存。
@Observable
@MainActor
final class PdfThumbnails {
    static let shared = PdfThumbnails()
    private(set) var pages: [URL: [CGImage]] = [:]
    private var loading: Set<URL> = []

    func thumbnails(for url: URL) -> [CGImage]? { pages[url] }

    func load(_ url: URL, width: CGFloat = 160) {
        guard pages[url] == nil, !loading.contains(url) else { return }
        loading.insert(url)
        Task.detached(priority: .userInitiated) {
            var out: [CGImage] = []
            if let doc = PDFDocument(url: url) {
                for i in 0..<doc.pageCount {
                    guard let page = doc.page(at: i) else { continue }
                    let bounds = page.bounds(for: .cropBox)
                    let size = CGSize(width: width * 2, height: width * 2 * bounds.height / max(1, bounds.width))
                    if let cg = page.thumbnail(of: size, for: .cropBox).cgImage(forProposedRect: nil, context: nil, hints: nil) {
                        out.append(cg)
                    }
                }
            }
            let result = out
            await MainActor.run {
                self.pages[url] = result
                self.loading.remove(url)
            }
        }
    }
}
