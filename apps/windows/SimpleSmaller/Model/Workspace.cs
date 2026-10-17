using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using uniffi.smaller_core;

namespace SimpleSmaller;

internal enum VideoSegment { Compress, Edit, Frames }
internal enum PdfSegment { Compress, Merge, Split, ToImages, FromImages }

internal static class Segments
{
    public static readonly VideoSegment[] AllVideo = [VideoSegment.Compress, VideoSegment.Edit, VideoSegment.Frames];
    public static readonly PdfSegment[] AllPdf = [PdfSegment.Compress, PdfSegment.Merge, PdfSegment.Split, PdfSegment.ToImages, PdfSegment.FromImages];

    public static string Title(this VideoSegment s) => s switch
    {
        VideoSegment.Compress => "压缩",
        VideoSegment.Edit => "剪辑",
        _ => "序列帧",
    };

    public static string Title(this PdfSegment s) => s switch
    {
        PdfSegment.Compress => "压缩",
        PdfSegment.Merge => "合并",
        PdfSegment.Split => "拆分",
        PdfSegment.ToImages => "转图片",
        _ => "图片转 PDF",
    };

    public static VideoSegment? VideoSegmentNamed(string name) => AllVideo.Cast<VideoSegment?>().FirstOrDefault(s => s!.Value.Title() == name);
    public static PdfSegment? PdfSegmentNamed(string name) => AllPdf.Cast<PdfSegment?>().FirstOrDefault(s => s!.Value.Title() == name);
}

/// 上一次点「开始」时的配方与分段。
internal sealed record LastRun(Recipe Draft, string Segment);

/// 一类文件（图片 / 视频 / PDF）的工作区。
/// 路径作键的字典一律不分大小写（Windows 文件名不分）；字典、集合整份替换才会通知界面，改动走 Set… 方法。
internal sealed partial class Workspace : ObservableObject
{
    public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public MediaType Type { get; }
    public ObservableCollection<FileItem> Files { get; } = [];
    [ObservableProperty] private IReadOnlySet<Guid> selection = new HashSet<Guid>();

    /// 图片不分「压缩 / 裁切」：比例是配方里的一个参数，选「原比例」就是只压缩。
    /// 选了比例时，左大卡默认是缩略图（每张画出裁切框）；点一张进大图调框，这时为 true。
    [ObservableProperty] private bool cropEditing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SegmentKey))] private VideoSegment videoSegment = VideoSegment.Compress;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SegmentKey), nameof(UsesRecipe))] private PdfSegment pdfSegment = PdfSegment.Compress;

    /// 当前选中的配方与它的可改副本；两者不同即「已修改」。
    [ObservableProperty] private string recipeId;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Modified))] private Recipe @base;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Modified), nameof(IsCropping))] private Recipe draft;

    [ObservableProperty] private Job? job;
    /// 这一批起过的全部任务（视频逐段各一个），撤销时逐个撤。
    public List<Job> BatchJobs { get; set; } = [];
    /// 这一批新建的输出根目录（如「素材-small」）。
    public string[] CreatedRoots { get; set; } = [];
    /// PDF 输出加的打开密码。只在这次处理里用，不写进配方、不保存。
    [ObservableProperty] private string outputPassword = "";
    [ObservableProperty] private bool outputPasswordOn;
    /// 第几轮处理；取消后重开，上一轮的回调据此作废。
    public int RunGeneration { get; set; }
    /// 这一轮处理的文件（重试时只是失败的那几个）。
    public HashSet<Guid> RunItemIds { get; set; } = [];
    [ObservableProperty] private bool running;
    /// 处理完换到完成页；「改设置再做一次」回到设置页，文件还在。
    [ObservableProperty] private bool showResult;
    /// 这一批任务里引擎展开后的文件顺序（进度回调按这个序号报）。
    public string[] PlanSources { get; set; } = [];
    [ObservableProperty] private int finishedCount;

    // 图片裁切：每张手调过的框（原图像素坐标）、当前第几张。动了就按新框，不用再确认。
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CropAdjustedCount))] private IReadOnlyDictionary<string, PixelRect> cropBoxes = new Dictionary<string, PixelRect>(PathComparer);
    [ObservableProperty] private int cropIndex;
    /// 每张框的改动记录，Ctrl+Z 撤回这一张用；null 表示改之前是默认框。
    public Dictionary<string, List<PixelRect?>> CropHistory { get; set; } = new(PathComparer);

    // 视频剪辑：当前是第几段，以及每段的截取、裁切框（原画面像素）、封面时间。
    [ObservableProperty] private int videoIndex;
    [ObservableProperty] private IReadOnlyDictionary<string, TimeRange> trim = new Dictionary<string, TimeRange>(PathComparer);
    [ObservableProperty] private IReadOnlyDictionary<string, PixelRect> videoCrop = new Dictionary<string, PixelRect>(PathComparer);
    [ObservableProperty] private IReadOnlyDictionary<string, double> coverAt = new Dictionary<string, double>(PathComparer);

    // PDF 合并：页面顺序（第几个文件, 第几页）。
    [ObservableProperty] private IReadOnlyList<PdfPageRef> pageOrder = [];
    /// pageOrder 里 file 序号对应的文件（排页时用来认出哪页属于哪个文件）。
    public IReadOnlyList<string> PageOrderFiles { get; set; } = [];

    public Workspace(MediaType type, Recipe recipe)
    {
        Type = type;
        recipeId = recipe.Id;
        @base = recipe;
        draft = recipe;
    }

    /// 改过配方参数才算「已修改」；切到「序列帧」分段带出的 outputFrames 不算。
    public bool Modified
    {
        get
        {
            var d = Draft with { Video = Draft.Video with { OutputFrames = Base.Video.OutputFrames } };
            return d != Base;
        }
    }

    /// 分段各用各的配方（配方条只放这一段能用的）：视频「压缩 · 剪辑 / 序列帧」、PDF「压缩 / 转图片」。
    /// 切过去时换成那一段上次用的，没用过就用它的第一个内置配方。
    private readonly Dictionary<string, string> segmentRecipe = [];

    private void SwitchRecipe(string group, string other, Func<Recipe, bool> fits, string fallback)
    {
        if (fits(Draft)) return;
        segmentRecipe[other] = RecipeId;
        var id = segmentRecipe.TryGetValue(group, out var saved) ? saved : fallback;
        if ((AppModel.Shared.Store.RecipeById(id) ?? Recipe.BuiltIns.FirstOrDefault(r => r.Id == id)) is { } r)
            Select(r);
    }

    public void EnterVideoSegment(VideoSegment s)
    {
        var frames = s == VideoSegment.Frames;
        SwitchRecipe(frames ? "frames" : "video", frames ? "video" : "frames",
                     r => r.Video.OutputFrames == frames, frames ? "vid.frames.web" : "vid.chat");
    }

    public void EnterPdfSegment(PdfSegment s)
    {
        if (s is not (PdfSegment.Compress or PdfSegment.ToImages)) return;
        var images = s == PdfSegment.ToImages;
        SwitchRecipe(images ? "pdfImages" : "pdf", images ? "pdf" : "pdfImages",
                     r => r.Pdf.LongImage == images, images ? "pdf.long" : "pdf.email");
    }

    public ulong TotalBytes => Files.Aggregate(0UL, (s, f) => s + f.Bytes);
    public IReadOnlyList<FileItem> SelectedItems => Files.Where(f => Selection.Contains(f.Id)).ToList();

    public void Select(Recipe recipe)
    {
        // 换了比例（或换成不裁的配方）：每张的框回到居中取最大，回到缩略图。
        if (Type == MediaType.Image && recipe.Image.Crop != Draft.Image.Crop)
        {
            ResetCrop();
            CropEditing = false;
        }
        RecipeId = recipe.Id;
        Base = recipe;
        Draft = recipe;
        if (Type == MediaType.Video)
        {
            if (recipe.Video.OutputFrames) VideoSegment = VideoSegment.Frames;
            else if (VideoSegment == VideoSegment.Frames) VideoSegment = VideoSegment.Compress;
        }
        if (Type == MediaType.Pdf)
        {
            if (recipe.Pdf.LongImage) PdfSegment = PdfSegment.ToImages;
            else if (PdfSegment == PdfSegment.ToImages) PdfSegment = PdfSegment.Compress;
        }
    }

    /// 交给引擎的输入：拖入的文件夹整个传（引擎照搬结构），其余逐个文件。
    public string[] EngineInputs
    {
        get
        {
            var seen = new HashSet<string>(PathComparer);
            var output = new List<string>();
            foreach (var f in Files)
            {
                var p = f.Root ?? f.FilePath;
                if (seen.Add(p)) output.Add(p);
            }
            return output.ToArray();
        }
    }

    /// 上一次点「开始」时的配方与分段：都没变、文件也都处理完了，再点开始只会多出一份重复的文件。
    public LastRun? LastRun { get; set; }
    public string SegmentKey => $"{VideoSegment.Title()}|{PdfSegment.Title()}";

    /// 图片这一批要裁（配方里选了比例）。
    public bool IsCropping => Type == MediaType.Image && Draft.Image.Crop != null;

    public bool IsUpToDate
    {
        get
        {
            if (LastRun is not { } last || Files.Count == 0 || Running) return false;
            var finished = Files.All(f => f.State.IsDoneOrKept);
            return finished && last.Draft == Draft && last.Segment == SegmentKey;
        }
    }

    /// PDF 合并、拆分、图片转 PDF 用不上配方：不显示配方条，换成一句说明。
    public bool UsesRecipe => !(Type == MediaType.Pdf && PdfSegment is PdfSegment.Merge or PdfSegment.Split or PdfSegment.FromImages);

    /// 路径 → 文件（含解锁副本的路径），开始处理时建好，进度与结果回调按路径找文件不用逐个比。
    private Dictionary<string, FileItem> pathIndex = new(PathComparer);

    public void RebuildPathIndex()
    {
        var index = new Dictionary<string, FileItem>(PathComparer);
        foreach (var f in Files)
        {
            index[f.FilePath] = f;
            if (f.UnlockedPath is { } u) index[u] = f;
        }
        pathIndex = index;
    }

    public FileItem? Item(string path) =>
        pathIndex.TryGetValue(path, out var f) ? f
        : Files.FirstOrDefault(x => PathComparer.Equals(x.FilePath, path) || (x.UnlockedPath != null && PathComparer.Equals(x.UnlockedPath, path)));

    // MARK: 整份替换的集合

    public void SetSelection(IEnumerable<Guid> ids) => Selection = ids.ToHashSet();

    public void SetTrim(string path, TimeRange? range) => Trim = With(Trim, path, range);
    public void SetVideoCrop(string path, PixelRect? rect) => VideoCrop = With(VideoCrop, path, rect);
    public void SetCoverAt(string path, double? seconds) => CoverAt = With(CoverAt, path, seconds);

    /// 换一个键的值（null 为删掉），返回新字典。
    internal static Dictionary<string, T> With<T>(IReadOnlyDictionary<string, T> source, string key, T? value) where T : struct
    {
        var d = new Dictionary<string, T>(source, PathComparer);
        if (value is { } v) d[key] = v; else d.Remove(key);
        return d;
    }

    internal static Dictionary<string, T> Empty<T>() => new(PathComparer);
}
