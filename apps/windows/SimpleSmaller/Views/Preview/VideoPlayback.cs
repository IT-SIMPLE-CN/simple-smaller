using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SimpleSmaller.Views.Preview;

/// 剪辑用的播放器（对应 mac VideoPlayback）：只在截取范围内播放，播到结尾回到开头停下。
/// 系统 MediaPlayer 自己在后台解码；界面线程上用 30 Hz 计时器读播放位置。
internal sealed class VideoPlayback : IDisposable
{
    public MediaPlayer Player { get; } = new() { AutoPlay = false, IsLoopingEnabled = false };
    public string? Path { get; private set; }
    public double Time { get; private set; }
    public bool Playing { get; private set; }
    /// 播放范围（截取）；播到上限就停。
    public TimeRange Range { get; set; }

    /// 时间或播放状态变了（界面线程）。
    public event Action? Changed;

    private readonly DispatcherQueueTimer timer;

    public VideoPlayback(DispatcherQueue queue)
    {
        timer = queue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) => Tick();
        Player.CommandManager.IsEnabled = false;
    }

    public void Load(string path, TimeRange range)
    {
        Range = range;
        if (Path == path) return;
        Path = path;
        Pause();
        Player.Source = MediaSource.CreateFromUri(new Uri(path));
        Seek(range.Start);
    }

    public void Toggle()
    {
        if (Playing) Pause(); else Play();
    }

    public void Play()
    {
        if (Time >= Range.End - 0.05 || Time < Range.Start) Seek(Range.Start);
        Player.Play();
        Playing = true;
        timer.Start();
        Changed?.Invoke();
    }

    public void Pause()
    {
        Player.Pause();
        Playing = false;
        timer.Stop();
        Changed?.Invoke();
    }

    /// 跳到某一秒（拖时间轴、对齐截取端点时用）。
    public void Seek(double t)
    {
        t = Math.Max(0, t);
        Time = t;
        try { Player.PlaybackSession.Position = TimeSpan.FromSeconds(t); } catch { }
        Changed?.Invoke();
    }

    private void Tick()
    {
        if (!Playing) return;
        var t = Player.PlaybackSession.Position.TotalSeconds;
        if (!double.IsFinite(t)) return;
        Time = t;
        if (t >= Range.End)
        {
            Pause();
            Seek(Range.Start);
            return;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        timer.Stop();
        Player.Pause();
        Player.Source = null;
        Player.Dispose();
    }
}
