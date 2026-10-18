using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace SimpleSmaller.Views.Main;

/// 顶部配方条（设计稿 V2.1）：只放这个分段能用的配方；选中变黑，改过参数带红点；
/// 文件可以直接拖到某个配方上；「＋」新建，「管理」进设置。
internal sealed class RecipeBar : Reactive
{
    private readonly Workspace ws;
    private readonly StackPanel pills = new() { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock label = new() { Text = "常用", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border card;
    private string built = "";
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle fade = new() { Width = 28, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };

    public RecipeBar(Workspace ws)
    {
        this.ws = ws;
        var scroll = new ScrollViewer
        {
            Content = pills,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        AutomationProperties.SetName(scroll, "常用");
        var plus = new CircleIconButton("", "新建常用", 36);
        plus.Click += (_, _) => App.OpenRecipeEditor(ws.Draft with { Id = "", Name = "", BuiltIn = false }, saved => ws.Select(saved));
        var manage = new CapsuleButton("管理", 36);
        ToolTipService.SetToolTip(manage, "在设置里排序、导入导出常用");
        manage.Click += (_, _) =>
        {
            Views.Sub.RecipeSettings.InitialType = ws.Type;
            App.OpenSettings("常用");
        };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(label);
        Grid.SetColumn(scroll, 1);
        row.Children.Add(scroll);
        // 右边渐隐，提示还能往后滚。
        Grid.SetColumn(fade, 1);
        row.Children.Add(fade);
        Grid.SetColumn(plus, 2);
        row.Children.Add(plus);
        Grid.SetColumn(manage, 3);
        row.Children.Add(manage);
        plus.VerticalAlignment = manage.VerticalAlignment = VerticalAlignment.Center;
        card = Card.Create(row, new Thickness(20, 10, 10, 10));
        Content = card;
        Watch(ws, nameof(Workspace.RecipeId), nameof(Workspace.Modified), nameof(Workspace.Draft), nameof(Workspace.Running), nameof(Workspace.VideoSegment), nameof(Workspace.PdfSegment));
        Watch(AppModel.Shared.Store);
    }

    private bool Fits(Recipe r) => ws.Type switch
    {
        MediaType.Image => true, // 图片只有一页，全部配方都在这里（带比例的就是裁切）
        MediaType.Video => r.Video.OutputFrames == (ws.VideoSegment == VideoSegment.Frames),
        _ => r.Pdf.LongImage == (ws.PdfSegment == PdfSegment.ToImages),
    };

    protected override void Refresh()
    {
        label.Foreground = Palette.Get(this, "BentoPrimary");
        var cardColor = ((SolidColorBrush)Palette.Get(this, "BentoCard")).Color;
        var stops = new GradientStopCollection
        {
            new GradientStop { Color = Windows.UI.Color.FromArgb(0, cardColor.R, cardColor.G, cardColor.B), Offset = 0 },
            new GradientStop { Color = cardColor, Offset = 1 },
        };
        fade.Fill = new LinearGradientBrush(stops, 0);
        var store = AppModel.Shared.Store;
        // 按设置 › 常用 里这一类的顺序排（内置和我的混排，设计稿 V2.2）。
        var list = store.ForType(ws.Type).Where(Fits).ToList();
        var key = string.Join("|", list.Select(r => r.Id + r.Name)) + $"#{ws.RecipeId}#{ws.Modified}";
        if (key != built)
        {
            built = key;
            pills.Children.Clear();
            foreach (var r in list)
            {
                pills.Children.Add(Pill(r, mine: !r.BuiltIn));
            }
        }
        // 处理中锁住：不能点、变淡。
        card.IsHitTestVisible = !ws.Running;
        card.Opacity = ws.Running ? 0.45 : 1;
    }

    private UIElement Pill(Recipe r, bool mine)
    {
        var on = ws.RecipeId == r.Id;
        var pill = new PillButton(r.Name) { IsOn = on, Dot = on && ws.Modified };
        ToolTipService.SetToolTip(pill, r.Summary);
        pill.Click += (_, _) => ws.Select(r);
        if (mine)
        {
            var menu = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "编辑…" };
            edit.Click += (_, _) => App.OpenRecipeEditor(r, saved => ws.Select(saved));
            var export = new MenuFlyoutItem { Text = "导出…" };
            export.Click += async (_, _) => await Export(r);
            var delete = new MenuFlyoutItem { Text = "删除" };
            delete.Click += (_, _) => AppModel.Shared.Store.Delete(r.Id);
            menu.Items.Add(edit);
            menu.Items.Add(export);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(delete);
            pill.ContextFlyout = menu;
        }
        // 把文件拖到某个配方上：加进这一批并换成这个配方（点「开始」才写出）。
        pill.AllowDrop = true;
        pill.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = $"用「{r.Name}」";
                pill.BorderBrush = Palette.Brand;
            }
        };
        pill.Drop += async (_, e) =>
        {
            e.Handled = true;
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }
            var items = await e.DataView.GetStorageItemsAsync();
            var model = AppModel.Shared;
            model.SelectedType = r.Type;
            model.Add(items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)), r.Type);
            model.Current.Select(r);
        };
        return pill;
    }

    /// 导出成 .smaller 文件，发给同事后在「新建配方 → 从文件导入」里导入。
    private static async Task Export(Recipe recipe)
    {
        var picker = new FileSavePicker { SuggestedFileName = recipe.Name };
        picker.FileTypeChoices.Add("简单小一点常用", new List<string> { ".smaller" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainHwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }
        try
        {
            AppModel.Shared.Store.Export(new[] { recipe.Id }, file.Path);
            AppModel.Shared.Flash($"已导出「{recipe.Name}」");
        }
        catch (Exception e)
        {
            AppModel.Shared.Flash($"导出失败：{e.Message}");
        }
    }
}
