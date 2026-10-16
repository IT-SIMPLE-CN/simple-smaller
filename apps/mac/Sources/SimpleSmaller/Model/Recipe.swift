import Foundation
import SmallerCore

// MARK: - 文件类型

enum MediaType: String, Codable, CaseIterable, Identifiable, Sendable {
    case image, video, pdf

    var id: String { rawValue }

    var title: String {
        switch self {
        case .image: "图片"
        case .video: "视频"
        case .pdf: "PDF"
        }
    }

    var symbol: String {
        switch self {
        case .image: "photo"
        case .video: "film"
        case .pdf: "doc.richtext"
        }
    }

    var kind: Kind {
        switch self {
        case .image: .image
        case .video: .video
        case .pdf: .pdf
        }
    }

    static func of(_ url: URL) -> MediaType? {
        switch url.pathExtension.lowercased() {
        case "jpg", "jpeg", "png", "webp", "avif", "heic", "heif", "gif", "tif", "tiff", "bmp": .image
        case "mp4", "mov", "m4v", "mkv", "webm", "avi", "mts", "m2ts": .video
        case "pdf": .pdf
        default: nil
        }
    }
}

// MARK: - 命名

struct NamingSettings: Codable, Equatable, Hashable, Sendable {
    var template = "{name}-small"
    var lowercase = false
    var pinyin = false
    var hyphenate = false

    func ffi(date: String) -> Naming {
        Naming(template: template, lowercase: lowercase, pinyin: pinyin, hyphenate: hyphenate, date: date)
    }
}

// MARK: - 比例

struct CropPreset: Codable, Equatable, Hashable, Identifiable, Sendable {
    var id: String
    var name: String
    /// 比例份数；0 表示自由比例。
    var ratioW: Int
    var ratioH: Int
    var ratioLabel: String
    /// 推荐输出宽度。
    var width: Int

    var aspect: Double? { ratioW > 0 && ratioH > 0 ? Double(ratioW) / Double(ratioH) : nil }

    /// 图片裁切的平台比例（设计稿 03）。
    static let imagePresets: [CropPreset] = [
        .init(id: "xhs", name: "小红书", ratioW: 3, ratioH: 4, ratioLabel: "3:4", width: 1242),
        .init(id: "dy", name: "抖音 · 视频号", ratioW: 9, ratioH: 16, ratioLabel: "9:16", width: 1080),
        .init(id: "gzh", name: "公众号封面", ratioW: 47, ratioH: 20, ratioLabel: "2.35:1", width: 900),
        .init(id: "gzh2", name: "公众号次图", ratioW: 1, ratioH: 1, ratioLabel: "1:1", width: 500),
        .init(id: "shop", name: "电商主图", ratioW: 1, ratioH: 1, ratioLabel: "1:1", width: 800),
        .init(id: "ig", name: "Instagram", ratioW: 4, ratioH: 5, ratioLabel: "4:5", width: 1080),
        .init(id: "banner", name: "网站横幅", ratioW: 16, ratioH: 9, ratioLabel: "16:9", width: 1920),
        .init(id: "free", name: "自由", ratioW: 0, ratioH: 0, ratioLabel: "", width: 1920),
    ]

    /// 视频画面比例（设计稿 04）。
    static let videoPresets: [CropPreset] = [
        .init(id: "v916", name: "9:16", ratioW: 9, ratioH: 16, ratioLabel: "9:16", width: 1080),
        .init(id: "v34", name: "3:4", ratioW: 3, ratioH: 4, ratioLabel: "3:4", width: 1080),
        .init(id: "v11", name: "1:1", ratioW: 1, ratioH: 1, ratioLabel: "1:1", width: 1080),
        .init(id: "v45", name: "4:5", ratioW: 4, ratioH: 5, ratioLabel: "4:5", width: 1080),
        .init(id: "v169", name: "16:9", ratioW: 16, ratioH: 9, ratioLabel: "16:9", width: 1920),
    ]
}

// MARK: - 图片参数

enum ImageFormatChoice: String, Codable, CaseIterable, Identifiable, Sendable {
    case webp, avif, jpeg, png, original

    var id: String { rawValue }

    var title: String {
        switch self {
        case .webp: "WebP"
        case .avif: "AVIF"
        case .jpeg: "JPG"
        case .png: "PNG"
        case .original: "原格式"
        }
    }

    var ffi: ImageFormat {
        switch self {
        case .webp: .webp
        case .avif: .avif
        case .jpeg: .jpeg
        case .png: .png
        case .original: .original
        }
    }
}

enum SizeAxis: String, Codable, CaseIterable, Identifiable, Sendable {
    case width, height, longEdge

    var id: String { rawValue }

    var title: String {
        switch self {
        case .width: "宽"
        case .height: "高"
        case .longEdge: "长边"
        }
    }

    var ffi: SizeBy {
        switch self {
        case .width: .width
        case .height: .height
        case .longEdge: .longEdge
        }
    }
}

struct ImageBody: Codable, Equatable, Hashable, Sendable {
    var format: ImageFormatChoice = .webp
    var quality = 78
    var lossless = false
    /// 限体积（KB）。
    var targetKB: Int?
    var sizeAxis: SizeAxis = .width
    /// nil 表示原尺寸。
    var size: Int? = 2560
    /// 「同时出 1280 / 640」这类附加档位。
    var extraSizes: [Int] = []
    var crop: CropPreset?
    /// 裁切时的输出宽度（可改，默认取预设的推荐宽度）。
    var cropWidth: Int?
}

// MARK: - 视频参数

struct VideoBody: Codable, Equatable, Hashable, Sendable {
    enum Codec: String, Codable, CaseIterable, Sendable { case h264, hevc, gif }
    enum Rate: String, Codable, CaseIterable, Sendable { case high, medium, low, size }
    enum FrameMode: String, Codable, CaseIterable, Sendable { case perSecond, every, total, all }

    var codec: Codec = .h264
    var rate: Rate = .medium
    var targetMB: Double = 25
    /// 短边上限：1080 / 720 / 540；nil 为原分辨率。
    var shortEdge: Int? = 1080
    var keepAudio = true
    var hdrToSdr = true
    /// 速度优先（系统硬件编码）。
    var fast = false
    var exportCover = false
    var crop: CropPreset?
    var gifFps: Double = 15
    var gifWidth = 480
    /// 只去声音，不重新编码。
    var muteOnly = false
    /// 输出序列帧（选中这个配方时切到「序列帧」）。
    var outputFrames = false
    var frameMode: FrameMode = .perSecond
    var frameValue: Double = 15
    var frameFormat: ImageFormatChoice = .webp
    var frameQuality = 80
    var frameWidth: Int? = 1920
    var frameZip = false
    var frameContactSheet = false
}

// MARK: - PDF 参数

struct PdfBody: Codable, Equatable, Hashable, Sendable {
    enum Level: String, Codable, CaseIterable, Sendable { case screen, email, print, size }
    enum PageMode: String, Codable, CaseIterable, Sendable { case fit, a4 }

    var level: Level = .email
    var targetMB: Double = 5
    var subsetFonts = true
    var grayscale = false
    var splitEvery = 1
    var imageWidth = 1600
    var imageFormat: ImageFormatChoice = .jpeg
    var imageQuality = 85
    var longImage = false
    var pageMode: PageMode = .a4
}

// MARK: - 配方

struct Recipe: Codable, Equatable, Hashable, Identifiable, Sendable {
    var id: String
    var name: String
    var type: MediaType
    var builtIn = false
    var image = ImageBody()
    var video = VideoBody()
    var pdf = PdfBody()
    var naming = NamingSettings()
    var stripMetadata = true
    /// 边栏小字按设计稿写死的，优先于自动生成。
    var summaryOverride: String?

    /// 边栏右侧的小字。
    var summary: String {
        if let summaryOverride { return summaryOverride }
        switch type {
        case .image:
            if let c = image.crop, c.aspect != nil {
                let w = image.cropWidth ?? c.width
                return "\(w)×\(Int((Double(w) / c.aspect!).rounded()))"
            }
            if image.targetKB != nil { return "按体积" }
            if image.format == .original { return "原格式" }
            return image.format.title
        case .video:
            if video.muteOnly { return "不重编码" }
            if video.outputFrames { return video.frameFormat.title }
            if video.codec == .gif { return "\(video.gifWidth) · \(Int(video.gifFps))fps" }
            if let c = video.crop { return "\(c.width)×\(Int((Double(c.width) / (c.aspect ?? 1)).rounded()))" }
            if !video.keepAudio { return "静音 \(video.shortEdge.map { "\($0)p" } ?? "")" }
            return video.codec == .hevc ? "HEVC" : "H.264"
        case .pdf:
            if pdf.longImage { return pdf.imageFormat.title }
            switch pdf.level {
            case .screen: return "110 dpi"
            case .email: return "≤ 10 MB"
            case .print: return "300 dpi"
            case .size: return "≤ \(Int(pdf.targetMB)) MB"
            }
        }
    }
}

extension Recipe {
    /// 内置配方（设计稿边栏「常用配方」）。
    static let builtIns: [Recipe] = {
        var list: [Recipe] = []
        func image(_ id: String, _ name: String, _ edit: (inout ImageBody) -> Void) {
            var r = Recipe(id: id, name: name, type: .image, builtIn: true)
            edit(&r.image)
            list.append(r)
        }
        image("img.web", "网站用") { $0.format = .webp; $0.quality = 78; $0.size = 2560 }
        image("img.chat", "发群预览") { $0.format = .jpeg; $0.quality = 82; $0.size = 2048 }
        image("img.xhs", "小红书 3:4") { $0.format = .jpeg; $0.quality = 90; $0.crop = CropPreset.imagePresets[0] }
        image("img.gzh", "公众号封面") { $0.format = .jpeg; $0.quality = 90; $0.crop = CropPreset.imagePresets[2] }
        list[list.count - 1].summaryOverride = "2.35:1"
        image("img.500k", "限 500 KB") { $0.format = .original; $0.quality = 90; $0.targetKB = 500; $0.size = nil }
        image("img.lossless", "无损交付") { $0.format = .original; $0.lossless = true; $0.quality = 100; $0.size = nil }

        func video(_ id: String, _ name: String, _ edit: (inout VideoBody) -> Void) {
            var r = Recipe(id: id, name: name, type: .video, builtIn: true)
            edit(&r.video)
            list.append(r)
        }
        video("vid.chat", "发群 25 MB") { $0.rate = .size; $0.targetMB = 25; $0.shortEdge = 1080 }
        video("vid.web", "网站背景") { $0.keepAudio = false; $0.shortEdge = 720; $0.rate = .medium }
        video("vid.dy", "抖音 9:16") { $0.crop = CropPreset.videoPresets[0]; $0.rate = .high; $0.shortEdge = 1080 }
        video("vid.xhs", "小红书 3:4") { $0.crop = CropPreset.videoPresets[1]; $0.rate = .high; $0.shortEdge = 1080 }
        video("vid.gif", "转 GIF") { $0.codec = .gif; $0.gifWidth = 480; $0.gifFps = 15 }
        video("vid.mute", "只去声音") { $0.muteOnly = true; $0.keepAudio = false }
        video("vid.frames.web", "序列帧 · 网页滚动动画") { $0.outputFrames = true; $0.frameMode = .perSecond; $0.frameValue = 15; $0.frameFormat = .webp; $0.frameWidth = 1920 }
        video("vid.frames.1s", "每秒一张截图") { $0.outputFrames = true; $0.frameMode = .perSecond; $0.frameValue = 1; $0.frameFormat = .jpeg; $0.frameWidth = nil }

        func pdf(_ id: String, _ name: String, _ edit: (inout PdfBody) -> Void) {
            var r = Recipe(id: id, name: name, type: .pdf, builtIn: true)
            edit(&r.pdf)
            list.append(r)
        }
        pdf("pdf.email", "邮件附件") { $0.level = .email }
        pdf("pdf.screen", "屏幕阅读") { $0.level = .screen }
        pdf("pdf.print", "打印保真") { $0.level = .print }
        pdf("pdf.long", "转长图") { $0.longImage = true; $0.imageFormat = .jpeg; $0.imageWidth = 1080 }

        // 新装的「我的配方」示例：官网三档 srcset。
        return list
    }()

    static let starterMine: [Recipe] = {
        var r = Recipe(id: "mine.srcset", name: "官网三档 srcset", type: .image)
        r.image.format = .webp
        r.image.size = 2560
        r.image.extraSizes = [1280, 640]
        r.naming = NamingSettings(template: "{name}-{w}", lowercase: true, pinyin: true, hyphenate: true)
        return [r]
    }()
}
