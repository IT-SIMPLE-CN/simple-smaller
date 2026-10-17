using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Storage.Pickers;

namespace SimpleSmaller.Views.Sub;

/// 设置（设计稿 V2.1 · 10）：便当盒小窗，通用 / 配方 / 自动 三页，顶部是 Top 样式的滑块分段。
internal sealed class SettingsWindow : SubWindow
{
    public static readonly string[] Tabs = ["通用", "配方", "自动"];
    private readonly Segments<string> tabs;
    private readonly ContentPresenter page = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private string current;

    public SettingsWindow(string? tab) : base("设置", 820, 580)
    {
        current = Tabs.Contains(tab) ? tab! : Tabs[0];
        tabs = new Segments<string>(Segments<string>.Kind.Top, Tabs.Select(t => (t, t, (string?)null)), current, "设置分类")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        tabs.Changed += t => Show(t);
        var stack = new Grid { RowSpacing = 14, Padding = new Thickness(14, 0, 14, 14) };
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.Children.Add(tabs);
        Grid.SetRow(page, 1);
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
            "配方" => new RecipeSettings(),
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

internal sealed class GeneralSettings : Reactive
{
    private readonly AppSettings settings = AppSettings.Shared;
    private string built = "";
    private string? updateNote;

    public GeneralSettings()
    {
        Watch(settings, nameof(AppSettings.OutputLocation), nameof(AppSettings.OutputFolder), nameof(AppSettings.RingEnabled), nameof(AppSettings.MenuBarIcon));
        Watch(Updates.Shared);
    }

    protected override void Refresh()
    {
        var key = $"{settings.OutputLocation}|{settings.OutputFolder}|{settings.RingEnabled}|{settings.MenuBarIcon}|{Updates.Shared.AutomaticallyChecks}|{updateNote}|{ActualTheme}";
        if (key == built) return;
        built = key;

        var location = new ComboBox { MinWidth = 130 };
        AutomationProperties.SetName(location, "存到");
        location.Items.Add("原文件旁边");
        location.Items.Add("指定文件夹");
        location.SelectedIndex = settings.OutputLocation == OutputLocation.Folder ? 1 : 0;
        location.SelectionChanged += (_, _) => settings.OutputLocation = location.SelectedIndex == 1 ? OutputLocation.Folder : OutputLocation.Beside;
        var output = new List<UIElement> { FormRow.Create("存到", location) };
        if (settings.OutputLocation == OutputLocation.Folder)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var path = FormRow.Value(settings.OutputFolder.Length == 0 ? "未选择" : settings.OutputFolder);
            path.MaxWidth = 170;
            path.VerticalAlignment = VerticalAlignment.Center;
            ToolTipService.SetToolTip(path, settings.OutputFolder);
            var choose = new CapsuleButton("选择…", 28, "BentoCard");
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
        var outputCard = FormCard.Create("", "输出", "原文件从不改动；压完变大时保留原文件。撤销会把新文件移到回收站。", output.ToArray());

        var check = new CapsuleButton("检查更新…", 28, "BentoCard");
        check.Click += async (_, _) => { updateNote = await Updates.Shared.CheckAsync(); Refresh(); };
        var privacy = FormCard.Create("", "隐私与更新", updateNote ?? "文件只在本机处理，不上传。除检查更新外，应用不联网。",
            FormRow.Toggle("自动检查更新", Updates.Shared.AutomaticallyChecks, on => Updates.Shared.AutomaticallyChecks = on),
            FormRow.Create("现在检查", check));

        var entry = FormCard.Create("", "入口", "圆环要读出你点的是哪个文件。只读取文件位置，不记录其他操作。",
            FormRow.Toggle("资源管理器圆环", settings.RingEnabled, on => settings.RingEnabled = on),
            FormRow.Create("唤出方式", FormRow.Value("按住 Alt 点文件")),
            FormRow.Toggle("托盘图标", settings.MenuBarIcon, on => settings.MenuBarIcon = on),
            FormRow.Toggle("资源管理器右键", ExplorerMenu.Enabled, on => ExplorerMenu.Enabled = on),
            FormRow.Create("右键位置", FormRow.Value("显示更多选项 → 用简单小一点处理")),
            FormRow.Create("处理剪贴板", FormRow.Key("Ctrl+Shift+V")));

        Content = Columns.Two(Columns.Stack(outputCard, privacy), entry);
    }

    private IntPtr WindowHwnd() => App.SettingsHwnd;
}

// MARK: - 配方

/// 配方管理：拖动排序（配方条、圆环按这个顺序），导入导出，新建、编辑、删除我的配方。
internal sealed class RecipeSettings : Reactive
{
    private readonly RecipeStore store = AppModel.Shared.Store;
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Extended, CanReorderItems = true, AllowDrop = true, CanDragItems = true };
    private readonly TextBlock message = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CapsuleButton export = new("导出所选…", 30);
    private readonly CapsuleButton delete = new("删除所选", 34);
    private readonly Border card = new() { CornerRadius = new CornerRadius(22), Padding = new Thickness(16) };
    private readonly TextBlock title = new() { Text = "配方", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private string built = "";
    private List<string> dragOrder = [];

    public RecipeSettings()
    {
        message.Text = "拖动排序，配方条和圆环按这个顺序";
        AutomationProperties.SetName(list, "配方");
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var import = new CapsuleButton("导入…", 30);
        import.Click += async (_, _) => await ImportAsync();
        export.Click += async (_, _) => await ExportAsync();
        head.Children.Add(title);
        Grid.SetColumn(message, 1);
        head.Children.Add(message);
        Grid.SetColumn(import, 2);
        head.Children.Add(import);
        Grid.SetColumn(export, 3);
        head.Children.Add(export);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        delete.Click += (_, _) =>
        {
            foreach (var id in SelectedIds().Where(id => store.RecipeById(id) is { BuiltIn: false })) store.Delete(id);
        };
        var add = new BrandButton("新建配方", 34, 13, 16);
        add.Click += (_, _) => App.OpenRecipeEditor(new Recipe { Id = "", Name = "", Type = AppModel.Shared.SelectedType }, _ => { });
        foot.Children.Add(delete);
        foot.Children.Add(add);

        var listBack = new Border { CornerRadius = new CornerRadius(14), Child = list };
        var g = new Grid { RowSpacing = 8 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(head);
        Grid.SetRow(listBack, 1);
        g.Children.Add(listBack);
        Grid.SetRow(foot, 2);
        g.Children.Add(foot);
        card.Child = g;
        Content = card;
        FormTheme.On(card, () =>
        {
            card.Background = Palette.Get(card, "BentoCard");
            title.Foreground = Palette.Get(card, "BentoPrimary");
            message.Foreground = Palette.Get(card, "BentoSecondary");
            listBack.Background = Palette.Get(card, "BentoFill");
        });

        list.SelectionChanged += (_, _) => UpdateButtons();
        list.DragItemsStarting += (_, _) => dragOrder = Ids();
        list.DragItemsCompleted += (_, e) =>
        {
            if (e.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;
            var moved = e.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToHashSet();
            var now = Ids();
            var old = dragOrder;
            if (moved.Count == 0 || now.SequenceEqual(old)) return;
            // 换算成 RecipeStore.Move 的规则：toOffset 是移动前列表里的插入位置。
            var lastMoved = now.FindLastIndex(moved.Contains);
            var after = now.Skip(lastMoved + 1).FirstOrDefault(id => !moved.Contains(id));
            var toOffset = after is null ? old.Count : old.IndexOf(after);
            built = string.Join(",", now);
            store.Move(old.Select((id, i) => (id, i)).Where(x => moved.Contains(x.id)).Select(x => x.i), toOffset);
        };
        Watch(store);
    }

    private List<string> Ids() => list.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
    private List<string> SelectedIds() => list.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();

    private void UpdateButtons()
    {
        var sel = SelectedIds();
        export.IsEnabled = sel.Count > 0;
        delete.IsEnabled = sel.Any(id => store.RecipeById(id) is { BuiltIn: false });
        export.Opacity = export.IsEnabled ? 1 : 0.4;
        delete.Opacity = delete.IsEnabled ? 1 : 0.4;
    }

    protected override void Refresh()
    {
        var all = store.All;
        var key = string.Join(",", all.Select(r => $"{r.Id}:{r.Name}:{r.Summary}")) + ActualTheme;
        if (key == built) { UpdateButtons(); return; }
        built = key;
        var selected = SelectedIds().ToHashSet();
        list.Items.Clear();
        foreach (var r in all)
        {
            var item = Row(r);
            list.Items.Add(item);
            if (selected.Contains(r.Id)) list.SelectedItems.Add(item);
        }
        UpdateButtons();
    }

    private ListViewItem Row(Recipe r)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var name = new TextBlock { Text = r.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Palette.Get(this, "BentoPrimary") };
        var type = new TextBlock { Text = r.Type.Title(), FontSize = 11, Foreground = Palette.Get(this, "BentoSecondary") };
        var summary = new TextBlock { Text = r.Summary, FontSize = 11, Foreground = Palette.Get(this, "BentoSecondary"), TextTrimming = TextTrimming.CharacterEllipsis };
        var kind = new TextBlock { Text = r.BuiltIn ? "内置" : "我的", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Foreground = r.BuiltIn ? Palette.Get(this, "BentoSecondary") : Palette.Brand };
        foreach (var t in new[] { name, type, summary, kind }) t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(name);
        Grid.SetColumn(type, 1);
        g.Children.Add(type);
        Grid.SetColumn(summary, 2);
        g.Children.Add(summary);
        Grid.SetColumn(kind, 3);
        g.Children.Add(kind);
        var item = new ListViewItem { Content = g, Tag = r.Id, MinHeight = 34, Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(item, $"{r.Name}，{r.Type.Title()}，{r.Summary}，{(r.BuiltIn ? "内置" : "我的")}");
        var menu = new MenuFlyout();
        if (!r.BuiltIn)
        {
            var edit = new MenuFlyoutItem { Text = "编辑…" };
            edit.Click += (_, _) => App.OpenRecipeEditor(r, _ => { });
            var del = new MenuFlyoutItem { Text = "删除" };
            del.Click += (_, _) => store.Delete(r.Id);
            menu.Items.Add(edit);
            menu.Items.Add(del);
        }
        else
        {
            var copy = new MenuFlyoutItem { Text = "复制为我的配方…" };
            copy.Click += (_, _) => App.OpenRecipeEditor(r with { Id = "", Name = $"{r.Name} 改", BuiltIn = false }, _ => { });
            menu.Items.Add(copy);
        }
        item.ContextFlyout = menu;
        item.DoubleTapped += (_, _) => { if (!r.BuiltIn) App.OpenRecipeEditor(r, _ => { }); };
        return item;
    }

    private async Task ImportAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".smaller");
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.SettingsHwnd);
        if (await picker.PickSingleFileAsync() is not { } file) return;
        try
        {
            var n = store.ImportRecipes(file.Path);
            message.Text = $"导入了 {n} 个配方";
        }
        catch
        {
            message.Text = "这个文件读不出配方";
        }
    }

    private async Task ExportAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) return;
        var picker = new FileSavePicker { SuggestedFileName = ids.Count == 1 ? store.RecipeById(ids[0])?.Name ?? "配方" : "配方" };
        picker.FileTypeChoices.Add("简单小一点配方", [".smaller"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.SettingsHwnd);
        if (await picker.PickSaveFileAsync() is not { } file) return;
        try
        {
            store.Export(ids, file.Path);
            message.Text = $"已导出 {ids.Count} 个配方";
        }
        catch (Exception e)
        {
            message.Text = $"导出失败：{e.Message}";
        }
    }
}

// MARK: - 自动

/// 自动：剪贴板自动压缩、监视文件夹。
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
        var key = $"{settings.ClipboardAuto}|{settings.ClipboardMaxEdge}|{string.Join(",", settings.WatchFolders.Select(w => w.Path + w.RecipeId))}|{AppModel.Shared.Store.All.Count}|{ActualTheme}";
        if (key == built) return;
        built = key;

        var clip = new List<UIElement> { FormRow.Toggle("复制的图片自动换成压缩版", settings.ClipboardAuto, on => settings.ClipboardAuto = on) };
        if (settings.ClipboardAuto)
        {
            int[] edges = [0, 2560, 1920, 1280];
            var box = new ComboBox { MinWidth = 110 };
            AutomationProperties.SetName(box, "最长边");
            foreach (var e in edges) box.Items.Add(e == 0 ? "不缩小" : $"{e} px");
            box.SelectedIndex = Math.Max(0, Array.IndexOf(edges, settings.ClipboardMaxEdge));
            box.SelectionChanged += (_, _) => settings.ClipboardMaxEdge = edges[Math.Max(0, box.SelectedIndex)];
            clip.Add(FormRow.Create("最长边", box));
        }
        var clipCard = FormCard.Create("", "剪贴板",
            "截图或从网页复制的图片会自动换成压缩版，粘贴到公众号、飞书、微信就是小图。复制的文件、文字不动。", clip.ToArray());

        var rows = new List<UIElement>();
        foreach (var watch in settings.WatchFolders)
        {
            var id = watch.Id;
            var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var picker = RecipePicker.Create(watch.RecipeId, r =>
                settings.WatchFolders = settings.WatchFolders.Select(w => w.Id == id ? w with { RecipeId = r.Id } : w).ToList());
            var remove = new CircleIconButton("", "不再监视", 28, "BentoCard");
            remove.Click += (_, _) => settings.WatchFolders = settings.WatchFolders.Where(w => w.Id != id).ToList();
            trailing.Children.Add(picker);
            trailing.Children.Add(remove);
            var name = Path.GetFileName(watch.Path.TrimEnd('\\'));
            var row = FormRow.Create(string.IsNullOrEmpty(name) ? watch.Path : name, trailing, 44);
            ToolTipService.SetToolTip(row, watch.Path);
            rows.Add(row);
        }
        var add = new CapsuleButton("添加文件夹…", 34);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += async (_, _) =>
        {
            var picker = new FolderPicker { CommitButtonText = "监视" };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, App.SettingsHwnd);
            if (await picker.PickSingleFolderAsync() is not { } f) return;
            if (settings.WatchFolders.Any(w => string.Equals(w.Path, f.Path, StringComparison.OrdinalIgnoreCase))) return;
            settings.WatchFolders = settings.WatchFolders.Append(new WatchFolder { Path = f.Path, RecipeId = "img.web" }).ToList();
        };
        rows.Add(add);
        var watchCard = FormCard.Create("", "监视文件夹",
            "放进这些文件夹的新文件按配方自动处理，结果放在旁边，处理完发通知。只处理之后放进来的文件。", rows.ToArray());

        Content = Columns.Two(clipCard, watchCard);
    }
}
