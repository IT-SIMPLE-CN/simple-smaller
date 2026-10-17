using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Cards;

namespace SimpleSmaller.Views.Main;

/// 一类文件的工作区：便当盒网格；做完换到完成页。
///
/// 网格（设计稿 03）：左边一张跨三行的大卡放文件或预览；右边上方横条是配方，下面两行参数卡，
/// 右下一格是输出与「开始」。两档：宽（≥ 1080）三列；窄时右侧合成一列可滚动、「开始」固定在底。
/// 每格放什么由 ImageSlots / VideoSlots / PdfSlots 决定。
internal sealed class WorkView : Reactive
{
    private const double BarHeight = 58;
    private const double MaxColumn = 400;
    private const double Gap = 14;

    private readonly Workspace ws;
    private readonly Grid host = new() { Padding = new Thickness(16, 6, 16, 16) };
    private string contentKey = "";
    private bool? narrow;
    private UIElement? main, bar, upper, lowerLeft, lowerRight;

    public WorkView(Workspace ws)
    {
        this.ws = ws;
        Content = host;
        Watch(ws, nameof(Workspace.ShowResult), nameof(Workspace.Running), nameof(Workspace.SegmentKey),
            nameof(Workspace.IsCropping), nameof(Workspace.CropEditing), nameof(Workspace.VideoSegment), nameof(Workspace.PdfSegment));
        WatchList(ws.Files);
        SizeChanged += (_, e) =>
        {
            var n = e.NewSize.Width < 1080;
            if (n != narrow)
            {
                narrow = n;
                Arrange(appear: false);
            }
            else
            {
                Size();
            }
        };
    }

    /// 换卡片内容时整组重新弹入。
    private string Key => ws.ShowResult && !ws.Running && ws.Files.Count > 0
        ? "result"
        : $"{ws.Type}|{ws.SegmentKey}|{ws.Files.Count == 0}|{ws.IsCropping && ws.CropEditing}";

    protected override void Refresh()
    {
        var key = Key;
        if (key == contentKey)
        {
            return;
        }
        var wasResult = contentKey == "result";
        contentKey = key;
        if (key == "result")
        {
            host.Children.Clear();
            var result = Slots.Result(ws);
            host.Children.Add(result);
            Motion.PageIn(result);
            return;
        }
        main = Region(Slots.MainCard(ws), "文件");
        bar = Region(Slots.TopBar(ws), "配方");
        upper = Region(Slots.Upper(ws), "设置");
        lowerLeft = Region(Slots.LowerLeft(ws), "设置");
        lowerRight = Region(Slots.LowerRight(ws), "开始");
        Arrange(appear: true);
        if (wasResult)
        {
            Motion.PageIn(host);
        }
    }

    /// 便当盒里的一格：旁白当作一个分组整体读完再读下一格。
    private static UIElement Region(UIElement e, string name)
    {
        if (e is FrameworkElement fe && string.IsNullOrEmpty(AutomationProperties.GetName(fe)))
        {
            AutomationProperties.SetName(fe, name);
        }
        return e;
    }

    private readonly Grid wide = new() { ColumnSpacing = Gap, RowSpacing = Gap };
    private readonly Grid tall = new() { ColumnSpacing = Gap, RowSpacing = Gap };

    private void Arrange(bool appear)
    {
        if (main is null || contentKey == "result" || narrow is null)
        {
            return;
        }
        host.Children.Clear();
        Detach(main, bar, upper, lowerLeft, lowerRight);
        if (narrow == true)
        {
            // 窄：左大卡占一半；右边配方条 + 可滚动的参数卡 + 固定在底的开始卡。
            tall.Children.Clear();
            tall.ColumnDefinitions.Clear();
            tall.RowDefinitions.Clear();
            tall.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tall.ColumnDefinitions.Add(new ColumnDefinition());
            tall.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });
            tall.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            tall.RowDefinitions.Add(new RowDefinition());
            Place(tall, main, 0, 0, rowSpan: 3);
            Place(tall, bar, 0, 1);
            var stack = new StackPanel { Spacing = Gap };
            ((FrameworkElement)upper!).MinHeight = 240;
            ((FrameworkElement)lowerLeft!).MinHeight = 220;
            stack.Children.Add(upper);
            stack.Children.Add(lowerLeft);
            var scroll = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Place(tall, scroll, 1, 1);
            Place(tall, lowerRight!, 2, 1);
            host.Children.Add(tall);
        }
        else
        {
            wide.Children.Clear();
            wide.ColumnDefinitions.Clear();
            wide.RowDefinitions.Clear();
            wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wide.ColumnDefinitions.Add(new ColumnDefinition());
            wide.ColumnDefinitions.Add(new ColumnDefinition());
            wide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarHeight) });
            wide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            wide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            ((FrameworkElement)upper!).MinHeight = 0;
            ((FrameworkElement)lowerLeft!).MinHeight = 0;
            Place(wide, main, 0, 0, rowSpan: 3);
            Place(wide, bar!, 0, 1, colSpan: 2);
            Place(wide, upper, 1, 1, colSpan: 2);
            Place(wide, lowerLeft, 2, 1);
            Place(wide, lowerRight!, 2, 2);
            host.Children.Add(wide);
        }
        Size();
        if (appear)
        {
            var i = 0;
            foreach (var e in new[] { main, bar, upper, lowerLeft, lowerRight })
            {
                var index = i++;
                if (e is FrameworkElement fe && fe.IsLoaded)
                {
                    Motion.Appear(fe, index);
                }
                else if (e is FrameworkElement f)
                {
                    void once(object s, RoutedEventArgs a) { f.Loaded -= once; Motion.Appear(f, index); }
                    f.Loaded += once;
                }
            }
        }
    }

    /// 列宽：右边两列各一份、最宽 400，左大卡占剩下的；窄档右侧占 46%（至少 300）。
    private void Size()
    {
        var w = ActualWidth - 32;
        var h = ActualHeight - 22;
        if (w <= 0)
        {
            return;
        }
        if (narrow == true)
        {
            tall.ColumnDefinitions[1].Width = new GridLength(Math.Max(300, w * 0.46));
            tall.RowDefinitions[2].Height = new GridLength(Math.Min(280, h * 0.42));
        }
        else if (wide.ColumnDefinitions.Count == 3)
        {
            var unit = (w - 2 * Gap) / (LeftFraction + 2);
            var col = Math.Min(MaxColumn, unit);
            wide.ColumnDefinitions[1].Width = new GridLength(col);
            wide.ColumnDefinitions[2].Width = new GridLength(col);
        }
    }

    /// 左大卡占几份（右边两列各一份）。图片 1.3，有预览的 1.5。
    private double LeftFraction => ws.Type == MediaType.Image && !(ws.IsCropping && ws.CropEditing) ? 1.3 : 1.5;

    private static void Place(Grid g, UIElement e, int row, int col, int rowSpan = 1, int colSpan = 1)
    {
        Grid.SetRow((FrameworkElement)e, row);
        Grid.SetColumn((FrameworkElement)e, col);
        Grid.SetRowSpan((FrameworkElement)e, rowSpan);
        Grid.SetColumnSpan((FrameworkElement)e, colSpan);
        g.Children.Add(e);
    }

    private static void Detach(params UIElement?[] items)
    {
        foreach (var e in items)
        {
            if (e is FrameworkElement { Parent: Panel p })
            {
                p.Children.Remove(e);
            }
        }
    }

    /// 标题栏正中的分段：视频（压缩 / 剪辑 / 序列帧）、PDF（五段）；图片只有一页，不用分段。
    public static UIElement? Segments(Workspace ws)
    {
        UIElement? seg = ws.Type switch
        {
            MediaType.Video => Tabs(SimpleSmaller.Segments.AllVideo, ws.VideoSegment, s => s.Title(), s => { ws.EnterVideoSegment(s); ws.ShowResult = false; }),
            MediaType.Pdf => Tabs(SimpleSmaller.Segments.AllPdf, ws.PdfSegment, s => s.Title(), s => { ws.EnterPdfSegment(s); ws.ShowResult = false; }),
            _ => null,
        };
        if (seg is Control c)
        {
            c.IsEnabled = !ws.Running;
        }
        return seg;
    }

    private static UIElement Tabs<T>(IEnumerable<T> all, T selected, Func<T, string> title, Action<T> pick) where T : notnull
    {
        var s = new Bento.Segments<T>(Bento.Segments<T>.Kind.Top, all.Select(v => (v, title(v), (string?)null)), selected, "视图");
        s.Changed += pick;
        return s;
    }
}
