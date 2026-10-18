using uniffi.smaller_core;

namespace SimpleSmaller;

/// 一批的处理：开始、按组 / 逐段跑引擎、进度与结果、完成、取消、重试、撤销。
internal sealed partial class AppModel
{
    // MARK: 开始、取消、撤销

    /// 开始处理这一批；`retrying` 给出时只重做这几个（失败重试），其余结果保留。
    public void Start(Workspace ws, IReadOnlyList<FileItem>? retrying = null)
    {
        if (ws.Running || ws.Files.Count == 0) return;
        if (ws.Type == MediaType.Pdf && ws.PdfSegment != PdfSegment.ToImages && ws.OutputPasswordOn && string.IsNullOrWhiteSpace(ws.OutputPassword))
        {
            Flash("先设好打开密码，或者关掉「输出加打开密码」");
            return;
        }
        // 开始处理后「撤销清空 / 移除」就不再提供（不往跑着或跑完的一批里塞回文件）。
        if (PendingUndo is { } pending && pending.Ws == ws) DismissUndo();
        ws.LastRun = new LastRun(ws.Draft, ws.SegmentKey);
        Log.Info($"开始 {ws.Type.Title()}：{ws.Files.Count} 个文件，配方「{ws.Draft.Name}」");
        var items = retrying ?? ws.Files.ToList();
        foreach (var f in items) { f.State = FileState.Waiting; f.Outputs = []; }
        ws.Running = true;
        ws.ShowResult = false;
        // 每一轮一个编号：取消后马上重开，上一轮的后台循环与回调都认得出自己已经过期。
        ws.RunGeneration += 1;
        var gen = ws.RunGeneration;
        ws.RunItemIds = items.Select(i => i.Id).ToHashSet();
        ws.RebuildPathIndex();
        BeginActivity();
        if (retrying == null)
        {
            ws.FinishedCount = 0;
            ws.BatchJobs = [];
        }
        else
        {
            ws.FinishedCount = ws.Files.Count(f => f.State.IsDoneOrKept);
        }
        var namingSettings = ws.Draft.Naming;
        // 序列帧按「{name}_frames/{name}_0001」命名（设计稿 04b），不套用压缩时的后缀。
        if (ws.Type == MediaType.Video && ws.Draft.Video.OutputFrames) namingSettings = namingSettings with { Template = "{name}" };
        // 实况：苹果实况一对同名（{name}-live.jpg / .mov），动态照片叫 {name}-live.MP.jpg。
        if (ws.Type == MediaType.Video && ws.Draft.Video.OutputLive) namingSettings = namingSettings with { Template = "{name}-live" };
        // 拆分放进「原名-拆分」文件夹，里面按「原名-01」编号。
        if (ws.Type == MediaType.Pdf && ws.PdfSegment == PdfSegment.Split) namingSettings = namingSettings with { Template = "{name}" };
        // 转图片：逐页放进「原名-图片」，长图叫「原名-长图」。
        if (ws.Type == MediaType.Pdf && ws.PdfSegment == PdfSegment.ToImages)
            namingSettings = namingSettings with { Template = ws.Draft.Pdf.LongImage ? "{name}-长图" : "{name}" };
        var naming = namingSettings.Ffi(Format.Today);
        var place = Settings.Place;
        var listener = new ListenerBridge(
            Ui.Post,
            (index, fraction) =>
            {
                if (ws.RunGeneration != gen) return;
                Progress(ws, (int)index, fraction);
            },
            r =>
            {
                if (ws.RunGeneration != gen) return;
                Finish(ws, r);
            },
            () =>
            {
                if (ws.RunGeneration != gen) return;
                Finished(ws);
            });
        var recipe = ws.Draft;
        var merging = ws.Type == MediaType.Pdf && ws.PdfSegment is PdfSegment.Merge or PdfSegment.FromImages;
        // 每个文件该去的输出目录（拖入的文件夹照样进「文件夹-small」）。重试和视频逐段都靠它。
        if (retrying == null && !merging)
        {
            var targets = Engine.PlanTargets(ws.EngineInputs, ws.Type.EngineKind(), place);
            var dirs = new Dictionary<string, string>(SimpleSmaller.Workspace.PathComparer);
            foreach (var t in targets.Items) dirs.TryAdd(t.Source, t.OutDir);
            foreach (var f in ws.Files) f.OutDir = dirs.TryGetValue(f.FilePath, out var d) ? d : null;
            ws.CreatedRoots = targets.CreatedRoots;
        }

        // 失败重试：按原本的输出目录分组，逐组跑，结果落回同一个地方。
        if (retrying != null && ws.Type != MediaType.Video && !merging)
        {
            RunGroups(ws, Group(retrying, place), naming, recipe, listener);
            return;
        }

        switch (ws.Type)
        {
            case MediaType.Image:
                ws.PlanSources = Engine.ListFiles(ws.EngineInputs, Kind.Image);
                ws.Job = Engine.RunImages(ws.EngineInputs, place, naming, recipe.ImageSettings(ws.CropBoxesForEngine), listener);
                ws.BatchJobs = ws.Job != null ? [ws.Job] : [];
                break;
            case MediaType.Video:
                // 剪辑里设的截取、裁切与封面按每段视频各自生效：逐段起任务，各自送到原本的输出目录。
                RunVideos(ws, items, recipe, place, naming, listener);
                break;
            case MediaType.Pdf:
            {
                var pdfs = ws.Files.Where(f => f.Type == MediaType.Pdf).ToList();
                // 解锁过的文件用副本代替；输出照样回到原文件该去的地方。
                if (!merging && pdfs.Any(f => f.UnlockedPath != null))
                {
                    RunGroups(ws, Group(items.Where(f => f.Type == MediaType.Pdf), place), naming, recipe, listener);
                    return;
                }
                string[] inputs = ws.PdfSegment switch
                {
                    PdfSegment.FromImages => ws.Files.Where(f => f.Type == MediaType.Image).Select(f => f.FilePath).ToArray(),
                    // 合并按页面顺序引用「第几个 PDF」，输入必须正好是列表里的 PDF 文件。
                    PdfSegment.Merge => pdfs.Select(f => f.UnlockedPath ?? f.FilePath).ToArray(),
                    _ => ws.EngineInputs,
                };
                ws.PlanSources = merging ? inputs : Engine.ListFiles(inputs, Kind.Pdf);
                // 合并、图片转 PDF 的结果放在第一个文件的位置；第一个可能是解锁出的临时副本，按原文件的位置算。
                var target = place;
                var first = ws.PdfSegment == PdfSegment.FromImages ? ws.Files.FirstOrDefault(f => f.Type == MediaType.Image) : pdfs.FirstOrDefault();
                if (merging && Settings.OutputLocation == OutputLocation.Beside && first != null)
                    target = new Place.Folder(Path.GetDirectoryName(first.FilePath) ?? "");
                ws.Job = Engine.RunPdfs(inputs, target, naming, PdfSettings(ws, recipe), listener);
                ws.BatchJobs = ws.Job != null ? [ws.Job] : [];
                break;
            }
        }
    }

    /// 按原本的输出目录分组；没有记下目录的用当前的存放位置。
    private static List<(string[] Paths, Place Place)> Group(IEnumerable<FileItem> items, Place place) =>
        items.GroupBy(f => f.OutDir ?? "", SimpleSmaller.Workspace.PathComparer)
             .Select(g => (g.Select(f => f.UnlockedPath ?? f.FilePath).ToArray(),
                           g.Key.Length == 0 ? place : (Place)new Place.Folder(g.Key)))
             .ToList();

    public PdfSettings PdfSettings(Workspace ws, Recipe recipe)
    {
        PdfOperation operation = ws.PdfSegment switch
        {
            PdfSegment.Compress => new PdfOperation.Compress(recipe.PdfLevel(), recipe.PdfTargetBytes(), true, recipe.Pdf.Grayscale),
            PdfSegment.Merge => new PdfOperation.Assemble(ws.PageOrder.ToArray()),
            PdfSegment.Split => new PdfOperation.Split((uint)Math.Max(1, recipe.Pdf.SplitEvery)),
            PdfSegment.ToImages => new PdfOperation.ToImages((uint)recipe.Pdf.ImageWidth, recipe.Pdf.ImageFormat.Ffi(),
                                                             (byte)Math.Clamp(recipe.Pdf.ImageQuality, 0, 255), recipe.Pdf.LongImage),
            _ => new PdfOperation.FromImages(recipe.Pdf.PageMode == PdfPageModeChoice.A4 ? PdfPageMode.A4 : PdfPageMode.FitImage),
        };
        // 转图片不出 PDF，不加密码。
        var pw = ws.PdfSegment == PdfSegment.ToImages || ws.OutputPassword.Length == 0 ? null : ws.OutputPassword;
        return new PdfSettings(operation, recipe.StripMetadata, pw);
    }

    /// 失败的重试一遍（配方可以先改）。
    public void RetryFailed(Workspace ws)
    {
        var failed = ws.Files.Where(f => f.State.IsFailed).ToList();
        if (failed.Count == 0) return;
        // 文件可能已经换好了：重读一次尺寸与错误。
        foreach (var f in failed) { f.MetaError = null; LoadMeta(f); }
        Start(ws, failed);
    }

    /// 等一个任务回报「完成」：新建的回调桥把完成信号接到这里。
    private static (ListenerBridge Bridge, Task Done) Waiter(Action<uint, double> progress, Action<JobItemResult> item)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return (new ListenerBridge(Ui.Post, progress, item, () => done.TrySetResult()), done.Task);
    }

    /// 按组逐个起任务（图片、PDF 的重试）；每组的进度按它自己的文件顺序对回列表。
    public void RunGroups(Workspace ws, List<(string[] Paths, Place Place)> groups, Naming naming, Recipe recipe, ListenerBridge listener)
    {
        var gen = ws.RunGeneration;
        var engine = Engine;
        var imageSettings = recipe.ImageSettings(ws.CropBoxesForEngine);
        var pdf = PdfSettings(ws, recipe);
        var type = ws.Type;
        Task.Run(async () =>
        {
            foreach (var group in groups)
            {
                var (bridge, done) = Waiter(
                    (i, f) =>
                    {
                        if (i < group.Paths.Length && ws.Item(group.Paths[i]) is { } item && item.State != FileState.Done)
                            item.State = FileState.Running(f);
                    },
                    r => listener.Item(r));
                var job = type == MediaType.Image
                    ? engine.RunImages(group.Paths, group.Place, naming, imageSettings, bridge)
                    : engine.RunPdfs(group.Paths, group.Place, naming, pdf, bridge);
                await Ui.RunAsync(() =>
                {
                    ws.Job = job;
                    ws.BatchJobs.Add(job);
                    // 起任务的这一会儿用户可能已经点了停止。
                    if (!ws.Running || ws.RunGeneration != gen) job.Cancel();
                });
                await done;
                if (await Ui.RunAsync(() => !ws.Running || ws.RunGeneration != gen)) break;
            }
            await Ui.RunAsync(() => listener.Finished());
        });
    }

    public void RunVideos(Workspace ws, IReadOnlyList<FileItem> items, Recipe recipe, Place place, Naming naming, ListenerBridge listener)
    {
        var gen = ws.RunGeneration;
        var engine = Engine;
        // 视频逐段跑；每段一个任务，用同一个回调汇总。
        var live = recipe.Video.OutputLive;
        var jobs = items.Select(item =>
        {
            var range = ws.TrimRange(item);
            // 封面没单击指定时，取截取开头往后 1 秒（片头常是黑场）。实况用自己的片段与封面。
            var cover = ws.CoverAt.TryGetValue(item.FilePath, out var c) ? c : range.Start + Math.Min(1, item.Duration / 2);
            return (
                Path: item.FilePath,
                Index: Math.Max(0, ws.Files.IndexOf(item)),
                Place: item.OutDir is { } dir ? new Place.Folder(dir) : place,
                Settings: live
                    ? recipe.VideoSettings(ws.LiveWindow(item), ws.VideoCropBox(item), ws.LiveCoverTime(item))
                    : recipe.VideoSettings(
                        ws.Trim.TryGetValue(item.FilePath, out var t) ? t : null,
                        ws.VideoCropBox(item),
                        cover,
                        Math.Min(item.Width, item.Height) >= 2000 && range.End - range.Start > 180));
        }).ToList();
        Task.Run(async () =>
        {
            foreach (var j in jobs)
            {
                var (bridge, done) = Waiter(
                    (_, f) => listener.Progress((uint)j.Index, f),
                    r => listener.Item(r with { Index = (uint)j.Index }));
                var job = engine.RunVideos([j.Path], j.Place, naming, j.Settings, bridge);
                await Ui.RunAsync(() =>
                {
                    ws.Job = job;
                    ws.BatchJobs.Add(job);
                    // 起任务的这一会儿用户可能已经点了停止。
                    if (!ws.Running || ws.RunGeneration != gen) job.Cancel();
                });
                await done;
                if (await Ui.RunAsync(() => !ws.Running || ws.RunGeneration != gen)) break;
            }
            await Ui.RunAsync(() => listener.Finished());
        });
    }

    public void Cancel(Workspace ws)
    {
        ws.Job?.Cancel();
        foreach (var j in ws.BatchJobs) j.Cancel();
        ws.Running = false;
        // 换一个编号：引擎之后迟到的回调（含「完成」）都作废，停掉的一批不写进最近记录。
        ws.RunGeneration += 1;
        foreach (var f in ws.Files) if (f.State.IsRunning) f.State = FileState.Cancelled;
        EndActivityIfIdle();
    }

    /// 最近记录的缩略图：用系统缩略图（视频、PDF 也有）。
    public async void LoadRecentThumbnail(Guid id, string? path)
    {
        if (path == null) return;
        try
        {
            if (await Platform.LoadThumbnailAsync(path, 64) is not { } image) return;
            Ui.Post(() =>
            {
                if (Recent.FirstOrDefault(r => r.Id == id) is { } job) job.Thumbnail = image;
            });
        }
        catch { }
    }

    public void Undo(Workspace ws)
    {
        var files = ws.Files.SelectMany(f => f.Outputs.Select(o => o.Path)).ToList();
        try
        {
            // 视频逐段各起一个任务，撤销要把这一批的每个任务都撤掉。
            var jobs = ws.BatchJobs.Count == 0 ? (ws.Job != null ? [ws.Job] : new List<Job>()) : ws.BatchJobs;
            foreach (var job in jobs) job.Undo();
            // 视频逐段按文件夹送出，新建的「文件夹-small」要另外清掉。
            if (ws.Type == MediaType.Video) Engine.Trash(ws.CreatedRoots);
            foreach (var f in ws.Files.Where(f => f.State.IsDoneOrKept)) { f.State = FileState.Waiting; f.Outputs = []; }
            // 回到处理前：不再显示上一批的结果。
            ws.FinishedCount = 0;
            ws.Job = null;
            ws.BatchJobs = [];
            ws.CreatedRoots = [];
            ws.ShowResult = false;
            Flash($"已撤销，{files.Count} 个新文件移到了回收站");
        }
        catch (Exception e)
        {
            Flash($"撤销失败：{e.UserMessage()}");
        }
    }

    public void Progress(Workspace ws, int index, double fraction)
    {
        if (index >= ws.Files.Count) return;
        var item = IndexItem(ws, index);
        if (item == null || item.State == FileState.Done) return;
        item.State = FileState.Running(fraction);
    }

    /// 引擎按输入展开后的顺序回报；图片与 PDF 用源路径对回列表。
    public FileItem? IndexItem(Workspace ws, int index) => ws.Type switch
    {
        MediaType.Video => index < ws.Files.Count ? ws.Files[index] : null,
        _ => index < ws.PlanSources.Length ? ws.Item(ws.PlanSources[index]) : null,
    };

    public void Finish(Workspace ws, JobItemResult r)
    {
        var item = ws.Type == MediaType.Video ? IndexItem(ws, (int)r.Index) : (ws.Item(r.Source) ?? IndexItem(ws, (int)r.Index));
        if (item == null) return;
        var wasFinished = item.State.IsFinished;
        item.Outputs = r.Outputs;
        if (r.State != ItemState.Failed) Log.Info($"{item.Name}：{r.State}，{r.Outputs.Length} 个输出");
        switch (r.State)
        {
            case ItemState.Done: item.State = FileState.Done; break;
            case ItemState.KeptOriginal: item.State = FileState.Kept; break;
            case ItemState.Failed:
                item.State = FileState.Failed(r.Message ?? "失败");
                Log.Error($"处理失败 {item.Name}：{r.Message ?? ""}");
                break;
            case ItemState.Cancelled: item.State = FileState.Cancelled; break;
        }
        // 合并、图片转 PDF 只出一个结果：其余文件也算完成。
        if (ws.Type == MediaType.Pdf && ws.PdfSegment is PdfSegment.Merge or PdfSegment.FromImages)
        {
            foreach (var f in ws.Files.Where(f => f.Id != item.Id)) f.State = item.State;
            ws.FinishedCount = ws.Files.Count(f => f.State.IsFinished);
        }
        else if (!wasFinished && item.State.IsFinished)
        {
            // 逐个累加，不每次全表重数（几百个文件时不再是 n²）。
            ws.FinishedCount += 1;
        }
        UpdateDock();
    }

    public void Finished(Workspace ws)
    {
        ws.Running = false;
        // 有结果（完成、保留原图或失败）就换到完成页；全部停掉的不换。
        if (ws.Files.Any(f => f.State != FileState.Cancelled && f.State != FileState.Waiting)) ws.ShowResult = true;
        NoteFinishedInBackground(ws.Files.Count(f => ws.RunItemIds.Contains(f.Id) && f.State == FileState.Done));
        EndActivityIfIdle();
        // 只统计处理成功的（失败的不算进前后体积）。
        var ok = ws.Files.Where(f => f.State.IsDoneOrKept).ToList();
        var before = ok.Aggregate(0UL, (s, f) => s + f.Bytes);
        var after = ok.Aggregate(0UL, (s, f) => s + (f.State == FileState.Done ? f.AfterBytes : f.Bytes));
        var title = ws.Files.Count == 1
            ? ws.Files[0].Name
            : $"{ws.Files.Count} {(ws.Type == MediaType.Image ? "张图片" : ws.Type == MediaType.Video ? "段视频" : "个文件")}";
        var outputs = ws.Files.SelectMany(f => f.Outputs.Select(o => o.Path)).ToList();
        Recent.Insert(0, new RecentJob
        {
            Title = title, Recipe = ws.Draft.Name, Before = before, After = after, Date = DateTime.Now, Outputs = outputs,
            Thumbnail = ws.Files.FirstOrDefault()?.Thumbnail,
        });
        TrimRecent();
        Notifier.JobFinished(title, after, outputs);
    }

    /// 最近记录只留 10 条。
    public void TrimRecent()
    {
        while (Recent.Count > 10) Recent.RemoveAt(Recent.Count - 1);
    }
}
