import SwiftUI

/// 圆环执行后的结果卡（设计稿 07，出现在指针旁）：
/// 「4 张 · 网站用」「71.4 MB → 1.6 MB −98%」，在访达中显示 / 拷贝 / 撤销。指针离开 6 秒后收起。
struct ResultCard: View {
    @Bindable var run: QuickRun
    let onClose: () -> Void
    @State private var undone = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 8) {
                icon
                Text(verbatim: "\(countText) · \(run.recipe.name)")
                    .font(.system(size: 13, weight: .semibold))
                    .lineLimit(1)
                Spacer(minLength: 0)
            }
            content
        }
        .padding(14)
        .frame(width: 300, alignment: .leading)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.primary.opacity(0.08), lineWidth: 0.5))
        .shadow(color: .black.opacity(0.18), radius: 16, y: 6)
        .padding(20)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .animation(.smooth(duration: 0.25), value: run.phase)
    }

    private var countText: String {
        let n = run.urls.count
        switch run.type {
        case .image: return n == 1 ? run.urls[0].lastPathComponent : "\(n) 张"
        case .video: return n == 1 ? run.urls[0].lastPathComponent : "\(n) 段"
        case .pdf: return n == 1 ? run.urls[0].lastPathComponent : "\(n) 个"
        }
    }

    @ViewBuilder private var icon: some View {
        switch run.phase {
        case .running: ProgressView().controlSize(.small)
        case .finished: Image(systemName: undone ? "arrow.uturn.backward.circle.fill" : "checkmark.circle.fill")
            .foregroundStyle(undone ? Color.secondary : Color.saving)
        case .failed: Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(Color.warning)
        }
    }

    @ViewBuilder private var content: some View {
        switch run.phase {
        case .running(let done, let total):
            Text(verbatim: total > 1 ? "处理中 \(done) / \(total)…" : "处理中…")
                .font(.system(size: 12)).foregroundStyle(.secondary).monospacedDigit()
            if run.type == .video {
                Text("可以先收起，处理完会发通知。").font(.system(size: 11)).foregroundStyle(.tertiary)
            }
        case .failed(let message):
            Text(message).font(.system(size: 12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            HStack { Spacer(); Button("好", action: onClose).controlSize(.small) }
        case .finished:
            if undone {
                Text("已撤销，新文件移到了废纸篓。").font(.system(size: 12)).foregroundStyle(.secondary)
            } else {
                HStack(spacing: 6) {
                    Text(verbatim: "\(Format.size(run.before)) → \(Format.size(run.after))")
                        .font(.system(size: 12).monospacedDigit()).foregroundStyle(.secondary)
                    if run.after > 0, run.after < run.before {
                        Text(verbatim: Format.change(run.before, run.after))
                            .font(.system(size: 12, weight: .semibold).monospacedDigit()).foregroundStyle(Color.saving)
                    }
                }
                if run.kept > 0 {
                    Text(verbatim: "\(run.kept) 个压完没变小，保留原文件").font(.system(size: 11)).foregroundStyle(Color.warning)
                }
                HStack(spacing: 6) {
                    Button("在访达中显示") { run.reveal(); onClose() }
                    Button("拷贝") { run.copy(); onClose() }
                    Spacer(minLength: 0)
                    Button("撤销") {
                        try? run.undo()
                        undone = true
                    }
                }
                .controlSize(.small)
                .disabled(run.outputs.isEmpty)
            }
        }
    }
}
