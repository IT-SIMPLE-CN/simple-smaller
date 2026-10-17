using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace SimpleSmaller;

/// 自定义入口（csproj 里定义了 DISABLE_XAML_GENERATED_MAIN）：只留一个实例。
/// 第二次启动（右键菜单、命令行、开始菜单再点一次）把参数转给已在跑的实例，自己马上退出；
/// 已在跑的实例拿出主窗口并按参数加文件（`LaunchArgs.Apply`）。
public static class Program
{
    /// 第二次启动转来的参数。主实例的界面还没起来时先存着，起来后一起交出去。
    private static readonly List<string[]> pending = [];
    private static Action<string[]>? deliver;
    private static readonly object gate = new();

    [STAThread]
    private static int Main(string[] args)
    {
        // Velopack 钩子放这里（最先执行）

#if DEBUG
        // 调试构建把日志写到 %TEMP%\SimpleSmaller-debug.log，方便在虚拟机里看。
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimpleSmaller-debug.log")));
        System.Diagnostics.Trace.AutoFlush = true;
#endif
        WinRT.ComWrappersSupport.InitializeComWrappers();
        // 自检要单独跑完退出，不和正在用的实例合并。
        var selftest = args.Length > 0 && args[0] == "--selftest";
        if (!selftest && RedirectToRunning()) return 0;
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// 界面起来后接收转来的参数（在后台线程回调，接收方自己投递到界面线程）。
    internal static void OnSecondLaunch(Action<string[]> handler)
    {
        string[][] queued;
        lock (gate)
        {
            deliver = handler;
            queued = pending.ToArray();
            pending.Clear();
        }
        foreach (var a in queued) handler(a);
    }

    /// 已有实例在跑：把这次启动转过去，返回 true（本进程退出）。自己是第一个：登记并开始收转来的启动。
    private static bool RedirectToRunning()
    {
        AppInstance key;
        try
        {
            key = AppInstance.FindOrRegisterForKey(InstanceKey());
        }
        catch (Exception e)
        {
            Log.Error($"单实例：{e.Message}");
            return false;
        }
        if (key.IsCurrent)
        {
            key.Activated += (_, e) => Received(e);
            return false;
        }
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        // 允许主实例把自己的窗口提到前台（否则只会在任务栏闪）。
        AllowSetForegroundWindow(key.ProcessId);
        // 转发是异步的，但入口线程是 STA，不能干等：另起线程转发，这里用 COM 的等法边等边处理消息。
        var done = CreateEvent(IntPtr.Zero, true, false, null);
        Task.Run(() =>
        {
            try { key.RedirectActivationToAsync(activation).AsTask().Wait(TimeSpan.FromSeconds(10)); }
            catch (Exception e) { Log.Error($"单实例转发：{e.Message}"); }
            SetEvent(done);
        });
        CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done], out _);
        CloseHandle(done);
        return true;
    }

    /// 同一份安装只留一个实例；装在不同位置（开发时的几份构建）各算各的。
    private static string InstanceKey()
    {
        var dir = AppContext.BaseDirectory.TrimEnd('\\').ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dir)))[..12];
        return $"SimpleSmaller-{hash}";
    }

    private static void Received(AppActivationArguments e)
    {
        var args = e.Kind == ExtendedActivationKind.Launch && e.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? SplitArguments(launch.Arguments)
            : [];
        Action<string[]>? handler;
        lock (gate)
        {
            handler = deliver;
            if (handler is null) pending.Add(args);
        }
        handler?.Invoke(args);
    }

    /// 非打包应用拿到的是整条命令行（可能带着 exe 本身），按系统规则拆开，去掉 exe。
    internal static string[] SplitArguments(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        var ptr = CommandLineToArgvW(line, out var count);
        if (ptr == IntPtr.Zero) return [];
        var list = new List<string>();
        try
        {
            for (var i = 0; i < count; i++) list.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * IntPtr.Size)) ?? "");
        }
        finally
        {
            LocalFree(ptr);
        }
        if (list.Count > 0 && list[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !list[0].StartsWith("--")) list.RemoveAt(0);
        return list.ToArray();
    }

    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initialState, string? name);
    [DllImport("kernel32.dll")] private static extern bool SetEvent(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("ole32.dll")] private static extern int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CommandLineToArgvW(string line, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
}
