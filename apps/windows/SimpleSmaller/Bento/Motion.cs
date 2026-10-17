using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Windows.UI.ViewManagement;

namespace SimpleSmaller.Bento;

/// 动效：系统弹簧（Composition），对应 mac 的 Views/Bento/Motion.swift。
/// 「减弱动态效果」（设置 › 辅助功能 › 视觉效果 › 动画效果 关闭）时只做短淡入、不缩放。
public static class Motion
{
    private static readonly UISettings Settings = new();

    /// 系统动画效果开着。
    public static bool Enabled => Settings.AnimationsEnabled;

    /// 自定义动效统一曲线 cubic-bezier(.22,1,.36,1)。
    public static CubicBezierEasingFunction Ease(Compositor c) => c.CreateCubicBezierEasingFunction(new Vector2(0.22f, 1f), new Vector2(0.36f, 1f));

    public static Visual VisualOf(UIElement element) => ElementCompositionPreview.GetElementVisual(element);

    /// 弹簧把 Scale 推到目标值；中心点跟着元素大小走。
    public static void SpringScale(UIElement element, float to, float damping = 0.6f, double periodMs = 45)
    {
        var visual = VisualOf(element);
        if (element is FrameworkElement fe)
        {
            visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        }
        if (!Enabled)
        {
            visual.Scale = new Vector3(1);
            return;
        }
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.Target = "Scale";
        spring.FinalValue = new Vector3(to, to, 1);
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(periodMs);
        visual.StartAnimation("Scale", spring);
    }

    /// 弹簧移动 Offset（滑块底块、布局切换）。
    public static void SpringOffset(UIElement element, Vector3 to, float damping = 0.82f, double periodMs = 55)
    {
        var visual = VisualOf(element);
        if (!Enabled)
        {
            visual.Offset = to;
            return;
        }
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.Target = "Offset";
        spring.FinalValue = to;
        spring.DampingRatio = damping;
        spring.Period = TimeSpan.FromMilliseconds(periodMs);
        visual.StartAnimation("Offset", spring);
    }

    /// 卡片出现：从 94% 弹到 100% 并淡入（不透明度从 0.25 起，换内容时不闪空底），按序号错开 40 毫秒。
    public static void Appear(UIElement element, int index = 0)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        var delay = TimeSpan.FromMilliseconds(index * 40);
        if (element is FrameworkElement fe)
        {
            visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
        }

        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0.25f);
        fade.InsertKeyFrame(1, 1f, c.CreateLinearEasingFunction());
        fade.Duration = TimeSpan.FromMilliseconds(Enabled ? 160 : 180);
        fade.DelayTime = delay;
        fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);

        if (!Enabled)
        {
            visual.Scale = new Vector3(1);
            return;
        }
        // 94% → 约 101% → 100%。
        var grow = c.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(0, new Vector3(0.94f, 0.94f, 1));
        grow.InsertKeyFrame(0.6f, new Vector3(1.01f, 1.01f, 1), Ease(c));
        grow.InsertKeyFrame(1, new Vector3(1, 1, 1), Ease(c));
        grow.Duration = TimeSpan.FromMilliseconds(420);
        grow.DelayTime = delay;
        grow.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Scale", grow);
    }

    /// 换页：整体从 98% 轻微放大并淡入（首页 ↔ 工作区、工作区 ↔ 完成页）。
    public static void PageIn(UIElement element)
    {
        var visual = VisualOf(element);
        var c = visual.Compositor;
        if (element is FrameworkElement fe)
        {
            void center() => visual.CenterPoint = new Vector3((float)fe.ActualWidth / 2, (float)fe.ActualHeight / 2, 0);
            center();
            fe.SizeChanged += (_, _) => center();
        }
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0f);
        fade.InsertKeyFrame(1, 1f, Ease(c));
        fade.Duration = TimeSpan.FromMilliseconds(Enabled ? 280 : 200);
        visual.StartAnimation("Opacity", fade);
        if (!Enabled)
        {
            return;
        }
        visual.Scale = new Vector3(0.98f, 0.98f, 1);
        var spring = c.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(1, 1, 1);
        spring.DampingRatio = 0.86f;
        spring.Period = TimeSpan.FromMilliseconds(60);
        visual.StartAnimation("Scale", spring);
    }

    // ---------- 按下回弹 ----------

    public static readonly DependencyProperty PressProperty =
        DependencyProperty.RegisterAttached("Press", typeof(bool), typeof(Motion), new PropertyMetadata(false, OnPressChanged));

    public static bool GetPress(DependencyObject d) => (bool)d.GetValue(PressProperty);

    /// 按下缩到 96%，松手回弹（设计稿：所有按钮、卡片）。
    public static void SetPress(DependencyObject d, bool value) => d.SetValue(PressProperty, value);

    private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element || e.NewValue is not true)
        {
            return;
        }
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => Down(element)), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => Up(element)), true);
        element.PointerCaptureLost += (_, _) => Up(element);
        element.PointerExited += (_, _) => Up(element);
        element.PointerCanceled += (_, _) => Up(element);
    }

    private static void Down(UIElement element)
    {
        if (Enabled)
        {
            SpringScale(element, 0.96f);
        }
        else
        {
            VisualOf(element).Opacity = 0.8f;
        }
    }

    private static void Up(UIElement element)
    {
        SpringScale(element, 1f);
        VisualOf(element).Opacity = 1f;
    }
}
