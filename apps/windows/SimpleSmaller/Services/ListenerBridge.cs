using SimpleSmaller.Core;

namespace SimpleSmaller;

/// 引擎回调可能来自任意线程，统一转到界面线程。
internal sealed class ListenerBridge : JobListener
{
    private readonly Action<Action> dispatch;
    /// 下面三个都在界面线程上调用。
    public readonly Action<uint, double> Progress;
    public readonly Action<JobItemResult> Item;
    public readonly Action Finished;

    public ListenerBridge(Action<Action> dispatch, Action<uint, double> progress, Action<JobItemResult> item, Action finished)
    {
        this.dispatch = dispatch;
        Progress = progress;
        Item = item;
        Finished = finished;
    }

    // 投递队列先进先出，「完成」不会抢在最后一个结果前面。
    public void OnProgress(uint index, double fraction) => dispatch(() => Progress(index, fraction));
    public void OnItem(JobItemResult result) => dispatch(() => Item(result));
    public void OnFinished() => dispatch(() => Finished());
}

internal static class EngineErrors
{
    /// 给用户看的一句话（引擎已写成中文）。
    public static string UserMessage(this EngineException e) => e switch
    {
        EngineException.Io x => x.message,
        EngineException.Unsupported x => x.message,
        EngineException.Failed x => x.message,
        _ => e.Message,
    };

    /// 任意异常转成一句话：引擎错误用引擎的中文说明。
    public static string UserMessage(this Exception e) => e is EngineException ee ? ee.UserMessage() : e.Message;
}
