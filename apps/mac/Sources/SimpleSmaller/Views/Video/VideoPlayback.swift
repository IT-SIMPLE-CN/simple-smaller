import AVFoundation
import AppKit
import ImageIO
import SwiftUI

/// 剪辑与序列帧共用的播放器：只在截取范围内播放，播到结尾回到开头停下。
@Observable
@MainActor
final class VideoPlayback {
    let player = AVPlayer()
    private(set) var url: URL?
    var time: Double = 0
    var playing = false
    /// 播放范围（截取）；播到上限就停。
    var range: ClosedRange<Double> = 0...0
    private var observer: Any?

    init() {
        player.isMuted = false
        player.actionAtItemEnd = .pause
        observer = player.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] t in
            MainActor.assumeIsolated { self?.tick(t.seconds) }
        }
    }

    func load(_ url: URL, range: ClosedRange<Double>) {
        self.range = range
        guard self.url != url else { return }
        self.url = url
        pause()
        player.replaceCurrentItem(with: AVPlayerItem(url: url))
        seek(range.lowerBound)
    }

    func toggle() {
        if playing { pause() } else { play() }
    }

    func play() {
        if time >= range.upperBound - 0.05 || time < range.lowerBound { seek(range.lowerBound) }
        player.play()
        playing = true
    }

    func pause() {
        player.pause()
        playing = false
    }

    /// 精确跳到某一秒（拖时间轴、对齐截取端点时用）。
    func seek(_ t: Double) {
        let t = max(0, t)
        time = t
        player.seek(to: CMTime(seconds: t, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }

    private func tick(_ t: Double) {
        guard t.isFinite else { return }
        if playing {
            time = t
            if t >= range.upperBound {
                pause()
                seek(range.lowerBound)
            }
        }
    }
}

/// AVPlayerLayer 放进 SwiftUI：等比缩放，不带系统控件。
struct PlayerLayerView: NSViewRepresentable {
    let player: AVPlayer

    func makeNSView(context: Context) -> PlayerNSView {
        let view = PlayerNSView()
        view.playerLayer.player = player
        return view
    }

    func updateNSView(_ nsView: PlayerNSView, context: Context) {
        if nsView.playerLayer.player !== player { nsView.playerLayer.player = player }
    }

    final class PlayerNSView: NSView {
        let playerLayer = AVPlayerLayer()

        override init(frame: NSRect) {
            super.init(frame: frame)
            wantsLayer = true
            playerLayer.videoGravity = .resize
            layer?.addSublayer(playerLayer)
        }

        required init?(coder: NSCoder) { fatalError() }

        override func layout() {
            super.layout()
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            playerLayer.frame = bounds
            CATransaction.commit()
        }
    }
}

/// 时间轴缩略图：均匀取 n 帧，按视频转正后的方向。
enum TimelineThumbnails {
    /// 动图（GIF、动画 WebP、APNG）系统播放器读不了：按时间从图片的帧里取。
    static func animatedFrame(url: URL, at t: Double, duration: Double, maxSide: CGFloat) -> CGImage? {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
        let n = CGImageSourceGetCount(source)
        guard n > 1 else { return nil }
        let k = min(n - 1, max(0, Int(Double(n) * t / max(0.001, duration))))
        let options: [CFString: Any] = [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: maxSide,
                                        kCGImageSourceCreateThumbnailWithTransform: true]
        return CGImageSourceCreateThumbnailAtIndex(source, k, options as CFDictionary)
    }

    static func make(url: URL, duration: Double, count: Int, height: CGFloat) async -> [CGImage] {
        if MediaType.isAnimated(url) {
            return (0..<count).compactMap { animatedFrame(url: url, at: duration * (Double($0) + 0.5) / Double(count), duration: duration, maxSide: height * 4) }
        }
        let asset = AVURLAsset(url: url)
        let generator = AVAssetImageGenerator(asset: asset)
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: height * 4, height: height * 2)
        generator.requestedTimeToleranceBefore = CMTime(seconds: 0.5, preferredTimescale: 600)
        generator.requestedTimeToleranceAfter = CMTime(seconds: 0.5, preferredTimescale: 600)
        var out: [CGImage] = []
        for k in 0..<count {
            let t = duration * (Double(k) + 0.5) / Double(count)
            if let (image, _) = try? await generator.image(at: CMTime(seconds: t, preferredTimescale: 600)) {
                out.append(image)
            } else if let last = out.last {
                out.append(last)
            }
        }
        return out
    }

    /// 精确取某一时刻的一帧（序列帧预览用）。
    static func frame(url: URL, at t: Double, maxSide: CGFloat, duration: Double = 0) async -> CGImage? {
        if MediaType.isAnimated(url) { return animatedFrame(url: url, at: t, duration: duration, maxSide: maxSide) }
        let generator = AVAssetImageGenerator(asset: AVURLAsset(url: url))
        generator.appliesPreferredTrackTransform = true
        generator.maximumSize = CGSize(width: maxSide, height: maxSide)
        generator.requestedTimeToleranceBefore = .zero
        generator.requestedTimeToleranceAfter = .zero
        return try? await generator.image(at: CMTime(seconds: t, preferredTimescale: 600)).image
    }
}

/// 动图在剪辑里的画面：系统图片视图自己会播放动画。
struct AnimatedImageView: NSViewRepresentable {
    let url: URL

    func makeNSView(context: Context) -> NSImageView {
        let v = NSImageView()
        v.imageScaling = .scaleAxesIndependently
        // 不按图片原尺寸撑大自己，跟着外面给的框走。
        for o in [NSLayoutConstraint.Orientation.horizontal, .vertical] {
            v.setContentHuggingPriority(.defaultLow, for: o)
            v.setContentCompressionResistancePriority(.defaultLow, for: o)
        }
        v.animates = true
        v.image = NSImage(contentsOf: url)
        return v
    }

    func updateNSView(_ v: NSImageView, context: Context) {}
}
