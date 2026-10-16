import SwiftUI
import UniformTypeIdentifiers
import SmallerCore

// M1 链路验证用的最小窗口：拖一张图进来，由 Rust 引擎读出宽高与格式。
// 正式界面在 M2 按设计稿移植。

@main
struct SimpleSmallerApp: App {
    init() {
        // SwiftPM 可执行文件直接运行时不会自动成为前台应用。
        NSApplication.shared.setActivationPolicy(.regular)
    }

    var body: some Scene {
        WindowGroup("简单小一点") {
            LinkCheckView()
                .frame(minWidth: 720, minHeight: 460)
        }
    }
}

enum MediaKind: String, CaseIterable, Identifiable {
    case image = "图片"
    case video = "视频"
    case pdf = "PDF"

    var id: String { rawValue }

    var symbol: String {
        switch self {
        case .image: "photo"
        case .video: "film"
        case .pdf: "doc.richtext"
        }
    }
}

/// 全局一个引擎；HEIC 等交给 ImageIO。
let engine = Engine(decoder: ImageIODecoder())

struct LinkCheckView: View {
    @State private var kind: MediaKind? = .image
    @State private var result: String = "将图片拖到这里"
    @State private var targeted = false

    var body: some View {
        NavigationSplitView {
            List(MediaKind.allCases, selection: $kind) { item in
                Label(item.rawValue, systemImage: item.symbol).tag(item)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200)
        } detail: {
            VStack(spacing: 12) {
                Image(systemName: "arrow.down.doc")
                    .font(.system(size: 40, weight: .light))
                    .foregroundStyle(.secondary)
                Text(result)
                    .font(.headline)
                Text("引擎 \(engine.version())")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .background {
                RoundedRectangle(cornerRadius: 10)
                    .strokeBorder(style: StrokeStyle(lineWidth: 1.5, dash: [6]))
                    .foregroundStyle(targeted ? Color.accentColor : Color.secondary.opacity(0.4))
                    .padding(20)
            }
            .dropDestination(for: URL.self) { urls, _ in
                guard let url = urls.first else { return false }
                probe(url)
                return true
            } isTargeted: { targeted = $0 }
        }
        .onAppear {
            // 命令行 `--open <路径>` 等同拖入，供脚本化验证与之后的「用简单小一点打开」。
            let args = CommandLine.arguments
            if let i = args.firstIndex(of: "--open"), i + 1 < args.count {
                probe(URL(fileURLWithPath: args[i + 1]))
            }
        }
        .tint(Color(red: 0xEF / 255, green: 0x39 / 255, blue: 0x22 / 255))
    }

    private func probe(_ url: URL) {
        result = "试压中…"
        let path = url.path
        Task.detached {
            let settings = ImageSettings(format: .webp, quality: 78, lossless: false, targetBytes: nil,
                                         sizes: [2560], sizeBy: .width, crop: nil, cropBoxes: [])
            let text: String
            do {
                let meta = try engine.imageMeta(path: path)
                let p = try engine.previewImage(path: path, settings: settings)
                let fmt = ByteCountFormatter()
                text = "\(url.lastPathComponent) · \(meta.width) × \(meta.height) · \(fmt.string(fromByteCount: Int64(p.beforeBytes))) → WebP \(p.width) × \(p.height) \(fmt.string(fromByteCount: Int64(p.bytes)))"
            } catch {
                text = "读不出这个文件：\(error)"
            }
            await MainActor.run { result = text }
        }
    }
}
