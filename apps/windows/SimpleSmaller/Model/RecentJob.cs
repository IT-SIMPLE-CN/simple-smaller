using CommunityToolkit.Mvvm.ComponentModel;

namespace SimpleSmaller;

/// 最近完成的一批（托盘小窗的「最近」）。只在内存里，和 mac 版一样不落盘。
internal sealed partial class RecentJob : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required string Recipe { get; init; }
    public ulong Before { get; init; }
    public ulong After { get; init; }
    public DateTime Date { get; init; }
    public IReadOnlyList<string> Outputs { get; init; } = [];
    /// 界面层的图片（同 FileItem.Thumbnail）。
    [ObservableProperty] private object? thumbnail;
}
