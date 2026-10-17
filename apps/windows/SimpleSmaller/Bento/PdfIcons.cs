using Microsoft.UI.Xaml.Controls;

namespace SimpleSmaller.Bento;

/// PDF 页用到的设计稿图标（V2.1 · 08 的 SVG 路径）。
public static class PdfIcons
{
    /// 两层叠起（文件与页面卡头）。
    public const string Layers = "M12 3l9 5-9 5-9-5zM3 13l9 5 9-5";
    /// 两根滑杆（压缩档卡头）。
    public static readonly string Sliders = "M4 8h9M17 8h3M4 16h3M11 16h9" + LineIcon.Circle(15, 8, 2) + LineIcon.Circle(9, 16, 2);
    /// 两张叠放的纸（选项卡头）。
    public const string Pages = "M7 3h8l4 4v12H7zM4 6v15h12";
    /// 对勾（选中的压缩档）。
    public const string Check = "M5 12l5 5 9-10";
}

/// 卡片头的圆形图标换成设计稿的线性图标（Header 自带的是字形图标）。
public static class HeaderIcon
{
    public static void Set(Header header, string data, double size = 16)
    {
        if (header.Children.Count > 0 && header.Children[0] is Border back)
        {
            back.Child = new LineIcon(data, size);
        }
    }
}
