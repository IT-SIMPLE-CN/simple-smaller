namespace SimpleSmaller;

/// 打开各个窗口：设置、关于、新建 / 编辑配方。
public partial class App
{
    /// 设置窗口；tab 是「通用」「配方」「自动」之一。
    internal static void OpenSettings(string? tab = null) => AppModel.Shared.Flash("设置还在做");

    internal static void OpenAbout() => AppModel.Shared.Flash("关于还在做");

    /// 新建或编辑配方；存好后回调。
    internal static void OpenRecipeEditor(Recipe initial, Action<Recipe> saved) => AppModel.Shared.Flash("配方编辑还在做");
}
