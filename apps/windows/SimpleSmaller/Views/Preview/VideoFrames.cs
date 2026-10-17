using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace SimpleSmaller.Views.Preview;

/// 视频取帧（时间轴缩略图、胶片条、序列帧预览）：用随包的 ffmpeg 在后台取一帧，输出 JPEG 字节，
/// 回到界面线程再解成图片。ffmpeg 会按视频的旋转信息转正；裁切框按原画面像素在缩放前裁。
/// 同时最多跑 3 个 ffmpeg，结果按参数缓存。
internal static class VideoFrames
{
    private static readonly SemaphoreSlim Gate = new(3);
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    private static string Ffmpeg => Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");

    /// 取 t 秒处的一帧，最长边不超过 maxSide；crop 是原画面像素的框（可空）。
    public static async Task<byte[]?> GrabAsync(string path, double t, int maxSide, PixelRect? crop = null, CancellationToken ct = default)
    {
        var inv = CultureInfo.InvariantCulture;
        var key = $"{path}|{t.ToString("0.000", inv)}|{maxSide}|{crop}";
        if (Cache.TryGetValue(key, out var hit)) return hit;
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (ct.IsCancellationRequested) return null;
            var filters = new List<string>();
            if (crop is { } c)
                filters.Add($"crop={(int)c.Width}:{(int)c.Height}:{(int)c.X}:{(int)c.Y}");
            filters.Add($"scale=w={maxSide}:h={maxSide}:force_original_aspect_ratio=decrease");
            var psi = new ProcessStartInfo(Ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "-v", "error", "-ss", Math.Max(0, t).ToString("0.000", inv), "-i", path, "-frames:v", "1",
                                      "-vf", string.Join(",", filters), "-f", "image2pipe", "-c:v", "mjpeg", "-q:v", "4", "-" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            using var reg = ct.Register(() => { try { p.Kill(); } catch { } });
            _ = p.StandardError.ReadToEndAsync(ct);
            using var ms = new MemoryStream();
            await p.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            if (p.ExitCode != 0 || ms.Length == 0) return null;
            var bytes = ms.ToArray();
            Cache[key] = bytes;
            return bytes;
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// 均匀取 n 帧（第 k 格取它中间那一刻），按顺序回调；取不到的格用上一张。
    public static async Task StripAsync(string path, double duration, int count, int maxSide, Action<int, byte[]> got, CancellationToken ct)
    {
        var tasks = Enumerable.Range(0, count)
            .Select(k => GrabAsync(path, duration * (k + 0.5) / count, maxSide, null, ct))
            .ToList();
        byte[]? last = null;
        for (var k = 0; k < count; k++)
        {
            var b = await tasks[k];
            if (ct.IsCancellationRequested) return;
            b ??= last;
            if (b != null) got(k, b);
            last = b;
        }
    }

    /// 装饰用的缩略图：铺满格子，旁白不读。
    public static Microsoft.UI.Xaml.Controls.Image DecorImage()
    {
        var i = new Microsoft.UI.Xaml.Controls.Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(i, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return i;
    }

    /// 在界面线程把 JPEG 字节解成图片（解码本身由系统在后台做）。
    public static async Task<BitmapImage?> ToImageAsync(byte[] bytes, int decodeHeight = 0)
    {
        try
        {
            var stream = new InMemoryRandomAccessStream();
            using (var w = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                w.WriteBytes(bytes);
                await w.StoreAsync();
                await w.FlushAsync();
                w.DetachStream();
            }
            stream.Seek(0);
            var img = new BitmapImage();
            if (decodeHeight > 0) img.DecodePixelHeight = decodeHeight;
            await img.SetSourceAsync(stream);
            return img;
        }
        catch
        {
            return null;
        }
    }
}
