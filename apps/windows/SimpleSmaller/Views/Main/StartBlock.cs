using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;
using Windows.System;

namespace SimpleSmaller.Views.Main;

/// 红色「开始」块，四种状态大小不变：
/// 没文件置灰 →「开始」→ 处理中（进度，点按停止）→「已完成」（改了设置又变回「开始」）。
internal sealed class StartBlock : Reactive
{
    private enum Phase { Empty, Ready, Running, Done }

    private readonly Workspace ws;
    private readonly Button button = new() { Style = (Style)Application.Current.Resources["BentoBareButton"] };
    private readonly Border back = new() { CornerRadius = new CornerRadius(18), Padding = new Thickness(22, 0, 14, 0), MinHeight = 84 };
    private readonly TextBlock big = new() { FontSize = 30, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 40 };
    private readonly TextBlock count = new() { FontSize = 20, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 2) };
    private readonly TextBlock small = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Grid bar = new() { Height = 6, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Border barFill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border knob = new() { Width = 56, Height = 56, CornerRadius = new CornerRadius(28) };
    private readonly FontIcon knobIcon = new() { FontSize = 22 };
    private Phase? last;

    public StartBlock(Workspace ws)
    {
        this.ws = ws;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(big);
        head.Children.Add(count);
        texts.Children.Add(head);
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 255, 255, 255)) });
        bar.Children.Add(barFill);
        barFill.Background = new SolidColorBrush(Microsoft.UI.Colors.White);
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
        Content = button;
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

    protected override void Refresh()
    {
        var phase = Current;
        var white = new SolidColorBrush(Microsoft.UI.Colors.White);
        back.Background = phase switch
        {
            Phase.Empty => Palette.Get(this, "BentoFill"),
            Phase.Done => Palette.Get(this, "BentoInk"),
            _ => Palette.Brand,
        };
        Brush fg = phase switch
        {
            Phase.Empty => Palette.Get(this, "BentoSecondary"),
            Phase.Done => Palette.Get(this, "BentoOnInk"),
            _ => white,
        };
        big.Foreground = count.Foreground = small.Foreground = fg;
        small.Opacity = 0.95;
        var running = phase == Phase.Running;
        bar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        count.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        big.FontSize = running ? 24 : 30;
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
            // 照 mac 的 ViewThatFits：放得下就带快捷键，放不下先去掉快捷键，再不够才折行。
            var sub = Subtitle(phase);
            small.Text = phase == Phase.Ready && Fits(sub + " · Ctrl+Enter") ? sub + " · Ctrl+Enter" : sub;
        }
        knob.Background = phase switch
        {
            Phase.Empty => Palette.Get(this, "BentoCard"),
            Phase.Done => new SolidColorBrush(Windows.UI.Color.FromArgb(36, 128, 128, 128)),
            _ => white,
        };
        knobIcon.Glyph = phase switch { Phase.Running => "", Phase.Done => "", _ => "" };
        knobIcon.FontSize = running ? 18 : 22;
        knobIcon.Foreground = phase switch
        {
            Phase.Empty => Palette.Get(this, "BentoSecondary"),
            Phase.Done => Palette.Get(this, "BentoOnInk"),
            _ => Palette.Brand,
        };
        button.IsEnabled = phase is Phase.Ready or Phase.Running;
        // 可以开始、处理中时底下带一圈品牌红的柔光。
        back.Translation = new System.Numerics.Vector3(0, 0, phase is Phase.Ready or Phase.Running ? 24 : 0);
        back.Shadow ??= new ThemeShadow();
        ToolTipService.SetToolTip(button, phase switch
        {
            Phase.Running => "停止处理（Ctrl+.）",
            Phase.Done => "这一批已按当前配方处理完；改配方或加文件后可以再开始",
            _ => "按当前配方处理全部文件（Ctrl+Enter）",
        });
        AutomationProperties.SetName(button, phase switch
        {
            Phase.Empty => $"开始，{Subtitle(phase)}",
            Phase.Ready => $"开始，{ws.StartSummary}",
            Phase.Running => $"处理中 {ws.FinishedCount} / {ws.Files.Count}，按下停止",
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
        // 文字区 = 整块宽 − 左右内边距（22 + 14）− 圆钮 56 − 间距 12。
        var width = back.ActualWidth - 22 - 14 - 56 - 12;
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
