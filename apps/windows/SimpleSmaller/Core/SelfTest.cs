using SimpleSmaller.Core;

namespace SimpleSmaller;

/// 不开界面跑一次图片压缩，确认引擎进程（smaller-core.exe）、通信与回调都通。
internal static class SelfTest
{
    private sealed class Listener : JobListener
    {
        public readonly List<JobItemResult> Items = new();
        public readonly ManualResetEventSlim Done = new();
        public void OnProgress(uint index, double fraction) { }
        public void OnItem(JobItemResult result) { lock (Items) Items.Add(result); }
        public void OnFinished() => Done.Set();
    }

    public static void Run(string[] inputs)
    {
        var log = Path.Combine(Path.GetTempPath(), "smaller-selftest.txt");
        try
        {
            var engine = new Engine();
            var listener = new Listener();
            var settings = new ImageSettings(ImageFormat.Webp, 80, false, null, Array.Empty<uint>(), SizeBy.Width, null, Array.Empty<CropBox>());
            var naming = new Naming("{name}-small", false, false, false, DateTime.Now.ToString("yyyyMMdd"));
            using var job = engine.RunImages(inputs, new Place.BesideSource("-small"), naming, settings, listener);
            listener.Done.Wait(TimeSpan.FromMinutes(5));
            var lines = listener.Items.OrderBy(i => i.Index).Select(i =>
                $"{i.State} {Path.GetFileName(i.Source)} {i.BeforeBytes} -> {string.Join(",", i.Outputs.Select(o => o.ToString()))} {i.Message}");
            File.WriteAllLines(log, lines.Prepend("ok"));
        }
        catch (Exception e)
        {
            File.WriteAllText(log, "error " + e);
        }
    }
}
