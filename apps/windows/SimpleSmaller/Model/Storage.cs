using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleSmaller;

/// 设置、配方存成 JSON，放在 %APPDATA%\SimpleSmaller。读不出（没有、损坏）就用默认值。
internal static class Storage
{
    public static string Directory
    {
        get
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleSmaller");
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    /// 键名、枚举值与 mac 版的 Codable 一致（小驼峰），导出的 .smaller 两端通用。
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // 中文原样写出，文件能直接看。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            WriteAtomic(path, JsonSerializer.Serialize(value, Options));
        }
        catch (Exception e)
        {
            Log.Error($"存不下 {path}：{e.Message}");
        }
    }

    /// 先写临时文件再替换，写到一半断电不会留下半个文件。
    public static void WriteAtomic(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
