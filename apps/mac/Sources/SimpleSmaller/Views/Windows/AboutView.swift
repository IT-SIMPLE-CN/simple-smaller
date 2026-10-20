import SwiftUI

/// 关于（设计稿 09）：五周年只在这里写一句。
struct AboutView: View {
    @State private var showLicenses = false

    private var version: String {
        let v = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0.0"
        let b = Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1"
        return "版本 \(v)（\(b)）"
    }

    var body: some View {
        VStack(spacing: 0) {
            Image(nsImage: NSApp.applicationIconImage)
                .resizable()
                .frame(width: 96, height: 96)
                .padding(.top, 28)
                .accessibilityHidden(true)
            Text("简单小一点").font(.system(size: 20, weight: .semibold)).padding(.top, 12)
            Text("SIMPLE SMALLER").font(.system(size: 11, weight: .medium)).tracking(1.2).foregroundStyle(Bento.secondary).padding(.top, 2)
            Text(version).font(.system(size: 11).monospacedDigit()).foregroundStyle(Bento.secondary.opacity(0.75)).padding(.top, 6)
            Text("IT-SIMPLE 成立五周年，\n做了这个小工具，送给一路同行的朋友。")
                .font(.system(size: 13))
                .multilineTextAlignment(.center)
                .lineSpacing(3)
                .padding(.top, 18)
            Link(destination: URL(string: "https://www.it-simple.cn/")!) {
                HStack(spacing: 6) {
                    BrandMark().frame(width: 16, height: 15)
                    Text("it-simple.cn")
                }
                .font(.system(size: 12))
            }
            .padding(.top, 16)
            Button("开源组件与许可证") { showLicenses = true }
                .buttonStyle(.link)
                .font(.system(size: 12))
                .padding(.top, 10)
            // AGPL-3.0 第 0、5(d) 条：交互界面里写明许可、无担保，并给出源代码。
            Text(LocalizedStringKey("本程序以 AGPL-3.0 授权，不提供任何担保。[源代码](" + AppLinks.source + ")"))
                .font(.system(size: 11))
                .foregroundStyle(Bento.secondary)
                .multilineTextAlignment(.center)
                .padding(.top, 14)
            Text("© 2021–2026 IT-SIMPLE · AGPL-3.0").font(.system(size: 11)).foregroundStyle(Bento.secondary.opacity(0.75)).padding(.top, 4).padding(.bottom, 22)
        }
        .frame(width: 340)
        .background(NotRestorable())
        .sheet(isPresented: $showLicenses) { LicensesView() }
    }
}

private struct LicensesView: View {
    @Environment(\.dismiss) private var dismiss
    private var text: String {
        Bundle.main.url(forResource: "Licenses", withExtension: "txt").flatMap { try? String(contentsOf: $0, encoding: .utf8) } ?? ""
    }

    var body: some View {
        VStack(spacing: 0) {
            // 按列排的清单，不自动换行，宽了就横向滚动。
            ScrollView([.vertical, .horizontal]) {
                Text(text)
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .fixedSize(horizontal: true, vertical: false)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(16)
            }
            Divider()
            HStack(spacing: 8) {
                Button("显示许可证全文") { revealThirdParty() }
                Link("源代码", destination: URL(string: AppLinks.source)!)
                Spacer()
                Button("完成") { dismiss() }.keyboardShortcut(.defaultAction)
            }
            .padding(12)
        }
        .frame(width: 560, height: 460)
    }

    /// 在访达里打开应用包内的许可证全文目录（Contents/Resources/ThirdParty）。
    private func revealThirdParty() {
        guard let dir = Bundle.main.resourceURL?.appendingPathComponent("ThirdParty", isDirectory: true),
              FileManager.default.fileExists(atPath: dir.path) else { return }
        NSWorkspace.shared.open(dir)
    }
}

/// 对外链接。
enum AppLinks {
    /// 源代码仓库（AGPL-3.0 要求向用户提供源代码）；每个版本对应 tag mac-v<版本>。
    static let source = "https://github.com/IT-SIMPLE-CN/simple-smaller"
}

/// 关于窗口不在下次启动时自动恢复。
private struct NotRestorable: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView { Probe() }
    func updateNSView(_ nsView: NSView, context: Context) {}

    final class Probe: NSView {
        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            window?.isRestorable = false
        }
    }
}
