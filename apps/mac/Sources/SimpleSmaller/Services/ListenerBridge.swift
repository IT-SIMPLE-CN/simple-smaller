import Foundation
import SmallerCore

/// 引擎回调可能来自任意线程，统一转到主线程。
final class ListenerBridge: JobListener, @unchecked Sendable {
    let progress: @MainActor (UInt32, Double) -> Void
    let item: @MainActor (JobItemResult) -> Void
    let finished: @MainActor () -> Void

    init(progress: @escaping @MainActor (UInt32, Double) -> Void, item: @escaping @MainActor (JobItemResult) -> Void, finished: @escaping @MainActor () -> Void) {
        self.progress = progress
        self.item = item
        self.finished = finished
    }

    // 用主队列而不是 Task：主队列先进先出，「完成」不会抢在最后一个结果前面。
    func onProgress(index: UInt32, fraction: Double) { DispatchQueue.main.async { MainActor.assumeIsolated { self.progress(index, fraction) } } }
    func onItem(result: JobItemResult) { DispatchQueue.main.async { MainActor.assumeIsolated { self.item(result) } } }
    func onFinished() { DispatchQueue.main.async { MainActor.assumeIsolated { self.finished() } } }
}

extension EngineError {
    /// 给用户看的一句话（引擎已写成中文）。
    var userMessage: String {
        switch self {
        case .Io(let m), .Unsupported(let m), .Failed(let m): m
        }
    }
}
