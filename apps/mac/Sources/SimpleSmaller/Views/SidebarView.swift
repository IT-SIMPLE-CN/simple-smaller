import SwiftUI

struct SidebarView: View {
    @Environment(AppModel.self) private var model
    @State private var editing: Recipe?

    var body: some View {
        let ws = model.current
        // 类型选中用浅灰底（系统的非强调选中色），品牌红只留给当前配方的描边。
        List {
            ForEach(MediaType.allCases) { type in
                TypeRow(type: type, count: model.workspace(type).files.count, selected: model.selectedType == type) {
                    model.selectedType = type
                }
            }

            Section("常用配方") {
                ForEach(model.store.builtIns(for: model.selectedType)) { recipe in
                    RecipeRow(recipe: recipe, active: ws.recipeID == recipe.id) { apply(recipe) }
                }
            }

            Section("我的配方") {
                ForEach(model.store.mine(for: model.selectedType)) { recipe in
                    RecipeRow(recipe: recipe, active: ws.recipeID == recipe.id) { apply(recipe) }
                        .contextMenu {
                            Button("编辑…") { editing = recipe }
                            Button("删除", role: .destructive) { model.store.delete(id: recipe.id) }
                        }
                }
                Button {
                    var r = ws.draft
                    r.id = ""
                    r.name = ""
                    r.builtIn = false
                    editing = r
                } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "plus").font(.system(size: 11, weight: .semibold))
                        Text("新建配方")
                    }
                    .foregroundStyle(.secondary)
                    .padding(.horizontal, 6)
                    .frame(height: 28)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .listRowInsets(EdgeInsets(top: 1, leading: 0, bottom: 1, trailing: 0))
            }
        }
        .listStyle(.sidebar)
        .safeAreaInset(edge: .bottom, spacing: 0) {
            BrandFooter()
        }
        .sheet(item: $editing) { recipe in
            RecipeEditorView(initial: recipe) { saved in
                apply(saved)
            }
        }
    }

    private func apply(_ recipe: Recipe) {
        let ws = model.current
        ws.select(recipe: recipe)
        model.schedulePreviews(ws.type)
    }
}

struct TypeRow: View {
    let type: MediaType
    let count: Int
    let selected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Image(systemName: type.symbol)
                    .foregroundStyle(Color.brand)
                    .frame(width: 18)
                Text(type.title)
                    .fontWeight(selected ? .medium : .regular)
                Spacer()
                if count > 0 {
                    Text("\(count)").font(.caption).foregroundStyle(.secondary).monospacedDigit()
                }
            }
            .padding(.horizontal, 6)
            .frame(height: 28)
            .contentShape(Rectangle())
            .background {
                if selected {
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(Color(nsColor: .unemphasizedSelectedContentBackgroundColor))
                }
            }
        }
        .buttonStyle(.plain)
        .listRowInsets(EdgeInsets(top: 1, leading: 0, bottom: 1, trailing: 0))
        .accessibilityLabel(type.title)
        .accessibilityValue(count > 0 ? "\(count) 个文件" : "")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }
}

struct RecipeRow: View {
    let recipe: Recipe
    let active: Bool
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 8) {
                Text(recipe.name)
                    .lineLimit(1)
                    .fontWeight(active ? .medium : .regular)
                Spacer(minLength: 4)
                Text(recipe.summary)
                    .font(.caption)
                    .monospacedDigit()
                    .foregroundStyle(active ? AnyShapeStyle(Color.brand) : AnyShapeStyle(.secondary))
                    .lineLimit(1)
            }
            // 与类型行同高、同边距：上下的选中块对齐。
            .padding(.horizontal, 6)
            .frame(height: 28)
            .contentShape(Rectangle())
            .background {
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(active ? Color.brand.opacity(0.12) : (hovering ? Color.primary.opacity(0.06) : .clear))
                    .overlay {
                        if active {
                            RoundedRectangle(cornerRadius: 6, style: .continuous)
                                .strokeBorder(Color.brand.opacity(0.55), lineWidth: 1)
                        }
                    }
            }
            .animation(.easeOut(duration: 0.15), value: hovering)
            .animation(.smooth(duration: 0.25), value: active)
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .listRowInsets(EdgeInsets(top: 1, leading: 0, bottom: 1, trailing: 0))
        .accessibilityLabel("\(recipe.name)，\(recipe.summary)")
        .accessibilityAddTraits(active ? .isSelected : [])
    }
}

/// 边栏左下角的 IT·SIMPLE 小标，点了打开官网。
struct BrandFooter: View {
    var body: some View {
        VStack(spacing: 0) {
            Divider()
            Link(destination: URL(string: "https://www.it-simple.cn/")!) {
                HStack(spacing: 8) {
                    BrandMark().frame(width: 18, height: 17)
                    (Text("IT").foregroundColor(.brand) + Text("·SIMPLE"))
                        .font(.system(size: 11, weight: .semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                    Spacer()
                }
                .padding(.horizontal, 18)
                .frame(height: 44)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("IT-SIMPLE 官网")
        }
    }
}

/// 官网标（随包的 SVG）。
struct BrandMark: View {
    var body: some View {
        if let url = Bundle.main.url(forResource: "logo-mark", withExtension: "svg"), let image = NSImage(contentsOf: url) {
            // 模板图：跟随文字颜色，深色模式下不会是一块黑。
            Image(nsImage: { image.isTemplate = true; return image }()).resizable().scaledToFit().foregroundStyle(.secondary)
        } else {
            Image(systemName: "circle.hexagongrid").resizable().scaledToFit().foregroundStyle(.secondary)
        }
    }
}
