using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SimpleSmaller.Views.Preview;

/// PDF 页面缩略图（放在这里而不是 Native/：那个目录给引擎 DLL 用，被 .gitignore 忽略）：用系统的 Windows.Data.Pdf 在后台逐页渲染，按文件缓存（对应 mac 的 PdfKit 版）。
/// 渲染、编码都在后台线程；回到界面线程只把 PNG 字节交给 BitmapImage（它自己在后台解码）。
/// 加密的 PDF 用解锁出的副本（FileItem.PreviewPath）渲染；没解锁时渲染不出，页面显示白纸。
internal sealed class PdfThumbnails
{
    public static PdfThumbnails Shared { get; } = new();

    private sealed class Entry
    {
        public ImageSource?[] Images = [];
        /// 每页高 / 宽。
        public double[] Aspects = [];
    }

    private readonly Dictionary<string, Entry> pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loading = new(StringComparer.OrdinalIgnoreCase);

    /// 某个文件又有页面渲染好了（参数是文件路径）。只在界面线程触发。
    public event Action<string>? Changed;

    public ImageSource? Page(string path, int index) =>
        pages.TryGetValue(path, out var e) && index >= 0 && index < e.Images.Length ? e.Images[index] : null;

    /// 页面高宽比；还没渲染时按 A4 竖版。
    public double Aspect(string path, int index) =>
        pages.TryGetValue(path, out var e) && index >= 0 && index < e.Aspects.Length && e.Aspects[index] > 0 ? e.Aspects[index] : 1.414;

    /// 开始渲染一个文件的全部页（已在渲染或渲染过就不重复）。在界面线程调用。
    public void Load(string path, int width = 160)
    {
        if (pages.ContainsKey(path) || !loading.Add(path)) return;
        _ = Task.Run(() => RenderAsync(path, width));
    }

    private async Task RenderAsync(string path, int width)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var doc = await PdfDocument.LoadFromFileAsync(file);
            var count = (int)doc.PageCount;
            Ui.Post(() => pages[path] = new Entry { Images = new ImageSource?[count], Aspects = new double[count] });
            for (var i = 0; i < count; i++)
            {
                using var page = doc.GetPage((uint)i);
                var aspect = page.Size.Width > 0 ? page.Size.Height / page.Size.Width : 1.414;
                var stream = new InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)(width * 2) });
                var index = i;
                Ui.Post(() => _ = Show(path, index, aspect, stream));
            }
        }
        catch (Exception e)
        {
            // 加密、损坏：没有缩略图，页面画白纸。
            Log.Info($"PDF 缩略图 {Path.GetFileName(path)}：{e.Message}");
            Ui.Post(() => pages.TryAdd(path, new Entry()));
        }
        finally
        {
            Ui.Post(() => loading.Remove(path));
        }
    }

    private async Task Show(string path, int index, double aspect, InMemoryRandomAccessStream stream)
    {
        if (!pages.TryGetValue(path, out var e) || index >= e.Images.Length) return;
        try
        {
            stream.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            e.Images[index] = bmp;
            e.Aspects[index] = aspect;
            Changed?.Invoke(path);
        }
        catch { }
        finally
        {
            stream.Dispose();
        }
    }

    /// 文件变了（解锁、移除）时丢掉缓存，下次重新渲染。
    public void Forget(string path) => pages.Remove(path);

    /// 图片缩略图（图片转 PDF 的排序网格）：BitmapImage 按目标宽度在后台解码，不整张解。
    public static ImageSource ImageThumb(string path, int width = 280) =>
        new BitmapImage { DecodePixelWidth = width, DecodePixelType = DecodePixelType.Logical, UriSource = new Uri(path) };
}
