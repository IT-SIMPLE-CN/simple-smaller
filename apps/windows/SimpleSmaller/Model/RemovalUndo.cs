using uniffi.smaller_core;

namespace SimpleSmaller;

/// 一次清空或移除之前，这一类的文件与按文件记的状态（设计稿 V3 · 11「已清空 12 个文件 · 撤销」）。
/// 字典类状态都是整份替换的不可变快照，直接留引用；裁切框的改动记录是可变的，拷一份。
internal sealed class RemovalUndo
{
    public required Workspace Ws { get; init; }
    /// 提示条上的字：「已清空 N 个文件」/「已移除 N 个文件」。
    public required string Text { get; init; }
    /// 拿走之前的全部文件（按原顺序）。
    public required IReadOnlyList<FileItem> Files { get; init; }
    /// 这次拿走的文件。
    public required IReadOnlyList<FileItem> Removed { get; init; }
    /// 清空会把按文件记的状态一起清掉，撤销时要整份放回；移除不动它们，只放回文件、选中与页序。
    public bool Cleared { get; init; }

    private IReadOnlySet<Guid> selection = new HashSet<Guid>();
    private IReadOnlyDictionary<string, PixelRect> cropBoxes = Workspace.Empty<PixelRect>();
    private Dictionary<string, List<PixelRect?>> cropHistory = new(Workspace.PathComparer);
    private int cropIndex;
    private bool cropEditing;
    private int videoIndex;
    private IReadOnlyDictionary<string, TimeRange> trim = Workspace.Empty<TimeRange>();
    private IReadOnlyDictionary<string, PixelRect> videoCrop = Workspace.Empty<PixelRect>();
    private IReadOnlyDictionary<string, double> coverAt = Workspace.Empty<double>();
    private IReadOnlyDictionary<string, TimeRange> liveRange = Workspace.Empty<TimeRange>();
    private IReadOnlyDictionary<string, double> liveCover = Workspace.Empty<double>();
    private IReadOnlyList<PdfPageRef> pageOrder = [];
    private IReadOnlyList<string> pageOrderFiles = [];
    private int finishedCount;
    private LastRun? lastRun;
    private Job? job;
    private List<Job> batchJobs = [];
    private string[] createdRoots = [];

    public static RemovalUndo Take(Workspace ws, string text, IReadOnlyList<FileItem> removed, bool cleared) => new()
    {
        Ws = ws,
        Text = text,
        Files = ws.Files.ToList(),
        Removed = removed,
        Cleared = cleared,
        selection = ws.Selection.ToHashSet(),
        cropBoxes = ws.CropBoxes,
        cropHistory = ws.CropHistory.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), Workspace.PathComparer),
        cropIndex = ws.CropIndex,
        cropEditing = ws.CropEditing,
        videoIndex = ws.VideoIndex,
        trim = ws.Trim,
        videoCrop = ws.VideoCrop,
        coverAt = ws.CoverAt,
        liveRange = ws.LiveRange,
        liveCover = ws.LiveCover,
        pageOrder = ws.PageOrder,
        pageOrderFiles = ws.PageOrderFiles,
        finishedCount = ws.FinishedCount,
        lastRun = ws.LastRun,
        job = ws.Job,
        batchJobs = ws.BatchJobs.ToList(),
        createdRoots = ws.CreatedRoots,
    };

    /// 放回去：拿走的文件插回原位置（这期间新加的留在后面；同一路径又被加进来的，留新加的那个）。
    public void Restore()
    {
        var ws = Ws;
        var present = ws.Files.Select(f => f.FilePath).ToHashSet(Workspace.PathComparer);
        for (var i = 0; i < Files.Count; i++)
        {
            var f = Files[i];
            if (ws.Files.Contains(f) || present.Contains(f.FilePath)) continue;
            ws.Files.Insert(Math.Min(i, ws.Files.Count), f);
        }
        if (Cleared)
        {
            ws.CropBoxes = cropBoxes;
            ws.CropHistory = cropHistory;
            ws.CropIndex = cropIndex;
            ws.VideoIndex = videoIndex;
            ws.Trim = trim;
            ws.VideoCrop = videoCrop;
            ws.CoverAt = coverAt;
            ws.LiveRange = liveRange;
            ws.LiveCover = liveCover;
            ws.FinishedCount = finishedCount;
            ws.LastRun = lastRun;
            ws.Job = job;
            ws.BatchJobs = batchJobs;
            ws.CreatedRoots = createdRoots;
            ws.CropEditing = cropEditing;
        }
        ws.PageOrderFiles = pageOrderFiles;
        ws.PageOrder = pageOrder;
        ws.SetSelection(selection.Where(id => ws.Files.Any(f => f.Id == id)));
    }
}
