namespace SimpleSmaller.Ring;

/// 圆环的内容（设计稿 06）：内圈选做什么，外圈选做到什么程度；带 ● 的是点内圈时直接用的默认项。
internal sealed record RingSlot(string Label, IReadOnlyList<RingChoice> Choices, int DefaultIndex);

/// 外圈一项：按配方直接处理，或打开主窗口（Recipe 为 null）。Sub 是配方摘要（不做试压，不写预估体积）。
internal sealed record RingChoice(string Label, string Sub, Recipe? Recipe);

internal static class RingMenu
{
    public static IReadOnlyList<RingSlot> Slots(MediaType type, RecipeStore store) => type switch
    {
        MediaType.Image => Image(store),
        MediaType.Video => Video(store),
        _ => Pdf(store),
    };

    private static RingChoice Run(Recipe r, string? label = null, string? sub = null) => new(label ?? r.Name, sub ?? r.Summary, r);

    // MARK: 图片：压缩 · WebP · AVIF · JPG · PNG · 尺寸 · 用途 · 自定义

    private static IReadOnlyList<RingSlot> Image(RecipeStore store)
    {
        var @base = store.RecipeById("img.web") ?? new Recipe { Id = "img.web", Name = "网站用", Type = MediaType.Image };
        Recipe tweak(string name, Func<ImageBody, ImageBody> edit) =>
            @base with { Id = $"ring.{name}", Name = name, BuiltIn = true, SummaryOverride = null, Image = edit(@base.Image) };
        var presets = CropPreset.ImagePresets.ToDictionary(p => p.Id);
        RingChoice crop(string id, string label)
        {
            var p = presets[id];
            var r = tweak($"{p.Name} {p.RatioLabel}", b => b with { Format = ImageFormatChoice.Jpeg, Quality = 90, Crop = p, Size = null });
            return new RingChoice(label, p.RatioLabel, r);
        }
        var compress = new[] { "img.web", "img.chat", "img.lossless", "img.500k" }.Select(store.RecipeById).OfType<Recipe>().Select(r => Run(r)).ToList();
        (string, int)[] quality = [("高", 90), ("均衡", 78), ("小", 60)];
        List<RingChoice> format(ImageFormatChoice f) =>
            quality.Select(q => Run(tweak($"{f.Title()} {q.Item1}", b => b with { Format = f, Quality = q.Item2, Lossless = false }), q.Item1, $"质量 {q.Item2}")).ToList();
        var sizes = new[] { 2560, 1920, 1280, 640 }
            .Select(w => Run(tweak($"长边 {w}", b => b with { Size = w, SizeAxis = SizeAxis.LongEdge, ExtraSizes = [] }), $"{w}", "长边"))
            .Append(Run(tweak("三档", b => b with { Size = 2560, SizeAxis = SizeAxis.LongEdge, ExtraSizes = [1280, 640] }), "三档", "2560/1280/640"))
            .ToList();
        return
        [
            new("压缩", compress, 0),
            new("WebP", [.. format(ImageFormatChoice.Webp), Run(tweak("WebP 无损", b => b with { Format = ImageFormatChoice.Webp, Lossless = true }), "无损", "WebP")], 1),
            new("AVIF", format(ImageFormatChoice.Avif), 1),
            new("JPG", format(ImageFormatChoice.Jpeg), 1),
            new("PNG",
            [
                Run(tweak("PNG 无损", b => b with { Format = ImageFormatChoice.Png, Lossless = true }), "无损", "原色"),
                Run(tweak("PNG 256 色", b => b with { Format = ImageFormatChoice.Png, Lossless = false, Quality = 80 }), "256 色", "小很多"),
            ], 0),
            new("尺寸", sizes, 0),
            new("用途", [crop("xhs", "小红书"), crop("gzh", "公众号"), crop("ig", "IG"), crop("dy", "抖音"), crop("shop", "商品方图")], 0),
            Custom(store, MediaType.Image),
        ];
    }

    // MARK: 视频：压缩 · 比例 · GIF · 序列帧 · 自定义

    private static IReadOnlyList<RingSlot> Video(RecipeStore store)
    {
        var @base = store.RecipeById("vid.chat") ?? new Recipe { Id = "vid.chat", Name = "发群 25 MB", Type = MediaType.Video };
        Recipe tweak(string name, Func<VideoBody, VideoBody> edit) =>
            @base with { Id = $"ring.{name}", Name = name, SummaryOverride = null, Video = edit(@base.Video) };
        var compress = new[] { "vid.chat", "vid.web", "vid.mute" }.Select(store.RecipeById).OfType<Recipe>().Select(r => Run(r)).ToList();
        var ratios = CropPreset.VideoPresets.Where(p => p.Id is "v916" or "v34" or "v11" or "v169")
            .Select(p => Run(tweak($"比例 {p.Name}", v => v with { Crop = p, Rate = VideoRateChoice.High, ShortEdge = 1080, Codec = VideoCodec.H264 }), p.Name, "1080p"))
            .ToList();
        var gifs = new[] { 480, 320, 640 }
            .Select(w => Run(tweak($"GIF {w}", v => v with { Codec = VideoCodec.Gif, GifWidth = w, GifFps = 15, KeepAudio = false }), $"{w}", "15 fps"))
            .ToList();
        var frames = new[] { "vid.frames.web", "vid.frames.1s" }.Select(store.RecipeById).OfType<Recipe>()
            .Select(r => Run(r, r.Video.FrameValue >= 2 ? $"每秒 {(int)r.Video.FrameValue} 帧" : "每秒 1 张", r.Video.FrameFormat.Title()))
            .ToList();
        return
        [
            new("压缩", compress, 0),
            new("比例", ratios, 0),
            new("GIF", gifs, 0),
            new("序列帧", frames, 0),
            Custom(store, MediaType.Video),
        ];
    }

    // MARK: PDF：压缩 · 转长图 · 自定义

    private static IReadOnlyList<RingSlot> Pdf(RecipeStore store)
    {
        var compress = new[] { "pdf.email", "pdf.screen", "pdf.print" }.Select(store.RecipeById).OfType<Recipe>().Select(r => Run(r)).ToList();
        var @long = new[] { "pdf.long" }.Select(store.RecipeById).OfType<Recipe>()
            .SelectMany(r => new[] { 1080, 750 }.Select(w => new RingChoice($"宽 {w}", "JPG 长图", r with { Id = $"ring.long.{w}", Pdf = r.Pdf with { ImageWidth = w } })))
            .ToList();
        return
        [
            new("压缩", compress, 0),
            new("转长图", @long, 0),
            Custom(store, MediaType.Pdf),
        ];
    }

    /// 自定义：我的配方 + 打开主窗口（带上这批文件，细调后再开始）。
    private static RingSlot Custom(RecipeStore store, MediaType type)
    {
        var mine = store.MineFor(type).Take(4).Select(r => new RingChoice(r.Name, "我的配方", r));
        return new RingSlot("自定义", [.. mine, new RingChoice("打开面板…", "", null)], 0);
    }
}
