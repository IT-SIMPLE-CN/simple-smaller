using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;

namespace SimpleSmaller.Views.Cards;

/// 右下一格：输出（文件名、去掉位置信息）与红色「开始」块。图片、视频、PDF 共用。
internal sealed class OutputStartCard : Reactive
{
    private readonly Workspace ws;
    private readonly Header header = new() { Glyph = "", Title = "输出" };
    private readonly PlainField template = new("文件名规则") { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextAlignment = TextAlignment.Right, ForegroundKey = "BentoSecondary", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock fixedName = Themed.Text("", 12, "BentoSecondary");
    private readonly ToggleSwitch strip = BentoToggle.Create("去掉位置与设备信息");
    private readonly UIElement stripRow;
    private readonly StackPanel settings = new() { Spacing = 10 };

    public OutputStartCard(Workspace ws)
    {
        this.ws = ws;
        fixedName.FontFamily = template.FontFamily;
        fixedName.TextTrimming = TextTrimming.CharacterEllipsis;
        fixedName.HorizontalAlignment = HorizontalAlignment.Right;
        template.TextChanged += (_, _) =>
        {
            if (template.FocusState != FocusState.Unfocused && template.Text != ws.Draft.Naming.Template)
            {
                ws.Draft = ws.Draft with { Naming = ws.Draft.Naming with { Template = template.Text } };
            }
        };
        strip.Toggled += (_, _) =>
        {
            if (strip.IsOn != ws.Draft.StripMetadata)
            {
                ws.Draft = ws.Draft with { StripMetadata = strip.IsOn };
            }
        };
        var nameSlot = new Grid();
        nameSlot.Children.Add(template);
        nameSlot.Children.Add(fixedName);
        settings.Children.Add(BentoRow.Create("文件名", nameSlot));
        stripRow = BentoRow.Create("去掉位置与设备信息", strip);
        settings.Children.Add(stripRow);

        var start = new StartBlock(ws) { Margin = new Thickness(0, 4, 0, 0) };
        var grid = new Grid { RowSpacing = 10 };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 84 });
        grid.Children.Add(header);
        Grid.SetRow(settings, 1);
        grid.Children.Add(settings);
        Grid.SetRow(start, 2);
        grid.Children.Add(start);
        Content = Card.Create(grid);
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Running), nameof(Workspace.VideoSegment), nameof(Workspace.PdfSegment));
    }

    protected override void Refresh()
    {
        header.Subtitle = ws.Running ? "处理中，设置先锁住" : "原文件不改动";
        if (OutputNaming.FixedName(ws) is { } name)
        {
            fixedName.Text = name;
            fixedName.Visibility = Visibility.Visible;
            template.Visibility = Visibility.Collapsed;
        }
        else
        {
            fixedName.Visibility = Visibility.Collapsed;
            template.Visibility = Visibility.Visible;
            template.Show(ws.Draft.Naming.Template);
        }
        stripRow.Visibility = ws.Type != MediaType.Pdf ? Visibility.Visible : Visibility.Collapsed;
        strip.IsOn = ws.Draft.StripMetadata;
        Lock.Apply(settings, ws.Running, dim: false);
    }
}

internal static class OutputNaming
{
    /// 拆分、转图片按固定规则命名（文件夹 + 编号），不用配方里的模板。
    public static string? FixedName(Workspace ws)
    {
        if (ws.Type == MediaType.Video && ws.VideoSegment == VideoSegment.Frames) return "{name}_frames/{name}_0001";
        if (ws.Type != MediaType.Pdf) return null;
        return ws.PdfSegment switch
        {
            PdfSegment.Split => "{name}-拆分/{name}-01",
            PdfSegment.ToImages => ws.Draft.Pdf.LongImage ? "{name}-长图" : "{name}-图片/{name}-01",
            _ => null,
        };
    }
}

/// 灰底圆角行：左名称，右值或控件（对应 mac 的 BentoRow）。
internal static class BentoRow
{
    public static Border Create(string label, UIElement trailing, double height = 44)
    {
        var name = Themed.Text(label, 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxLines = 1;
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(name);
        if (trailing is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Right;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }
        Grid.SetColumn((FrameworkElement)trailing, 1);
        row.Children.Add(trailing);
        var b = new Border { Height = height, Padding = new Thickness(16, 0, 12, 0), CornerRadius = new CornerRadius(15), Child = row };
        Themed.Bg(b, "BentoFill");
        return b;
    }
}

/// 系统开关，开着是品牌红；不显示「开 / 关」字样，名字给旁白。
internal static class BentoToggle
{
    public static ToggleSwitch Create(string label)
    {
        var t = new ToggleSwitch { OnContent = null, OffContent = null, MinWidth = 0, Width = 44, Padding = new Thickness(0) };
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed" })
        {
            t.Resources[key] = Palette.Brand;
        }
        t.Resources["ToggleSwitchPreContentMargin"] = 0.0;
        t.Resources["ToggleSwitchPostContentMargin"] = 0.0;
        AutomationProperties.SetName(t, label);
        return t;
    }
}

/// 卡片内容：放得下时撑满卡片（星号行照常把面板推到底），放不下时（窗口矮、胶囊换了行）可以上下滚，不裁掉。
internal static class CardBody
{
    public static ScrollViewer Fit(FrameworkElement body)
    {
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroll.SizeChanged += (_, _) => body.MinHeight = scroll.ViewportHeight;
        return scroll;
    }
}

/// 处理中锁住设置：不能点，卡片变淡（mac 的 bentoLocked）。
internal static class Lock
{
    public static void Apply(FrameworkElement e, bool locked, bool dim = true)
    {
        if (e is Control c)
        {
            c.IsEnabled = !locked;
        }
        e.IsHitTestVisible = !locked;
        if (dim)
        {
            e.Opacity = locked ? 0.45 : 1;
        }
    }
}

/// 「已修改」+「存为新配方」（卡片头右侧）。
internal sealed class SaveRecipeAccessory : StackPanel
{
    private readonly Workspace ws;
    private bool shown;

    public SaveRecipeAccessory(Workspace ws)
    {
        this.ws = ws;
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        var tag = Themed.Capsule(Themed.Text("已修改", 11, "BentoAlertText"), "BentoAlertFill", 26, 10);
        var save = new CapsuleButton("存为新配方");
        save.Click += (_, _) =>
        {
            var r = ws.Draft with
            {
                Id = "",
                Name = ws.Base.BuiltIn ? $"{ws.Base.Name} 改" : ws.Base.Name,
                BuiltIn = false,
            };
            App.OpenRecipeEditor(r, saved => ws.Select(saved));
        };
        Children.Add(tag);
        Children.Add(save);
        Visibility = Visibility.Collapsed;
    }

    public void Update()
    {
        var show = ws.Modified && ws.UsesRecipe;
        if (show == shown)
        {
            return;
        }
        shown = show;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            Motion.Appear(this);
        }
    }
}
