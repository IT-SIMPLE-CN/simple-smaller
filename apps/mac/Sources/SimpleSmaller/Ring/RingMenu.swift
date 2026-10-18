import Foundation

/// 圆环的内容（设计稿 06）：内圈选做什么，外圈选做到什么程度；带 ● 的是点内圈时直接用的默认项。
struct RingSlot: Identifiable {
    let id = UUID()
    let label: String
    var choices: [RingChoice]
    /// 点内圈直接执行的那一项。
    var defaultIndex: Int
}

struct RingChoice: Identifiable {
    enum Action { case run(Recipe), openMain }
    let id = UUID()
    let label: String
    /// 外圈小字：配方摘要（不做试压，不写预估体积）。
    let sub: String
    let action: Action
}

@MainActor
enum RingMenu {
    /// 第一格「压缩」的外圈：这一类在设置 › 常用 里排在前 5 个的常用（顺序照设置）。
    static func favorites(_ store: RecipeStore, _ type: MediaType) -> [RingChoice] {
        store.ordered(type).prefix(5).map { RingChoice(label: $0.name, sub: $0.summary, action: .run($0)) }
    }

    static func slots(for type: MediaType, store: RecipeStore) -> [RingSlot] {
        switch type {
        case .image: image(store)
        case .video: video(store)
        case .pdf: pdf(store)
        }
    }

    // MARK: 图片：压缩 · WebP · AVIF · JPG · PNG · 尺寸 · 用途 · 自定义

    private static func image(_ store: RecipeStore) -> [RingSlot] {
        let base = store.recipe(id: "img.web") ?? Recipe(id: "img.web", name: "网站用", type: .image)
        func tweak(_ name: String, _ edit: (inout ImageBody) -> Void) -> Recipe {
            var r = base
            r.id = "ring.\(name)"
            r.name = name
            r.builtIn = true
            edit(&r.image)
            return r
        }
        func run(_ r: Recipe, _ label: String? = nil, _ sub: String? = nil) -> RingChoice {
            RingChoice(label: label ?? r.name, sub: sub ?? r.summary, action: .run(r))
        }
        let presets = Dictionary(uniqueKeysWithValues: CropPreset.imagePresets.map { ($0.id, $0) })
        func crop(_ id: String, _ label: String) -> RingChoice {
            let p = presets[id]!
            let r = tweak("\(p.name) \(p.ratioLabel)") { $0.format = .jpeg; $0.quality = 90; $0.crop = p; $0.size = nil }
            return RingChoice(label: label, sub: p.ratioLabel, action: .run(r))
        }
        let compress = favorites(store, .image)
        let quality: [(String, Int)] = [("高", 90), ("均衡", 78), ("小", 60)]
        func format(_ f: ImageFormatChoice) -> [RingChoice] {
            quality.map { q in run(tweak("\(f.title) \(q.0)") { $0.format = f; $0.quality = q.1; $0.lossless = false }, q.0, "质量 \(q.1)") }
        }
        let sizes = [2560, 1920, 1280, 640].map { w in run(tweak("长边 \(w)") { $0.size = w; $0.sizeAxis = .longEdge; $0.extraSizes = [] }, "\(w)", "长边") }
            + [run(tweak("三档") { $0.size = 2560; $0.sizeAxis = .longEdge; $0.extraSizes = [1280, 640] }, "三档", "2560/1280/640")]
        return [
            RingSlot(label: "压缩", choices: compress, defaultIndex: 0),
            RingSlot(label: "WebP", choices: format(.webp) + [run(tweak("WebP 无损") { $0.format = .webp; $0.lossless = true }, "无损", "WebP")], defaultIndex: 1),
            RingSlot(label: "AVIF", choices: format(.avif), defaultIndex: 1),
            RingSlot(label: "JPG", choices: format(.jpeg), defaultIndex: 1),
            RingSlot(label: "PNG", choices: [run(tweak("PNG 无损") { $0.format = .png; $0.lossless = true }, "无损", "原色"),
                                             run(tweak("PNG 256 色") { $0.format = .png; $0.lossless = false; $0.quality = 80 }, "256 色", "小很多")], defaultIndex: 0),
            RingSlot(label: "尺寸", choices: sizes, defaultIndex: 0),
            RingSlot(label: "用途", choices: [crop("xhs", "小红书"), crop("gzh", "公众号"), crop("ig", "IG"), crop("dy", "抖音"), crop("shop", "商品方图")], defaultIndex: 0),
            custom(store, .image),
        ]
    }

    // MARK: 视频：压缩 · 比例 · GIF · 序列帧 · 自定义

    private static func video(_ store: RecipeStore) -> [RingSlot] {
        let base = store.recipe(id: "vid.chat") ?? Recipe(id: "vid.chat", name: "发群 25 MB", type: .video)
        func tweak(_ name: String, _ edit: (inout VideoBody) -> Void) -> Recipe {
            var r = base
            r.id = "ring.\(name)"
            r.name = name
            edit(&r.video)
            return r
        }
        func run(_ r: Recipe, _ label: String? = nil, _ sub: String? = nil) -> RingChoice {
            RingChoice(label: label ?? r.name, sub: sub ?? r.summary, action: .run(r))
        }
        let compress = favorites(store, .video)
        let ratios = CropPreset.videoPresets.filter { ["v916", "v34", "v11", "v169"].contains($0.id) }.map { p in
            run(tweak("比例 \(p.name)") { $0.crop = p; $0.rate = .high; $0.shortEdge = 1080; $0.codec = .h264 }, p.name, "1080p")
        }
        let gifs = [480, 320, 640].map { w in run(tweak("GIF \(w)") { $0.codec = .gif; $0.gifWidth = w; $0.gifFps = 15; $0.keepAudio = false }, "\(w)", "15 fps") }
        let frames = ["vid.frames.web", "vid.frames.1s"].compactMap { store.recipe(id: $0) }.map { r in
            run(r, r.video.frameValue >= 2 ? "每秒 \(Int(r.video.frameValue)) 帧" : "每秒 1 张", r.video.frameFormat.title)
        }
        return [
            RingSlot(label: "压缩", choices: compress, defaultIndex: 0),
            RingSlot(label: "比例", choices: ratios, defaultIndex: 0),
            RingSlot(label: "GIF", choices: gifs, defaultIndex: 0),
            RingSlot(label: "序列帧", choices: frames, defaultIndex: 0),
            custom(store, .video),
        ]
    }

    // MARK: PDF：压缩 · 转长图 · 自定义

    private static func pdf(_ store: RecipeStore) -> [RingSlot] {
        let compress = favorites(store, .pdf)
        let long = ["pdf.long"].compactMap { store.recipe(id: $0) }.map { r in
            [1080, 750].map { w -> RingChoice in
                var x = r
                x.id = "ring.long.\(w)"
                x.pdf.imageWidth = w
                return RingChoice(label: "宽 \(w)", sub: "JPG 长图", action: .run(x))
            }
        }.flatMap { $0 }
        return [
            RingSlot(label: "压缩", choices: compress, defaultIndex: 0),
            RingSlot(label: "转长图", choices: long, defaultIndex: 0),
            custom(store, .pdf),
        ]
    }

    /// 自定义：我的配方 + 打开主窗口（带上这批文件，细调后再开始）。
    private static func custom(_ store: RecipeStore, _ type: MediaType) -> RingSlot {
        let mine = store.mine(for: type).prefix(4).map { RingChoice(label: $0.name, sub: "我的常用", action: .run($0)) }
        return RingSlot(label: "自定义", choices: Array(mine) + [RingChoice(label: "打开窗口…", sub: "细调后开始", action: .openMain)],
                        defaultIndex: 0)
    }
}
