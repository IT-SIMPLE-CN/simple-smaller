using System.ComponentModel;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Common;

/// 文件卡右上的「清空」（设计稿 V3 · 12：灰底胶囊 34 高、左右 12、字 12，放在「＋」后面）。
/// 一次清掉这一批，底部出「已清空 N 个文件 · 撤销 Ctrl+Z」；处理中禁用。快捷键 Ctrl+Shift+Delete 在主窗口上。
internal static class ClearButton
{
    public static TextCapsule Create(Workspace ws)
    {
        var b = new TextCapsule("清空", 34, 12, 12);
        // 旁白名照设计稿「清空这一批」（字面仍是「清空」）。
        AutomationProperties.SetName(b, "清空这一批");
        AutomationProperties.SetHelpText(b, "可以撤销");
        ToolTipService.SetToolTip(b, "清空这一批（Ctrl+Shift+Delete）");
        b.Click += (_, _) => AppModel.Shared.ClearUndoable(ws);
        void sync() => b.IsEnabled = !ws.Running;
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName == nameof(Workspace.Running)) sync(); };
        b.Loaded += (_, _) =>
        {
            ws.PropertyChanged -= changed;
            ws.PropertyChanged += changed;
            sync();
        };
        b.Unloaded += (_, _) => ws.PropertyChanged -= changed;
        return b;
    }
}
