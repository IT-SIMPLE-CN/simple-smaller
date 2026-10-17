using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SimpleSmaller.Views.Sub;

/// 「发现新版本」询问（对应 mac Sparkle 的更新弹窗）：版本号、更新说明，现在更新 / 以后再说。
internal static class UpdateDialog
{
    public static async Task<bool> AskAsync(string version, string notes)
    {
        if (App.Main?.Content is not FrameworkElement root || root.XamlRoot is null) return false;
        App.ShowMain();
        var body = new StackPanel { Spacing = 10, MaxWidth = 420 };
        body.Children.Add(new TextBlock { Text = $"简单小一点 {version} 可以更新了，当前是 {Updates.Shared.CurrentVersion}。更新后会自动重新打开。", TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(notes))
        {
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 240,
                Content = new TextBlock { Text = Plain(notes), TextWrapping = TextWrapping.Wrap, FontSize = 13, Opacity = 0.85 },
            });
        }
        var dialog = new ContentDialog
        {
            Title = "发现新版本",
            Content = body,
            PrimaryButtonText = "现在更新",
            CloseButtonText = "以后再说",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.ActualTheme,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// 更新说明是 Markdown 列表，这里只去掉标记按纯文本显示。
    private static string Plain(string markdown) =>
        string.Join("\n", markdown.Split('\n').Select(l => l.TrimStart().StartsWith("- ") ? "· " + l.TrimStart()[2..] : l.TrimStart('#', ' ')));
}
