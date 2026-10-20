using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleSmaller.Core;

/// 引擎宿主进程 smaller-core.exe（与本程序同目录）：压缩引擎在这个独立进程里运行，经它的标准输入输出通信，
/// 每行一个 JSON（协议见 core-host/src/main.rs）。
/// - 按需拉起：第一次调用时启动；意外退出时，进行中的任务把没回报的文件标成失败，等着的调用报错，随后重新拉起。
/// - 不开窗口（CREATE_NO_WINDOW），不监听端口，只用自己的管道。
/// - 放进一个「关闭即结束」的作业对象：本程序退出（含崩溃）时，宿主和它起的 ffmpeg 一并结束，不留进程。
internal sealed class EngineHost
{
    public static readonly EngineHost Shared = new();

    /// 与宿主约定的协议版本（core-host 的 PROTOCOL）。
    private const int Protocol = 1;
    private const string Crashed = "处理意外中断，请重试";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly object gate = new();
    private readonly object writeGate = new();
    private Process? process;
    private StreamWriter? input;
    private IntPtr jobObject;
    /// 第几次拉起：读线程据此认出自己是否已过期。
    private int generation;
    private long nextId;
    private long nextJob;
    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> pending = [];
    private readonly Dictionary<ulong, Job> jobs = [];
    private readonly List<DateTime> recentCrashes = [];
    private bool quitting;

    private EngineHost()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
    }

    private static string ExePath => Path.Combine(AppContext.BaseDirectory, "smaller-core.exe");

    // MARK: 调用

    /// 发一个请求并等结果（在调用线程上同步等）。引擎报错或宿主退出时抛 EngineException。
    public T Call<T>(string method, object parameters)
    {
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        long id;
        int gen;
        lock (gate)
        {
            EnsureStarted();
            id = ++nextId;
            gen = generation;
            pending[id] = tcs;
        }
        if (!Send(gen, new { id, method, @params = parameters }))
        {
            lock (gate) pending.Remove(id);
            throw new EngineException.Failed(Crashed);
        }
        var result = tcs.Task.GetAwaiter().GetResult();
        return result.ValueKind == JsonValueKind.Undefined ? default! : result.Deserialize<T>(Json)!;
    }

    /// 只发不等（取消）。
    public void Post(string method, object parameters)
    {
        int gen;
        lock (gate)
        {
            if (process == null) return;
            gen = generation;
        }
        Send(gen, new { id = 0, method, @params = parameters });
    }

    /// 起一个处理任务：先登记任务编号再发请求，事件按编号回到这个 Job。
    public Job Run(string method, string[] inputs, Place place, Naming naming, object settings, JobListener listener)
    {
        Job job;
        lock (gate)
        {
            job = new Job(this, (ulong)Interlocked.Increment(ref nextJob), inputs, listener);
            jobs[job.Id] = job;
        }
        try
        {
            var planned = Call<Planned>(method, new { job = job.Id, inputs, place, naming, settings });
            job.Planned(planned.Sources, planned.CreatedRoots);
        }
        catch (Exception e)
        {
            // 没起来：这一批的文件都标成失败（异步回报，和正常任务一样不在调用线程上回调）。
            Log.Error($"引擎：起任务失败 {e.Message}");
            lock (gate) jobs.Remove(job.Id);
            var message = e is EngineException.Failed f ? f.message : Crashed;
            Task.Run(() => job.Fail(message));
        }
        return job;
    }

    private sealed record Planned(string[] Sources, string[] CreatedRoots);

    // MARK: 进程

    /// 在 gate 里调用。
    private void EnsureStarted()
    {
        if (process != null) return;
        if (quitting) throw new EngineException.Failed("应用正在退出");
        if (!File.Exists(ExePath)) throw new EngineException.Failed("找不到引擎 smaller-core.exe，请重新安装");
        var info = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = AppContext.BaseDirectory,
        };
        var started = Stopwatch.StartNew();
        var p = Process.Start(info) ?? throw new EngineException.Failed("引擎启动失败");
        // 还没发任何请求，宿主不会起子进程：现在放进作业对象，之后它起的 ffmpeg 也都在里面。
        jobObject = KillOnCloseJob();
        if (jobObject != IntPtr.Zero && !AssignProcessToJobObject(jobObject, p.Handle))
            Log.Error($"引擎：放进作业对象失败 {Marshal.GetLastWin32Error()}");
        process = p;
        input = p.StandardInput;
        input.AutoFlush = false;
        var gen = ++generation;
        var output = p.StandardOutput;
        new Thread(() => ReadLoop(gen, output)) { IsBackground = true, Name = "smaller-core 读" }.Start();
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Info($"引擎：{e.Data}"); };
        p.BeginErrorReadLine();
        // 正常情况下读线程读到结尾就知道宿主退出了；万一管道被别的进程握着读不到结尾，以进程退出为准（稍等让读线程先读完剩下的）。
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => Task.Delay(1000).ContinueWith(_ => Exited(gen));
        Log.Info($"引擎已启动（pid {p.Id}，{started.ElapsedMilliseconds} ms）");
    }

    private bool Send(int gen, object message)
    {
        var line = JsonSerializer.Serialize(message, Json);
        lock (writeGate)
        {
            StreamWriter? w;
            lock (gate)
            {
                if (gen != generation || input == null) return false;
                w = input;
            }
            try
            {
                w.Write(line);
                w.Write('\n');
                w.Flush();
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"引擎：写入失败 {e.Message}");
                return false;
            }
        }
    }

    private void ReadLoop(int gen, StreamReader output)
    {
        try
        {
            while (output.ReadLine() is { } line)
            {
                if (line.Length == 0) continue;
                try
                {
                    Dispatch(line);
                }
                catch (Exception e)
                {
                    Log.Error($"引擎：处理消息出错 {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"引擎：读取中断 {e.Message}");
        }
        Exited(gen);
    }

    private void Dispatch(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.TryGetProperty("id", out var idElement))
        {
            var id = idElement.GetInt64();
            TaskCompletionSource<JsonElement>? tcs;
            lock (gate)
            {
                if (!pending.Remove(id, out tcs)) return;
            }
            if (root.TryGetProperty("error", out var error))
                tcs.TrySetException(ToException(error));
            else
                tcs.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
            return;
        }
        var kind = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        switch (kind)
        {
            case "progress":
                JobOf(root)?.Progress(root.GetProperty("index").GetUInt32(), root.GetProperty("fraction").GetDouble());
                break;
            case "item":
                JobOf(root)?.Item(root.GetProperty("result").Deserialize<JobItemResult>(Json)!);
                break;
            case "finished":
            {
                var id = root.GetProperty("job").GetUInt64();
                Job? job;
                lock (gate)
                {
                    if (!jobs.Remove(id, out job)) return;
                }
                job.Finished();
                break;
            }
            case "log":
            {
                var message = root.GetProperty("message").GetString() ?? "";
                if (root.TryGetProperty("level", out var level) && level.GetString() == "error") Log.Error($"引擎：{message}");
                else Log.Info($"引擎：{message}");
                break;
            }
            case "ready":
            {
                var protocol = root.TryGetProperty("protocol", out var v) ? v.GetInt32() : 0;
                if (protocol != Protocol) Log.Error($"引擎：协议版本不符（应用 {Protocol}，引擎 {protocol}）");
                break;
            }
        }
    }

    private Job? JobOf(JsonElement root)
    {
        var id = root.GetProperty("job").GetUInt64();
        lock (gate) return jobs.TryGetValue(id, out var job) ? job : null;
    }

    private static EngineException ToException(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return (error.TryGetProperty("kind", out var k) ? k.GetString() : null) switch
        {
            "io" => new EngineException.Io(message),
            "unsupported" => new EngineException.Unsupported(message),
            _ => new EngineException.Failed(message),
        };
    }

    /// 宿主退出（被结束、崩溃）：等着的调用报错，进行中的任务标失败；结束它留下的子进程；不在退出中就重新拉起。
    private void Exited(int gen)
    {
        List<TaskCompletionSource<JsonElement>> calls;
        List<Job> running;
        Process? p;
        IntPtr oldJob;
        bool restart;
        lock (gate)
        {
            if (gen != generation || process == null) return;
            p = process;
            process = null;
            input = null;
            generation++;
            oldJob = jobObject;
            jobObject = IntPtr.Zero;
            calls = pending.Values.ToList();
            pending.Clear();
            running = jobs.Values.ToList();
            jobs.Clear();
            var now = DateTime.UtcNow;
            recentCrashes.RemoveAll(t => now - t > TimeSpan.FromMinutes(1));
            recentCrashes.Add(now);
            // 一分钟内反复退出就不再主动拉起（下次调用时再试），免得来回重启。
            restart = !quitting && recentCrashes.Count <= 3;
        }
        if (!quitting) Log.Error($"引擎意外退出（{ExitCode(p)}），{running.Count} 个任务标为失败");
        // 宿主没了，它起的 ffmpeg 可能还在跑：结束作业对象里剩下的进程。
        if (oldJob != IntPtr.Zero)
        {
            TerminateJobObject(oldJob, 1);
            CloseHandle(oldJob);
        }
        p.Dispose();
        foreach (var c in calls) c.TrySetException(new EngineException.Failed(Crashed));
        foreach (var j in running) j.Fail(Crashed);
        if (restart)
        {
            Task.Run(() =>
            {
                try { lock (gate) if (process == null && !quitting) EnsureStarted(); }
                catch (Exception e) { Log.Error($"引擎：重新拉起失败 {e.Message}"); }
            });
        }
    }

    private static string ExitCode(Process p)
    {
        try { return p.HasExited ? $"退出码 {p.ExitCode}" : "管道关闭"; } catch { return "未知"; }
    }

    /// 应用退出：关掉管道（宿主读到结尾自己退出），再结束作业对象里的一切。
    public void Shutdown()
    {
        // 刚取消的任务给引擎一点时间收尾（结束 ffmpeg、删掉做到一半的临时文件），最多等 2 秒。
        var wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < 2000)
        {
            lock (gate) if (jobs.Count == 0 || process == null) break;
            Thread.Sleep(50);
        }
        StreamWriter? w;
        Process? p;
        IntPtr job;
        lock (gate)
        {
            quitting = true;
            w = input;
            p = process;
            job = jobObject;
            input = null;
            process = null;
            jobObject = IntPtr.Zero;
            generation++;
        }
        try { w?.Close(); } catch { }
        try { if (p != null && !p.WaitForExit(300)) p.Kill(true); } catch { }
        if (job != IntPtr.Zero)
        {
            TerminateJobObject(job, 0);
            CloseHandle(job);
        }
    }

    // MARK: 作业对象（关闭即结束里面的进程）

    private static IntPtr KillOnCloseJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)size))
            {
                Log.Error($"引擎：设置作业对象失败 {Marshal.GetLastWin32Error()}");
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
