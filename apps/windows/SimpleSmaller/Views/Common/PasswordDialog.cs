using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Common;

/// 加密 PDF 输入打开密码（对应 mac PasswordSheet）。密码只用来解出临时副本，不保存。
/// 解锁（引擎重写一份不加密的副本）在后台跑，界面线程只等结果。
internal static class PasswordDialog
{
    /// 同一时间只开一个（ContentDialog 一个窗口只能显示一个）。
    private static bool open;

    /// owner：发起的控件（取它的 XamlRoot 和深浅色，强制深色时对话框跟着深色）。
    public static async Task ShowAsync(FileItem item, FrameworkElement owner)
    {
        if (open) return;
        open = true;
        try
        {
            var box = new PasswordBox { PlaceholderText = "密码" };
            AutomationProperties.SetName(box, "密码");
            var error = PdfUi.Text("", 12, "Warning");
            error.TextWrapping = TextWrapping.Wrap;
            error.Visibility = Visibility.Collapsed;
            var note = PdfUi.Text("输入密码后就能压缩、合并。密码只在这次处理里用，不会保存；输出的文件不带密码，需要的话在「选项」里另加。", 12, "BentoSecondary");
            note.TextWrapping = TextWrapping.Wrap;
            note.TextTrimming = TextTrimming.None;
            var ring = new ProgressRing { IsActive = false, Width = 16, Height = 16, Visibility = Visibility.Collapsed };
            var errorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            errorRow.Children.Add(ring);
            errorRow.Children.Add(error);
            var body = new StackPanel { Spacing = 12, Width = 340 };
            body.Children.Add(note);
            body.Children.Add(box);
            body.Children.Add(errorRow);

            var dialog = new ContentDialog
            {
                XamlRoot = owner.XamlRoot,
                Title = new TextBlock { Text = $"「{item.Name}」有打开密码", FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                Content = body,
                PrimaryButtonText = "解锁",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                RequestedTheme = owner.ActualTheme,
            };
            dialog.Resources["AccentButtonBackground"] = Palette.Brand;
            dialog.Resources["AccentButtonBackgroundPointerOver"] = Application.Current.Resources["BrandPressed"];
            dialog.Resources["AccentButtonBackgroundPressed"] = Application.Current.Resources["BrandPressed"];
            dialog.Resources["AccentButtonForeground"] = Application.Current.Resources["OnBrand"];
            dialog.Resources["AccentButtonForegroundPointerOver"] = Application.Current.Resources["OnBrand"];
            dialog.Resources["AccentButtonForegroundPressed"] = Application.Current.Resources["OnBrand"];
            box.PasswordChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Password.Length > 0;
            dialog.PrimaryButtonClick += async (_, e) =>
            {
                // 解锁要几百毫秒到几秒：先挡住关闭，后台解完再决定。
                var deferral = e.GetDeferral();
                var password = box.Password;
                dialog.IsPrimaryButtonEnabled = false;
                box.IsEnabled = false;
                error.Visibility = Visibility.Collapsed;
                ring.Visibility = Visibility.Visible;
                ring.IsActive = true;
                var message = await AppModel.Shared.UnlockAsync(item, password);
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                box.IsEnabled = true;
                if (message is not null)
                {
                    error.Text = message;
                    error.Visibility = Visibility.Visible;
                    e.Cancel = true;
                    dialog.IsPrimaryButtonEnabled = true;
                    box.SelectAll();
                    box.Focus(FocusState.Programmatic);
                }
                else
                {
                    Preview.PdfThumbnails.Shared.Forget(item.FilePath);
                }
                deferral.Complete();
            };
            dialog.Opened += (_, _) => box.Focus(FocusState.Programmatic);
            await dialog.ShowAsync();
        }
        finally
        {
            open = false;
        }
    }
}
