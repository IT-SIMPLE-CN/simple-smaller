using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using uniffi.smaller_core;

namespace SimpleSmaller;

/// 应用状态的入口：当前类型、首页、三个工作区、提示条、最近记录。
/// 具体职责分在几个分部里：
/// - AppModel+Files：文件进出（拖入分类、元信息、解锁、剪贴板、移除清空、合并页序）
/// - AppModel+Batch：一批的处理（开始、跑引擎、进度结果、完成、取消、重试、撤销）
/// - AppModel+Activity：处理中不睡眠、任务栏进度与角标
/// 全部成员只在界面线程上用；后台结果经 Ui.Post 回到界面线程。
internal sealed partial class AppModel : ObservableObject
{
    private static AppModel? shared;
    public static AppModel Shared => shared ?? throw new InvalidOperationException("先调用 AppModel.Initialize");

    /// 启动时调用一次：`dispatch` 投递到界面线程（DispatcherQueue.TryEnqueue），`platform` 由界面层给出。
    public static AppModel Initialize(Action<Action> dispatch, IPlatform? platform = null, INotifier? notifier = null)
    {
        Ui.Post = dispatch;
        Notifier.Current = notifier;
        shared = new AppModel(platform ?? new WindowsPlatform());
        return shared;
    }

    // 平台解码：mac 用 ImageIO 解 HEIC；Windows 暂不接，引擎自带的解码器处理其余格式。
    public Engine Engine { get; } = new(null);
    public RecipeStore Store { get; } = new();
    public AppSettings Settings => AppSettings.Shared;
    public IPlatform Platform { get; }

    private MediaType selectedType = MediaType.Image;
    /// 切到某一类就离开首页（Ctrl+1/2/3、拖入文件、启动参数都走这里）。
    public MediaType SelectedType
    {
        get => selectedType;
        set
        {
            SetProperty(ref selectedType, value);
            OnPropertyChanged(nameof(Current));
            ShowHome = false;
        }
    }

    /// 首页：打开应用时显示；左上返回回到这里，各类的文件保留。
    [ObservableProperty] private bool showHome = true;
    private readonly Dictionary<MediaType, Workspace> workspaces = [];
    [ObservableProperty] private string? toast;
    /// 最近完成的任务（托盘小窗）。
    public ObservableCollection<RecentJob> Recent { get; } = [];

    // 处理中（见 AppModel+Activity）。
    private IDisposable? activity;
    /// 后台完成、还没回到应用时任务栏上的角标数。
    public int Unseen { get; private set; }

    private AppModel(IPlatform platform)
    {
        Platform = platform;
        foreach (var type in MediaTypes.All)
            workspaces[type] = new Workspace(type, Store.BuiltInsFor(type)[0]);
        // 记下图片上次用的比例（选常用、点比例格子都算），首页「裁切」进来时用。
        var image = workspaces[MediaType.Image];
        image.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SimpleSmaller.Workspace.Draft) && image.Draft.Image.Crop is { } crop && crop.Id != Settings.LastCropRatio)
                Settings.LastCropRatio = crop.Id;
        };
    }

    public Workspace Current => workspaces[SelectedType];
    public Workspace Workspace(MediaType type) => workspaces[type];

    public void Reveal(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Platform.Reveal(paths);
    }

    public void Flash(string text)
    {
        Toast = text;
        _ = ClearToastLater(text);
    }

    private async Task ClearToastLater(string text)
    {
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        Ui.Post(() => { if (Toast == text) Toast = null; });
    }
}
