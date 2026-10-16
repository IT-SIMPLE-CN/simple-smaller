import SwiftUI

// 占位：逐个替换成正式实现。
struct PdfInspector: View { let ws: Workspace; var body: some View { Text("PDF") } }
struct PdfDetailView: View { let ws: Workspace; var body: some View { Text("PDF") } }
struct SettingsView: View { var body: some View { Text("设置") } }
struct AboutView: View { var body: some View { Text("关于") } }
struct TrayView: View { var body: some View { Text("小窗") } }
struct RecipeEditorView: View { let initial: Recipe; let onSave: (Recipe) -> Void; var body: some View { Text("配方") } }
@MainActor final class RingController { static let shared = RingController(); func refresh() {} }
@MainActor enum ServiceProvider { static func register() {} }
