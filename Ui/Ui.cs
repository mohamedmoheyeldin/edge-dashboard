using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EdgeDashboard.Ui;

/// <summary>Shared colours and small element builders, so the layout code stays readable.</summary>
public static class Theme
{
    // Surfaces and text come from Windows' own dark theme (the same values Settings and other
    // Windows 11 apps use); the hex values are only fallbacks if a resource is missing.
    // The page and the cards are deliberately a step darker than stock Windows.
    public static readonly Color Page = Darken(Res("SolidBackgroundFillColorBase", "#202020"), 0.62);
    public static readonly Color Panel = Res("CardBackgroundFillColorDefault", "#0DFFFFFF");
    public static readonly Color PanelBorder = Res("CardStrokeColorDefault", "#19000000");
    public static readonly Color Card = Fade(Res("ControlFillColorDefault", "#0FFFFFFF"), 0.62);
    /// <summary>The colour a card actually appears as (its translucent fill over the page), for fades that must blend into it.</summary>
    public static readonly Color CardSolid = Color.FromArgb(255,
        (byte)(Page.R + (Card.R - Page.R) * Card.A / 255.0), (byte)(Page.G + (Card.G - Page.G) * Card.A / 255.0), (byte)(Page.B + (Card.B - Page.B) * Card.A / 255.0));
    public static readonly Color CardBorder = Fade(Res("ControlStrokeColorDefault", "#12FFFFFF"), 0.8);
    public static readonly Color Track = Res("ControlAltFillColorSecondary", "#19000000");
    public static readonly Color Text = Res("TextFillColorPrimary", "#FFFFFF");
    public static readonly Color TextSoft = Res("TextFillColorSecondary", "#C5FFFFFF");
    public static readonly Color TextMuted = Res("TextFillColorTertiary", "#87FFFFFF");
    // The accent is the colour chosen in Windows Settings > Personalisation > Colours.
    public static readonly Color Accent = WindowsAccent();
    public static readonly Color Amd = Hex("#F26522");
    public static readonly Color Nvidia = Hex("#76B900");
    public static readonly Color Danger = Hex("#C42B1C");

    public static readonly FontFamily Body = new("Segoe UI Variable Text, Segoe UI");
    public static readonly FontFamily Display = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255; int i = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); i = 2; }
        return Color.FromArgb(a, Convert.ToByte(hex.Substring(i, 2), 16), Convert.ToByte(hex.Substring(i + 2, 2), 16), Convert.ToByte(hex.Substring(i + 4, 2), 16));
    }

    public static SolidColorBrush Brush(Color c) => new(c);

    private static Color Darken(Color c, double factor) => Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

    /// <summary>Reduces a translucent overlay's strength, which makes the surface it tints darker.</summary>
    private static Color Fade(Color c, double factor) => Color.FromArgb((byte)(c.A * factor), c.R, c.G, c.B);

    private static Color Res(string key, string fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out object value) && value is Color c) return c;
        }
        catch (Exception) { }
        return Hex(fallback);
    }

    private static Color WindowsAccent()
    {
        try
        {
            // "Light 2" is the accent shade Windows uses for text and controls on dark backgrounds.
            return new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentLight2);
        }
        catch (Exception) { return Hex("#60CDFF"); }
    }

    /// <summary>
    /// The dashboard's colour rule for temperatures and loads: 0 = white, 50 = blue, 75 = amber, 100 = red,
    /// sweeping through green between blue and amber so mid-range values stay bright.
    /// </summary>
    public static Color Rule(double v)
    {
        if (double.IsNaN(v) || v <= 0) return Text;
        if (v <= 50) return Hsl(210, 1, 1 - 0.40 * (v / 50));
        if (v <= 75) { double k = (v - 50) / 25; return Hsl(210 - 168 * k, 1, 0.60 - 0.04 * k); }
        if (v < 100) { double k = (v - 75) / 25; return Hsl(42 - 42 * k, 1, 0.56 + 0.04 * k); }
        return Hsl(0, 1, 0.60);
    }

    private static Color Hsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = ((int)Math.Floor(h / 60) % 6) switch
        {
            0 => (c, x, 0d), 1 => (x, c, 0d), 2 => (0d, c, x), 3 => (0d, x, c), 4 => (x, 0d, c), _ => (c, 0d, x),
        };
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public static class Make
{
    public static TextBlock Text(string text, double size, Color? color = null, bool bold = false, FontFamily? font = null) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = font ?? Theme.Body,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Theme.Brush(color ?? Theme.Text),
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static FontIcon Icon(string glyph, double size, Color color) => new()
    {
        Glyph = glyph, FontSize = size, FontFamily = Theme.Icons, Foreground = Theme.Brush(color),
    };

    /// <summary>An outer panel (the large rounded boxes).</summary>
    // A section is only a layout group now: no fill and no outline, so the inner cards sit
    // directly on the page background. A small inset keeps neighbouring sections apart.
    public static Border Panel(UIElement child, Thickness padding) => new()
    {
        Padding = new Thickness(Math.Min(padding.Left, 4), Math.Min(padding.Top, 2), Math.Min(padding.Right, 4), Math.Min(padding.Bottom, 2)),
        Child = child,
    };

    /// <summary>An inner card.</summary>
    public static Border Card(UIElement child, Thickness padding, double border = 1) => new()
    {
        Background = Theme.Brush(Theme.Card),
        BorderBrush = Theme.Brush(Theme.CardBorder),
        BorderThickness = new Thickness(border),
        CornerRadius = new CornerRadius(8),
        Padding = padding,
        Child = child,
    };

    public static Grid Columns(double spacing, params GridLength[] widths)
    {
        var g = new Grid { ColumnSpacing = spacing };
        foreach (var w in widths) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        return g;
    }

    public static Grid Rows(double spacing, params GridLength[] heights)
    {
        var g = new Grid { RowSpacing = spacing };
        foreach (var h in heights) g.RowDefinitions.Add(new RowDefinition { Height = h });
        return g;
    }

    public static T At<T>(this T element, Grid grid, int row = 0, int column = 0, int rowSpan = 1, int columnSpan = 1) where T : FrameworkElement
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan); Grid.SetColumnSpan(element, columnSpan);
        grid.Children.Add(element);
        return element;
    }

    public static readonly GridLength Auto = GridLength.Auto;
    public static readonly GridLength Star = new(1, GridUnitType.Star);
    public static GridLength Px(double v) => new(v);
}

/// <summary>A small stat tile: grey label on top, value (with optional small unit) underneath, centred.</summary>
public sealed class StatTile : Grid
{
    private readonly TextBlock _value;
    private readonly TextBlock _unit;

    public StatTile(string label, double valueSize = 34)
    {
        Background = Theme.Brush(Theme.Card);
        BorderBrush = Theme.Brush(Theme.CardBorder);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14, 5, 14, 5);

        _value = Make.Text("–", valueSize, bold: true);
        _unit = Make.Text("", 18, Theme.TextMuted);
        _unit.VerticalAlignment = VerticalAlignment.Bottom;
        _unit.Margin = new Thickness(5, 0, 0, valueSize * 0.14);

        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        line.Children.Add(_value); line.Children.Add(_unit);

        var label1 = Make.Text(label, 18, Theme.TextMuted);
        label1.HorizontalAlignment = HorizontalAlignment.Center;

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(label1); stack.Children.Add(line);
        Children.Add(stack);
    }

    private SolidColorBrush? _brush;
    private Motion.Value? _motion;
    private string _format = "", _suffix = "", _shown = "";
    private bool _rule, _blank = true;

    /// <summary>Shows fixed text (used for "–" when there is no reading).</summary>
    public void Set(string value, string unit = "", Color? color = null)
    {
        _blank = true;
        _shown = value;
        _value.Text = value;
        _unit.Text = unit;
        Paint(color ?? Theme.Text);
    }

    /// <summary>Shows a number that glides to each new value. With <paramref name="rule"/> it is coloured by the colour rule.</summary>
    public void SetNumber(double value, string format, string unit = "", string suffix = "", bool rule = false)
    {
        _format = format; _suffix = suffix; _rule = rule;
        if (_unit.Text != unit) _unit.Text = unit;
        _motion ??= Motion.Create(v =>
        {
            if (_blank) return;
            string s = v.ToString(_format, System.Globalization.CultureInfo.GetCultureInfo("en-US")) + _suffix;
            if (s != _shown) { _shown = s; _value.Text = s; }
            Paint(_rule ? Theme.Rule(v) : Theme.Text);
        });
        bool wasBlank = _blank;
        _blank = false;
        _motion.To(value);
        if (wasBlank) // coming back from "–": show the number straight away
        {
            _shown = _motion.Current.ToString(format, System.Globalization.CultureInfo.GetCultureInfo("en-US")) + suffix;
            _value.Text = _shown;
            Paint(rule ? Theme.Rule(_motion.Current) : Theme.Text);
        }
    }

    private void Paint(Color c)
    {
        if (_brush is null) { _brush = new SolidColorBrush(c); _value.Foreground = _brush; }
        else if (_brush.Color != c) _brush.Color = c;
    }
}
