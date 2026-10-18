using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace SimpleSmaller.Views.Sub;

/// 按类型分组的配方下拉（托盘小窗、监视文件夹共用）：图片 / 视频 / PDF 各一个不可选的小标题。
internal static class RecipePicker
{
    public static ComboBox Create(string selectedId, Action<Recipe> changed, string label = "常用")
    {
        var box = new ComboBox { MinWidth = 150, MaxWidth = 200 };
        AutomationProperties.SetName(box, label);
        foreach (var type in MediaTypes.All)
        {
            box.Items.Add(new ComboBoxItem
            {
                Content = type.Title(),
                IsEnabled = false,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            foreach (var r in AppModel.Shared.Store.All.Where(r => r.Type == type))
            {
                var item = new ComboBoxItem { Content = r.Name, Tag = r.Id };
                box.Items.Add(item);
                if (r.Id == selectedId) box.SelectedItem = item;
            }
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: string id } && AppModel.Shared.Store.RecipeById(id) is { } r) changed(r);
        };
        return box;
    }
}
