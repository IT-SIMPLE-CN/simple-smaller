import AppKit
import Photos
import SmallerCore

/// 实况做好后的去处：存进「照片」（照片库里就是一张实况，开了 iCloud 照片会同步到 iPhone）、隔空投送、共享。
enum LiveLibrary {
    /// 一个视频做出的实况文件。
    struct Files {
        /// 苹果实况：同名的照片 + 视频。
        var photo: URL?
        var video: URL?
        /// 安卓动态照片（一张 .MP.jpg）。
        var motion: URL?

        var all: [URL] { [photo, video, motion].compactMap { $0 } }
        var pair: (photo: URL, video: URL)? { photo.flatMap { p in video.map { (p, $0) } } }
    }

    static func files(_ outputs: [Output]) -> Files {
        var f = Files()
        for o in outputs {
            let url = URL(fileURLWithPath: o.path)
            let name = url.lastPathComponent.lowercased()
            if name.hasSuffix(".mp.jpg") { f.motion = url }
            else if name.hasSuffix(".jpg") { f.photo = url }
            else if name.hasSuffix(".mov") { f.video = url }
        }
        return f
    }

    /// 存进「照片」：每对照片 + 视频建成一张实况。成功返回 nil，否则返回给用户看的说明。
    /// 只要「添加」权限（不读用户的照片库）。
    static func saveToPhotos(_ pairs: [(photo: URL, video: URL)]) async -> String? {
        guard !pairs.isEmpty else { return nil }
        let status = await PHPhotoLibrary.requestAuthorization(for: .addOnly)
        guard status == .authorized || status == .limited else {
            return "没有存进「照片」的权限：在系统设置 › 隐私与安全性 › 照片 里允许简单小一点"
        }
        do {
            try await PHPhotoLibrary.shared().performChanges {
                for p in pairs {
                    let request = PHAssetCreationRequest.forAsset()
                    let options = PHAssetResourceCreationOptions()
                    options.shouldMoveFile = false
                    request.addResource(with: .photo, fileURL: p.photo, options: options)
                    request.addResource(with: .pairedVideo, fileURL: p.video, options: options)
                }
            }
            return nil
        } catch {
            return "存进「照片」失败：\(error.localizedDescription)"
        }
    }

    /// 隔空投送。这台 Mac 不能隔空投送时返回 false。
    @MainActor static func airDrop(_ urls: [URL]) -> Bool {
        guard let service = NSSharingService(named: .sendViaAirDrop), service.canPerform(withItems: urls) else { return false }
        service.perform(withItems: urls)
        return true
    }
}
