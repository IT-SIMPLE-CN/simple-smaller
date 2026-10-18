using Microsoft.UI.Xaml;

namespace SimpleSmaller.Views.Guide;

/// 小教学聚光框要框的界面元素：放元素的地方按名字登记，教学按名字找，不认具体控件类型。
/// 换了控件（比如顶部切换从小分段换成大标签）只要登记同一个名字就接得上。只记弱引用，不拖住界面。
internal static class GuideTargets
{
    /// 标题栏正中的顶部切换（视频、PDF 的分段 / 大标签）。
    public const string Tabs = "tabs";
    /// 左边大卡（文件 / 预览）。
    public const string Main = "main";
    /// 右上横条：常用条。
    public const string Recipes = "recipes";
    /// 右边上方的主设置卡（图片：画面；视频：画面与画质；PDF：压缩档）。
    public const string Upper = "upper";
    /// 右下格：开始块所在的卡。
    public const string Start = "start";

    private static readonly Dictionary<string, WeakReference<FrameworkElement>> map = new();

    public static void Register(string name, UIElement? element)
    {
        if (element is FrameworkElement fe) map[name] = new WeakReference<FrameworkElement>(fe);
    }

    /// 找到且正在显示（在界面树里、有尺寸）才返回。
    public static FrameworkElement? Find(string name)
    {
        if (!map.TryGetValue(name, out var weak) || !weak.TryGetTarget(out var fe)) return null;
        if (fe.XamlRoot is null || fe.ActualWidth <= 0 || fe.ActualHeight <= 0 || fe.Visibility != Visibility.Visible) return null;
        return fe;
    }
}
