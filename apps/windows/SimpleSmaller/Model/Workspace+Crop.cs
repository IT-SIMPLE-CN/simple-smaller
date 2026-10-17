namespace SimpleSmaller;

/// 图片裁切：框一律用原图像素坐标（已按 EXIF 转正），与引擎一致。
/// 没手调过的图居中、按比例取最大；自由比例默认整张。
internal sealed partial class Workspace
{
    public CropPreset? CropPreset => Draft.Image.Crop;
    public double? CropAspect => Draft.Image.Crop?.Aspect;
    public int CropOutputWidth => Draft.Image.CropWidth ?? Draft.Image.Crop?.Width ?? 1920;

    /// 能裁的图：已读出尺寸的图片。
    public IReadOnlyList<FileItem> CropItems => Files.Where(f => f.Type == MediaType.Image && f.Width > 0 && f.Height > 0).ToList();

    public int CropCurrentIndex => Math.Min(Math.Max(0, CropIndex), Math.Max(0, CropItems.Count - 1));

    /// 「小红书 3:4」；自由比例写「自由」。
    public string CropTitle
    {
        get
        {
            if (CropPreset is not { } p) return "不裁切";
            return p.RatioLabel.Length == 0 ? p.Name : $"{p.Name} {p.RatioLabel}";
        }
    }

    public PixelRect DefaultCropBox(FileItem item)
    {
        double w = item.Width, h = item.Height;
        if (CropAspect is not { } a) return new PixelRect(0, 0, w, h);
        return PixelRect.CenteredMax(w, h, a, PixelRect.Rounded);
    }

    /// 第 i 张实际用的框：手调过的，否则居中取最大。
    public PixelRect EffectiveCropBox(int index)
    {
        var items = CropItems;
        if (index < 0 || index >= items.Count) return PixelRect.Zero;
        return CropBoxes.TryGetValue(items[index].FilePath, out var r) ? r : DefaultCropBox(items[index]);
    }

    /// 收进画面：先缩到放得下（保持比例），再把位置推回画面里，坐标取整。
    public static PixelRect Clamp(PixelRect rect, double width, double height, double? aspect)
    {
        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        if (aspect is { } a)
        {
            if (w > width) { w = width; h = width / a; }
            if (h > height) { w = height * a; h = height; }
        }
        else
        {
            w = Math.Min(w, width);
            h = Math.Min(h, height);
        }
        x = Math.Min(Math.Max(0, x), width - w);
        y = Math.Min(Math.Max(0, y), height - h);
        return new PixelRect(Num.Round(x), Num.Round(y), Math.Max(1, Num.Round(w)), Math.Max(1, Num.Round(h)));
    }

    /// 手调一张的框：记下改之前的框（撤回用），动了就按新框。
    public void SetCropBox(FileItem item, PixelRect rect)
    {
        var history = new Dictionary<string, List<PixelRect?>>(CropHistory, PathComparer);
        var stack = history.TryGetValue(item.FilePath, out var s) ? new List<PixelRect?>(s) : [];
        stack.Add(CropBoxes.TryGetValue(item.FilePath, out var old) ? old : null);
        history[item.FilePath] = stack;
        CropHistory = history;
        CropBoxes = With(CropBoxes, item.FilePath, rect);
    }

    /// Ctrl+Z：撤回这一张的上一次改动；没有记录时回到默认。
    public void UndoCropBox(FileItem item)
    {
        if (CropHistory.TryGetValue(item.FilePath, out var stack) && stack.Count > 0)
        {
            var last = stack[^1];
            var history = new Dictionary<string, List<PixelRect?>>(CropHistory, PathComparer)
            {
                [item.FilePath] = stack.Take(stack.Count - 1).ToList(),
            };
            CropHistory = history;
            CropBoxes = With(CropBoxes, item.FilePath, last);
        }
        else
        {
            CropBoxes = With<PixelRect>(CropBoxes, item.FilePath, null);
        }
    }

    /// 换比例后全部回到默认（设计稿 03）。
    public void ResetCrop()
    {
        CropBoxes = Empty<PixelRect>();
        CropHistory = new(PathComparer);
        CropIndex = 0;
    }

    /// 手调过框的张数（标题栏、开始块写）。
    public int CropAdjustedCount => CropItems.Count(f => CropBoxes.ContainsKey(f.FilePath));

    /// 交给引擎的框：只传手调过的，其余由引擎按居中取最大处理。
    public IReadOnlyDictionary<string, PixelRect> CropBoxesForEngine =>
        Draft.Image.Crop == null ? Empty<PixelRect>() : CropBoxes;
}
