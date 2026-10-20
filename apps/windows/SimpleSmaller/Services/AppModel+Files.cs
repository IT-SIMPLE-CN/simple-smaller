using SimpleSmaller.Core;

namespace SimpleSmaller;

/// 文件进出：拖入与分类、元信息与缩略图、加密 PDF 解锁、剪贴板、移除与清空、合并页序。
internal sealed partial class AppModel
{
    // MARK: 添加文件

    /// 拖入、选取、打开都走这里。文件夹按当前类型展开；文件按自身类型分到对应工作区。
    public void Add(IEnumerable<string> paths, MediaType? preferred = null)
    {
        var byType = new Dictionary<MediaType, List<FileItem>>();
        List<FileItem> Bucket(MediaType t) => byType.TryGetValue(t, out var l) ? l : byType[t] = [];
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var type = preferred ?? SelectedType;
                var files = Engine.ListFiles([path], type.EngineKind());
                if (files.Length == 0)
                {
                    // 当前类型里没有，就找文件夹里最多的那类。
                    var best = MediaTypes.All.Select(t => (t, list: Engine.ListFiles([path], t.EngineKind())))
                                             .MaxBy(x => x.list.Length);
                    if (best.list is { Length: > 0 })
                    {
                        type = best.t;
                        files = best.list;
                    }
                }
                Bucket(type).AddRange(files.Select(f => new FileItem(f, path, type)));
            }
            else if (File.Exists(path) && MediaTypes.Of(path) is { } type)
            {
                // 「图片转 PDF」时，图片进 PDF 工作区。
                if (type == MediaType.Image && SelectedType == MediaType.Pdf && Current.PdfSegment == PdfSegment.FromImages)
                    Bucket(MediaType.Pdf).Add(new FileItem(path, null, MediaType.Image));
                else
                    Bucket(type).Add(new FileItem(path, null, type));
            }
        }
        if (byType.Count == 0)
        {
            Flash("没有可以处理的图片、视频或 PDF");
            return;
        }
        foreach (var (type, items) in byType)
        {
            var ws = Workspace(type);
            var existing = ws.Files.Select(f => f.FilePath).ToHashSet(SimpleSmaller.Workspace.PathComparer);
            var fresh = items.Where(i => existing.Add(i.FilePath)).ToList();
            foreach (var f in fresh) ws.Files.Add(f);
            if (fresh.Count > 0) ws.ShowResult = false;
            foreach (var item in fresh) LoadMeta(item);
            if (type == MediaType.Pdf) RebuildPageOrder(ws);
        }
        if (!byType.ContainsKey(SelectedType)) SelectedType = byType.Keys.First();
        ShowHome = false;
    }

    /// 按分段名切到当前类型的某个分段（与工具栏分段切换效果相同）。
    public void OpenSegment(string name)
    {
        var ws = Current;
        switch (ws.Type)
        {
            case MediaType.Image:
                // 图片只有一页：「压缩」= 原比例；「裁切」= 上次用的比例（没用过是小红书 3:4）。
                ws.SetCrop(name == "裁切" ? Settings.LastCropPreset : null);
                break;
            case MediaType.Video:
                if (Segments.VideoSegmentNamed(name) is not { } vs) return;
                ws.VideoSegment = vs;
                ws.EnterVideoSegment(vs);
                break;
            case MediaType.Pdf:
                if (Segments.PdfSegmentNamed(name) is { } ps) { ws.PdfSegment = ps; ws.EnterPdfSegment(ps); }
                break;
        }
        ws.ShowResult = false;
    }

    /// 首页卡片上的小标签：直接进这一类的这个功能。
    public void Enter(MediaType type, string segment)
    {
        SelectedType = type;
        if (!Current.Running) OpenSegment(segment);
    }

    /// 文件放到首页卡片分出来的某一格：这一类的文件进那个功能，其余类型照旧按类型分流。
    public void AddInto(IEnumerable<string> paths, MediaType type, string segment)
    {
        Enter(type, segment);
        Add(paths, type);
        if (Workspace(type).Files.Count > 0) SelectedType = type;
    }

    // MARK: 加密的 PDF

    /// 解锁副本放这里；启动与退出时清空。
    public static string UnlockedDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleSmaller", "unlocked");

    /// 用密码解出一份不加密的副本，处理时代替原文件；输出照样放在原文件旁边。密码不对返回提示文字。
    public string? Unlock(FileItem item, string password)
    {
        var output = UnlockedCopyPath(item);
        if (UnlockCopy(Engine, item.FilePath, password, output) is { } message) return message;
        Unlocked(item, output);
        return null;
    }

    /// 同 Unlock，但引擎重写副本这一步在后台跑（大文件要几秒），界面线程只改状态。
    public async Task<string?> UnlockAsync(FileItem item, string password)
    {
        var output = UnlockedCopyPath(item);
        var engine = Engine;
        var source = item.FilePath;
        if (await Task.Run(() => UnlockCopy(engine, source, password, output)) is { } message) return message;
        Unlocked(item, output);
        return null;
    }

    private static string UnlockedCopyPath(FileItem item) =>
        Path.Combine(UnlockedDir, Guid.NewGuid().ToString().ToUpperInvariant(), Path.GetFileName(item.FilePath));

    private static string? UnlockCopy(Engine engine, string source, string password, string output)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            engine.UnlockPdf(source, password, output);
            return null;
        }
        catch (EngineException e)
        {
            return e.UserMessage();
        }
        catch
        {
            return "解不开这个文件";
        }
    }

    private void Unlocked(FileItem item, string output)
    {
        item.UnlockedPath = output;
        item.MetaError = null;
        item.Thumbnail = null;
        if (item.State.IsFailed) item.State = FileState.Waiting;
        LoadMeta(item);
    }

    public static void ClearUnlocked()
    {
        try { Directory.Delete(UnlockedDir, recursive: true); } catch { }
    }

    private static void DeleteUnlockedCopy(FileItem f)
    {
        if (f.UnlockedPath is { } u && Path.GetDirectoryName(u) is { } dir)
            try { Directory.Delete(dir, recursive: true); } catch { }
    }

    /// 从列表移除（Ctrl+Delete、Delete、右键「从列表移除」）：底部出「已移除 N 个文件 · 撤销」，5 秒内可以放回来。
    public void Remove(IReadOnlySet<Guid> ids, Workspace ws)
    {
        // 处理中不动列表：引擎按开始时的顺序回报结果。
        if (ws.Running) return;
        var gone = ws.Files.Where(f => ids.Contains(f.Id)).ToList();
        if (gone.Count == 0) return;
        var undo = RemovalUndo.Take(ws, $"已移除 {gone.Count} 个文件", gone, cleared: false);
        foreach (var f in gone) ws.Files.Remove(f);
        ws.SetSelection(ws.Selection.Where(id => !ids.Contains(id)));
        if (ws.Type == MediaType.Pdf) RebuildPageOrder(ws);
        OfferUndo(undo);
    }

    /// 文件卡右上「清空」（Ctrl+Shift+Delete）：一次清掉这一批，回到空状态；底部出「已清空 N 个文件 · 撤销」。
    public void ClearUndoable(Workspace ws)
    {
        if (ws.Running || ws.Files.Count == 0) return;
        var undo = RemovalUndo.Take(ws, $"已清空 {ws.Files.Count} 个文件", ws.Files.ToList(), cleared: true);
        Reset(ws);
        ws.CropEditing = false;
        OfferUndo(undo);
    }

    /// 再来一批：清空这一批的文件和所有跟文件相关的状态（与逐个移除一致）。完成页上用，不可撤销。
    public void Clear(Workspace ws)
    {
        if (ws.Running) return;
        // 先收掉这一类还挂着的撤销（里面的文件也一起作废）。
        if (PendingUndo is { } u && u.Ws == ws) DismissUndo();
        foreach (var f in ws.Files) DeleteUnlockedCopy(f);
        Reset(ws);
    }

    private static void Reset(Workspace ws)
    {
        ws.Files.Clear();
        ws.SetSelection([]);
        ws.ResetCrop();
        ws.VideoIndex = 0;
        ws.Trim = SimpleSmaller.Workspace.Empty<TimeRange>();
        ws.VideoCrop = SimpleSmaller.Workspace.Empty<PixelRect>();
        ws.CoverAt = SimpleSmaller.Workspace.Empty<double>();
        ws.LiveRange = SimpleSmaller.Workspace.Empty<TimeRange>();
        ws.LiveCover = SimpleSmaller.Workspace.Empty<double>();
        ws.PageOrder = [];
        ws.PageOrderFiles = [];
        ws.FinishedCount = 0;
        ws.LastRun = null;
        ws.Job = null;
        ws.BatchJobs = [];
        ws.CreatedRoots = [];
        ws.ShowResult = false;
    }

    // MARK: 清空 / 移除的撤销（设计稿 V3 · 11）

    /// 底部提示条对应的那一次清空或移除；为 null 时提示条不在，Ctrl+Z 不做撤销清空。
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private RemovalUndo? pendingUndo;

    /// 提示条停多久。
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(5);

    private void OfferUndo(RemovalUndo undo)
    {
        // 上一条还在：它就作废了（只撤最近一次）。
        if (PendingUndo is { } old) Discard(old);
        PendingUndo = undo;
        _ = ExpireLater(undo);
    }

    private async Task ExpireLater(RemovalUndo undo)
    {
        await Task.Delay(UndoWindow).ConfigureAwait(false);
        Ui.Post(() => { if (PendingUndo == undo) DismissUndo(); });
    }

    /// 提示条到时间或被别的操作顶掉：不再能撤，拿走的文件的解锁副本这时才删。
    public void DismissUndo()
    {
        if (PendingUndo is not { } u) return;
        PendingUndo = null;
        Discard(u);
    }

    private static void Discard(RemovalUndo u)
    {
        foreach (var f in u.Removed.Where(f => !u.Ws.Files.Contains(f))) DeleteUnlockedCopy(f);
    }

    /// 撤销清空 / 移除：文件、顺序、每张的裁切框 / 截取 / 封面、选中都放回去，回到那一类。
    public void UndoRemoval()
    {
        if (PendingUndo is not { } u || u.Ws.Running) return;
        PendingUndo = null;
        u.Restore();
        if (u.Ws.Type == MediaType.Pdf && u.Ws.Files.Count > u.Files.Count) RebuildPageOrder(u.Ws);
        if (ShowHome || SelectedType != u.Ws.Type) SelectedType = u.Ws.Type;
    }
    // MARK: 剪贴板

    private static readonly string ClipboardMark =
        $"{Path.DirectorySeparatorChar}简单小一点{Path.DirectorySeparatorChar}剪贴板-";

    /// 剪贴板里的图片存成临时 PNG 再处理；剪贴板里是文件就直接加。
    public async Task PasteFromClipboardAsync()
    {
        if (await ClipboardFilesAsync() is not { } paths) return;
        var fromImage = paths.Count == 1 && paths[0].Contains(ClipboardMark);
        if (fromImage) SelectedType = MediaType.Image;
        Add(paths, fromImage ? MediaType.Image : null);
    }

    /// 剪贴板里的文件；是图片数据时先存成「图片\简单小一点\剪贴板-时间.png」。没有可处理的返回 null 并提示。
    public async Task<IReadOnlyList<string>?> ClipboardFilesAsync()
    {
        var content = await Platform.ReadClipboardAsync();
        if (content?.Files is { Count: > 0 } files) return files;
        if (content?.Png is not { } png)
        {
            Flash("剪贴板里没有图片");
            return null;
        }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "简单小一点");
        var path = Path.Combine(dir, $"剪贴板-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        try
        {
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(path, png);
            return [path];
        }
        catch (Exception e)
        {
            Flash($"剪贴板图片存不下来：{e.Message}");
            return null;
        }
    }

    // MARK: 元信息与缩略图

    private static ulong FileSize(string path)
    {
        try { return (ulong)new FileInfo(path).Length; } catch { return 0; }
    }

    public void LoadMeta(FileItem item)
    {
        var engine = Engine;
        var path = item.FilePath;
        var unlocked = item.UnlockedPath;
        switch (item.Type)
        {
            case MediaType.Image:
                Task.Run(() =>
                {
                    // 只读文件头拿尺寸，不整张解码（30 张 6000 万像素的图同时整张解码要 6 GB 内存）。
                    var header = ImageHeader.Read(path, out var problem);
                    var size = header == null ? FileSize(path) : 0;
                    Ui.Post(() =>
                    {
                        if (header != null)
                        {
                            item.Width = header.Width; item.Height = header.Height; item.Bytes = header.Bytes;
                        }
                        else
                        {
                            item.MetaError = problem;
                            item.Bytes = size;
                        }
                    });
                });
                break;
            case MediaType.Video:
                Task.Run(() =>
                {
                    VideoMeta? m = null;
                    try { m = engine.VideoMeta(path); } catch { }
                    Ui.Post(() =>
                    {
                        if (m != null)
                        {
                            item.Width = (int)m.Width; item.Height = (int)m.Height; item.Duration = m.Duration;
                            item.Fps = m.Fps; item.Codec = m.Codec; item.HasAudio = m.HasAudio;
                            item.Hdr = m.Hdr; item.Bytes = m.Bytes;
                        }
                        else
                        {
                            item.MetaError = "读不出这段视频";
                        }
                    });
                });
                break;
            case MediaType.Pdf:
                Task.Run(() =>
                {
                    PdfMeta? m = null;
                    try { m = engine.PdfMeta(unlocked ?? path); } catch { }
                    var original = unlocked == null ? 0 : FileSize(path);
                    Ui.Post(() =>
                    {
                        if (m != null)
                        {
                            item.Pages = (int)m.Pages;
                            item.Bytes = unlocked == null ? m.Bytes : (original > 0 ? original : m.Bytes);
                            item.MetaError = m.NeedsPassword ? "有打开密码" : null;
                        }
                        else
                        {
                            item.MetaError = "读不出这个 PDF";
                        }
                        RebuildPageOrder(Workspace(MediaType.Pdf));
                    });
                });
                break;
        }
        _ = LoadThumbnailAsync(item);
    }

    private async Task LoadThumbnailAsync(FileItem item)
    {
        try
        {
            if (await Platform.LoadThumbnailAsync(item.PreviewPath, 96) is { } image)
                Ui.Post(() => item.Thumbnail = image);
        }
        catch { }
    }

    /// 合并的页面顺序跟着文件变：已经手排过的页保持原样，移除的文件去掉它的页，新文件的页接在后面。
    public void RebuildPageOrder(Workspace ws)
    {
        var pdfs = ws.Files.Where(f => f.Type == MediaType.Pdf).ToList();
        var paths = pdfs.Select(f => f.FilePath).ToList();
        var newIndex = new Dictionary<string, uint>(SimpleSmaller.Workspace.PathComparer);
        for (var i = 0; i < paths.Count; i++) newIndex.TryAdd(paths[i], (uint)i);
        var order = new List<PdfPageRef>();
        var seen = new HashSet<string>(SimpleSmaller.Workspace.PathComparer);
        foreach (var r in ws.PageOrder)
        {
            if (r.File >= ws.PageOrderFiles.Count) continue;
            var path = ws.PageOrderFiles[(int)r.File];
            if (!newIndex.TryGetValue(path, out var i)) continue;
            var f = pdfs.First(x => SimpleSmaller.Workspace.PathComparer.Equals(x.FilePath, path));
            if (r.Page >= f.Pages) continue;
            order.Add(new PdfPageRef(i, r.Page));
            seen.Add(path);
        }
        // 还没排进去的文件（新加的，或刚读出页数的）整份接在后面。
        for (var i = 0; i < pdfs.Count; i++)
        {
            var f = pdfs[i];
            if (seen.Contains(f.FilePath) || f.Pages <= 0) continue;
            for (var p = 0; p < f.Pages; p++) order.Add(new PdfPageRef((uint)i, (uint)p));
        }
        ws.PageOrderFiles = paths;
        ws.PageOrder = order;
    }
}
