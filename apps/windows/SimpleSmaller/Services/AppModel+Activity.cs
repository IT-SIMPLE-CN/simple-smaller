namespace SimpleSmaller;

/// 处理中：不让系统睡眠，任务栏显示进度，后台完成时给角标。
internal sealed partial class AppModel
{
    /// 圆环 / 小窗、监视文件夹那些直接处理的任务还在跑（由对应模块挂上来）。
    public List<Func<bool>> OtherBusy { get; } = [];

    /// 有任务在跑时，不让系统闲置睡眠（屏幕可以照常关）。
    public void BeginActivity()
    {
        activity ??= Platform.BeginActivity("正在压缩文件");
        UpdateDock();
    }

    public void EndActivityIfIdle()
    {
        if (IsBusy || activity == null) { UpdateDock(); return; }
        activity.Dispose();
        activity = null;
        UpdateDock();
    }

    /// 主窗口任意一类在跑，或圆环 / 小窗直接处理的任务没完。
    public bool IsBusy => MediaTypes.All.Any(t => Workspace(t).Running) || OtherBusy.Any(f => f());

    /// 任务栏：处理中写「3/9」；后台做完写完成个数，回到应用后清掉。
    public void UpdateDock()
    {
        var ws = MediaTypes.All.Select(Workspace).FirstOrDefault(w => w.Running);
        if (ws != null)
            Platform.SetBadge($"{ws.FinishedCount}/{ws.Files.Count}");
        else
            Platform.SetBadge(Unseen > 0 ? $"{Unseen}" : null);
    }

    public void NoteFinishedInBackground(int n)
    {
        if (!Platform.IsAppActive) Unseen += n;
        UpdateDock();
    }

    public void ClearUnseen()
    {
        Unseen = 0;
        UpdateDock();
    }
}
