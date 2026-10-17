using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace SimpleSmaller.Bento;

/// 无边框输入框（对应 mac 的 .textFieldStyle(.plain)）：没有底色、边框、清除按钮，只有文字与光标。
public class PlainField : TextBox
{
    private static Style? style;

    public PlainField(string label)
    {
        Style = style ??= (Style)XamlReader.Load(Xaml);
        AutomationProperties.SetName(this, label);
        IsSpellCheckEnabled = false;
        IsTextPredictionEnabled = false;
        Loaded += (_, _) => Paint();
        ActualThemeChanged += (_, _) => Paint();
    }

    /// 文字颜色的色板键（默认主文字色）。
    public string ForegroundKey { get; set; } = "BentoPrimary";

    private void Paint() => Foreground = Palette.Get(this, ForegroundKey);

    /// 外部改了值才覆盖；正在输入时不打断。
    public void Show(string text)
    {
        if (FocusState == FocusState.Unfocused && Text != text)
        {
            Text = text;
        }
    }

    private const string Xaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TextBox">
  <Setter Property="Padding" Value="0" />
  <Setter Property="MinWidth" Value="0" />
  <Setter Property="MinHeight" Value="0" />
  <Setter Property="SelectionHighlightColor" Value="{StaticResource Brand}" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="TextBox">
        <Grid Background="Transparent">
          <ScrollViewer x:Name="ContentElement" Padding="{TemplateBinding Padding}" IsTabStop="False" ZoomMode="Disabled"
                        HorizontalScrollMode="Disabled" HorizontalScrollBarVisibility="Hidden"
                        VerticalScrollMode="Disabled" VerticalScrollBarVisibility="Disabled"
                        AutomationProperties.AccessibilityView="Raw" />
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";
}

/// 整数输入框：回车或离开时提交给 Commit（它把值夹到范围内写进模型、返回实际的值），然后显示这个值。
public sealed class NumberField : PlainField
{
    private int value;

    public Func<int, int>? Commit { get; set; }

    public NumberField(string label) : base(label)
    {
        InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } };
        LostFocus += (_, _) => Submit();
        KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };
    }

    public int Value
    {
        get => value;
        set
        {
            this.value = value;
            Show(value.ToString());
        }
    }

    private void Submit()
    {
        if (int.TryParse(Text.Trim(), out var v) && v != value && Commit is { } commit)
        {
            value = commit(v);
        }
        // 提交后（或输入不是数字时）显示模型里的值。
        Text = value.ToString();
    }
}
