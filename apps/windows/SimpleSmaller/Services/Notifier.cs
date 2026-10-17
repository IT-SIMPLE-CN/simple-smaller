namespace SimpleSmaller;

/// 系统通知（Windows 通知中心）。由界面层实现（AppNotificationManager），点按时在资源管理器中选中 `paths`。
internal interface INotifier
{
    void Show(string title, string subtitle, string body, IReadOnlyList<string> paths);
}

/// 后台任务完成且应用不在前台时发系统通知，点按在资源管理器中显示。文案在这里拼好，界面层只负责发。
internal static class Notifier
{
    /// 没接上时不发。
    public static INotifier? Current { get; set; }

    /// 监视文件夹处理完：不管应用在不在前台都发，因为用户通常不看本应用。
    public static void WatchFinished(string folder, QuickRun run)
    {
        if (Current == null || run.Outputs.Count == 0) return;
        Current.Show($"简单小一点 · {folder}", "点按在资源管理器中显示",
                     $"{run.Title} 已按「{run.Recipe.Name}」处理，{Format.Size(run.Before)} → {Format.Size(run.After)}", run.Outputs);
    }

    public static void JobFinished(string title, ulong after, IReadOnlyList<string> outputs)
    {
        if (Current == null || AppModel.Shared.Platform.IsAppActive || outputs.Count == 0) return;
        Current.Show("简单小一点", "点按在资源管理器中显示", $"{title} 已完成，{Format.Size(after)}", outputs);
    }
}

/// 系统通知的实现：Windows 通知中心。先用 AppNotificationManager（非打包应用要先 Register）；
/// 自带运行库（self-contained）的非打包构建里 Register 会报 0x8007007E，这时退回系统的 ToastNotificationManager：
/// 在 HKCU\Software\Classes\AppUserModelId 登记应用名与图标，按这个 AUMID 发通知。
/// 点按通知（应用在跑时）在资源管理器中选中输出。
internal sealed class AppNotifier : INotifier
{
    public const string Aumid = "cn.it-simple.smaller";
    private bool appNotifications;
    private global::Windows.UI.Notifications.ToastNotifier? toaster;

    /// 启动时、建窗口之前调用一次。先挂点按事件再注册（顺序反了收不到）。
    public void Setup()
    {
        try
        {
            var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
            manager.NotificationInvoked += (_, e) => Ui.Post(() => RevealFrom(e.Arguments));
            manager.Register("简单小一点", new Uri(IconPath));
            appNotifications = true;
            return;
        }
        catch (Exception e)
        {
            Log.Error($"AppNotificationManager 不可用，改用系统通知：{e.Message}");
        }
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{Aumid}"))
            {
                key.SetValue("DisplayName", "简单小一点");
                key.SetValue("IconUri", IconPath);
            }
            SetCurrentProcessExplicitAppUserModelID(Aumid);
            toaster = global::Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(Aumid);
        }
        catch (Exception e)
        {
            Log.Error($"通知注册失败：{e.Message}");
        }
    }

    private static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon-256.png");

    public void Teardown()
    {
        if (!appNotifications) return;
        try { Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Unregister(); } catch { }
    }

    public void Show(string title, string subtitle, string body, IReadOnlyList<string> paths)
    {
        // 参数里放路径（太多时只放前 20 个，资源管理器里选中前 20 个就够找了）。
        var joined = string.Join("|", paths.Take(20));
        try
        {
            if (appNotifications)
            {
                var builder = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddArgument("action", "reveal")
                    .AddArgument("paths", joined)
                    .AddText(title)
                    .AddText(body)
                    .SetAttributionText(subtitle);
                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(builder.BuildNotification());
                return;
            }
            if (toaster is null) return;
            static string esc(string t) => System.Security.SecurityElement.Escape(t) ?? "";
            var xml = new global::Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml($"<toast launch=\"{esc("action=reveal")}\"><visual><binding template=\"ToastGeneric\">" +
                        $"<text>{esc(title)}</text><text>{esc(body)}</text><text placement=\"attribution\">{esc(subtitle)}</text>" +
                        "</binding></visual></toast>");
            var toast = new global::Windows.UI.Notifications.ToastNotification(xml);
            var list = paths.Take(20).ToList();
            toast.Activated += (_, _) => Ui.Post(() => RevealFrom(new Dictionary<string, string> { ["paths"] = string.Join("|", list) }));
            toaster.Show(toast);
        }
        catch (Exception e)
        {
            Log.Error($"发通知失败：{e.Message}");
        }
    }

    /// 通知参数里取出路径，在资源管理器中选中。
    public static void RevealFrom(IDictionary<string, string> args)
    {
        if (!args.TryGetValue("paths", out var joined) || string.IsNullOrEmpty(joined)) return;
        var paths = joined.Split('|', StringSplitOptions.RemoveEmptyEntries).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (paths.Count > 0) AppModel.Shared.Reveal(paths);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
}
