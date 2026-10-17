using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace SimpleSmaller;

/// 自动更新（Velopack），做法同 mac 的 Sparkle：启动后每 6 小时在后台检查一次，发现新版本先问用户，同意了才下载安装。
/// 更新源（releases.<频道>.json）放自家域名 bots.it-simple.cn，安装包放 OSS，和 mac 的 appcast / DMG 一样分开放。
/// 频道按架构分：win-x64、win-arm64（打包时写进安装包，装好后自动用自己的频道）。
/// 开关存在 %APPDATA%\SimpleSmaller\updates.json。
internal sealed partial class Updates : ObservableObject
{
    public const string FeedUrl = "https://bots.it-simple.cn/downloads/smaller/win";
    public const string PackagesUrl = "https://itsimple-bot.oss-cn-shenzhen.aliyuncs.com/smaller/win";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static Updates? shared;
    public static Updates Shared => shared ??= new Updates();

    private readonly string filePath = Path.Combine(Storage.Directory, "updates.json");
    private readonly UpdateManager manager = new(new SplitSource());
    private CancellationTokenSource? timer;
    private bool busy;

    /// 设置里的「自动检查更新」。
    [ObservableProperty] private bool automaticallyChecks = true;

    /// 界面层弹「发现新版本」的询问：参数是版本号与更新说明（Markdown），返回用户是否现在更新。
    public Func<string, string, Task<bool>>? Ask { get; set; }

    private sealed class Saved
    {
        public bool AutomaticallyChecks { get; set; } = true;
    }

    private Updates()
    {
        if (Storage.Load<Saved>(filePath) is { } s) automaticallyChecks = s.AutomaticallyChecks;
        PropertyChanged += (_, e) =>
        {
            Storage.Save(filePath, new Saved { AutomaticallyChecks = AutomaticallyChecks });
            if (e.PropertyName == nameof(AutomaticallyChecks)) Schedule();
        };
    }

    /// 当前版本（没安装时，例如开发构建，返回程序集版本）。
    public string CurrentVersion => manager.CurrentVersion?.ToString()
        ?? typeof(Updates).Assembly.GetName().Version?.ToString(3) ?? "";

    /// 启动时调用：开着自动检查就排定后台检查（第一次在启动 1 分钟后）。
    public void Start() => Schedule();

    private void Schedule()
    {
        timer?.Cancel();
        if (!AutomaticallyChecks || !manager.IsInstalled) return;
        var cts = timer = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            var wait = TimeSpan.FromMinutes(1);
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(wait, cts.Token); } catch (TaskCanceledException) { return; }
                await Ui.RunAsync(() => _ = CheckAndOfferAsync(manual: false));
                wait = Interval;
            }
        });
    }

    /// 手动检查（设置里的「检查更新」）；返回给用户看的一句话。
    public Task<string> CheckAsync() => CheckAndOfferAsync(manual: true);

    private async Task<string> CheckAndOfferAsync(bool manual)
    {
        if (!manager.IsInstalled) return "开发版不检查更新，装好的正式版才会";
        if (busy) return "正在检查…";
        busy = true;
        try
        {
            var info = await Task.Run(() => manager.CheckForUpdatesAsync());
            if (info is null) return $"已是最新版本（{CurrentVersion}）";
            var target = info.TargetFullRelease;
            var version = target.Version.ToString();
            var notes = target.NotesMarkdown ?? "";
            if (Ask is null || !await Ask(version, notes)) return $"有新版本 {version}，没有安装";
            await Task.Run(() => manager.DownloadUpdatesAsync(info));
            // 处理中不打断：等这一批做完再重启安装。
            await WaitIdle();
            manager.ApplyUpdatesAndRestart(target);
            return $"正在更新到 {version}";
        }
        catch (Exception e)
        {
            Log.Error($"检查更新失败：{e.Message}");
            return manual ? $"检查更新失败：{e.Message}" : "";
        }
        finally
        {
            busy = false;
        }
    }

    private static async Task WaitIdle()
    {
        while (await Ui.RunAsync(() => MediaTypes.All.Any(t => AppModel.Shared.Workspace(t).Running)))
        {
            await Task.Delay(1000);
        }
    }

    /// 更新源读自家域名，安装包从 OSS 下：两边各是一个 SimpleWebSource。
    private sealed class SplitSource : IUpdateSource
    {
        private readonly SimpleWebSource feed = new(FeedUrl);
        private readonly SimpleWebSource packages = new(PackagesUrl);

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) =>
            feed.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default) =>
            packages.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
