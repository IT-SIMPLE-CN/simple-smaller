using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace SimpleSmaller.Auto;

/// 剪贴板自动压缩（默认关，设置 → 自动）：复制了图片（截图、网页里的图），自动换成压缩版，
/// 粘贴到公众号、飞书、微信就是小图。只处理图片数据，不碰复制的文件、文字。
/// 换过的内容带一个本应用的标记，不会被自己再压一遍。剪贴板变化由 MessageWindow 通知。
internal static class ClipboardWatcher
{
    public const string Marker = "cn.it-simple.smaller.compressed";
    private static bool on;
    private static bool working;
    private static bool hooked;
    private static uint ownSequence;

    public static void Refresh()
    {
        on = AppSettings.Shared.ClipboardAuto;
        if (on && !hooked)
        {
            hooked = true;
            MessageWindow.Shared.ClipboardChanged += () => { if (on) _ = CheckAsync(); };
        }
    }

    private static async Task CheckAsync()
    {
        var seq = GetClipboardSequenceNumber();
        if (working || seq == ownSequence) return;
        DataPackageView view;
        try
        {
            view = Clipboard.GetContent();
            // 复制的是文件（资源管理器里 Ctrl+C）不动；只认图片数据。
            if (view.Contains(Marker) || view.Contains(StandardDataFormats.StorageItems) || !view.Contains(StandardDataFormats.Bitmap)) return;
        }
        catch
        {
            return;
        }
        working = true;
        try
        {
            var content = await AppModel.Shared.Platform.ReadClipboardAsync();
            if (content?.Png is not { } png || png.Length >= 80_000_000) return;
            var maxEdge = (uint)Math.Max(0, AppSettings.Shared.ClipboardMaxEdge);
            var engine = AppModel.Shared.Engine;
            var result = await Task.Run(() =>
            {
                var tmp = Path.Combine(Path.GetTempPath(), $"smaller-clip-{Guid.NewGuid()}.png");
                try
                {
                    File.WriteAllBytes(tmp, png);
                    return engine.CompressForClipboard(tmp, maxEdge);
                }
                catch (Exception e)
                {
                    Log.Error($"剪贴板压缩：{e.Message}");
                    return null;
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
            });
            // 这期间用户又复制了别的，就不覆盖。
            if (result is null || GetClipboardSequenceNumber() != seq) return;
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(await Stream(result.Bytes)));
            // 原样的 PNG / JPEG 字节也放一份：认这两种格式的应用（浏览器、聊天软件）直接拿到小文件。
            package.SetData(result.Png ? "PNG" : "JFIF", await Stream(result.Bytes));
            package.SetData(Marker, "1");
            Clipboard.SetContent(package);
            Clipboard.Flush();
            ownSequence = GetClipboardSequenceNumber();
            Hud.Show($"剪贴板图片 {Format.Size(result.BeforeBytes)} → {Format.Size((ulong)result.Bytes.Length)}");
        }
        catch (Exception e)
        {
            Log.Error($"剪贴板：{e.Message}");
        }
        finally
        {
            working = false;
        }
    }

    private static async Task<IRandomAccessStream> Stream(byte[] bytes)
    {
        var s = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(s.GetOutputStreamAt(0)))
        {
            w.WriteBytes(bytes);
            await w.StoreAsync();
            await w.FlushAsync();
        }
        s.Seek(0);
        return s;
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
