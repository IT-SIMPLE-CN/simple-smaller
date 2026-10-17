using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SimpleSmaller;

/// 剪贴板里读到的东西：文件路径，或一张图（已转成 PNG）。
internal sealed record ClipboardContent(IReadOnlyList<string>? Files, byte[]? Png);

/// 模型层用到的系统能力（mac 上是 NSWorkspace、NSPasteboard、Dock、Quick Look 等）。
/// WindowsPlatform 给出能直接做的部分；要窗口或 XAML 类型的（前台判断、任务栏角标、缩略图）由界面层继承覆盖。
internal interface IPlatform
{
    /// 应用窗口在前台（决定后台完成时要不要发通知、记角标）。
    bool IsAppActive { get; }

    /// 在资源管理器中显示并选中这些文件。
    void Reveal(IReadOnlyList<string> paths);

    /// 处理中不让系统闲置睡眠（屏幕可以照常关）；Dispose 结束。
    IDisposable? BeginActivity(string reason);

    /// 任务栏按钮上的角标：处理中写「3/9」，后台做完写完成个数；null 清掉。
    void SetBadge(string? text);

    /// 文件缩略图（视频、PDF 也要有），返回界面层的图片对象（ImageSource）。在界面线程调用。
    Task<object?> LoadThumbnailAsync(string path, int size);

    /// 读剪贴板：文件或图片；都没有返回 null。在界面线程调用。
    Task<ClipboardContent?> ReadClipboardAsync();

    /// 把文件放进剪贴板，可以直接粘贴进聊天窗口或资源管理器。在界面线程调用。
    Task CopyFilesAsync(IReadOnlyList<string> paths);
}

internal class WindowsPlatform : IPlatform
{
    /// 默认当作在前台（不发通知）；界面层按主窗口激活状态覆盖。
    public virtual bool IsAppActive => true;

    /// 默认不显示角标；界面层用 ITaskbarList3 覆盖图标实现。
    public virtual void SetBadge(string? text) { }

    /// 默认没有缩略图；界面层用 StorageFile.GetThumbnailAsync + BitmapImage 实现。
    public virtual Task<object?> LoadThumbnailAsync(string path, int size) => Task.FromResult<object?>(null);

    // MARK: 资源管理器

    public virtual void Reveal(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        // 同一个文件夹里的一起选中；分在几个文件夹的各开一个窗口。
        foreach (var group in paths.GroupBy(p => Path.GetDirectoryName(p) ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (!SelectInFolder(group.Key, group.ToList()))
                Start("explorer.exe", $"/select,\"{group.First()}\"");
        }
    }

    private static bool SelectInFolder(string folder, List<string> files)
    {
        if (folder.Length == 0) return false;
        var pidls = new List<IntPtr>();
        var folderPidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(folder, IntPtr.Zero, out folderPidl, 0, out _) != 0) return false;
            foreach (var f in files)
                if (SHParseDisplayName(f, IntPtr.Zero, out var p, 0, out _) == 0) pidls.Add(p);
            if (pidls.Count == 0) return false;
            var children = pidls.Select(ILFindLastID).ToArray();
            return SHOpenFolderAndSelectItems(folderPidl, (uint)children.Length, children, 0) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            foreach (var p in pidls) ILFree(p);
            if (folderPidl != IntPtr.Zero) ILFree(folderPidl);
        }
    }

    private static void Start(string file, string args)
    {
        try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); } catch { }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, uint flags);
    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    // MARK: 不睡眠

    public virtual IDisposable? BeginActivity(string reason)
    {
        try { return PowerRequest.Create(reason); } catch { return null; }
    }

    /// 电源请求：不随线程失效，`powercfg /requests` 里能看到原因。
    private sealed class PowerRequest : IDisposable
    {
        private IntPtr handle;
        private IntPtr reasonText;

        public static PowerRequest? Create(string reason)
        {
            var text = Marshal.StringToHGlobalUni(reason);
            var ctx = new REASON_CONTEXT { Version = 0, Flags = 1 /* POWER_REQUEST_CONTEXT_SIMPLE_STRING */, SimpleReasonString = text };
            var h = PowerCreateRequest(ref ctx);
            if (h == IntPtr.Zero || h == new IntPtr(-1) || !PowerSetRequest(h, 1 /* PowerRequestSystemRequired */))
            {
                if (h != IntPtr.Zero && h != new IntPtr(-1)) CloseHandle(h);
                Marshal.FreeHGlobal(text);
                return null;
            }
            return new PowerRequest { handle = h, reasonText = text };
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            PowerClearRequest(handle, 1);
            CloseHandle(handle);
            Marshal.FreeHGlobal(reasonText);
            handle = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct REASON_CONTEXT
        {
            public uint Version;
            public uint Flags;
            public IntPtr SimpleReasonString;
            // 联合体里详细原因那一支更长，补齐到同样大小。
            private IntPtr pad1;
            private IntPtr pad2;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerSetRequest(IntPtr handle, int requestType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool PowerClearRequest(IntPtr handle, int requestType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    // MARK: 剪贴板

    public virtual async Task<ClipboardContent?> ReadClipboardAsync()
    {
        try
        {
            var view = Clipboard.GetContent();
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                var items = await view.GetStorageItemsAsync();
                var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
                if (paths.Count > 0) return new ClipboardContent(paths, null);
            }
            if (view.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await view.GetBitmapAsync();
                using var input = await reference.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(input);
                using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
                using var output = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
                var bytes = new byte[output.Size];
                using var reader = new DataReader(output.GetInputStreamAt(0));
                await reader.LoadAsync((uint)output.Size);
                reader.ReadBytes(bytes);
                return new ClipboardContent(null, bytes);
            }
        }
        catch (Exception e)
        {
            Log.Error($"读剪贴板：{e.Message}");
        }
        return null;
    }

    public virtual async Task CopyFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        try
        {
            var items = new List<IStorageItem>();
            foreach (var p in paths) items.Add(await StorageFile.GetFileFromPathAsync(p));
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems(items);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception e)
        {
            Log.Error($"拷贝到剪贴板：{e.Message}");
        }
    }
}
