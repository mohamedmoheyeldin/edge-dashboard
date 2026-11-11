using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace EdgeDashboard.Ui;

/// <summary>
/// A reading on the left, a 60-second graph in the middle and a scale on the right.
/// Fixed scale (0-100 %) or a scale that follows the lowest and highest value seen since launch.
/// The graph is positioned by time, not by sample count, and slides at a constant speed, so uneven
/// sample timing never shows up as a stutter. The shape is only rebuilt when a sample arrives;
/// between samples a single transform moves it.
/// </summary>
public sealed class GraphCard : Grid
{
    private const double SpanMs = 60_000;   // time shown across the full width
    private const double DelayMs = 900;     // the right edge shows "a moment ago", so new samples slide in from off-screen

    private readonly List<(long Time, double Value)> _samples = [];
    private readonly TextBlock _value, _unit, _scaleTop, _scaleBottom;
    private readonly Grid _plot = new();
    private readonly Polygon _fill = new();
    private readonly Polyline _line = new() { StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TranslateTransform _scroll = new();
    private readonly SolidColorBrush _valueBrush = new(Theme.Text);
    private readonly bool _fixedScale;
    private readonly Func<double, string> _formatScale;
    private double _min = double.NaN, _max = double.NaN;
    private long _drawnAt;                  // the moment the current shape was built for

    public GraphCard(string label, Color color, Color dark, bool fixedScale, Func<double, string> formatScale)
    {
        _fixedScale = fixedScale;
        _formatScale = formatScale;

        Background = Theme.Brush(Theme.Card);
        BorderBrush = Theme.Brush(Theme.CardBorder);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14, 8, 10, 8);
        Height = 88;

        var grid = Make.Columns(6, Make.Px(180), Make.Star, Make.Px(72));

        _value = Make.Text("–", 48, bold: true, font: Theme.Display);
        _value.Foreground = _valueBrush;
        _unit = Make.Text("", 22, Theme.TextMuted);
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new Thickness(6, 0, 0, 8);
        var valueLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -6, 0, 0) };
        valueLine.Children.Add(_value); valueLine.Children.Add(_unit);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Make.Text(label, 18, Theme.TextMuted));
        left.Children.Add(valueLine);
        left.At(grid, column: 0);

        _line.Stroke = Theme.Brush(color);
        _fill.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = dark, Offset = 0 },
                new GradientStop { Color = Color.FromArgb(150, color.R, color.G, color.B), Offset = 0.45 },
                new GradientStop { Color = Color.FromArgb(0, color.R, color.G, color.B), Offset = 1 },
            },
        };
        _fill.RenderTransform = _scroll;
        _line.RenderTransform = _scroll;
        _plot.Children.Add(_fill);
        _plot.Children.Add(_line);
        // Soft ends: the graph fades into the card on both sides instead of stopping at a hard edge.
        foreach (bool onLeft in new[] { true, false })
        {
            var solid = Theme.CardSolid;
            var clear = Color.FromArgb(0, solid.R, solid.G, solid.B);
            _plot.Children.Add(new Rectangle
            {
                Width = 150, IsHitTestVisible = false,
                HorizontalAlignment = onLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop { Color = onLeft ? solid : clear, Offset = 0 },
                        new GradientStop { Color = onLeft ? clear : solid, Offset = 1 },
                    },
                },
            });
        }
        _plot.SizeChanged += (_, e) =>
        {
            _plot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Redraw();
        };
        _plot.At(grid, column: 1);

        _scaleTop = Make.Text(fixedScale ? formatScale(100) : "", 14, Theme.TextMuted);
        _scaleBottom = Make.Text(fixedScale ? formatScale(0) : "", 14, Theme.TextMuted);
        _scaleTop.HorizontalAlignment = _scaleBottom.HorizontalAlignment = HorizontalAlignment.Right;
        _scaleTop.VerticalAlignment = VerticalAlignment.Top;
        _scaleBottom.VerticalAlignment = VerticalAlignment.Bottom;
        var scale = new Grid();
        scale.Children.Add(_scaleTop); scale.Children.Add(_scaleBottom);
        scale.At(grid, column: 2);

        Children.Add(grid);
        CompositionTarget.Rendering += (_, _) => Scroll();
    }

    public void SetReading(string value, string unit, Color? color = null)
    {
        if (_value.Text != value) _value.Text = value;
        if (_unit.Text != unit) _unit.Text = unit;
        var c = color ?? Theme.Text;
        if (_valueBrush.Color != c) _valueBrush.Color = c;
    }

    private Motion.Value? _motion;
    private string _format = "0", _shownText = "";
    private bool _rule;

    /// <summary>Shows a reading that glides to each new value instead of jumping.</summary>
    public void SetNumber(double value, string format, string unit, bool rule = false)
    {
        _format = format; _rule = rule;
        if (_unit.Text != unit) _unit.Text = unit;
        // with the colour rule on, the unit ("%") shares the number's brush so both change colour together
        if (rule && !ReferenceEquals(_unit.Foreground, _valueBrush)) _unit.Foreground = _valueBrush;
        _motion ??= Motion.Create(v =>
        {
            string s = v.ToString(_format, System.Globalization.CultureInfo.GetCultureInfo("en-US"));
            if (s != _shownText) { _shownText = s; _value.Text = s; }
            var c = _rule ? Theme.Rule(v) : Theme.Text;
            if (_valueBrush.Color != c) _valueBrush.Color = c;
        });
        _motion.To(value);
    }

    public void Push(double sample)
    {
        if (double.IsNaN(sample)) return;
        long now = Environment.TickCount64;
        _samples.Add((now, sample));
        while (_samples.Count > 2 && now - _samples[0].Time > SpanMs + 3000) _samples.RemoveAt(0);
        if (!_fixedScale)
        {
            _min = double.IsNaN(_min) ? sample : Math.Min(_min, sample);
            _max = double.IsNaN(_max) ? sample : Math.Max(_max, sample);
            string top = _formatScale(_max), bottom = _formatScale(_min);
            if (_scaleTop.Text != top) _scaleTop.Text = top;
            if (_scaleBottom.Text != bottom) _scaleBottom.Text = bottom;
        }
        Redraw();
    }

    /// <summary>Moves the finished shape left at a constant speed; runs every frame and does nothing else.</summary>
    private void Scroll()
    {
        double w = _plot.ActualWidth;
        if (_drawnAt == 0 || w <= 0) return;
        _scroll.X = -(Environment.TickCount64 - _drawnAt) / SpanMs * w;
    }

    private void Redraw()
    {
        double w = _plot.ActualWidth, h = _plot.ActualHeight;
        if (w <= 0 || h <= 0 || _samples.Count < 2) return;

        double lo = _fixedScale ? 0 : _min, hi = _fixedScale ? 100 : _max;
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        const double inset = 2; // keeps the 3 px line from being clipped at the top and bottom

        long now = Environment.TickCount64;
        var line = new PointCollection();
        var fill = new PointCollection();
        int last = _samples.Count - 1;
        double firstX = 0, lastX = 0;
        for (int i = 0; i <= last; i++)
        {
            // light smoothing so the line reads as a curve rather than a sawtooth
            double v = (_samples[Math.Max(0, i - 1)].Value + _samples[i].Value + _samples[Math.Min(last, i + 1)].Value) / 3;
            // x by age: a sample DelayMs old sits exactly on the right edge
            double x = w - (now - DelayMs - _samples[i].Time) / SpanMs * w;
            double y = inset + (1 - Math.Clamp((v - lo) / (hi - lo), 0, 1)) * (h - 2 * inset);
            if (i == 0) { firstX = x; fill.Add(new Point(x, h)); }
            line.Add(new Point(x, y));
            fill.Add(new Point(x, y));
            lastX = x;
        }
        fill.Add(new Point(lastX, h));
        fill.Add(new Point(firstX, h));
        _line.Points = line;
        _fill.Points = fill;
        _drawnAt = now;
        _scroll.X = 0;
    }
}
