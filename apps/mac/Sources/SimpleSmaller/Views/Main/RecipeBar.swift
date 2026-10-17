import SwiftUI
import UniformTypeIdentifiers

/// 顶部配方条（设计稿 V2.1）：只放这个分段能用的配方；选中变黑，改过参数带红点；
/// 文件可以直接拖到某个配方上；「＋」新建，「管理」进设置。
struct RecipeBar: View {
    @Environment(AppModel.self) private var model
    @Environment(\.openSettings) private var openSettings
    let ws: Workspace
    @State private var editing: Recipe?

    private var recipes: (builtIn: [Recipe], mine: [Recipe]) {
        let fits: (Recipe) -> Bool = { r in
            switch ws.type {
            case .image: return (r.image.crop != nil) == (ws.imageSegment == .crop)
            case .video: return r.video.outputFrames == (ws.videoSegment == .frames)
            case .pdf: return r.pdf.longImage == (ws.pdfSegment == .toImages)
            }
        }
        return (model.store.builtIns(for: ws.type).filter(fits), model.store.mine(for: ws.type).filter(fits))
    }

    var body: some View {
        let list = recipes
        BentoCard(padding: EdgeInsets(top: 10, leading: 20, bottom: 10, trailing: 10)) {
            HStack(spacing: 12) {
                Text("配方").font(.system(size: 13, weight: .semibold))
                ScrollView(.horizontal) {
                    HStack(spacing: 6) {
                        ForEach(list.builtIn) { pill($0) }
                        if !list.mine.isEmpty {
                            Rectangle().fill(Bento.line).frame(width: 1, height: 20).padding(.horizontal, 4)
                            ForEach(list.mine) { r in
                                pill(r).contextMenu {
                                    Button("编辑…") { editing = r }
                                    Button("导出…") { export(r) }
                                    Button("删除", role: .destructive) { model.store.delete(id: r.id) }
                                }
                            }
                        }
                    }
                    .padding(.trailing, 24)
                }
                .scrollIndicators(.never)
                .mask(LinearGradient(stops: [.init(color: .black, location: 0.9), .init(color: .clear, location: 1)], startPoint: .leading, endPoint: .trailing))
                .accessibilityElement(children: .contain)
                .accessibilityLabel("配方")
                CircleIconButton(symbol: "plus", label: "新建配方", size: 36) {
                    var r = ws.draft
                    r.id = ""
                    r.name = ""
                    r.builtIn = false
                    editing = r
                }
                CapsuleButton(title: "管理", height: 36) { openSettings() }
                    .help("在设置里排序、导入导出配方")
            }
            .frame(maxHeight: .infinity)
        }
        .bentoLocked(ws.running)
        .sheet(item: $editing) { recipe in
            RecipeEditorView(initial: recipe) { saved in ws.select(recipe: saved) }
        }
    }

    private func pill(_ r: Recipe) -> some View {
        let on = ws.recipeID == r.id
        return PillButton(title: r.name, on: on, dot: on && ws.modified) { ws.select(recipe: r) }
            .modifier(RecipeDrop(recipe: r))
            .help(r.summary)
    }

    /// 导出成 .smaller 文件，发给同事后在「新建配方 → 从文件导入」里导入。
    private func export(_ recipe: Recipe) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = "\(recipe.name).smaller"
        panel.allowedContentTypes = [.init(filenameExtension: "smaller") ?? .json]
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            try model.store.export(ids: [recipe.id], to: url)
            model.flash("已导出「\(recipe.name)」")
        } catch {
            model.flash("导出失败：\(error.localizedDescription)")
        }
    }
}

/// 把文件拖到某个配方上：加进这一批并换成这个配方（点「开始」才写出）。
struct RecipeDrop: ViewModifier {
    @Environment(AppModel.self) private var model
    let recipe: Recipe
    @State private var targeted = false

    func body(content: Content) -> some View {
        content
            .overlay {
                if targeted {
                    Capsule().strokeBorder(Color.brand, lineWidth: 2).allowsHitTesting(false)
                }
            }
            .dropDestination(for: URL.self) { urls, _ in
                model.selectedType = recipe.type
                model.add(urls, preferred: recipe.type)
                model.current.select(recipe: recipe)
                return true
            } isTargeted: { targeted = $0 }
            .animation(.easeOut(duration: 0.12), value: targeted)
    }
}
