using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SimpleSmaller.Views;

/// 代码搭的视图基类：订阅模型的属性变化与集合变化，变了就调 Refresh。
/// 只在显示时订阅（Loaded 订阅、Unloaded 退订），不让关掉的卡片继续收通知、也不泄漏。
internal abstract class Reactive : UserControl
{
    private readonly List<(Action Attach, Action Detach)> watches = new();
    private bool attached;
    private bool queued;

    protected Reactive()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        ActualThemeChanged += (_, _) => Refresh();
    }

    /// 属性变化时刷新；props 为空表示任何属性。
    protected void Watch(INotifyPropertyChanged source, params string[] props)
    {
        PropertyChangedEventHandler h = (_, e) =>
        {
            if (props.Length == 0 || e.PropertyName is null || props.Contains(e.PropertyName))
            {
                Schedule();
            }
        };
        watches.Add((() => source.PropertyChanged += h, () => source.PropertyChanged -= h));
    }

    protected void WatchList(INotifyCollectionChanged source)
    {
        NotifyCollectionChangedEventHandler h = (_, _) => Schedule();
        watches.Add((() => source.CollectionChanged += h, () => source.CollectionChanged -= h));
    }

    /// 同一轮里多次变化只刷新一次。
    protected void Schedule()
    {
        if (queued)
        {
            return;
        }
        queued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            queued = false;
            if (attached)
            {
                Refresh();
            }
        });
    }

    private void Attach()
    {
        if (attached)
        {
            return;
        }
        attached = true;
        foreach (var w in watches)
        {
            w.Attach();
        }
        Refresh();
    }

    private void Detach()
    {
        if (!attached)
        {
            return;
        }
        attached = false;
        foreach (var w in watches)
        {
            w.Detach();
        }
    }

    protected abstract void Refresh();
}
