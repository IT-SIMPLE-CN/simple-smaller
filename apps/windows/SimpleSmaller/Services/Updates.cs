using CommunityToolkit.Mvvm.ComponentModel;

namespace SimpleSmaller;

/// 自动更新（之后接 Velopack）。现在只有设置里的开关与「检查更新」入口：
/// 开关存在 %APPDATA%\SimpleSmaller\updates.json，检查更新先回一句还没接上。
internal sealed partial class Updates : ObservableObject
{
    private static Updates? shared;
    public static Updates Shared => shared ??= new Updates();

    private readonly string filePath = Path.Combine(Storage.Directory, "updates.json");

    /// 设置里的「自动检查更新」。
    [ObservableProperty] private bool automaticallyChecks = true;

    private sealed class Saved
    {
        public bool AutomaticallyChecks { get; set; } = true;
    }

    private Updates()
    {
        if (Storage.Load<Saved>(filePath) is { } s) automaticallyChecks = s.AutomaticallyChecks;
        PropertyChanged += (_, _) => Storage.Save(filePath, new Saved { AutomaticallyChecks = AutomaticallyChecks });
    }

    /// 手动检查；返回给用户看的一句话。
    public Task<string> CheckAsync() => Task.FromResult("自动更新还在接入，暂时请到官网下载新版本");
}
