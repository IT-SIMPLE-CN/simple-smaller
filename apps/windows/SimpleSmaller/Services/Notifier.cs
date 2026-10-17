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
