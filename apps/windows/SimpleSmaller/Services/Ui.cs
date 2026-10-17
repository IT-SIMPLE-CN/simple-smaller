using System.Diagnostics;

namespace SimpleSmaller;

/// 界面线程：模型层的状态只在这里改。界面层启动时把 DispatcherQueue.TryEnqueue 包成 Post 交进来。
internal static class Ui
{
    /// 投递到界面线程，先进先出。没设置时（自检、测试）就地执行。
    public static Action<Action> Post { get; set; } = a => a();

    /// 等界面线程执行完再继续（相当于 `await MainActor.run`）。
    public static Task RunAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { action(); tcs.SetResult(); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    public static Task<T> RunAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }
}

/// 处理日志：写到调试输出（DebugView / VS 输出窗口可看）。
internal static class Log
{
    public static void Info(string message) => Trace.WriteLine($"[smaller] {message}");
    public static void Error(string message) => Trace.WriteLine($"[smaller] 错误 {message}");
}
