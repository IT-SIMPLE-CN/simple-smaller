using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Ring;

/// 圆环执行后的结果卡（设计稿 BentoTray「圆环执行后的结果卡」，出现在指针旁）：
/// 400 宽白卡、内边距 16、块间距 14。上排 52 见方圆角 14 的缩略图、「4 张 · 网站用」（15 粗）与「71.4 MB → 1.6 MB」（12 灰），
/// 右边浅红底「−98%」胶囊；中排墨黑「在资源管理器中显示」与灰底「拷贝」「撤销」（40 高，1.4 : 1 : 1）；
/// 底下一句「指针离开 6 秒后收起……」。处理中、失败、已撤销是同一张卡换文字。
internal sealed class ResultCard : Grid
{
    public const double CardWidth = 400;
    private readonly QuickRun run;
    private readonly Action onClose;
    private readonly StackPanel body = new() { Spacing = 14, Padding = new Thickness(16), Width = CardWidth };
    private Border? thumb;
    private ImageSource? thumbnail;
    private bool undone;

    public ResultCard(QuickRun run, Action onClose)
    {
        this.run = run;
        this.onClose = onClose;
        VerticalAlignment = VerticalAlignment.Top;
        Children.Add(body);
        run.PropertyChanged += OnRunChanged;
        Unloaded += (_, _) => run.PropertyChanged -= OnRunChanged;
        ActualThemeChanged += (_, _) => Build();
        Build();
        LoadThumbnail();
    }

    private void OnRunChanged(object? sender, PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(Build);

    /// 缩略图用系统缩略图（视频、PDF 也有），在后台取。
    private async void LoadThumbnail()
    {
        if (run.Paths.FirstOrDefault() is not { } path) return;
        try
        {
            if (await AppModel.Shared.Platform.LoadThumbnailAsync(path, 104) is ImageSource image)
            {
                thumbnail = image;
                PaintThumb();
            }
        }
        catch { }
    }

    private void PaintThumb()
    {
        if (thumb is null) return;
        thumb.Background = thumbnail is not null
        ? new ImageBrush { ImageSource = thumbnail, Stretch = Stretch.UniformToFill }
        : Palette.Get(this, "BentoFill");
    }

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
        Background = Palette.Get(this, "BentoCard");
        body.Children.Clear();
        // 每次重建都换一个新的缩略图方块（同一个元素挪到新父级时 WinUI 会在原生层崩溃）。
        thumb = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(thumb, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        PaintThumb();
        var phase = run.Phase;

        // 上排：缩略图 · 标题与说明 · 右侧（省下比例 / 处理中转圈）。
        var head = new Grid { ColumnSpacing = 12 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(thumb);
        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = FormTheme.Text($"{CountText} · {run.Recipe.Name}", 15, semibold: true);
        texts.Children.Add(title);
        string sub = phase.Kind switch
        {
            QuickRunPhaseKind.Running => phase.Total > 1 ? $"处理中 {phase.Done} / {phase.Total}…" : "处理中…",
            QuickRunPhaseKind.Failed => phase.Message ?? "没有处理成功",
            _ => undone ? "已撤销，新文件移到了回收站。" : $"{Format.Size(run.Before)} → {Format.Size(run.After)}",
        };
        var subText = FormTheme.Text(sub, 12, phase.Kind == QuickRunPhaseKind.Failed ? "BentoAlertText" : "BentoSecondary");
        subText.TextWrapping = phase.Kind == QuickRunPhaseKind.Failed ? TextWrapping.Wrap : TextWrapping.NoWrap;
        texts.Children.Add(subText);
        Grid.SetColumn(texts, 1);
        head.Children.Add(texts);
        if (phase.Kind == QuickRunPhaseKind.Running)
        {
            var ring = new ProgressRing { Width = 20, Height = 20, IsActive = true, Foreground = Palette.Brand, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(ring, "处理中");
            Grid.SetColumn(ring, 2);
            head.Children.Add(ring);
        }
        else if (phase.Kind == QuickRunPhaseKind.Finished && !undone && run.After > 0 && run.After < run.Before)
        {
            var pct = new TextBlock { Text = Format.Change(run.Before, run.After), FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            var badge = new Border { Height = 32, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(16), Child = pct, VerticalAlignment = VerticalAlignment.Center };
            badge.Background = Palette.Get(this, "BentoAlertFill");
            pct.Foreground = Palette.Get(this, "BentoAlertText");
            Grid.SetColumn(badge, 2);
            head.Children.Add(badge);
        }
        body.Children.Add(head);

        switch (phase.Kind)
        {
            case QuickRunPhaseKind.Running:
                if (run.Type == MediaType.Video)
                    body.Children.Add(Note("可以先收起，处理完会发通知。"));
                break;
            case QuickRunPhaseKind.Failed:
            {
                var ok = new TextCapsule("好", 40, 13, 20) { HorizontalAlignment = HorizontalAlignment.Right };
                ok.Click += (_, _) => onClose();
                body.Children.Add(ok);
                break;
            }
            default:
                if (undone) break;
                if (run.Kept > 0)
                {
                    var kept = new TextBlock { Text = $"{run.Kept} 个压完没变小，保留原文件", FontSize = 11, Foreground = Palette.Warning, Margin = new Thickness(0, -6, 0, 0) };
                    body.Children.Add(kept);
                }
                var actions = new Grid { ColumnSpacing = 6 };
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var enabled = run.Outputs.Count > 0;
                var reveal = Action("在资源管理器中显示", "BentoInk", "BentoOnInk", true, enabled, () => { run.Reveal(); onClose(); });
                var copy = Action("拷贝", "BentoFill", "BentoPrimary", false, enabled, async () => { await run.CopyAsync(); onClose(); });
                var undo = Action("撤销", "BentoFill", "BentoPrimary", false, enabled, () =>
                {
                    try { run.Undo(); } catch (Exception e) { Log.Error($"撤销：{e.Message}"); }
                    undone = true;
                    Build();
                });
                Grid.SetColumn(copy, 1);
                Grid.SetColumn(undo, 2);
                actions.Children.Add(reveal);
                actions.Children.Add(copy);
                actions.Children.Add(undo);
                body.Children.Add(actions);
                body.Children.Add(Note("指针离开 6 秒后收起。撤销会把新文件移到回收站，原图从不改动。"));
                break;
        }
    }

    private static TextBlock Note(string text)
    {
        var t = FormTheme.Text(text, 11, "BentoSecondary");
        t.TextWrapping = TextWrapping.Wrap;
        t.TextTrimming = TextTrimming.None;
        t.LineHeight = 17;
        return t;
    }

    private static TextCapsule Action(string title, string fill, string text, bool semibold, bool enabled, Action action)
    {
        var b = new TextCapsule(title, 40, 13, 10, fill, text, semibold: semibold)
        {
            IsEnabled = enabled,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        b.Click += (_, _) => action();
        return b;
    }
}
