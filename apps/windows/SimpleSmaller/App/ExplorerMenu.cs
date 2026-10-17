using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SimpleSmaller;

/// 资源管理器右键「用简单小一点处理」：经典菜单（Windows 11 在「显示更多选项」里）。
/// 按扩展名注册在 HKCU\Software\Classes\SystemFileAssociations\<扩展名>\shell\SimpleSmaller，
/// 不管默认用哪个应用打开都在。多选时 MultiSelectModel=Player 让资源管理器把文件交给同一次调用，
/// 再由单实例把几次启动合并进同一个窗口。
///
/// Windows 11 新菜单（IExplorerCommand）要应用带包标识并签名，非打包应用做不了，所以只在经典菜单里。
/// 开关存在 HKCU\Software\SimpleSmaller\ExplorerMenu（没写过就是开）；每次启动按开关重写一遍，
/// 应用换了位置（更新、换盘）菜单也跟着指过去。
internal static class ExplorerMenu
{
    public const string Title = "用简单小一点处理";
    private const string Verb = "SimpleSmaller";
    private const string PrefPath = @"Software\SimpleSmaller";
    private const string PrefName = "ExplorerMenu";

    /// 和 MediaTypes.Of 认的扩展名一致。
    private static readonly string[] Extensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".avif", ".heic", ".heif", ".tif", ".tiff", ".bmp", ".gif",
        ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi", ".mts", ".m2ts",
        ".pdf",
    ];

    /// 设置里的「资源管理器右键」开关。默认开。
    public static bool Enabled
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(PrefPath);
                return k?.GetValue(PrefName) is not int v || v != 0;
            }
            catch
            {
                return true;
            }
        }
        set
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(PrefPath);
                k.SetValue(PrefName, value ? 1 : 0, RegistryValueKind.DWord);
            }
            catch (Exception e)
            {
                Log.Error($"右键菜单开关：{e.Message}");
            }
            Task.Run(value ? Register : Unregister);
        }
    }

    /// 启动时调用：开着就（重新）注册。
    public static void Sync()
    {
        if (Enabled) Task.Run(Register);
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        try
        {
            foreach (var ext in Extensions)
            {
                using var verb = Registry.CurrentUser.CreateSubKey(KeyFor(ext));
                verb.SetValue("", Title);
                verb.SetValue("Icon", $"\"{exe}\",0");
                verb.SetValue("MultiSelectModel", "Player");
                using var command = verb.CreateSubKey("command");
                command.SetValue("", $"\"{exe}\" --open \"%1\"");
            }
            Notify();
        }
        catch (Exception e)
        {
            Log.Error($"注册右键菜单：{e.Message}");
        }
    }

    /// 去掉右键菜单（卸载时由安装器的卸载钩子调用；不改开关）。
    public static void Unregister()
    {
        try
        {
            foreach (var ext in Extensions) Registry.CurrentUser.DeleteSubKeyTree(KeyFor(ext), false);
            Notify();
        }
        catch (Exception e)
        {
            Log.Error($"去掉右键菜单：{e.Message}");
        }
    }

    private static string KeyFor(string ext) => $@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}";

    /// 通知资源管理器关联变了，菜单马上生效。
    private static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    [DllImport("shell32.dll")] private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
