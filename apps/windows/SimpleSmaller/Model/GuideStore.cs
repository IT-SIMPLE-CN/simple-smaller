namespace SimpleSmaller;

/// 教学「看过」的记录：圆环教学（ring）、各功能小教学（image / video / pdf）。
/// 单独存在 %APPDATA%\SimpleSmaller\guides.json，不和 settings.json 混在一起。
/// 想从头再看（测试用）：启动参数 --reset-guides，或删掉这个文件。
internal sealed class GuideStore
{
    public const string Ring = "ring";

    private static GuideStore? shared;
    public static GuideStore Shared => shared ??= new GuideStore();

    private readonly string filePath;
    private readonly HashSet<string> seen;

    private sealed class Saved
    {
        public List<string> Seen { get; set; } = [];
    }

    public GuideStore(string? directory = null)
    {
        filePath = Path.Combine(directory ?? Storage.Directory, "guides.json");
        seen = new HashSet<string>(Storage.Load<Saved>(filePath)?.Seen ?? []);
    }

    /// 每类文件的小教学的键。
    public static string KeyOf(MediaType type) => type switch
    {
        MediaType.Image => "image",
        MediaType.Video => "video",
        _ => "pdf",
    };

    public bool IsSeen(string key) => seen.Contains(key);

    public void MarkSeen(string key)
    {
        if (seen.Add(key)) Save();
    }

    /// 全部忘掉：下次启动圆环教学再弹一次，每类第一次拖进文件后小教学再出现一次。
    public void Reset()
    {
        seen.Clear();
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
    }

    private void Save() => Storage.Save(filePath, new Saved { Seen = seen.OrderBy(s => s).ToList() });
}
