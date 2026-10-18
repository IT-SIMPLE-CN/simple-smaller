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
            FormRow.Create("圆环怎么用", RingGuideButton()),
            FormRow.Create("唤出方式", trigger),
            FormRow.Toggle("托盘图标", settings.MenuBarIcon, on => settings.MenuBarIcon = on),
            FormRow.Toggle("资源管理器右键「显示更多选项」", ExplorerMenu.Enabled, on => ExplorerMenu.Enabled = on),
            FormRow.Create("处理剪贴板图片", FormRow.Key("Ctrl+Shift+V")));

        Content = Columns.Two(Columns.Stack(outputCard, privacy), entry);
    }

    private IntPtr WindowHwnd() => App.SettingsHwnd;

    /// 「再看一遍」：把主窗口拿出来，打开圆环教学（设计稿 V2.2 · D）。
    private static TextCapsule RingGuideButton()
    {
        var b = new TextCapsule("再看一遍", 28, 12, 10, "BentoCard");
        AutomationProperties.SetName(b, "再看一遍圆环教学");
        b.Click += (_, _) =>
        {
            App.ShowMain();
            Views.Guide.GuideHost.Shared?.ShowRing();
        };
        return b;
    }
}

// MARK: - 配方

/// 配方管理（设计稿 V2.1 · 10 配方页）：一张白卡，灰底 40 高的行（把手 · 名称 · 类型 · 摘要 · 内置 / 我的），
/// 选中的行墨黑底白字；拖动排序（配方条、圆环按这个顺序），导入导出，新建、编辑、删除我的配方。
internal sealed class RecipeSettings : Reactive
{
    private readonly RecipeStore store = AppModel.Shared.Store;
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Extended, CanReorderItems = true, AllowDrop = true, CanDragItems = true };
    private readonly TextBlock message = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextCapsule export = new("导出所选…", 30, 12, 12);
    private readonly TextCapsule delete = new("删除所选", 36, 13, 16);
    private readonly Border card = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 16) };
    private readonly TextBlock title = new() { Text = "常用", FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<ListViewItem, Action> painters = [];
    private string built = "";
    private List<string> dragOrder = [];
    private static Style? itemStyle;

    public RecipeSettings()
    {
        message.Text = "拖动排序，常用条和圆环按这个顺序";
        AutomationProperties.SetName(list, "常用");
        list.ItemContainerStyle = itemStyle ??= (Style)Microsoft.UI.Xaml.Markup.XamlReader.Load(ItemXaml);
        var head = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 4), MinHeight = 30 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var import = new TextCapsule("导入…", 30, 12, 12);
        import.Click += async (_, _) => await ImportAsync();
        export.Click += async (_, _) => await ExportAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(import);
        buttons.Children.Add(export);
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        head.Children.Add(title);
        Grid.SetColumn(message, 1);
        head.Children.Add(message);
        Grid.SetColumn(buttons, 2);
        head.Children.Add(buttons);

        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(0, 6, 0, 0) };
        delete.Click += (_, _) =>
        {
            foreach (var id in SelectedIds().Where(id => store.RecipeById(id) is { BuiltIn: false })) store.Delete(id);
        };
        var add = new TextCapsule("新建常用", 36, 13, 16, "Brand", "OnBrand", SubIcons.Plus, semibold: true);
        add.Click += (_, _) => App.OpenRecipeEditor(new Recipe { Id = "", Name = "", Type = AppModel.Shared.SelectedType }, _ => { });
        foot.Children.Add(add);
        foot.Children.Add(delete);

        var g = new Grid { RowSpacing = 6 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(head);
        Grid.SetRow(list, 1);
        g.Children.Add(list);
        Grid.SetRow(foot, 2);
        g.Children.Add(foot);
        card.Child = g;
        Content = card;
        FormTheme.On(card, () =>
        {
            card.Background = Palette.Get(card, "BentoCard");
            title.Foreground = Palette.Get(card, "BentoPrimary");
            message.Foreground = Palette.Get(card, "BentoSecondary");
        });

        list.SelectionChanged += (_, _) => { UpdateButtons(); foreach (var p in painters.Values) p(); };
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

    // 行容器：去掉系统的选中底色、左侧指示条与悬停底，行自己画（选中墨黑底白字）；行距 6。
    private const string ItemXaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ListViewItem">
  <Setter Property="Padding" Value="0" />
  <Setter Property="MinHeight" Value="0" />
  <Setter Property="Margin" Value="0,0,0,6" />
  <Setter Property="HorizontalContentAlignment" Value="Stretch" />
  <Setter Property="VerticalContentAlignment" Value="Stretch" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ListViewItem">
        <ListViewItemPresenter ContentTransitions="{TemplateBinding ContentTransitions}"
            SelectionCheckMarkVisualEnabled="False" SelectionIndicatorVisualEnabled="False"
            Background="Transparent" PointerOverBackground="Transparent" PressedBackground="Transparent"
            SelectedBackground="Transparent" SelectedPointerOverBackground="Transparent" SelectedPressedBackground="Transparent"
            PointerOverForeground="{TemplateBinding Foreground}" SelectedForeground="{TemplateBinding Foreground}"
            FocusVisualMargin="0" CornerRadius="13" DragOpacity="0.8"
            HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
            VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
            ContentMargin="{TemplateBinding Padding}" />
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";

    private List<string> Ids() => list.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();
    private List<string> SelectedIds() => list.SelectedItems.OfType<ListViewItem>().Select(i => (string)i.Tag).ToList();

    private void UpdateButtons()
    {
        var sel = SelectedIds();
        export.IsEnabled = sel.Count > 0;
        delete.IsEnabled = sel.Any(id => store.RecipeById(id) is { BuiltIn: false });
    }

    protected override void Refresh()
    {
        var all = store.All;
        var key = string.Join(",", all.Select(r => $"{r.Id}:{r.Name}:{r.Summary}")) + ActualTheme;
        if (key == built) { UpdateButtons(); return; }
        built = key;
        var selected = SelectedIds().ToHashSet();
        list.Items.Clear();
        painters.Clear();
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
        var g = new Grid { ColumnSpacing = 8, Height = 40, Padding = new Thickness(6, 0, 14, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var back = new Border { CornerRadius = new CornerRadius(13), Child = g };
        var grip = new LineIcon(SubIcons.Grip, 15, 3, "BentoSecondary") { HorizontalAlignment = HorizontalAlignment.Center };
        var name = new TextBlock { Text = r.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var type = new TextBlock { Text = r.Type.Title(), FontSize = 12 };
        var summary = new TextBlock { Text = r.Summary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var kind = new TextBlock { Text = r.BuiltIn ? "内置" : "我的", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var t in new FrameworkElement[] { grip, name, type, summary, kind }) t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(grip);
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        Grid.SetColumn(type, 2);
        g.Children.Add(type);
        Grid.SetColumn(summary, 3);
        g.Children.Add(summary);
        Grid.SetColumn(kind, 4);
        g.Children.Add(kind);
        var item = new ListViewItem { Content = back, Tag = r.Id };
        void paint()
        {
            var on = item.IsSelected;
            var dark = back.ActualTheme == ElementTheme.Dark;
            back.Background = Palette.Get(back, on ? "BentoInk" : "BentoFill");
            name.Foreground = Palette.Get(back, on ? "BentoOnInk" : "BentoPrimary");
            name.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            // 选中行的次要字：设计稿 #B9BCC2（深色里墨色是浅底，用浅色的次要灰 #63676E）。
            Brush sub = on ? new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 0x63, 0x67, 0x6E) : Windows.UI.Color.FromArgb(255, 0xB9, 0xBC, 0xC2)) : Palette.Get(back, "BentoSecondary");
            type.Foreground = summary.Foreground = kind.Foreground = sub;
            grip.ColorKey = on ? "BentoLine" : "BentoSecondary";
        }
        painters[item] = paint;
        FormTheme.On(back, paint);
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
            var copy = new MenuFlyoutItem { Text = "复制为我的常用…" };
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
            message.Text = $"导入了 {n} 个常用";
        }
        catch
        {
            message.Text = "这个文件里没有可用的常用";
        }
    }

    private async Task ExportAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0) return;
        var picker = new FileSavePicker { SuggestedFileName = ids.Count == 1 ? store.RecipeById(ids[0])?.Name ?? "常用" : "常用" };
        picker.FileTypeChoices.Add("简单小一点常用", [".smaller"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.SettingsHwnd);
        if (await picker.PickSaveFileAsync() is not { } file) return;
        try
        {
            store.Export(ids, file.Path);
            message.Text = $"已导出 {ids.Count} 个常用";
        }
        catch (Exception e)
        {
            message.Text = $"导出失败：{e.Message}";
        }
    }
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
