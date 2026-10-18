using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.System;

namespace SimpleSmaller.Views.Main;

/// 红色「开始」块（设计稿 V2.1 · 02 / 05 / 06），四种状态大小不变：
/// 没文件浅红置灰 →「开始」→ 处理中（进度，点按停止）→「已完成」（改了设置又变回「开始」）。
internal sealed class StartBlock : Reactive
{
    private enum Phase { Empty, Ready, Running, Done }

    private static readonly SolidColorBrush White = new(Microsoft.UI.Colors.White);
    private readonly Workspace ws;
    private readonly Button button = new() { Style = (Style)Application.Current.Resources["BentoBareButton"] };
    private readonly Border back = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 0, 14, 0), MinHeight = 80 };
    private readonly ColorShadow glow = new(Windows.UI.Color.FromArgb(77, 0xEF, 0x39, 0x22), 24, 10, 18);
    private readonly StackPanel texts = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock big = new() { FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 40, LineHeight = 34, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
    private readonly TextBlock count = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 1) };
    private readonly TextBlock small = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid bar = new() { Height = 6 };
    private readonly Border barFill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = White };
    private readonly Border knob = new() { Width = 60, Height = 60, CornerRadius = new CornerRadius(30) };
    private readonly Grid knobIcon = new();
    private Phase? last;
    private bool? lastDark;

    public StartBlock(Workspace ws)
    {
        this.ws = ws;
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(big);
        head.Children.Add(count);
        texts.Children.Add(head);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 255, 255, 255)) });
        bar.Children.Add(barFill);
        AutomationProperties.SetAccessibilityView(bar, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        texts.Children.Add(bar);
        texts.Children.Add(small);
        knob.Child = knobIcon;
        AutomationProperties.SetAccessibilityView(knob, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(texts);
        Grid.SetColumn(knob, 1);
        knob.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(knob);
        back.Child = row;
        button.Content = back;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        Motion.SetPress(button, true);
        button.Click += (_, _) => Tap();
        button.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = VirtualKey.Enter, Modifiers = VirtualKeyModifiers.Control });
        var stop = new KeyboardAccelerator { Key = (VirtualKey)190, Modifiers = VirtualKeyModifiers.Control };
        stop.Invoked += (_, e) => { if (ws.Running) { AppModel.Shared.Cancel(ws); e.Handled = true; } };
        button.KeyboardAccelerators.Add(stop);
        button.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        // 柔光放在按钮下面、尺寸跟按钮一样；按下回弹时柔光不动。
        var root = new Grid();
        root.Children.Add(glow.Host);
        root.Children.Add(button);
        Content = root;
        bar.SizeChanged += (_, _) => Refresh();
        back.SizeChanged += (_, _) => Schedule();
        Watch(ws);
        WatchList(ws.Files);
        Watch(AppModel.Shared.Settings);
    }

    private Phase Current
    {
        get
        {
            if (ws.Files.Count == 0 || !ws.CanStart) return Phase.Empty;
            if (ws.Running) return Phase.Running;
            if (ws.IsUpToDate) return Phase.Done;
            return Phase.Ready;
        }
    }

    private void Tap()
    {
        switch (Current)
        {
            case Phase.Ready: AppModel.Shared.Start(ws); break;
            case Phase.Running: AppModel.Shared.Cancel(ws); break;
        }
    }

    private static SolidColorBrush Rgb(byte r, byte g, byte b, byte a = 255) => new(Windows.UI.Color.FromArgb(a, r, g, b));

    protected override void Refresh()
    {
        var phase = Current;
        var dark = ActualTheme == ElementTheme.Dark;
        // 不能开始：浅红底、白字、深红副标题，圆钮半透明白、箭头淡红（设计稿 02）。
        back.Background = phase switch
        {
            Phase.Empty => dark ? Rgb(0x3A, 0x22, 0x1E) : Rgb(0xF7, 0xD3, 0xCC),
            Phase.Done => Palette.Get(this, "BentoInk"),
            _ => Palette.Brand,
        };
        big.Foreground = count.Foreground = phase switch
        {
            Phase.Empty => dark ? Rgb(255, 255, 255, 110) : White,
            Phase.Done => Palette.Get(this, "BentoOnInk"),
            _ => White,
        };
        small.Foreground = phase switch
        {
            Phase.Empty => Palette.Get(this, "BentoAlertText"),
            Phase.Done => Palette.Get(this, "BentoOnInk"),
            _ => White,
        };
        small.Opacity = phase == Phase.Empty ? 1 : 0.95;
        var running = phase == Phase.Running;
        bar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        count.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        big.FontSize = running ? 24 : 30;
        big.CharacterSpacing = running ? 0 : 40;
        big.LineHeight = running ? 28 : 34;
        // 处理中三行等距 8；其余标题与副标题之间 4。
        texts.Spacing = running ? 8 : 0;
        small.Margin = new Thickness(0, running ? 0 : 4, 0, 0);
        if (running)
        {
            big.Text = "处理中";
            count.Text = $"{ws.FinishedCount} / {ws.Files.Count}";
            var fraction = (ws.FinishedCount + RunningFraction) / Math.Max(1, ws.Files.Count);
            barFill.Width = Math.Max(6, bar.ActualWidth * Math.Min(1, fraction));
            small.Text = "停止 · Ctrl+.";
        }
        else
        {
            big.Text = phase == Phase.Done ? "已完成" : "开始";
            // 放得下就带快捷键，放不下先去掉快捷键，再不够才折行。
            var sub = Subtitle(phase);
            small.Text = phase == Phase.Ready && Fits(sub + " · Ctrl+Enter") ? sub + " · Ctrl+Enter" : sub;
        }
        knob.Background = phase switch
        {
            Phase.Empty => dark ? Rgb(255, 255, 255, 30) : Rgb(255, 255, 255, 153),
            Phase.Done => Rgb(128, 128, 128, 36),
            _ => White,
        };
        if (last != phase || lastDark != dark || knobIcon.Children.Count == 0)
        {
            lastDark = dark;
            knobIcon.Children.Clear();
            Microsoft.UI.Xaml.Shapes.Path path;
            switch (phase)
            {
                case Phase.Running:
                    knobIcon.Children.Add(Glyph24.Create(Icons.StopFill, 20, out path));
                    path.Fill = Palette.Brand;
                    break;
                case Phase.Done:
                    knobIcon.Children.Add(Glyph24.Create(Icons.Check, 26, out path, 2.4));
                    path.Stroke = Palette.Get(this, "BentoOnInk");
                    break;
                default:
                    knobIcon.Children.Add(Glyph24.Create(Icons.ArrowRight, 26, out path, 2.4));
                    path.Stroke = phase == Phase.Empty ? (dark ? Rgb(0xA0, 0x67, 0x5E) : Rgb(0xEF, 0xA5, 0x95)) : Palette.Brand;
                    break;
            }
        }
        button.IsEnabled = phase is Phase.Ready or Phase.Running;
        // 可以开始、处理中时底下带一圈品牌红柔光（0 10px 24px rgba(239,57,34,.3)）。
        glow.Visible = phase is Phase.Ready or Phase.Running;
        ToolTipService.SetToolTip(button, phase switch
        {
            Phase.Running => "停止处理（Ctrl+.）",
            Phase.Done => "这一批已按当前设置处理完；改设置或加文件后可以再开始",
            _ => "按当前设置处理全部文件（Ctrl+Enter）",
        });
        AutomationProperties.SetName(button, phase switch
        {
            Phase.Empty => $"开始，{Subtitle(phase)}",
            Phase.Ready => $"开始，{ws.StartSummary}",
            Phase.Running => $"处理中 {ws.FinishedCount} / {ws.Files.Count}，点按停止",
            _ => "已完成",
        });
        if (last is { } l && l != phase)
        {
            Motion.SpringScale(back, 1.02f, 0.5f);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Motion.SpringScale(back, 1f, 0.7f));
        }
        last = phase;
    }

    /// 一行放得下这段副标题吗（按文字区的实际宽度量）。
    private bool Fits(string text)
    {
        // 文字区 = 整块宽 − 左右内边距（22 + 14）− 圆钮 60 − 间距 12。
        var width = back.ActualWidth - 22 - 14 - 60 - 12;
        if (width <= 0)
        {
            return true;
        }
        var probe = new TextBlock { Text = text, FontSize = small.FontSize, FontFamily = small.FontFamily };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width <= width;
    }

    private double RunningFraction => ws.Files.Where(f => f.State.IsRunning).Sum(f => f.State.Fraction);

    private string Subtitle(Phase phase) => phase switch
    {
        Phase.Empty => ws.Files.Count == 0 ? "先把文件拖进来" : (ws.BlockReason ?? ""),
        Phase.Done => "改了设置可以再开始",
        _ => ws.StartSummary,
    };
}
