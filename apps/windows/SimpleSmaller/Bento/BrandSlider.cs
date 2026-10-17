using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace SimpleSmaller.Bento;

/// 粗轨道品牌红滑杆（设计稿：8 px 轨道、24 px 白色圆钮）。
/// 就是系统 Slider 换了模板：方向键 / PageUp / Home / End 可调，旁白按系统滑杆读（有数值、可调）。
public sealed class BrandSlider : Slider
{
    private static Style? style;

    public BrandSlider(double min, double max, double step = 1)
    {
        Style = style ??= (Style)XamlReader.Load(Xaml);
        Minimum = min;
        Maximum = max;
        StepFrequency = step;
        SmallChange = step;
        LargeChange = step * 6;
        IsThumbToolTipEnabled = false;
        Height = 24;
    }

    // 模板只留水平方向用到的部件：轨道、左边已走过的一段、圆钮（Slider 代码按名字找它们）。
    private const string Xaml = """
<Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
  <Setter Property="UseSystemFocusVisuals" Value="True" />
  <Setter Property="FocusVisualMargin" Value="-4,-2" />
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Slider">
        <Grid x:Name="SliderContainer" Background="Transparent">
          <Grid x:Name="HorizontalTemplate" Background="Transparent">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="Auto" />
              <ColumnDefinition Width="Auto" />
              <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <Rectangle x:Name="HorizontalTrackRect" Grid.ColumnSpan="3" Height="8" RadiusX="4" RadiusY="4"
                       VerticalAlignment="Center" Fill="{ThemeResource BentoLine}" />
            <Rectangle x:Name="HorizontalDecreaseRect" Height="8" RadiusX="4" RadiusY="4" Margin="0,0,-12,0"
                       VerticalAlignment="Center" Fill="{StaticResource Brand}" />
            <Thumb x:Name="HorizontalThumb" Grid.Column="1" Width="24" Height="24" VerticalAlignment="Center"
                   AutomationProperties.AccessibilityView="Raw">
              <Thumb.Template>
                <ControlTemplate TargetType="Thumb">
                  <Grid>
                    <Ellipse Fill="#24000000" Margin="0,2,0,-2" />
                    <Ellipse Fill="White" Stroke="#14000000" StrokeThickness="0.5" />
                  </Grid>
                </ControlTemplate>
              </Thumb.Template>
            </Thumb>
          </Grid>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
""";
}
