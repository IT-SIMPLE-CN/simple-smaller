import Foundation
import Observation

/// 配方：内置 + 我的。我的配方存在 ~/Library/Application Support/SimpleSmaller/recipes.json，
/// 可导出为 .smaller（JSON）发给同事。
@Observable
@MainActor
final class RecipeStore {
    private(set) var mine: [Recipe] = []
    /// 设置里拖动排序后的顺序（含内置），前 5 个显示在圆环外圈。
    private(set) var order: [String] = []

    private let fileURL: URL

    init(directory: URL? = nil) {
        let dir = directory ?? FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("SimpleSmaller", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        fileURL = dir.appendingPathComponent("recipes.json")
        load()
    }

    private struct Saved: Codable {
        var mine: [Recipe]
        var order: [String]
    }

    private func load() {
        if let data = try? Data(contentsOf: fileURL), let saved = try? JSONDecoder().decode(Saved.self, from: data) {
            mine = saved.mine
            order = saved.order
        } else {
            mine = Recipe.starterMine
            order = []
            save()
        }
    }

    private func save() {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? encoder.encode(Saved(mine: mine, order: order)) {
            try? data.write(to: fileURL, options: .atomic)
        }
    }

    var all: [Recipe] {
        let combined = Recipe.builtIns + mine
        guard !order.isEmpty else { return combined }
        return combined.sorted { (order.firstIndex(of: $0.id) ?? Int.max) < (order.firstIndex(of: $1.id) ?? Int.max) }
    }

    func builtIns(for type: MediaType) -> [Recipe] { all.filter { $0.type == type && $0.builtIn } }
    func mine(for type: MediaType) -> [Recipe] { all.filter { $0.type == type && !$0.builtIn } }
    func recipe(id: String) -> Recipe? { all.first { $0.id == id } }

    /// 存一个新配方（或同名覆盖我的配方），返回存下的配方。
    @discardableResult
    func saveMine(_ recipe: Recipe) -> Recipe {
        var r = recipe
        r.builtIn = false
        r.summaryOverride = nil
        if let i = mine.firstIndex(where: { $0.id == r.id || ($0.name == r.name && $0.type == r.type) }) {
            r.id = mine[i].id
            mine[i] = r
        } else {
            if r.id.isEmpty || Recipe.builtIns.contains(where: { $0.id == r.id }) { r.id = "mine.\(UUID().uuidString)" }
            mine.append(r)
        }
        save()
        return r
    }

    func delete(id: String) {
        mine.removeAll { $0.id == id }
        order.removeAll { $0 == id }
        save()
    }

    func move(fromOffsets: IndexSet, toOffset: Int) {
        var ids = all.map(\.id)
        ids.move(fromOffsets: fromOffsets, toOffset: toOffset)
        order = ids
        save()
    }


    func export(ids: [String], to url: URL) throws {
        let list = all.filter { ids.contains($0.id) }.map { r -> Recipe in
            var copy = r
            copy.builtIn = false
            return copy
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(list).write(to: url, options: .atomic)
    }

    /// 导入 .smaller，返回导入的个数。同名的覆盖。
    @discardableResult
    func importRecipes(from url: URL) throws -> Int {
        let data = try Data(contentsOf: url)
        let list: [Recipe]
        if let many = try? JSONDecoder().decode([Recipe].self, from: data) {
            list = many
        } else {
            list = [try JSONDecoder().decode(Recipe.self, from: data)]
        }
        for r in list { saveMine(r) }
        return list.count
    }
}
