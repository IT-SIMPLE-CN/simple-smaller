using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Main;

namespace SimpleSmaller.Views.Cards;

/// 网格里每一格放什么。按类型分给 ImageSlots / VideoSlots / PdfSlots（各自一个文件）。
internal static class Slots
{
    public static UIElement Main(Workspace ws) => ws.Type switch
    {
        MediaType.Image => ImageSlots.Main(ws),
        MediaType.Video => VideoSlots.Main(ws),
        _ => PdfSlots.Main(ws),
    };

    /// 配方条；PDF 的合并、拆分、图片转 PDF 不用配方，换成这一段的说明。
    public static UIElement TopBar(Workspace ws) => ws.UsesRecipe ? new RecipeBar(ws) : SegmentNote(ws);

    public static UIElement Upper(Workspace ws) => ws.Type switch
    {
        MediaType.Image => ImageSlots.Upper(ws),
        MediaType.Video => VideoSlots.Upper(ws),
        _ => PdfSlots.Upper(ws),
    };

    public static UIElement LowerLeft(Workspace ws) => ws.Type switch
    {
        MediaType.Image => ImageSlots.LowerLeft(ws),
        MediaType.Video => VideoSlots.LowerLeft(ws),
        _ => PdfSlots.LowerLeft(ws),
    };

    public static UIElement LowerRight(Workspace ws) => ws.Type switch
    {
        MediaType.Image => ImageSlots.LowerRight(ws),
        MediaType.Video => VideoSlots.LowerRight(ws),
        _ => PdfSlots.LowerRight(ws),
    };

    public static UIElement Result(Workspace ws) => ResultSlots.Result(ws);

    private static UIElement SegmentNote(Workspace ws)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { Text = ws.PdfSegment.Title(), FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var n = new TextBlock { Text = PdfNotes.SegmentNote(ws.PdfSegment), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(t);
        row.Children.Add(n);
        void paint() { t.Foreground = Palette.Get(row, "BentoPrimary"); n.Foreground = Palette.Get(row, "BentoSecondary"); }
        row.Loaded += (_, _) => paint();
        row.ActualThemeChanged += (_, _) => paint();
        return Card.Create(row, new Thickness(20, 0, 20, 0));
    }

    /// 还没做好的格子先放一张写着名字的卡。
    public static UIElement Placeholder(string name)
    {
        var t = new TextBlock { Text = name, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        t.Loaded += (_, _) => t.Foreground = Palette.Get(t, "BentoSecondary");
        return Card.Create(t);
    }
}
