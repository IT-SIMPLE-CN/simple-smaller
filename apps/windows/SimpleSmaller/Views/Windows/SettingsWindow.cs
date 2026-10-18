using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Storage.Pickers;

namespace SimpleSmaller.Views.Sub;

/// 设置（设计稿 V2.1 · 10）：便当盒小窗 860 × 636，通用 / 配方 / 自动 三页。
/// 标题行 60 高：左边 15 号粗体「设置」，正中是 Top 样式的滑块分段（和设计稿一样放在标题行里）。
internal sealed class SettingsWindow : SubWindow
{
    public static readonly string[] Tabs = ["通用", "常用", "自动"];
    private readonly Segments<string> tabs;
    private readonly ContentPresenter page = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string current;

    public SettingsWindow(string? tab) : base("设置", 860, 636)
    {
        current = Tabs.Contains(tab) ? tab! : Tabs[0];
        tabs = new Segments<string>(Segments<string>.Kind.Top, Tabs.Select(t => (t, t, (string?)null)), current, "设置分类");
        tabs.Changed += t => Show(t);
        UseHeader(60, 15, tabs);
        var stack = new Grid { Padding = new Thickness(14, 0, 14, 14) };
        stack.Children.Add(page);
        Body.Children.Add(stack);
        Show(current);
    }

    public void Show(string tab)
    {
        if (!Tabs.Contains(tab)) return;
        current = tab;
        tabs.Selected = tab;
        UIElement content = tab switch
        {
            "常用" => new RecipeSettings(),
            "自动" => new AutoSettings(),
            _ => new GeneralSettings(),
        };
        page.Content = content;
        Motion.Appear(content);
    }
}

/// 设置里的两栏：各自从上往下排。
internal static class Columns
{
    public static Grid Two(UIElement left, UIElement right)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        if (left is FrameworkElement l) l.VerticalAlignment = VerticalAlignment.Top;
        if (right is FrameworkElement r) r.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(left);
        Grid.SetColumn((FrameworkElement)right, 1);
        g.Children.Add(right);
        return g;
    }

    public static StackPanel Stack(params UIElement[] items)
    {
        var s = new StackPanel { Spacing = 12 };
        foreach (var i in items) s.Children.Add(i);
        return s;
    }
}

// MARK: - 通用

/// 通用（设计稿 V2.1 · 10）：输出 · 隐私 · 入口。
/// 设计稿里没有对应功能的行（重名时、压缩后变大、去掉位置信息、硬件编码、同时处理张数）不放：
/// 这些在 Windows 版是固定行为或按配方设，放一个改不了的开关会误导。
internal sealed class GeneralSettings : Reactive
{
    private readonly AppSettings settings = AppSettings.Shared;
    private string built = "";

    public GeneralSettings()
    {
        Watch(settings, nameof(AppSettings.OutputLocation), nameof(AppSettings.OutputFolder), nameof(AppSettings.RingEnabled), nameof(AppSettings.MenuBarIcon), nameof(AppSettings.RingCtrlAlt));
        Watch(Updates.Shared);
    }

    protected override void Refresh()
    {
        var key = $"{settings.OutputLocation}|{settings.OutputFolder}|{settings.RingEnabled}|{settings.MenuBarIcon}|{settings.RingCtrlAlt}|{Updates.Shared.AutomaticallyChecks}|{ActualTheme}";
        if (key == built) return;
        built = key;

        var location = new MenuCapsule("存到", "");
        location.Options([(OutputLocation.Beside, "原文件旁边"), (OutputLocation.Folder, "指定文件夹")], settings.OutputLocation, v => settings.OutputLocation = v);
        var output = new List<UIElement> { FormRow.Create("存到", location) };
        if (settings.OutputLocation == OutputLocation.Folder)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var path = FormRow.Value(settings.OutputFolder.Length == 0 ? "未选择" : settings.OutputFolder);
            path.MaxWidth = 170;
            path.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(path, settings.OutputFolder);
            var choose = new TextCapsule("选择…", 28, 12, 10, "BentoCard");
            choose.Click += async (_, _) =>
            {
                var picker = new FolderPicker { CommitButtonText = "选择" };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHwnd());
                if (await picker.PickSingleFolderAsync() is { } f) settings.OutputFolder = f.Path;
            };
            row.Children.Add(path);
            row.Children.Add(choose);
            output.Add(FormRow.Create("文件夹", row));
        }
        output.Add(FormRow.Create("默认文件名", FormRow.Mono("{name}-small")));
        output.Add(FormRow.Create("重名时", FormRow.Value("加序号")));
        var outputCard = FormCard.Create(SubIcons.Folder, "输出", "原文件从不改动，撤销会把新文件移到回收站。", output.ToArray());

        var privacy = FormCard.Create(SubIcons.Lock, "隐私与更新", "文件只在本机处理。除检查更新外，应用不联网。",
            FormRow.Toggle("自动检查更新", Updates.Shared.AutomaticallyChecks, on => Updates.Shared.AutomaticallyChecks = on));

        var trigger = new MenuCapsule("唤出方式", "");
        trigger.Options([(false, "按住 Alt 点文件"), (true, "按住 Ctrl + Alt 点文件")], settings.RingCtrlAlt, v => settings.RingCtrlAlt = v);
        var entry = FormCard.Create(Icons.Ring, "入口", "圆环要读出你点的是哪个文件。只读取文件位置，不记录其他操作。",
            FormRow.Toggle("资源管理器圆环", settings.RingEnabled, on => settings.RingEnabled = on),
            FormRow.Create("唤出方式", trigger),
            FormRow.Toggle("托盘图标", settings.MenuBarIcon, on => settings.MenuBarIcon = on),
            FormRow.Toggle("资源管理器右键「显示更多选项」", ExplorerMenu.Enabled, on => ExplorerMenu.Enabled = on),
            FormRow.Create("处理剪贴板图片", FormRow.Key("Ctrl+Shift+V")));

        Content = Columns.Two(Columns.Stack(outputCard, privacy), entry);
    }

    private IntPtr WindowHwnd() => App.SettingsHwnd;
}

// MARK: - 自动

/// 自动（设计稿 V2.1 · 10 自动页）：剪贴板自动压缩、监视文件夹，两张只有标题的白卡并排。
internal sealed class AutoSettings : Reactive
{
    private readonly AppSettings settings = AppSettings.Shared;
    private string built = "";

    public AutoSettings()
    {
        Watch(settings, nameof(AppSettings.ClipboardAuto), nameof(AppSettings.ClipboardMaxEdge), nameof(AppSettings.WatchFolders));
        Watch(AppModel.Shared.Store);
    }

    protected override void Refresh()
    {
        var store = AppModel.Shared.Store;
        var key = $"{settings.ClipboardAuto}|{settings.ClipboardMaxEdge}|{string.Join(",", settings.WatchFolders.Select(w => w.Path + w.RecipeId))}|{string.Join(",", store.All.Select(r => r.Id + r.Name))}|{ActualTheme}";
        if (key == built) return;
        built = key;

        int[] edges = [0, 2560, 1920, 1280];
        var edge = new MenuCapsule("最长边", "", arrowSuffix: true);
        edge.Options(edges.Select(e => (e, e == 0 ? "不缩小" : $"{e} px")), edges.Contains(settings.ClipboardMaxEdge) ? settings.ClipboardMaxEdge : 0, v => settings.ClipboardMaxEdge = v);
        var clipCard = FormCard.Create(null, "剪贴板", "复制截图或网页里的图片后，粘贴出去就是小图。复制的文件、文字不动。",
            FormRow.Toggle("复制的图片自动换成压缩版", settings.ClipboardAuto, on => settings.ClipboardAuto = on),
            FormRow.Create("最长边", edge));

        var rows = new List<UIElement>();
        foreach (var watch in settings.WatchFolders)
        {
            var id = watch.Id;
            var name = Path.GetFileName(watch.Path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = watch.Path;
            var picker = new MenuCapsule($"{name} 的常用", "", arrowSuffix: true);
            picker.Options(store.All.Select(r => (r.Id, r.Name)), watch.RecipeId,
                v => settings.WatchFolders = settings.WatchFolders.Select(w => w.Id == id ? w with { RecipeId = v } : w).ToList(),
                ("不再监视", () => settings.WatchFolders = settings.WatchFolders.Where(w => w.Id != id).ToList()));
            if (store.RecipeById(watch.RecipeId) is null) picker.Value = "选择常用";
            var row = FormRow.Create(name, picker);
            ToolTipService.SetToolTip(row, watch.Path);
            rows.Add(row);
        }
        var add = new TextCapsule("添加文件夹…", 30, 12, 12);
        add.Click += async (_, _) =>
        {
            var picker = new FolderPicker { CommitButtonText = "监视" };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.SettingsHwnd);
            if (await picker.PickSingleFolderAsync() is not { } f) return;
            if (settings.WatchFolders.Any(w => string.Equals(w.Path, f.Path, StringComparison.OrdinalIgnoreCase))) return;
            settings.WatchFolders = settings.WatchFolders.Append(new WatchFolder { Path = f.Path, RecipeId = "img.web" }).ToList();
        };
        var watchCard = FormCard.Create(null, "监视文件夹", add,
            "放进这些文件夹的新文件按常用自动处理，结果放在旁边，处理完发通知。", rows.ToArray());

        Content = Columns.Two(clipCard, watchCard);
    }
}
