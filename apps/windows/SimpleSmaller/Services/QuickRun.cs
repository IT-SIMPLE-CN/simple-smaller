using CommunityToolkit.Mvvm.ComponentModel;
using uniffi.smaller_core;

namespace SimpleSmaller;

internal enum QuickRunPhaseKind { Running, Finished, Failed }

/// 进行到哪：running 带「做完几个 / 共几个」，failed 带原因。
internal readonly record struct QuickRunPhase(QuickRunPhaseKind Kind, int Done = 0, int Total = 0, string? Message = null)
{
    public static QuickRunPhase Running(int done, int total) => new(QuickRunPhaseKind.Running, done, total);
    public static readonly QuickRunPhase Finished = new(QuickRunPhaseKind.Finished);
    public static QuickRunPhase Failed(string message) => new(QuickRunPhaseKind.Failed, 0, 0, message);
}

/// 不进主窗口、直接按一个配方处理一批文件（圆环与右键菜单共用）。
/// 图片、PDF 一个任务跑完；视频逐段跑。结果写出后可在资源管理器中显示、拷贝、撤销。
internal sealed partial class QuickRun : ObservableObject
{
    public Recipe Recipe { get; }
    public IReadOnlyList<string> Paths { get; }
    public MediaType Type { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsFinished))] private QuickRunPhase phase;
    [ObservableProperty] private ulong before;
    [ObservableProperty] private ulong after;
    [ObservableProperty] private IReadOnlyList<string> outputs = [];
    [ObservableProperty] private int kept;
    public List<string> Failures { get; } = [];
    private readonly List<Job> jobs = [];
    private bool cancelled;
    public Action? OnFinished { get; set; }
    /// 每写出一个文件回调一次（监视文件夹用来避开自己的输出）。
    public Action<string>? OnOutput { get; set; }

    public QuickRun(Recipe recipe, IReadOnlyList<string> paths, MediaType type)
    {
        Recipe = recipe;
        Paths = paths;
        Type = type;
        phase = QuickRunPhase.Running(0, paths.Count);
    }

    /// 「4 张图片」「teaser.mov」。
    public string Title
    {
        get
        {
            if (Paths.Count == 1) return Path.GetFileName(Paths[0]);
            return $"{Paths.Count} {(Type == MediaType.Image ? "张图片" : Type == MediaType.Video ? "段视频" : "个 PDF")}";
        }
    }

    public bool IsFinished => Phase.Kind != QuickRunPhaseKind.Running;

    public void Start()
    {
        var model = AppModel.Shared;
        var engine = model.Engine;
        var place = model.Settings.Place;
        var naming = Recipe.Naming;
        if (Type == MediaType.Video && Recipe.Video.OutputFrames) naming = naming with { Template = "{name}" };
        if (Type == MediaType.Video && Recipe.Video.OutputLive) naming = naming with { Template = "{name}-live" };
        if (Type == MediaType.Pdf && Recipe.Pdf.LongImage) naming = naming with { Template = "{name}-长图" };
        var ffiNaming = naming.Ffi(Format.Today);
        var inputs = Paths.ToArray();
        Before = Paths.Aggregate(0UL, (s, p) => s + SizeOf(p));
        var bridge = new ListenerBridge(Ui.Post, (_, _) => { }, Record, Finish);
        switch (Type)
        {
            case MediaType.Image:
                jobs.Add(engine.RunImages(inputs, place, ffiNaming, Recipe.ImageSettings(), bridge));
                break;
            case MediaType.Pdf:
            {
                PdfOperation operation = Recipe.Pdf.LongImage
                    ? new PdfOperation.ToImages((uint)Recipe.Pdf.ImageWidth, Recipe.Pdf.ImageFormat.Ffi(), (byte)Math.Clamp(Recipe.Pdf.ImageQuality, 0, 255), true)
                    : new PdfOperation.Compress(Recipe.PdfLevel(), Recipe.PdfTargetBytes(), true, Recipe.Pdf.Grayscale);
                jobs.Add(engine.RunPdfs(inputs, place, ffiNaming, new PdfSettings(operation, Recipe.StripMetadata, null), bridge));
                break;
            }
            case MediaType.Video:
                RunVideos(inputs, place, ffiNaming);
                break;
        }
    }

    /// 视频逐段：先读尺寸算比例框（居中取最大），再起任务；一段完成再起下一段。
    private void RunVideos(string[] inputs, Place place, Naming naming)
    {
        var engine = AppModel.Shared.Engine;
        var recipe = Recipe;
        Task.Run(async () =>
        {
            foreach (var path in inputs)
            {
                if (await Ui.RunAsync(() => cancelled)) break;
                VideoMeta? meta = null;
                try { meta = engine.VideoMeta(path); } catch { }
                PixelRect? crop = null;
                if (meta != null && recipe.Video.Crop?.Aspect is { } a)
                    crop = PixelRect.CenteredMax(meta.Width, meta.Height, a, PixelRect.Even);
                var long4K = meta != null && Math.Min(meta.Width, meta.Height) >= 2000 && meta.Duration > 180;
                var settings = recipe.VideoSettings(null, crop, recipe.Video.ExportCover || recipe.Video.OutputLive ? Math.Min(1, (meta?.Duration ?? 2) / 2) : null, long4K);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var bridge = new ListenerBridge(Ui.Post, (_, _) => { }, Record, () => done.TrySetResult());
                var job = engine.RunVideos([path], place, naming, settings, bridge);
                await Ui.RunAsync(() =>
                {
                    jobs.Add(job);
                    if (cancelled) job.Cancel();
                });
                await done.Task;
            }
            await Ui.RunAsync(Finish);
        });
    }

    private void Record(JobItemResult r)
    {
        switch (r.State)
        {
            case ItemState.Done:
            {
                var fresh = r.Outputs.Select(o => o.Path).ToList();
                Outputs = Outputs.Concat(fresh).ToList();
                foreach (var p in fresh) OnOutput?.Invoke(p);
                After += r.Outputs.Aggregate(0UL, (s, o) => s + o.Bytes);
                break;
            }
            case ItemState.KeptOriginal:
                Kept += 1;
                After += r.BeforeBytes;
                break;
            case ItemState.Failed:
                Failures.Add(r.Message ?? "失败");
                break;
            case ItemState.Cancelled:
                break;
        }
        if (Phase.Kind == QuickRunPhaseKind.Running)
            Phase = QuickRunPhase.Running(Math.Min(Phase.Total, Phase.Done + 1), Phase.Total);
    }

    private void Finish()
    {
        if (IsFinished) return;
        if (Outputs.Count == 0 && Kept == 0 && Failures.Count > 0)
        {
            Phase = QuickRunPhase.Failed(Failures[0]);
        }
        else
        {
            Phase = QuickRunPhase.Finished;
            var model = AppModel.Shared;
            var job = new RecentJob { Title = Title, Recipe = Recipe.Name, Before = Before, After = After, Date = DateTime.Now, Outputs = Outputs };
            model.Recent.Insert(0, job);
            model.LoadRecentThumbnail(job.Id, Paths.FirstOrDefault());
            model.TrimRecent();
            Notifier.JobFinished(Title, After, Outputs);
        }
        OnFinished?.Invoke();
    }

    public void Cancel()
    {
        cancelled = true;
        foreach (var j in jobs) j.Cancel();
    }

    /// 撤销：新文件移到回收站（原文件从不改动）。
    public void Undo()
    {
        foreach (var job in jobs) job.Undo();
        Outputs = [];
    }

    public void Reveal() => AppModel.Shared.Platform.Reveal(Outputs);

    /// 拷贝新文件，可以直接粘贴进聊天窗口或资源管理器。
    public Task CopyAsync() => AppModel.Shared.Platform.CopyFilesAsync(Outputs);

    private static ulong SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return (ulong)new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            ulong total = 0;
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = true };
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", options))
                if (MediaTypes.Of(f.FullName) != null) total += (ulong)f.Length;
            return total;
        }
        catch
        {
            return 0;
        }
    }
}
