using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SimpleSmaller.Bento;

/// 在代码里取便当盒色板：按元素实际的深浅色解析 Themes/Bento.xaml 的主题字典。
/// 代码搭的控件在 ActualThemeChanged 时重新取一次。
public static class Palette
{
    public static Brush Get(FrameworkElement element, string key)
    {
        var resources = Application.Current.Resources;
        // 先只查各字典的主题分支：XamlControlsResources 没有「Dark」分支，按键直接查会按应用主题（浅色）
        // 解析到 Bento.xaml 的浅色值，--appearance dark 时整窗变浅。
        foreach (var dict in resources.MergedDictionaries)
        {
            if (ThemeOnly(dict, element.ActualTheme, key) is Brush b)
            {
                return b;
            }
        }
        foreach (var dict in resources.MergedDictionaries)
        {
            if (Lookup(dict, element.ActualTheme, key) is Brush b)
            {
                return b;
            }
        }
        return Lookup(resources, element.ActualTheme, key) as Brush ?? (Brush)resources[key];
    }

    private static object? ThemeOnly(ResourceDictionary dict, ElementTheme theme, string key)
    {
        var themeKey = IsHighContrast ? "HighContrast" : theme == ElementTheme.Dark ? "Dark" : "Light";
        if (!dict.ThemeDictionaries.TryGetValue(themeKey, out var t) || t is not ResourceDictionary td || !td.TryGetValue(key, out var v)) return null;
        return v;
    }

    private static object? Lookup(ResourceDictionary dict, ElementTheme theme, string key)
    {
        var themeKey = theme == ElementTheme.Dark ? "Dark" : "Light";
        if (IsHighContrast)
        {
            themeKey = "HighContrast";
        }
        if (dict.ThemeDictionaries.TryGetValue(themeKey, out var t) && t is ResourceDictionary td && td.TryGetValue(key, out var v))
        {
            return v;
        }
        return dict.TryGetValue(key, out var plain) ? plain : null;
    }

    private static readonly Windows.UI.ViewManagement.AccessibilitySettings Accessibility = new();

    public static bool IsHighContrast => Accessibility.HighContrast;

    public static Brush Brand => (Brush)Application.Current.Resources["Brand"];
    public static Brush Saving => (Brush)Application.Current.Resources["Saving"];
    public static Brush Warning => (Brush)Application.Current.Resources["Warning"];
}
