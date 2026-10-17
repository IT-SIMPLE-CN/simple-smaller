using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SimpleSmaller.Bento;

namespace SimpleSmaller.Views.Cards;

/// 格式与质量：格式胶囊 + 灰底质量面板。
internal sealed class FormatQualityCard : Reactive
{
    private readonly Workspace ws;
    private readonly PillChoice<ImageFormatChoice> formats;
    private readonly Border card;

    public FormatQualityCard(Workspace ws)
    {
        this.ws = ws;
        var all = new[] { ImageFormatChoice.Webp, ImageFormatChoice.Avif, ImageFormatChoice.Jpeg, ImageFormatChoice.Png, ImageFormatChoice.Original };
        formats = new PillChoice<ImageFormatChoice>(all.Select(f => (f, f.Title())), ws.Draft.Image.Format, label: "格式");
        formats.Changed += f => ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Format = f } };
        var header = new Header { Glyph = "", Title = "格式与质量", Subtitle = "跟着配方走" };
        var formatGroup = new StackPanel { Spacing = 8 };
        formatGroup.Children.Add(Themed.Text("格式", 11, "BentoSecondary"));
        formatGroup.Children.Add(formats);
        var body = new Grid { RowSpacing = 14 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.Children.Add(header);
        Grid.SetRow(formatGroup, 1);
        body.Children.Add(formatGroup);
        var quality = new QualityPanel(ws);
        Grid.SetRow(quality, 3);
        body.Children.Add(quality);
        card = Card.Create(CardBody.Fit(body));
        Content = card;
        Watch(ws, nameof(Workspace.Draft), nameof(Workspace.Running));
    }

    protected override void Refresh()
    {
        formats.Selected = ws.Draft.Image.Format;
        Lock.Apply(card, ws.Running);
    }
}

/// 灰底质量面板：大数字 + 红色滑杆 +「更小 / 更清晰」；无损、限体积时换成对应说明。
internal sealed class QualityPanel : Reactive
{
    private enum Mode { Quality, Lossless, Target }

    private readonly Workspace ws;
    private readonly Border panel = new() { Padding = new Thickness(18, 16, 18, 12), CornerRadius = new CornerRadius(18) };
    private Mode? mode;

    // 质量。
    private readonly StackPanel qualityView = new() { Spacing = 10 };
    private readonly TextBlock qualityNumber = Themed.Text("", 22, "BentoPrimary", Themed.Medium);
    private readonly BrandSlider slider = new(30, 100);
    private bool syncing;

    // 无损。
    private readonly StackPanel losslessView = new() { Spacing = 10 };
    private readonly ToggleSwitch lossless = BentoToggle.Create("无损");

    // 限体积。
    private readonly StackPanel targetView = new() { Spacing = 10 };
    private readonly NumberField target = new("体积上限") { FontSize = 22, FontWeight = Themed.Medium, Width = 80 };

    public QualityPanel(Workspace ws)
    {
        this.ws = ws;
        Themed.Bg(panel, "BentoFill");
        Content = panel;

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(Themed.Text("质量", 13));
        head.Children.Add(qualityNumber);
        qualityView.Children.Add(head);
        AutomationProperties.SetName(slider, "质量");
        slider.ValueChanged += (_, e) =>
        {
            if (syncing)
            {
                return;
            }
            var q = (int)Math.Round(e.NewValue);
            if (q != ws.Draft.Image.Quality)
            {
                ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Quality = q } };
            }
        };
        qualityView.Children.Add(slider);
        var ends = new Grid();
        var smaller = TextButton("更小", () => SetQuality(ws.Draft.Image.Quality - 6));
        var clearer = TextButton("更清晰", () => SetQuality(ws.Draft.Image.Quality + 6));
        clearer.HorizontalAlignment = HorizontalAlignment.Right;
        ends.Children.Add(smaller);
        ends.Children.Add(clearer);
        qualityView.Children.Add(ends);

        var lhead = new Grid { ColumnSpacing = 8 };
        lhead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lhead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lhead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lhead.Children.Add(Themed.Text("质量", 13));
        var lnum = Themed.Text("无损", 22, "BentoPrimary", Themed.Medium);
        Grid.SetColumn(lnum, 1);
        lhead.Children.Add(lnum);
        lossless.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(lossless, 2);
        lhead.Children.Add(lossless);
        lossless.Toggled += (_, _) =>
        {
            if (lossless.IsOn != ws.Draft.Image.Lossless)
            {
                ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Lossless = lossless.IsOn } };
            }
        };
        losslessView.Children.Add(lhead);
        losslessView.Children.Add(Note("画面一点不变，只去掉多余数据。"));

        var thead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        thead.Children.Add(Themed.Text("不超过", 13));
        target.VerticalAlignment = VerticalAlignment.Center;
        target.Commit = v =>
        {
            var kb = Math.Max(10, v);
            ws.Draft = ws.Draft with { Image = ws.Draft.Image with { TargetKB = kb } };
            return kb;
        };
        thead.Children.Add(target);
        thead.Children.Add(Themed.Text("KB", 13, "BentoSecondary"));
        targetView.Children.Add(thead);
        targetView.Children.Add(Note("逐档降质量，直到每张不超过这个体积。"));

        Watch(ws, nameof(Workspace.Draft));
    }

    private static TextBlock Note(string text)
    {
        var t = Themed.Text(text, 11, "BentoSecondary");
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    private static Button TextButton(string title, Action action)
    {
        var b = new Button { Style = (Style)Application.Current.Resources["BentoBareButton"], Content = Themed.Text(title, 11, "BentoSecondary"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(b, title);
        Motion.SetPress(b, true);
        b.Click += (_, _) => action();
        return b;
    }

    private void SetQuality(int q)
    {
        ws.Draft = ws.Draft with { Image = ws.Draft.Image with { Quality = Math.Clamp(q, 30, 100) } };
    }

    protected override void Refresh()
    {
        var image = ws.Draft.Image;
        var m = image.TargetKB is not null ? Mode.Target : image.Lossless ? Mode.Lossless : Mode.Quality;
        if (m != mode)
        {
            var first = mode is null;
            mode = m;
            panel.Child = m switch { Mode.Target => targetView, Mode.Lossless => losslessView, _ => qualityView };
            if (!first)
            {
                Motion.Appear(panel);
            }
        }
        switch (m)
        {
            case Mode.Target:
                target.Value = image.TargetKB ?? 0;
                break;
            case Mode.Lossless:
                lossless.IsOn = true;
                break;
            default:
                qualityNumber.Text = $"{image.Quality} · {QualityWord.Of(image.Quality)}";
                if ((int)Math.Round(slider.Value) != image.Quality)
                {
                    syncing = true;
                    slider.Value = image.Quality;
                    syncing = false;
                }
                break;
        }
    }
}
