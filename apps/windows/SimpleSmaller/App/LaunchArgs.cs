namespace SimpleSmaller;

/// 启动参数（验证用，也给资源管理器右键菜单用）：
/// --open <路径…>  加文件；--type 图片|视频|PDF；--segment <分段名>；--start 直接开始；
/// --appearance dark|light 强制深浅色（验证用）；--reset-guides 忘掉看过的教学（在 App.OnLaunched 里最先处理）。
internal static class LaunchArgs
{
    public static void Apply(string[] args)
    {
        var model = AppModel.Shared;
        var paths = new List<string>();
        MediaType? type = null;
        string? segment = null;
        var start = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--open":
                    while (i + 1 < args.Length && !args[i + 1].StartsWith("--")) paths.Add(args[++i]);
                    break;
                case "--type" when i + 1 < args.Length:
                {
                    // 先取出参数再比较：写在 lambda 里的 ++i 会对每个类型各加一次而越界。
                    var name = args[++i];
                    type = MediaTypes.All.Cast<MediaType?>().FirstOrDefault(t => t!.Value.Title() == name);
                }
                    break;
                case "--segment" when i + 1 < args.Length:
                    segment = args[++i];
                    break;
                case "--appearance" when i + 1 < args.Length:
                    App.Main?.SetAppearance(args[++i]);
                    break;
                case "--start":
                    start = true;
                    break;
            }
        }
        if (type is { } t) model.SelectedType = t;
        // 指定了类型时先切分段再加文件：「--type PDF --segment 图片转 PDF --open 图片…」图片才进 PDF 工作区。
        if (type is not null && segment is not null) model.OpenSegment(segment);
        if (paths.Count > 0) model.Add(paths, type);
        if (type is null && segment is not null) model.OpenSegment(segment);
        if (start)
        {
            // 文件元信息是异步读的，稍等再开始。
            _ = Task.Delay(1500).ContinueWith(_ => Ui.Post(() =>
            {
                var ws = model.Current;
                if (ws.CanStart && ws.Files.Count > 0) model.Start(ws);
            }));
        }
    }
}
