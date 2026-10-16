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
            Text("SIMPLE SMALLER").font(.system(size: 11, weight: .medium)).tracking(1.2).foregroundStyle(.secondary).padding(.top, 2)
            Text(version).font(.system(size: 11).monospacedDigit()).foregroundStyle(.tertiary).padding(.top, 6)
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
            Text("© 2021–2026 IT-SIMPLE").font(.system(size: 11)).foregroundStyle(.tertiary).padding(.top, 14).padding(.bottom, 22)
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
            ScrollView {
                Text(text)
                    .font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(16)
            }
            Divider()
            HStack { Spacer(); Button("完成") { dismiss() }.keyboardShortcut(.defaultAction) }.padding(12)
        }
        .frame(width: 560, height: 460)
    }
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
