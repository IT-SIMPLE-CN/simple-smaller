using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;

namespace SimpleSmaller.Views.Cards;

/// PDF 合并、拆分、转图片、图片转 PDF 的右下格：输出（文件名）与红色「开始」块。
/// 对应 mac 通用的 OutputStartCard 的 PDF 部分（PDF 不显示「去掉位置与设备信息」）；合并代码时换成通用的输出卡。
internal sealed class PdfOutputCard : Reactive
{
    private readonly Workspace ws;
    private readonly Header head = new() { Glyph = "\uE8B7", Title = "输出" };
    private readonly TextBlock fixedName = PdfUi.Text("", 12, "BentoSecondary");
    private readonly TextBox template = new()
    {
        FontSize = 12,
        TextAlignment = TextAlignment.Right,
        BorderThickness = new Thickness(0),
        MinHeight = 0,
        MinWidth = 0,
        Padding = new Thickness(0, 4, 0, 4),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly PdfRow row;

    public PdfOutputCard(Workspace ws)
    {
        this.ws = ws;
        fixedName.FontFamily = PdfUi.Mono;
        fixedName.TextTrimming = TextTrimming.CharacterEllipsis;
        fixedName.HorizontalAlignment = HorizontalAlignment.Right;
        template.FontFamily = PdfUi.Mono;
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
        {
            template.Resources[k] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        AutomationProperties.SetName(template, "文件名规则");
        template.TextChanged += (_, _) =>
        {
            if (template.Text != ws.Draft.Naming.Template)
                ws.Draft = ws.Draft with { Naming = ws.Draft.Naming with { Template = template.Text } };
        };
        var value = new Grid();
        value.Children.Add(fixedName);
        value.Children.Add(template);
        row = new PdfRow("文件名", value);
        var body = new Grid { RowSpacing = 10 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(head);
        Grid.SetRow(row, 1);
        body.Children.Add(row);
        var start = new StartBlock(ws) { Margin = new Thickness(0, 4, 0, 0) };
        Grid.SetRow(start, 2);
        body.Children.Add(start);
        Content = Card.Create(body);
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Running));
    }

    /// 拆分、转图片按固定规则命名（文件夹 + 编号），不用配方里的模板。
    public static string? FixedName(Workspace ws) => ws.PdfSegment switch
    {
        PdfSegment.Split => "{name}-拆分/{name}-01",
        PdfSegment.ToImages => ws.Draft.Pdf.LongImage ? "{name}-长图" : "{name}-图片/{name}-01",
        _ => null,
    };

    protected override void Refresh()
    {
        head.Subtitle = ws.Running ? "处理中，设置先锁住" : "原文件不改动";
        var name = FixedName(ws);
        fixedName.Visibility = name is null ? Visibility.Collapsed : Visibility.Visible;
        template.Visibility = name is null ? Visibility.Visible : Visibility.Collapsed;
        fixedName.Text = name ?? "";
        ToolTipService.SetToolTip(fixedName, name);
        AutomationProperties.SetName(fixedName, name is null ? "" : $"文件名 {name}");
        if (template.Text != ws.Draft.Naming.Template) template.Text = ws.Draft.Naming.Template;
        template.Foreground = Palette.Get(this, "BentoSecondary");
        row.IsHitTestVisible = !ws.Running;
        template.IsEnabled = !ws.Running;
    }
}
