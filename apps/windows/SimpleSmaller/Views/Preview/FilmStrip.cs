using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;
using SimpleSmaller.Views.Common;

namespace SimpleSmaller.Views.Preview;

/// 底部胶片条（裁切的图、剪辑与序列帧的视频共用）：当前这张外圈品牌红、内圈卡片色，
/// 右下角放一个小标（调过的裁切、截取后的时长）；切换时自动滚到当前这张。
internal sealed class FilmStrip : UserControl
{
    private readonly ScrollViewer scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Disabled,
        Height = 64,
    };
    private readonly StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Action<int> select;
    private readonly Func<FileItem, string> label;
    private readonly Func<FileItem, UIElement?> badge;
    private readonly List<Cell> cells = new();
    private string built = "";
    private int index = -1;

    public FilmStrip(Action<int> select, Func<FileItem, string>? label = null, Func<FileItem, UIElement?>? badge = null)
    {
        this.select = select;
        this.label = label ?? (f => f.Name);
        this.badge = badge ?? (_ => null);
        scroll.Content = row;
        Content = scroll;
        AutomationProperties.SetName(scroll, "胶片条");
    }

    /// 换一批文件或当前张、小标变了都调一次。
    public void Update(IReadOnlyList<FileItem> items, int current)
    {
        var key = string.Join(",", items.Select(i => i.Id));
        if (key != built)
        {
            built = key;
            row.Children.Clear();
            cells.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                var k = i;
                var cell = new Cell(items[i]);
                cell.Click += (_, _) => select(k);
                cells.Add(cell);
                row.Children.Add(cell);
            }
            index = -1;
        }
        for (var i = 0; i < cells.Count; i++)
        {
            cells[i].Set(i == current, label(cells[i].Item), badge(cells[i].Item));
        }
        if (current != index)
        {
            var first = index < 0;
            index = current;
            ScrollTo(current, animated: !first);
        }
    }

    private void ScrollTo(int i, bool animated)
    {
        if (i < 0 || i >= cells.Count)
        {
            return;
        }
        void go()
        {
            var cell = cells[i];
            if (cell.ActualWidth <= 0) return;
            var x = cell.TransformToVisual(row).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
            var target = Math.Max(0, x + cell.ActualWidth / 2 - scroll.ViewportWidth / 2);
            scroll.ChangeView(target, null, null, !animated || !Motion.Enabled);
        }
        if (cells[i].ActualWidth > 0) go();
        else cells[i].Loaded += (_, _) => go();
    }

    private sealed class Cell : Button
    {
        public FileItem Item { get; }
        private readonly Border outer = new() { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Padding = new Thickness(0) };
        private readonly Border inner = new() { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2), Padding = new Thickness(0) };
        private readonly Grid frame = new() { Width = 72, Height = 48 };
        private readonly ContentPresenter badgeHost = new() { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(3) };
        private bool current;

        public Cell(FileItem item)
        {
            Item = item;
            Style = (Style)Application.Current.Resources["BentoBareButton"];
            Motion.SetPress(this, true);
            var thumb = new Thumb(item, 9);
            frame.Children.Add(thumb);
            frame.Children.Add(badgeHost);
            inner.Child = frame;
            outer.Child = inner;
            Content = outer;
            Themed.OnTheme(this, Paint);
        }

        public void Set(bool current, string name, UIElement? badge)
        {
            this.current = current;
            AutomationProperties.SetName(this, name);
            AutomationProperties.SetItemStatus(this, current ? "已选" : "");
            if (!ReferenceEquals(badgeHost.Content, badge))
            {
                var appear = badge is not null && badgeHost.Content is null;
                badgeHost.Content = badge;
                if (appear && badge is FrameworkElement fe)
                {
                    Motion.Appear(fe);
                }
            }
            Paint();
        }

        private void Paint()
        {
            var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            outer.BorderBrush = current ? Palette.Brand : clear;
            inner.BorderBrush = current ? Palette.Get(this, "BentoCard") : clear;
        }
    }
}
