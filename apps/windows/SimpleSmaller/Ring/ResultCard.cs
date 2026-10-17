using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Ring;

/// 圆环执行后的结果卡（设计稿 07，出现在指针旁）：
/// 「4 张 · 网站用」「71.4 MB → 1.6 MB −98%」，在资源管理器中显示 / 拷贝 / 撤销。指针离开 6 秒后收起。
internal sealed class ResultCard : Grid
{
    public const double CardWidth = 300;
    private readonly QuickRun run;
    private readonly Action onClose;
    private readonly StackPanel body = new() { Spacing = 10, Padding = new Thickness(14), Width = CardWidth };
    private bool undone;

    public ResultCard(QuickRun run, Action onClose)
    {
        this.run = run;
        this.onClose = onClose;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(body);
        run.PropertyChanged += OnRunChanged;
        Unloaded += (_, _) => run.PropertyChanged -= OnRunChanged;
        ActualThemeChanged += (_, _) => Build();
        Build();
    }

    private void OnRunChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Build);

    private string CountText
    {
        get
        {
            var n = run.Paths.Count;
            if (n == 1) return System.IO.Path.GetFileName(run.Paths[0]);
            return run.Type switch
            {
                MediaType.Image => $"{n} 张",
                MediaType.Video => $"{n} 段",
                _ => $"{n} 个",
            };
        }
    }

    private void Build()
    {
        body.Children.Clear();
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(Icon());
        var title = Themed.Text($"{CountText} · {run.Recipe.Name}", 13, semibold: true);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        body.Children.Add(header);

        var phase = run.Phase;
        switch (phase.Kind)
        {
            case QuickRunPhaseKind.Running:
                body.Children.Add(Themed.Text(phase.Total > 1 ? $"处理中 {phase.Done} / {phase.Total}…" : "处理中…", 12, "BentoSecondary"));
                if (run.Type == MediaType.Video)
                {
                    var note = Themed.Text("可以先收起，处理完会发通知。", 11, "BentoSecondary");
                    note.Opacity = 0.7;
                    body.Children.Add(note);
                }
                break;
            case QuickRunPhaseKind.Failed:
            {
                var message = Themed.Text(phase.Message ?? "", 12, "BentoSecondary");
                message.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(message);
                var ok = new CapsuleButton("好", 26, "BentoFill");
                ok.HorizontalAlignment = HorizontalAlignment.Right;
                ok.Click += (_, _) => onClose();
                body.Children.Add(ok);
                break;
            }
            default:
                if (undone)
                {
                    body.Children.Add(Themed.Text("已撤销，新文件移到了回收站。", 12, "BentoSecondary"));
                    break;
                }
                var sizes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                sizes.Children.Add(Themed.Text($"{Format.Size(run.Before)} → {Format.Size(run.After)}", 12, "BentoSecondary"));
                if (run.After > 0 && run.After < run.Before)
                {
                    sizes.Children.Add(Colored(Format.Change(run.Before, run.After), 12, Palette.Saving, true));
                }
                body.Children.Add(sizes);
                if (run.Kept > 0)
                {
                    body.Children.Add(Colored($"{run.Kept} 个压完没变小，保留原文件", 11, Palette.Warning, false));
                }
                var actions = new Grid { ColumnSpacing = 6 };
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                actions.ColumnDefinitions.Add(new ColumnDefinition());
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var enabled = run.Outputs.Count > 0;
                var reveal = Button("在资源管理器中显示", enabled, () => { run.Reveal(); onClose(); });
                var copy = Button("拷贝", enabled, async () => { await run.CopyAsync(); onClose(); });
                var undo = Button("撤销", enabled, () =>
                {
                    try { run.Undo(); } catch (Exception e) { Log.Error($"撤销：{e.Message}"); }
                    undone = true;
                    Build();
                });
                Grid.SetColumn(copy, 1);
                Grid.SetColumn(undo, 3);
                actions.Children.Add(reveal);
                actions.Children.Add(copy);
                actions.Children.Add(undo);
                body.Children.Add(actions);
                break;
        }
    }

    /// 固定颜色的字（省下 / 警告色不随深浅色变；Themed.Text 加载时会改回主题色）。
    private static TextBlock Colored(string text, double size, Brush brush, bool semibold) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontWeight = semibold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
    };

    private static CapsuleButton Button(string title, bool enabled, Action action)
    {
        var b = new CapsuleButton(title, 26, "BentoFill") { IsEnabled = enabled };
        if (b.Content is Border back)
        {
            back.Padding = new Thickness(11, 0, 11, 0);
            if (back.Child is TextBlock t) t.FontSize = 12;
        }
        b.Click += (_, _) => action();
        return b;
    }

    private FrameworkElement Icon()
    {
        var phase = run.Phase.Kind;
        if (phase == QuickRunPhaseKind.Running)
        {
            var ring = new ProgressRing { Width = 16, Height = 16, IsActive = true, Foreground = Palette.Brand };
            AutomationProperties.SetName(ring, "处理中");
            return ring;
        }
        var icon = new FontIcon { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        if (phase == QuickRunPhaseKind.Failed)
        {
            icon.Glyph = "";
            icon.Foreground = Palette.Warning;
        }
        else if (undone)
        {
            icon.Glyph = "";
            icon.Foreground = Palette.Get(this, "BentoSecondary");
        }
        else
        {
            icon.Glyph = "";
            icon.Foreground = Palette.Saving;
        }
        AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return icon;
    }
}
