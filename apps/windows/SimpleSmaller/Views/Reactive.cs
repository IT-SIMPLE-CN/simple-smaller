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
        // 换排布（窄档 / 宽档）时格子先拿出来再放进新网格：WinUI 会在 Loaded 之后才补发旧的 Unloaded，
        // 这时视图其实还在界面上，不能退订，否则之后模型怎么变它都不刷新。
        Unloaded += (_, _) => { if (!InLiveTree()) Detach(); };
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

    /// 是否还挂在窗口的界面树里（从自己往上找得到窗口内容）。
    private bool InLiveTree()
    {
        if (XamlRoot?.Content is not { } root) return false;
        for (DependencyObject? p = this; p is not null; p = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(p))
        {
            if (ReferenceEquals(p, root)) return true;
        }
        return false;
    }

    protected abstract void Refresh();
}
