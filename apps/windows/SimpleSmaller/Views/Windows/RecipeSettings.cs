using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.Storage.Pickers;
using Windows.UI;

namespace SimpleSmaller.Views.Sub;

// MARK: - 常用

/// 常用管理（设计稿 V2.1 · 10 常用页，V2.2 按类型分开）：左栏 200 宽选类型（图片 / 视频 / PDF，带数量），
/// 右边只列这一类：灰底 40 高的行（把手 · 名称 · 摘要 · 圆环外圈 · 内置 / 我的），选中的行墨黑底白字。
/// 每一类的顺序就是那个功能页常用条的顺序，前 5 个进圆环外圈（第 5 个后面一条虚线）。
internal sealed class RecipeSettings : Reactive
{
    /// 打开时先选哪一类（功能页常用条的「管理」带过来）。
    public static MediaType InitialType { get; set; } = MediaType.Image;

    private readonly RecipeStore store = AppModel.Shared.Store;
    private MediaType kind = InitialType;
    private readonly List<(MediaType Type, Button Button, Action Paint)> kinds = [];
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Extended, CanReorderItems = true, AllowDrop = true, CanDragItems = true };
    private readonly TextBlock message = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextCapsule export = new("导出所选…", 30, 12, 12);
    private readonly TextCapsule delete = new("删除所选", 36, 13, 16);
    private readonly ContentPresenter addHost = new();
    private readonly Border side = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(12) };
    private readonly Border card = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 14, 16, 16) };
    private readonly TextBlock title = new() { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<ListViewItem, Action> painters = [];
    private string built = "";
    private List<string> dragOrder = [];
    private static Style? itemStyle;

    public RecipeSettings()
    {
        Content = Columns2(Side(), Main());
        list.SelectionChanged += (_, _) => { UpdateButtons(); foreach (var p in painters.Values) p(); };
        list.DragItemsStarting += (_, _) => dragOrder = Ids();
        list.DragItemsCompleted += (_, e) =>
        {
            if (e.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;
            var moved = e.Items.OfType<ListViewItem>().Select(i => (string)i.Tag).ToHashSet();
            var now = Ids();
            var old = dragOrder;
            if (moved.Count == 0 || now.SequenceEqual(old)) return;
            // 换算成 RecipeStore.MoveWithin 的规则：toOffset 是移动前列表里的插入位置。
            var lastMoved = now.FindLastIndex(moved.Contains);
            var after = now.Skip(lastMoved + 1).FirstOrDefault(id => !moved.Contains(id));
            var toOffset = after is null ? old.Count : old.IndexOf(after);
            store.MoveWithin(kind, old.Select((id, i) => (id, i)).Where(x => moved.Contains(x.id)).Select(x => x.i), toOffset);
        };
        Watch(store);
    }

    private static Grid Columns2(UIElement left, UIElement right)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(left);
        Grid.SetColumn((FrameworkElement)right, 1);
        g.Children.Add(right);
        return g;
    }

    // ---------- 左栏：按类型分开管 ----------

    private UIElement Side()
    {
        var g = new Grid { RowSpacing = 6 };
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var head = FormTheme.Text("按类型分开管", 12, "BentoSecondary");
        head.Margin = new Thickness(6, 4, 6, 6);
        AutomationProperties.SetHeadingLevel(head, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        g.Children.Add(head);
        var r = 1;
        foreach (var t in MediaTypes.All)
        {
            var b = KindButton(t);
            Grid.SetRow(b, r++);
            g.Children.Add(b);
        }
        var noteText = new TextBlock { Text = "每一类的顺序，就是那个功能页「常用」条的顺序；前 5 个也出现在资源管理器圆环外圈。", FontSize = 11, LineHeight = 17.6, TextWrapping = TextWrapping.Wrap };
        var note = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(14), Child = noteText, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(note, 4);
        g.Children.Add(note);
        side.Child = g;
        AutomationProperties.SetName(side, "常用的类型");
        FormTheme.On(side, () =>
        {
            side.Background = Palette.Get(side, "BentoCard");
            note.Background = Palette.Get(side, "BentoFill");
            noteText.Foreground = Palette.Get(side, "BentoSecondary");
        });
        return side;
    }

    private Button KindButton(MediaType t)
    {
        var icon = new LineIcon(t switch { MediaType.Image => FeatureIcons.TypeImage, MediaType.Video => FeatureIcons.TypeVideo, _ => FeatureIcons.TypePdf }, 17, 1.9);
        var iconBox = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(11), Child = icon };
        var name = new TextBlock { Text = t.Title(), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var count = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var countBox = new Border { MinWidth = 22, Height = 20, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(10), Child = count, VerticalAlignment = VerticalAlignment.Center };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(iconBox);
        Grid.SetColumn(name, 1);
        row.Children.Add(name);
        Grid.SetColumn(countBox, 2);
        row.Children.Add(countBox);
        var back = new Border { Height = 52, Padding = new Thickness(8, 0, 12, 0), CornerRadius = new CornerRadius(16), Child = row };
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = back, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Motion.SetPress(b, true);
        b.Click += (_, _) =>
        {
            if (kind == t) return;
            kind = InitialType = t;
            list.SelectedItems.Clear();
            foreach (var k in kinds) k.Paint();
            Refresh();
            Motion.Appear(list);
        };
        void paint()
        {
            var on = kind == t;
            var n = store.ForType(t).Count;
            count.Text = n.ToString();
            back.Background = Palette.Get(back, on ? "BentoInk" : "BentoFill");
            name.Foreground = Palette.Get(back, on ? "BentoOnInk" : "BentoPrimary");
            name.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            iconBox.Background = on ? Palette.Brand : Palette.Get(back, "BentoCard");
            icon.ColorKey = on ? "OnBrand" : "BentoPrimary";
            // 选中：数量底是 16% 的反色（设计稿 rgba(255,255,255,.16)）。
            var onInk = ((SolidColorBrush)Palette.Get(back, "BentoOnInk")).Color;
            countBox.Background = on ? new SolidColorBrush(Color.FromArgb(41, onInk.R, onInk.G, onInk.B)) : Palette.Get(back, "BentoCard");
            count.Foreground = Palette.Get(back, on ? "BentoOnInk" : "BentoSecondary");
            AutomationProperties.SetName(b, $"{t.Title()}，{n} 个常用");
            AutomationProperties.SetItemStatus(b, on ? "已选" : "");
        }
        FormTheme.On(back, paint);
        kinds.Add((t, b, paint));
        return b;
    }

    // ---------- 右边：这一类的常用 ----------

    private UIElement Main()
    {
        message.Text = "拖动排序";
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

        var foot = new Grid { ColumnSpacing = 6, Padding = new Thickness(0, 6, 0, 0) };
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        delete.Click += (_, _) =>
        {
            foreach (var id in SelectedIds().Where(id => store.RecipeById(id) is { BuiltIn: false })) store.Delete(id);
        };
        var cannot = FormTheme.Text("内置的不能删", 11, "BentoSecondary");
        cannot.HorizontalAlignment = HorizontalAlignment.Right;
        cannot.VerticalAlignment = VerticalAlignment.Center;
        foot.Children.Add(addHost);
        Grid.SetColumn(delete, 1);
        foot.Children.Add(delete);
        Grid.SetColumn(cannot, 2);
        foot.Children.Add(cannot);

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
        AutomationProperties.SetName(card, "常用列表");
        FormTheme.On(card, () =>
        {
            card.Background = Palette.Get(card, "BentoCard");
            title.Foreground = Palette.Get(card, "BentoPrimary");
            message.Foreground = Palette.Get(card, "BentoSecondary");
        });
        return card;
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
        foreach (var k in kinds) k.Paint();
        var rows = store.ForType(kind);
        var key = $"{kind}|" + string.Join(",", rows.Select(r => $"{r.Id}:{r.Name}:{r.Summary}")) + ActualTheme;
        if (key == built) { UpdateButtons(); return; }
        var kindChanged = !built.StartsWith($"{kind}|", StringComparison.Ordinal);
        built = key;
        title.Text = $"{kind.Title()}的常用";
        AutomationProperties.SetName(list, title.Text);
        if (kindChanged || addHost.Content is null)
        {
            var add = new TextCapsule($"新建{kind.Title()}常用", 36, 13, 16, "Brand", "OnBrand", SubIcons.Plus, semibold: true);
            var type = kind;
            add.Click += (_, _) => App.OpenRecipeEditor(new Recipe { Id = "", Name = "", Type = type }, _ => { });
            addHost.Content = add;
        }
        var selected = SelectedIds().ToHashSet();
        list.Items.Clear();
        painters.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var item = Row(rows[i], i, rows.Count);
            list.Items.Add(item);
            if (selected.Contains(rows[i].Id)) list.SelectedItems.Add(item);
        }
        UpdateButtons();
    }

    private ListViewItem Row(Recipe r, int index, int total)
    {
        var ring = index < RecipeStore.RingCount;
        var g = new Grid { ColumnSpacing = 8, Height = 40, Padding = new Thickness(6, 0, 14, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var back = new Border { CornerRadius = new CornerRadius(13), Child = g };
        var grip = new LineIcon(SubIcons.Grip, 15, 3, "BentoSecondary") { HorizontalAlignment = HorizontalAlignment.Center };
        var name = new TextBlock { Text = r.Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var summary = new TextBlock { Text = r.Summary, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var ringText = new TextBlock { Text = "圆环外圈", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        var ringTag = new Border { Height = 20, Padding = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(10), Child = ringText, HorizontalAlignment = HorizontalAlignment.Left, Visibility = ring ? Visibility.Visible : Visibility.Collapsed };
        var src = new TextBlock { Text = r.BuiltIn ? "内置" : "我的", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var t in new FrameworkElement[] { grip, name, summary, ringTag, src }) t.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(grip);
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        Grid.SetColumn(summary, 2);
        g.Children.Add(summary);
        Grid.SetColumn(ringTag, 3);
        g.Children.Add(ringTag);
        Grid.SetColumn(src, 4);
        g.Children.Add(src);

        // 第 5 个后面一条虚线：「以上 5 个出现在圆环外圈」（只在多于 5 个时）。
        UIElement content = back;
        Action paintSep = () => { };
        if (index == RecipeStore.RingCount - 1 && total > RecipeStore.RingCount)
        {
            var sepText = new TextBlock { Text = "以上 5 个出现在圆环外圈", FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            var (h1, l1) = DashedLine();
            var (h2, l2) = DashedLine();
            var sep = new Grid { ColumnSpacing = 10, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 6, 0, 0) };
            sep.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sep.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sep.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sep.Children.Add(h1);
            Grid.SetColumn(sepText, 1);
            sep.Children.Add(sepText);
            Grid.SetColumn(h2, 2);
            sep.Children.Add(h2);
            AutomationProperties.SetAccessibilityView(sep, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            var stack = new StackPanel();
            stack.Children.Add(back);
            stack.Children.Add(sep);
            content = stack;
            paintSep = () =>
            {
                sepText.Foreground = new SolidColorBrush(back.ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 0x7C, 0x80, 0x87) : Color.FromArgb(255, 0x8A, 0x8E, 0x95));
                l1.Stroke = l2.Stroke = Palette.Get(back, "BentoLine");
            };
        }

        var item = new ListViewItem { Content = content, Tag = r.Id };
        void paint()
        {
            var on = item.IsSelected;
            var dark = back.ActualTheme == ElementTheme.Dark;
            back.Background = Palette.Get(back, on ? "BentoInk" : "BentoFill");
            name.Foreground = Palette.Get(back, on ? "BentoOnInk" : "BentoPrimary");
            name.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            // 选中行的次要字：设计稿 #B9BCC2（深色里墨色是浅底，用浅色的次要灰 #63676E）。
            Brush sub = on ? new SolidColorBrush(dark ? Color.FromArgb(255, 0x63, 0x67, 0x6E) : Color.FromArgb(255, 0xB9, 0xBC, 0xC2)) : Palette.Get(back, "BentoSecondary");
            summary.Foreground = sub;
            if (r.BuiltIn)
            {
                src.Foreground = sub;
                src.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            }
            else
            {
                // 「我的」：品牌红粗体，选中时白字。
                src.Foreground = on ? Palette.Get(back, "BentoOnInk") : Palette.Brand;
                src.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            }
            var onInk = ((SolidColorBrush)Palette.Get(back, "BentoOnInk")).Color;
            ringTag.Background = on ? new SolidColorBrush(Color.FromArgb(36, onInk.R, onInk.G, onInk.B)) : Palette.Get(back, "BentoAlertFill");
            ringText.Foreground = on ? Palette.Get(back, "BentoOnInk") : Palette.Get(back, "BentoAlertText");
            grip.ColorKey = on ? "BentoLine" : "BentoSecondary";
            paintSep();
        }
        painters[item] = paint;
        FormTheme.On(back, paint);
        AutomationProperties.SetName(item, $"{r.Name}，{r.Summary}{(ring ? "，圆环外圈" : "")}，{(r.BuiltIn ? "内置" : "我的")}");
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

    /// 虚线（设计稿 border-top: 1px dashed）：线长跟着格子宽。
    private static (Grid Host, Microsoft.UI.Xaml.Shapes.Line Line) DashedLine()
    {
        var line = new Microsoft.UI.Xaml.Shapes.Line { X1 = 0, Y1 = 0.5, Y2 = 0.5, StrokeThickness = 1, StrokeDashArray = [3, 3] };
        var host = new Grid { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(line);
        host.SizeChanged += (_, e) => line.X2 = e.NewSize.Width;
        return (host, line);
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
