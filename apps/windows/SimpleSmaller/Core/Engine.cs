using System.Text.Json;

namespace SimpleSmaller.Core;

/// 引擎入口：接口与 mac 端（UniFFI 生成的 Engine）一致，实际经 EngineHost 调独立进程 smaller-core.exe。
/// 可以在任意线程调用；处理任务（Run*）立即返回，进度与结果经 JobListener 回报。
internal sealed class Engine
{
    private readonly EngineHost host = EngineHost.Shared;

    /// 展开输入（含文件夹）后会处理多少个文件、跳过哪些。
    public PlanSummary Plan(string[] inputs, Kind kind, Place place) =>
        Safe(() => host.Call<PlanSummary>("plan", new { inputs, kind, place }), new PlanSummary(0, []));

    /// 展开输入后每个文件的输出目录，以及会新建的输出根目录。
    public PlanTargets PlanTargets(string[] inputs, Kind kind, Place place) =>
        Safe(() => host.Call<PlanTargets>("plan_targets", new { inputs, kind, place }), new PlanTargets([], []));

    /// 展开输入后实际会处理的文件（与任务用同一套规则）。
    public string[] ListFiles(string[] inputs, Kind kind) =>
        Safe(() => host.Call<string[]>("list_files", new { inputs, kind }), []);

    /// 按命名规则算出示例文件名（不含扩展名）。
    public string PreviewName(Naming naming, string sourceName, uint width, uint height) =>
        Safe(() => host.Call<string>("preview_name", new { naming, source_name = sourceName, width, height }), Path.GetFileNameWithoutExtension(sourceName));

    /// <exception cref="EngineException"></exception>
    public VideoMeta VideoMeta(string path) => host.Call<VideoMeta>("video_meta", new { path });

    /// <exception cref="EngineException"></exception>
    public PdfMeta PdfMeta(string path) => host.Call<PdfMeta>("pdf_meta", new { path });

    /// 用密码解出一份不加密的副本。密码不对报「密码不对」。
    /// <exception cref="EngineException"></exception>
    public void UnlockPdf(string path, string password, string output) => host.Call<JsonElement>("unlock_pdf", new { path, password, output });

    /// 把一批路径移到回收站（撤销时清掉新建的输出文件夹）。不存在的跳过。
    /// <exception cref="EngineException"></exception>
    public void Trash(string[] paths) => host.Call<JsonElement>("trash", new { paths });

    /// 剪贴板用：把一张图压成字节直接返回，不写文件；压完不比原来小时返回 null。
    /// <exception cref="EngineException"></exception>
    public ClipboardImage? CompressForClipboard(string path, uint maxLongEdge) =>
        host.Call<ClipboardImage?>("compress_for_clipboard", new { path, max_long_edge = maxLongEdge });

    public Job RunImages(string[] inputs, Place place, Naming naming, ImageSettings settings, JobListener listener) =>
        host.Run("run_images", inputs, place, naming, settings, listener);

    public Job RunVideos(string[] inputs, Place place, Naming naming, VideoSettings settings, JobListener listener) =>
        host.Run("run_videos", inputs, place, naming, settings, listener);

    public Job RunPdfs(string[] inputs, Place place, Naming naming, PdfSettings settings, JobListener listener) =>
        host.Run("run_pdfs", inputs, place, naming, settings, listener);

    /// 原本不会失败的几个查询：引擎刚好退出时重启再试一次，仍不行给空结果（界面照常走，不抛到界面线程）。
    private static T Safe<T>(Func<T> call, T fallback)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return call();
            }
            catch (Exception e)
            {
                if (attempt >= 1)
                {
                    Log.Error($"引擎：{e.Message}");
                    return fallback;
                }
            }
        }
    }
}

/// 一次处理任务。取消、撤销都可以在任意线程调用。
internal sealed class Job : IDisposable
{
    private readonly EngineHost host;
    private readonly JobListener listener;
    private readonly object gate = new();
    /// 展开后的文件（宿主回响应后才有；之前先用输入代替），引擎意外退出时据此把没回报的标成失败。
    private string[] sources;
    private string[] createdRoots = [];
    private readonly List<JobItemResult> results = [];
    private readonly HashSet<uint> reported = [];
    private bool finished;

    internal ulong Id { get; }

    internal Job(EngineHost host, ulong id, string[] inputs, JobListener listener)
    {
        this.host = host;
        Id = id;
        sources = inputs;
        this.listener = listener;
    }

    public void Cancel()
    {
        lock (gate) if (finished) return;
        host.Post("cancel", new { job = Id });
    }

    public bool IsFinished()
    {
        lock (gate) return finished;
    }

    /// 把这一批的产出移到回收站（新建的输出文件夹一并移走）。
    /// <exception cref="EngineException"></exception>
    public void Undo()
    {
        string[] outputs, roots;
        lock (gate)
        {
            outputs = results.SelectMany(r => r.Outputs.Select(o => o.Path)).ToArray();
            roots = createdRoots;
        }
        host.Call<JsonElement>("undo", new { outputs, created_roots = roots });
    }

    public void Dispose() { }

    internal void Planned(string[] sources, string[] createdRoots)
    {
        lock (gate)
        {
            this.sources = sources;
            this.createdRoots = createdRoots;
        }
    }

    internal void Progress(uint index, double fraction)
    {
        lock (gate) if (finished) return;
        listener.OnProgress(index, fraction);
    }

    internal void Item(JobItemResult result)
    {
        lock (gate)
        {
            if (finished) return;
            results.Add(result);
            reported.Add(result.Index);
        }
        listener.OnItem(result);
    }

    internal void Finished()
    {
        lock (gate)
        {
            if (finished) return;
            finished = true;
        }
        listener.OnFinished();
    }

    /// 引擎意外退出（或起不来）：还没回报的文件标成失败，再报结束。
    internal void Fail(string message)
    {
        List<JobItemResult> missing;
        lock (gate)
        {
            if (finished) return;
            missing = sources.Select((s, i) => (s, i: (uint)i))
                .Where(x => !reported.Contains(x.i))
                .Select(x => new JobItemResult(x.i, x.s, SizeOf(x.s), [], ItemState.Failed, message))
                .ToList();
        }
        foreach (var r in missing) Item(r);
        Finished();
    }

    private static ulong SizeOf(string path)
    {
        try { return File.Exists(path) ? (ulong)new FileInfo(path).Length : 0; } catch { return 0; }
    }
}
