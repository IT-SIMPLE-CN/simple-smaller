using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using uniffi.smaller_core;

namespace SimpleSmaller;

internal enum OutputLocation { Beside, Folder }

internal sealed record WatchFolder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Path { get; init; } = "";
    [JsonPropertyName("recipeID")]
    public string RecipeId { get; init; } = "";
}

/// 设置窗口里的选项，存在 %APPDATA%\SimpleSmaller\settings.json。
/// 圆环、剪贴板、监视文件夹这些开关改了要刷新对应的监视器：由界面层订阅 PropertyChanged 处理。
internal sealed partial class AppSettings : ObservableObject
{
    private static AppSettings? shared;
    public static AppSettings Shared => shared ??= new AppSettings();

    private readonly string filePath;

    [ObservableProperty] private OutputLocation outputLocation = OutputLocation.Beside;
    [ObservableProperty] private string outputFolder = "";
    [ObservableProperty] private bool ringEnabled = true;
    /// 圆环的唤出方式：按住 Ctrl + Alt 点文件（默认只按 Alt）。设计稿 RingWin：快捷键可在设置里改为 Ctrl + Alt。
    [ObservableProperty] private bool ringCtrlAlt;
    [ObservableProperty] private bool menuBarIcon = true;
    /// 复制图片时自动换成压缩版（默认关）。
    [ObservableProperty] private bool clipboardAuto;
    /// 剪贴板图片的长边上限；0 为不缩小。
    [ObservableProperty] private int clipboardMaxEdge;
    /// 监视的文件夹与各自的配方。整份替换才会保存。
    [ObservableProperty] private IReadOnlyList<WatchFolder> watchFolders = [];
    /// 图片上次用的裁切比例（CropPreset.Id）。首页图片卡「裁切」标签、拖到「裁切」格时用它；没用过是小红书 3:4。
    [ObservableProperty] private string lastCropRatio = "xhs";

    private sealed class Saved
    {
        public OutputLocation OutputLocation { get; set; } = OutputLocation.Beside;
        public string OutputFolder { get; set; } = "";
        public bool RingEnabled { get; set; } = true;
        public bool RingCtrlAlt { get; set; }
        public bool MenuBarIcon { get; set; } = true;
        public bool ClipboardAuto { get; set; }
        public int ClipboardMaxEdge { get; set; }
        public List<WatchFolder> WatchFolders { get; set; } = [];
        public string? LastCropRatio { get; set; }
    }

    public AppSettings(string? directory = null)
    {
        filePath = Path.Combine(directory ?? Storage.Directory, "settings.json");
        if (Storage.Load<Saved>(filePath) is { } s)
        {
            outputLocation = s.OutputLocation;
            outputFolder = s.OutputFolder ?? "";
            ringEnabled = s.RingEnabled;
            ringCtrlAlt = s.RingCtrlAlt;
            menuBarIcon = s.MenuBarIcon;
            clipboardAuto = s.ClipboardAuto;
            clipboardMaxEdge = s.ClipboardMaxEdge;
            watchFolders = s.WatchFolders ?? [];
            lastCropRatio = string.IsNullOrEmpty(s.LastCropRatio) ? "xhs" : s.LastCropRatio;
        }
        PropertyChanged += (_, _) => Save();
    }

    private void Save() => Storage.Save(filePath, new Saved
    {
        OutputLocation = OutputLocation,
        OutputFolder = OutputFolder,
        RingEnabled = RingEnabled,
        RingCtrlAlt = RingCtrlAlt,
        MenuBarIcon = MenuBarIcon,
        ClipboardAuto = ClipboardAuto,
        ClipboardMaxEdge = ClipboardMaxEdge,
        WatchFolders = WatchFolders.ToList(),
        LastCropRatio = LastCropRatio,
    });

    /// 上次用的裁切比例；记下的比例已不存在时回到小红书 3:4。
    public CropPreset LastCropPreset =>
        CropPreset.ImagePresets.FirstOrDefault(p => p.Id == LastCropRatio) ?? CropPreset.ImagePresets[0];

    public Place Place
    {
        get
        {
            if (OutputLocation == OutputLocation.Folder && OutputFolder.Length > 0) return new Place.Folder(OutputFolder);
            return new Place.BesideSource("-small");
        }
    }
}
