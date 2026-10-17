namespace SimpleSmaller;

/// 原图 / 原画面的像素坐标框（代替 CGRect）。
internal readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public static readonly PixelRect Zero = default;
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public PixelSize Size => new(Width, Height);

    /// 画面里按比例取最大、居中的框（图片裁切、视频取景的默认框都用它）。
    /// `round` 决定坐标怎么取整：图片四舍五入，视频取偶数（编码器要求）。
    public static PixelRect CenteredMax(double w, double h, double a, Func<double, double> round)
    {
        var (cw, ch) = w / h > a ? (h * a, h) : (w, w / a);
        return new PixelRect(round((w - cw) / 2), round((h - ch) / 2), round(cw), round(ch));
    }

    /// 图片用：四舍五入。
    public static readonly Func<double, double> Rounded = Num.Round;

    /// 视频用：向下取到偶数。
    public static readonly Func<double, double> Even = v => Math.Floor(v / 2) * 2;
}

internal readonly record struct PixelSize(double Width, double Height);

/// 视频截取范围（秒），代替 ClosedRange<Double>。
internal readonly record struct TimeRange(double Start, double End)
{
    public double Length => End - Start;
}
