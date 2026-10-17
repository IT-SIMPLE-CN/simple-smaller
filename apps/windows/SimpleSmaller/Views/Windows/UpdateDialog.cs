using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Sub;

/// 「发现新版本」询问（对应 mac Sparkle 的更新弹窗）：设计稿没有这一屏，按设置 / 配方编辑的便当盒样子做——
/// 白底小窗，红色圆形图标 + 22 号粗体标题 + 一句说明，更新说明放在圆角 20 的灰底块里，
/// 右下灰底「以后再说」与品牌红「现在更新」两个 44 高胶囊。回车 = 现在更新，Esc / 关窗 = 以后再说。
internal static class UpdateDialog
{
    private static UpdateWindow? open;

    public static Task<bool> AskAsync(string version, string notes)
    {
        if (open is not null)
        {
            open.Bring();
            return open.Result;
        }
        App.ShowMain();
        var w = new UpdateWindow(version, notes);
        open = w;
        w.Closed += (_, _) => open = null;
        w.Activate();
        return w.Result;
    }

    /// 更新说明是 Markdown 列表，这里只去掉标记按纯文本显示。
    internal static string Plain(string markdown) =>
        string.Join("\n", markdown.Split('\n').Select(l => l.TrimStart().StartsWith("- ") ? "· " + l.TrimStart()[2..] : l.TrimStart('#', ' ')));
}

internal sealed class UpdateWindow : SubWindow
{
    private readonly TaskCompletionSource<bool> result = new();

    public Task<bool> Result => result.Task;

    public UpdateWindow(string version, string notes) : base("发现新版本", 520, string.IsNullOrWhiteSpace(notes) ? 260 : 440, "BentoCard", showTitle: false)
    {
        OverlayTitle();
        var page = new Grid { Padding = new Thickness(28, 24, 28, 22), RowSpacing = 14 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var circle = new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Palette.Brand, VerticalAlignment = VerticalAlignment.Top, Child = new LineIcon(SubIcons.Download, 20, 2, "OnBrand") };
        AutomationProperties.SetAccessibilityView(circle, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var texts = new StackPanel { Spacing = 4 };
        var title = FormTheme.Text("发现新版本", 22);
        title.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        var sub = FormTheme.Text($"简单小一点 {version} 可以更新了，当前是 {Updates.Shared.CurrentVersion}。更新后会自动重新打开。", 12, "BentoSecondary");
        sub.TextWrapping = TextWrapping.Wrap;
        sub.TextTrimming = TextTrimming.None;
        sub.MaxWidth = 390;
        texts.Children.Add(title);
        texts.Children.Add(sub);
        head.Children.Add(circle);
        head.Children.Add(texts);
        page.Children.Add(head);

        if (!string.IsNullOrWhiteSpace(notes))
        {
            var text = FormTheme.Text(UpdateDialog.Plain(notes).Trim(), 13);
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            text.LineHeight = 21;
            text.IsTextSelectionEnabled = true;
            var scroll = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(16, 12, 16, 12) };
            var group = new Border { CornerRadius = new CornerRadius(20), Child = scroll };
            FormTheme.On(group, () => group.Background = Palette.Get(group, "BentoFill"));
            AutomationProperties.SetName(scroll, "更新说明");
            Grid.SetRow(group, 1);
            page.Children.Add(group);
        }

        var foot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var later = new TextCapsule("以后再说", 44, 13, 20);
        later.Click += (_, _) => Finish(false);
        var now = new BrandButton("现在更新", 44);
        now.Click += (_, _) => Finish(true);
        foot.Children.Add(later);
        foot.Children.Add(now);
        Grid.SetRow(foot, 2);
        page.Children.Add(foot);
        Body.Children.Add(page);

        var enter = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Enter };
        enter.Invoked += (_, e) => { e.Handled = true; Finish(true); };
        Root.KeyboardAccelerators.Add(enter);
        Root.Loaded += (_, _) => now.Focus(FocusState.Programmatic);
        Closed += (_, _) => result.TrySetResult(false);
    }

    private void Finish(bool update)
    {
        result.TrySetResult(update);
        Close();
    }
}
