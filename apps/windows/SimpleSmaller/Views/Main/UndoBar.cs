using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Main;

/// 清空 / 移除后的提示条（设计稿 V3 · 11）：左大卡底部居中、底边离卡底 78，墨黑胶囊「已清空 12 个文件」+ 浅色内胶囊「撤销 Ctrl+Z」。
/// 只显示这一类自己的那次清空 / 移除。
/// 停 5 秒（AppModel.UndoWindow）后伸缩淡出；点「撤销」或 Ctrl+Z 把文件放回去。
internal sealed class UndoBar : Grid
{
    private readonly AppModel model = AppModel.Shared;
    private readonly Workspace ws;
    private readonly Border pill = new() { Height = 44, CornerRadius = new CornerRadius(22), Padding = new Thickness(18, 0, 8, 0) };
    private readonly TextBlock text = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border undoBack = new() { Height = 30, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 0, 14, 0) };
    private readonly TextBlock undoText = new() { Text = "撤销 Ctrl+Z", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly ColorShadow shadow = new(Windows.UI.Color.FromArgb(71, 0, 0, 0), 30, 12, 22);
    private int ticket;

    public UndoBar(Workspace ws)
    {
        this.ws = ws;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 0, 78);
        Visibility = Visibility.Collapsed;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(text);
        undoBack.Child = undoText;
        var undo = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = undoBack, VerticalAlignment = VerticalAlignment.Center };
        Motion.SetPress(undo, true);
        AutomationProperties.SetName(undo, "撤销");
        AutomationProperties.SetAcceleratorKey(undo, "Ctrl+Z");
        undo.Click += (_, _) => model.UndoRemoval();
        row.Children.Add(undo);
        pill.Child = row;
        Children.Add(shadow.Host);
        Children.Add(pill);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);

        // 每张左大卡各有一个（换内容时新建，不搬动）：装上时按当前状态摆好，卸下时退订。
        Loaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            model.PropertyChanged += OnModelChanged;
            Paint();
            Sync();
        };
        // 窄档 / 宽档切换时整张卡连同提示条被搬进新网格，WinUI 会在新位置 Loaded 之后才补发旧的 Unloaded：还装着就不退订。
        Unloaded += (_, _) => { if (!IsLoaded) model.PropertyChanged -= OnModelChanged; };
        ActualThemeChanged += (_, _) => Paint();
    }

    private void Paint()
    {
        pill.Background = Palette.Get(this, "BentoInk");
        var on = ((SolidColorBrush)Palette.Get(this, "BentoOnInk")).Color;
        text.Foreground = new SolidColorBrush(on);
        undoText.Foreground = new SolidColorBrush(on);
        // 内胶囊：墨黑上叠 16% 的反色（设计稿 rgba(255,255,255,.16)）。
        undoBack.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(41, on.R, on.G, on.B));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppModel.PendingUndo)) return;
        var t = ++ticket;
        if (model.PendingUndo is { } u && u.Ws == ws)
        {
            text.Text = u.Text;
            AutomationProperties.SetName(this, $"{u.Text}，按 Ctrl+Z 撤销");
            var wasHidden = Visibility != Visibility.Visible;
            Visibility = Visibility.Visible;
            var visual = Motion.VisualOf(this);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.Opacity = 1;
            if (wasHidden) Motion.Appear(this);
            else Motion.SpringScale(this, 1f);
            if (FrameworkElementAutomationPeer.FromElement(this) is { } peer)
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        else if (Visibility == Visibility.Visible)
        {
            Hide(t);
        }
    }

    /// 重新装上时：提示条该在就直接在（不重播出现动画），不该在就收起。
    private void Sync()
    {
        var on = model.PendingUndo is { } u && u.Ws == ws;
        if (on)
        {
            text.Text = model.PendingUndo!.Text;
            AutomationProperties.SetName(this, $"{text.Text}，按 Ctrl+Z 撤销");
        }
        if (on == (Visibility == Visibility.Visible)) return;
        ++ticket;
        Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var visual = Motion.VisualOf(this);
        visual.Opacity = 1;
        visual.Scale = new Vector3(1);
    }

    /// 伸缩淡出：缩到 92% 并淡掉（0.22 秒），然后收起；「减弱动态效果」时只淡出。
    private void Hide(int t)
    {
        var visual = Motion.VisualOf(this);
        var c = visual.Compositor;
        visual.CenterPoint = new Vector3((float)ActualWidth / 2, (float)ActualHeight / 2, 0);
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, Motion.Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Opacity", fade);
        if (Motion.Enabled)
        {
            var shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1, new Vector3(0.92f, 0.92f, 1), Motion.Ease(c));
            shrink.Duration = TimeSpan.FromMilliseconds(220);
            visual.StartAnimation("Scale", shrink);
        }
        batch.End();
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (t != ticket) return;
            Visibility = Visibility.Collapsed;
            visual.Opacity = 1;
            visual.Scale = new Vector3(1);
        });
    }
}
