using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EdgeDashboard.Ui;

/// <summary>
/// Makes values glide instead of jump. Sensors only report twice a second; every displayed number,
/// bar and colour eases toward the newest reading on each drawn frame, so the screen is always in
/// gentle motion rather than "refreshing".
/// </summary>
public static class Motion
{
    public sealed class Value
    {
        internal double Shown = double.NaN, Target = double.NaN, Tau;
        internal Action<double> Apply = _ => { };

        /// <summary>Sets a new destination; the first value is shown immediately.</summary>
        public void To(double target)
        {
            if (double.IsNaN(target)) return;
            Target = target;
            if (double.IsNaN(Shown)) { Shown = target; Apply(Shown); }
        }

        public double Current => Shown;
    }

    private static readonly List<Value> Items = [];
    private static long _last;
    private static bool _hooked;

    /// <param name="apply">Called with the eased value whenever it changes.</param>
    /// <param name="tauMs">Glide time constant; larger is slower and softer.</param>
    public static Value Create(Action<double> apply, double tauMs = 260)
    {
        if (!_hooked) { _hooked = true; CompositionTarget.Rendering += (_, _) => Step(); }
        var v = new Value { Apply = apply, Tau = tauMs };
        Items.Add(v);
        return v;
    }

    private static void Step()
    {
        long now = Environment.TickCount64;
        double dt = _last == 0 ? 16 : Math.Clamp(now - _last, 1, 100);
        _last = now;
        foreach (var v in Items)
        {
            if (double.IsNaN(v.Target) || v.Shown == v.Target) continue;
            double delta = v.Target - v.Shown;
            if (Math.Abs(delta) < Math.Max(Math.Abs(v.Target) * 0.0004, 0.0005)) v.Shown = v.Target;
            else v.Shown += delta * (1 - Math.Exp(-dt / v.Tau));
            v.Apply(v.Shown);
        }
    }

    /// <summary>A text block whose number counts smoothly to each new value, optionally recoloured by the colour rule.</summary>
    public static Value Number(TextBlock text, Func<double, string> format, bool rule = false, Func<double, double>? ruleInput = null)
    {
        var brush = rule ? new SolidColorBrush(Theme.Text) : null;
        if (brush is not null) text.Foreground = brush;
        string shown = "";
        return Create(v =>
        {
            string s = format(v);
            if (s != shown) { shown = s; text.Text = s; }
            if (brush is not null)
            {
                Color c = Theme.Rule(ruleInput is null ? v : ruleInput(v));
                if (brush.Color != c) brush.Color = c;
            }
        });
    }
}
