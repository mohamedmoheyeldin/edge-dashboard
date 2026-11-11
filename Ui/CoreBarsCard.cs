using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace EdgeDashboard.Ui;

/// <summary>One CCD: a title row and eight slim vertical bars, one per core, coloured by load.</summary>
public sealed class CoreBarsCard : Grid
{
    private const int Cores = 8;
    private readonly TextBlock _temp, _clock;
    private readonly Border _gameTag;
    private readonly TextBlock[] _percent = new TextBlock[Cores];
    private readonly Border[] _track = new Border[Cores];
    private readonly Border[] _bar = new Border[Cores];
    private readonly double[] _target = new double[Cores];
    private readonly double[] _shown = new double[Cores];

    public CoreBarsCard(string title, int firstCore)
    {
        Background = Theme.Brush(Theme.Card);
        BorderBrush = Theme.Brush(Theme.CardBorder);
        BorderThickness = new Thickness(2);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(18, 12, 18, 12);

        var root = Make.Rows(12, Make.Auto, Make.Star);

        var header = Make.Columns(12, Make.Auto, Make.Auto, Make.Star, Make.Auto, Make.Auto);
        Make.Text(title, 26, bold: true).At(header, column: 0);
        var tagText = Make.Text("Game", 16, Theme.Hex("#E4E4E4"));
        _gameTag = new Border
        {
            BorderBrush = Theme.Brush(Theme.Hex("#8A8A8A")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 1, 10, 2), VerticalAlignment = VerticalAlignment.Center, Child = tagText, Visibility = Visibility.Collapsed,
        };
        _gameTag.At(header, column: 1);
        _temp = Make.Text("–", 24, bold: true);
        _temp.VerticalAlignment = VerticalAlignment.Center;
        _temp.At(header, column: 3);
        _clock = Make.Text("", 24, Theme.TextMuted);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.At(header, column: 4);
        header.At(root, row: 0);

        var bars = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < Cores; i++)
        {
            bars.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
            var column = Make.Rows(6, Make.Auto, Make.Star, Make.Auto);

            _percent[i] = Make.Text("0", 20);
            _percent[i].HorizontalAlignment = HorizontalAlignment.Center;
            _percent[i].At(column, row: 0);

            _bar[i] = new Border { CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            _track[i] = new Border { Width = 18, CornerRadius = new CornerRadius(9), Background = Theme.Brush(Theme.Track), Child = _bar[i] };
            _track[i].At(column, row: 1);

            var index = Make.Text((firstCore + i).ToString(), 18, Theme.TextMuted);
            index.HorizontalAlignment = HorizontalAlignment.Center;
            index.At(column, row: 2);

            column.At(bars, column: i);
        }
        bars.At(root, row: 1);
        Children.Add(root);

        // Bars ease toward their target every frame instead of jumping twice a second.
        CompositionTarget.Rendering += (_, _) => Animate();
    }

    public void SetHeader(double? temp, double? clockMhz, bool game)
    {
        // Both readings glide to new values rather than jumping.
        if (temp is double t) (_tempMotion ??= Motion.Number(_temp, v => $"{v:0}°C", rule: true)).To(t);
        if (clockMhz is double c) (_clockMotion ??= Motion.Number(_clock, v => $"{v / 1000:0.0} GHz")).To(c);
        var tag = game ? Visibility.Visible : Visibility.Collapsed;
        if (_gameTag.Visibility != tag) _gameTag.Visibility = tag;
    }

    private Motion.Value? _tempMotion, _clockMotion;

    public void SetCore(int index, double percent)
    {
        if (index is < 0 or >= Cores) return;
        _target[index] = Math.Clamp(percent, 0, 100);
    }

    private readonly SolidColorBrush[] _brush = new SolidColorBrush[Cores];
    private readonly double[] _drawn = new double[Cores];
    private long _lastFrame;

    private void Animate()
    {
        long now = Environment.TickCount64;
        double dt = _lastFrame == 0 ? 16 : Math.Clamp(now - _lastFrame, 1, 100);
        _lastFrame = now;
        double ease = 1 - Math.Exp(-dt / 300.0); // same glide speed whatever the frame rate

        for (int i = 0; i < Cores; i++)
        {
            double delta = _target[i] - _shown[i];
            if (Math.Abs(delta) > 0.02) _shown[i] += delta * ease; else _shown[i] = _target[i];
            // the number above the bar counts along with it
            string text = Math.Round(_shown[i]).ToString("0");
            if (_percent[i].Text != text) _percent[i].Text = text;

            double full = _track[i].ActualHeight;
            if (full <= 0) continue;
            double height = Math.Max(_shown[i] > 0.5 ? 18 : 0, full * _shown[i] / 100); // never shorter than its own rounded cap
            if (Math.Abs(height - _drawn[i]) < 0.25 && _brush[i] is not null) continue;  // nothing visible changed
            _drawn[i] = height;
            _bar[i].Height = height;
            // one brush per bar, recoloured in place, so animating creates no garbage
            if (_brush[i] is null) { _brush[i] = new SolidColorBrush(Theme.Rule(_shown[i])); _bar[i].Background = _brush[i]; }
            else _brush[i].Color = Theme.Rule(_shown[i]);
        }
    }
}
