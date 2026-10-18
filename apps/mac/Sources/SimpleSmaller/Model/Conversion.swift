import Foundation
import SmallerCore

/// 配方 → 引擎参数。
extension Recipe {
    func imageSettings(cropBoxes: [URL: CGRect] = [:]) -> ImageSettings {
        var sizes: [UInt32] = []
        if let s = image.size { sizes.append(UInt32(s)) }
        sizes += image.extraSizes.map(UInt32.init)
        var crop: CropSetting?
        if let c = image.crop {
            let w = UInt32(image.cropWidth ?? c.width)
            if c.aspect != nil {
                crop = CropSetting(ratioWidth: UInt32(c.ratioW), ratioHeight: UInt32(c.ratioH), width: w,
                                   label: c.ratioLabel.replacingOccurrences(of: ":", with: "x"))
            } else {
                // 自由比例：按每张框的比例，没框的按原图比例。
                crop = CropSetting(ratioWidth: 0, ratioHeight: 0, width: w, label: nil)
            }
        }
        let boxes = cropBoxes.map { url, r in
            CropBox(path: url.path, x: UInt32(max(0, r.minX)), y: UInt32(max(0, r.minY)),
                    width: UInt32(max(1, r.width)), height: UInt32(max(1, r.height)))
        }
        return ImageSettings(
            format: image.format.ffi,
            quality: UInt8(clamping: image.quality),
            lossless: image.lossless,
            targetBytes: image.targetKB.map { UInt64($0) * 1000 },
            sizes: image.crop == nil ? sizes : [],
            sizeBy: image.sizeAxis.ffi,
            crop: crop,
            cropBoxes: boxes
        )
    }

    /// `coverAt`：导出封面、做实况时封面取原视频的第几秒。
    /// `long4K`：这段是 4K、3 分钟以上的长片，自动改用系统硬件编码（软件编码要几十分钟）。
    func videoSettings(trim: ClosedRange<Double>? = nil, crop: CGRect? = nil, coverAt: Double? = nil, long4K: Bool = false) -> VideoSettings {
        let rate: VideoRate = switch video.rate {
        case .high: .high
        case .medium: .medium
        case .low: .low
        case .size: .targetBytes(bytes: UInt64(video.targetMB * 1_000_000))
        }
        let output: VideoOutput
        if video.outputLive {
            // 封面没给时取 1 秒处（引擎会把片段放在封面周围、不超过 3 秒）。
            output = .live(apple: video.liveApple, android: video.liveAndroid, coverAt: coverAt ?? 1, maxLongEdge: UInt32(max(0, video.liveLongEdge)))
        } else if video.muteOnly {
            output = .mute
        } else if video.outputFrames {
            let pick: FramePick = switch video.frameMode {
            case .perSecond: .perSecond(count: video.frameValue)
            case .every: .everySeconds(seconds: video.frameValue)
            case .total: .total(count: UInt32(max(1, video.frameValue)))
            case .all: .all
            }
            output = .frames(pick: pick, width: video.frameWidth.map(UInt32.init), format: video.frameFormat.ffi,
                             quality: UInt8(clamping: video.frameQuality), zip: video.frameZip, contactSheet: video.frameContactSheet)
        } else if video.codec == .gif {
            output = .gif(fps: video.gifFps, width: UInt32(video.gifWidth), quality: 90)
        } else {
            output = .mp4
        }
        let box = crop.map { VideoCropBox(x: UInt32(max(0, $0.minX)), y: UInt32(max(0, $0.minY)), width: UInt32($0.width), height: UInt32($0.height)) }
        return VideoSettings(
            codec: video.codec == .hevc ? .hevc : .h264,
            // HEVC 只走硬件编码（不内置 x265）。
            encoder: (video.fast || video.codec == .hevc || long4K) ? .fast : .quality,
            rate: rate,
            maxShortEdge: video.shortEdge.map(UInt32.init),
            keepAudio: video.keepAudio,
            trimStart: trim?.lowerBound,
            trimEnd: trim?.upperBound,
            crop: box,
            hdrToSdr: video.hdrToSdr,
            maxFps: nil,
            output: output,
            coverAt: video.exportCover && !video.outputLive ? (coverAt ?? 0) : nil
        )
    }

    var pdfLevel: PdfLevel {
        switch pdf.level {
        case .screen: .screen
        case .email, .size: .email
        case .print: .print
        }
    }

    var pdfTargetBytes: UInt64? {
        switch pdf.level {
        case .size: UInt64(pdf.targetMB * 1_000_000)
        case .email: 10_000_000
        default: nil
        }
    }
}

enum Format {
    static let bytes: ByteCountFormatter = {
        let f = ByteCountFormatter()
        f.countStyle = .decimal
        return f
    }()

    /// 体积：不足 1 KB 写 B，其余用十进制 KB / MB / GB（与访达一致）。
    static func size(_ bytes: UInt64) -> String {
        if bytes == 0 { return "—" }
        if bytes < 1000 { return "\(bytes) B" }
        return Format.bytes.string(fromByteCount: Int64(bytes))
    }

    /// 变化百分比。省超过 99% 也写「−99%」，不写成「−100%」让人以为文件没了。
    static func change(_ before: UInt64, _ after: UInt64) -> String {
        guard before > 0, after > 0 else { return "—" }
        let pct = (Double(after) / Double(before) - 1) * 100
        if pct < 0 { return "−\(min(99, Int((-pct).rounded())))%" }
        return "+\(Int(pct.rounded()))%"
    }

    /// 「01:23」；不到 1 秒的（动图常见）写「00:00.3」，免得显示成 00:00。
    static func duration(_ s: Double) -> String {
        if s > 0, s < 1 { return String(format: "00:00.%d", Int((s * 10).rounded(.down))) }
        let t = Int(s.rounded(.down))
        return String(format: "%02d:%02d", t / 60, t % 60)
    }

    static func timecode(_ s: Double) -> String {
        let t = max(0, s)
        return String(format: "%02d:%04.1f", Int(t) / 60, t.truncatingRemainder(dividingBy: 60))
    }

    static var today: String {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd"
        return f.string(from: Date())
    }
}
