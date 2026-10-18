using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SimpleSmaller;

/// 配方：内置 + 我的。我的配方存在 %APPDATA%\SimpleSmaller\recipes.json，
/// 可导出为 .smaller（JSON）发给同事。
internal sealed partial class RecipeStore : ObservableObject
{
    public IReadOnlyList<Recipe> Mine { get; private set; } = [];
    /// 设置里拖动排序后的顺序（含内置），前 5 个显示在圆环外圈。
    public IReadOnlyList<string> Order { get; private set; } = [];

    private readonly string filePath;

    public RecipeStore(string? directory = null)
    {
        var dir = directory ?? Storage.Directory;
        try { Directory.CreateDirectory(dir); } catch { }
        filePath = Path.Combine(dir, "recipes.json");
        Load();
    }

    private sealed class Saved
    {
        public List<Recipe> Mine { get; set; } = [];
        public List<string> Order { get; set; } = [];
    }

    private void Load()
    {
        if (Storage.Load<Saved>(filePath) is { } saved)
        {
            Mine = saved.Mine;
            Order = saved.Order;
        }
        else
        {
            Mine = Recipe.StarterMine.ToList();
            Order = [];
            Save();
        }
    }

    private void Save()
    {
        Storage.Save(filePath, new Saved { Mine = Mine.ToList(), Order = Order.ToList() });
        OnPropertyChanged(nameof(Mine));
        OnPropertyChanged(nameof(Order));
        OnPropertyChanged(nameof(All));
    }

    public IReadOnlyList<Recipe> All
    {
        get
        {
            var combined = Recipe.BuiltIns.Concat(Mine).ToList();
            if (Order.Count == 0) return combined;
            var rank = new Dictionary<string, int>();
            for (var i = 0; i < Order.Count; i++) rank.TryAdd(Order[i], i);
            return combined.OrderBy(r => rank.TryGetValue(r.Id, out var i) ? i : int.MaxValue).ToList();
        }
    }

    public IReadOnlyList<Recipe> BuiltInsFor(MediaType type) => All.Where(r => r.Type == type && r.BuiltIn).ToList();
    public IReadOnlyList<Recipe> MineFor(MediaType type) => All.Where(r => r.Type == type && !r.BuiltIn).ToList();
    public Recipe? RecipeById(string id) => All.FirstOrDefault(r => r.Id == id);

    /// 存一个新配方（或同名覆盖我的配方），返回存下的配方。
    public Recipe SaveMine(Recipe recipe)
    {
        var r = recipe with { BuiltIn = false, SummaryOverride = null };
        var mine = Mine.ToList();
        var i = mine.FindIndex(m => m.Id == r.Id || (m.Name == r.Name && m.Type == r.Type));
        if (i >= 0)
        {
            r = r with { Id = mine[i].Id };
            mine[i] = r;
        }
        else
        {
            if (string.IsNullOrEmpty(r.Id) || Recipe.BuiltIns.Any(b => b.Id == r.Id))
                r = r with { Id = $"mine.{Guid.NewGuid().ToString().ToUpperInvariant()}" };
            mine.Add(r);
        }
        Mine = mine;
        Save();
        return r;
    }

    public void Delete(string id)
    {
        Mine = Mine.Where(r => r.Id != id).ToList();
        Order = Order.Where(o => o != id).ToList();
        Save();
    }

    /// 与 SwiftUI 的 `move(fromOffsets:toOffset:)` 同一规则：toOffset 是移动前列表里的插入位置。
    public void Move(IEnumerable<int> fromOffsets, int toOffset)
    {
        var ids = All.Select(r => r.Id).ToList();
        var from = fromOffsets.Where(i => i >= 0 && i < ids.Count).Distinct().OrderBy(i => i).ToList();
        var moving = from.Select(i => ids[i]).ToList();
        var insert = toOffset - from.Count(i => i < toOffset);
        foreach (var i in Enumerable.Reverse(from)) ids.RemoveAt(i);
        ids.InsertRange(Math.Clamp(insert, 0, ids.Count), moving);
        Order = ids;
        Save();
    }

    public void Export(IReadOnlyCollection<string> ids, string path)
    {
        var list = All.Where(r => ids.Contains(r.Id)).Select(r => r with { BuiltIn = false }).ToList();
        Storage.WriteAtomic(path, JsonSerializer.Serialize(list, Storage.Options));
    }

    /// 导入 .smaller，返回导入的个数。同名的覆盖。
    public int ImportRecipes(string path)
    {
        var text = File.ReadAllText(path);
        List<Recipe> list;
        try
        {
            list = JsonSerializer.Deserialize<List<Recipe>>(text, Storage.Options) ?? [];
        }
        catch (JsonException)
        {
            list = [JsonSerializer.Deserialize<Recipe>(text, Storage.Options) ?? throw new JsonException("不是常用文件")];
        }
        foreach (var r in list) SaveMine(r);
        return list.Count;
    }
}
