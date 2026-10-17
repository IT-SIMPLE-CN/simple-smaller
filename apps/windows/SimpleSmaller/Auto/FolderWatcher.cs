namespace SimpleSmaller.Auto;

/// 监视文件夹（设置 → 自动）：放进去的新文件按这个文件夹的配方自动处理，结果放在旁边。
/// 只看文件夹第一层；隐藏文件、之前的输出（-small、-长图 等）、不是这个配方那一类的都跳过。
/// 新文件要等大小稳定（截图、下载、录屏写完）再处理；处理完发系统通知。
internal static class FolderWatcher
{
    private static readonly List<FileSystemWatcher> watchers = [];
    /// 已经处理或排队过的文件（路径 + 修改时间），同一个文件不重复处理。
    private static readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (long Size, int Checks)> pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<QuickRun> runs = [];
    private static Microsoft.UI.Dispatching.DispatcherQueueTimer? settleTimer;
    private static bool registered;

    public static bool HasRunning => runs.Any(r => !r.IsFinished);

    public static void Refresh()
    {
        if (!registered)
        {
            registered = true;
            AppModel.Shared.OtherBusy.Add(() => HasRunning);
        }
        Stop();
        var folders = AppSettings.Shared.WatchFolders.Where(f => Directory.Exists(f.Path)).ToList();
        if (folders.Count == 0) return;
        foreach (var f in folders)
        {
            // 开始监视时已经在里面的文件不处理，只处理之后放进来的。
            foreach (var file in Files(f.Path)) seen.Add(Key(file));
            try
            {
                var w = new FileSystemWatcher(f.Path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                w.Created += (_, e) => Ui.Post(() => Changed(e.FullPath));
                w.Changed += (_, e) => Ui.Post(() => Changed(e.FullPath));
                w.Renamed += (_, e) => Ui.Post(() => Changed(e.FullPath));
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception e)
            {
                Log.Error($"监视 {f.Path}：{e.Message}");
            }
        }
    }

    private static void Stop()
    {
        foreach (var w in watchers) w.Dispose();
        watchers.Clear();
        settleTimer?.Stop();
        pending.Clear();
    }

    private static string Folder(string path) => Path.GetFullPath(Path.GetDirectoryName(path) ?? "").TrimEnd('\\');

    private static void Changed(string path)
    {
        var folders = AppSettings.Shared.WatchFolders.Select(f => Path.GetFullPath(f.Path).TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!folders.Contains(Folder(path)) || !Eligible(path) || seen.Contains(Key(path))) return;
        if (!pending.ContainsKey(path)) pending[path] = (-1, 0);
        if (settleTimer is null)
        {
            settleTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            settleTimer.Interval = TimeSpan.FromSeconds(1);
            settleTimer.Tick += (_, _) => Settle();
        }
        if (!settleTimer.IsRunning) settleTimer.Start();
    }

    /// 这个文件夹有任务在跑：期间新出现的文件先不认（可能是自己的输出），等任务结束再判断。
    private static bool Busy(string folder) =>
        runs.Any(r => !r.IsFinished && r.Paths.Count > 0 && string.Equals(Folder(r.Paths[0]), folder, StringComparison.OrdinalIgnoreCase));

    /// 每秒看一次大小，连续两次不变才算写完。
    private static void Settle()
    {
        var ready = new List<string>();
        foreach (var (path, state) in pending.ToList())
        {
            if (Busy(Folder(path))) continue;
            long size;
            try { size = new FileInfo(path).Length; }
            catch { pending.Remove(path); continue; }
            if (size == state.Size && size > 0 && !Locked(path)) { ready.Add(path); pending.Remove(path); }
            else if (state.Checks > 120) pending.Remove(path);
            else pending[path] = (size, state.Checks + 1);
        }
        if (pending.Count == 0) settleTimer?.Stop();
        // 等待期间可能已经认出是自己的输出（处理完会记进 seen）。
        ready.RemoveAll(p => seen.Contains(Key(p)));
        if (ready.Count == 0) return;
        foreach (var p in ready) seen.Add(Key(p));
        foreach (var group in ready.GroupBy(Folder, StringComparer.OrdinalIgnoreCase))
        {
            var folder = group.Key;
            var watch = AppSettings.Shared.WatchFolders.FirstOrDefault(w => string.Equals(Path.GetFullPath(w.Path).TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase));
            if (watch is null || AppModel.Shared.Store.RecipeById(watch.RecipeId) is not { } recipe) continue;
            var matching = group.Where(p => MediaTypes.Of(p) == recipe.Type).ToList();
            if (matching.Count == 0) continue;
            var run = new QuickRun(recipe, matching, recipe.Type);
            // 每写出一个结果就记下，哪怕命名里没有 -small 也不会被当成新文件。
            run.OnOutput = p => seen.Add(Key(p));
            run.OnFinished = () =>
            {
                // 结果写在同一个文件夹里，记下来不再当新文件处理。
                foreach (var o in run.Outputs) seen.Add(Key(o));
                AppModel.Shared.NoteFinishedInBackground(run.Outputs.Count == 0 ? 0 : matching.Count);
                AppModel.Shared.EndActivityIfIdle();
                Notifier.WatchFinished(Path.GetFileName(folder), run);
            };
            AppModel.Shared.BeginActivity();
            run.Start();
            runs.RemoveAll(r => r.IsFinished);
            runs.Add(run);
        }
    }

    /// 别的程序还开着写（下载、录屏）时打不开独占读。
    private static bool Locked(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string Key(string path)
    {
        long m = 0;
        try { m = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds(); } catch { }
        return $"{Path.GetFullPath(path)}|{m}";
    }

    /// 是图片、视频或 PDF，不是隐藏文件、不是之前的输出、不是正在下载的临时文件。
    private static bool Eligible(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith('.') || name.StartsWith("~$")) return false;
        try
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
        }
        catch
        {
            return false;
        }
        var stem = Path.GetFileNameWithoutExtension(path);
        if (new[] { "-small", "-长图", "-cover" }.Any(s => stem.EndsWith(s, StringComparison.OrdinalIgnoreCase))) return false;
        return MediaTypes.Of(path) != null;
    }

    private static IEnumerable<string> Files(string dir)
    {
        try { return Directory.EnumerateFiles(dir).ToList(); } catch { return []; }
    }
}
