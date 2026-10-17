using SimpleSmaller.Bento;
using SimpleSmaller.Views.Sub;

namespace SimpleSmaller;

/// 打开各个窗口：设置、关于、许可证、新建 / 编辑配方。都是单例：再次打开时激活已有的窗口。
public partial class App
{
    private static SettingsWindow? settings;
    private static AboutWindow? about;
    private static LicensesWindow? licenses;
    private static RecipeEditorWindow? editor;

    /// 设置窗口句柄（文件选择器用）；没开时用主窗口。
    internal static IntPtr SettingsHwnd => settings?.Hwnd ?? MainHwnd;

    /// 设置窗口；tab 是「通用」「配方」「自动」之一。
    internal static void OpenSettings(string? tab = null)
    {
        if (settings is null)
        {
            settings = new SettingsWindow(tab);
            settings.Closed += (_, _) => { settings = null; AfterSubWindowClosed(); };
        }
        else if (tab is not null)
        {
            settings.Show(tab);
        }
        settings.Bring();
    }

    internal static void OpenAbout()
    {
        if (about is null)
        {
            about = new AboutWindow();
            about.Closed += (_, _) => { about = null; AfterSubWindowClosed(); };
        }
        about.Bring();
    }

    internal static void OpenLicenses()
    {
        if (licenses is null)
        {
            licenses = new LicensesWindow();
            licenses.Closed += (_, _) => { licenses = null; AfterSubWindowClosed(); };
        }
        licenses.Bring();
    }

    /// 新建或编辑配方；存好后回调。已经开着一个就把它提到前面。
    internal static void OpenRecipeEditor(Recipe initial, Action<Recipe> saved)
    {
        if (editor is null)
        {
            editor = new RecipeEditorWindow(initial, saved);
            editor.Closed += (_, _) => { editor = null; AfterSubWindowClosed(); };
        }
        editor.Bring();
    }

    /// 关掉托盘图标、主窗口也收起时，最后一个小窗口关掉就退出（没有别的入口能回来）。
    private static void AfterSubWindowClosed()
    {
        if (settings is null && about is null && licenses is null && editor is null
            && !TrayIcon.IsShown && Main is { } m && !m.AppWindow.IsVisible)
        {
            Quit();
        }
    }

    internal static IEnumerable<SubWindow> OpenSubWindows() =>
        new SubWindow?[] { settings, about, licenses, editor }.OfType<SubWindow>();
}
