using System.Collections.Concurrent;
using EdgeDashboard.Display;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EdgeDashboard.Ui;

/// <summary>
/// The second page (swipe left from the dashboard): brightness, contrast and the other settings each
/// monitor exposes. Changes are sent on a background thread because monitors answer slowly.
/// </summary>
public sealed class DisplayPage
{
    private readonly Grid _host;
    private readonly DispatcherQueue _ui;
    private readonly ConcurrentDictionary<(DdcMonitor Monitor, byte Code), uint> _pending = new();
    private volatile bool _running = true;
    private readonly Dictionary<(DdcMonitor Monitor, byte Code), (Slider Slider, TextBlock Value)> _controls = new();
    private bool _refreshing; // true while sliders are being set from the monitor, so they don't send the value back

    public DisplayPage(Grid host)
    {
        _host = host;
        _ui = DispatcherQueue.GetForCurrentThread();
        _host.Padding = new Thickness(8);
        _host.Children.Add(Centered("Reading monitor settings…"));
        _ = Task.Run(Load);
        _ = Task.Run(SendLoop);
    }

    public void Stop() => _running = false;

    private static TextBlock Centered(string text)
    {
        var t = Make.Text(text, 26, Theme.TextMuted);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    private sealed record Setting(string Label, byte Code, uint Current, uint Max);

    private void Load()
    {
        var found = new List<(DdcMonitor Monitor, List<Setting> Settings)>();
        try
        {
            var monitors = DdcMonitor.Enumerate().OrderByDescending(m => m.IsEdge).ThenBy(m => m.Y).ThenBy(m => m.X).ToList();
            foreach (var m in monitors)
            {
                var wanted = new List<(string, byte)> { ("Brightness", DdcMonitor.Brightness), ("Contrast", DdcMonitor.Contrast) };
                if (m.IsEdge) wanted.AddRange([("Sharpness", DdcMonitor.Sharpness), ("Red", DdcMonitor.RedGain), ("Green", DdcMonitor.GreenGain), ("Blue", DdcMonitor.BlueGain)]);
                var settings = new List<Setting>();
                foreach (var (label, code) in wanted)
                    if (m.Get(code) is var (current, max)) settings.Add(new Setting(label, code, current, max));
                found.Add((m, settings));
            }
        }
        catch (Exception) { /* fall through with whatever was found */ }
        _ui.TryEnqueue(() => Build(found));
    }

    private void Build(List<(DdcMonitor Monitor, List<Setting> Settings)> found)
    {
        _host.Children.Clear();
        var rows = Make.Rows(14, Make.Auto, Make.Star);

        var header = Make.Columns(16, Make.Auto, Make.Auto, Make.Star, Make.Auto);
        var icon = Make.Icon("", 28, Theme.Accent);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.At(header, column: 0);
        Make.Text("Screen settings", 28, bold: true).At(header, column: 1);
        var hint = Make.Text("Swipe right to return to the dashboard", 20, Theme.TextMuted);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.At(header, column: 3);
        header.At(rows, row: 0);

        if (found.Count == 0)
        {
            Centered("No monitors answered. Check that DDC/CI is enabled in each monitor's menu.").At(rows, row: 1);
            _host.Children.Add(Make.Panel(rows, new Thickness(20, 14, 20, 16)));
            return;
        }

        var cards = new Grid { ColumnSpacing = 16 };
        int alienware = 0;
        for (int i = 0; i < found.Count; i++)
        {
            var (monitor, settings) = found[i];
            // The Edge has more settings, so it gets a wider card with two columns of sliders.
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(monitor.IsEdge ? 2 : 1, GridUnitType.Star) });
            string name = monitor.IsEdge ? "Xeneon Edge" : monitor.Description.Replace("Dell ", "").Replace("(DisplayPort)", "").Trim() + (found.Count(f => !f.Monitor.IsEdge) > 1 ? (alienware++ == 0 ? " · upper" : " · lower") : "");
            MonitorCard(name, monitor, settings).At(cards, column: i);
        }
        cards.At(rows, row: 1);
        _host.Children.Add(Make.Panel(rows, new Thickness(20, 14, 20, 16)));
    }

    private Border MonitorCard(string name, DdcMonitor monitor, List<Setting> settings)
    {
        var rows = Make.Rows(10, Make.Auto, Make.Star);

        var title = Make.Columns(16, Make.Auto, Make.Star, Make.Auto);
        Make.Text(name, 26, bold: true).At(title, column: 0);
        var detail = Make.Text($"{monitor.Width} × {monitor.Height}" + (monitor.RefreshRate > 0 ? $" · {monitor.RefreshRate} Hz" : ""), 20, Theme.TextMuted);
        detail.VerticalAlignment = VerticalAlignment.Center;
        detail.At(title, column: 1);
        if (monitor.IsEdge && settings.Count > 0)
        {
            // The monitor's own "restore factory defaults" command; only offered for the Edge.
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(Make.Icon("\uE72C", 20, Theme.Text));
            content.Children.Add(Make.Text("Reset to factory", 20));
            var reset = new Button { Height = 48, Padding = new Thickness(16, 0, 16, 0), CornerRadius = new CornerRadius(8), Content = content };
            reset.Click += (_, _) => ResetToFactory(monitor, reset);
            reset.At(title, column: 2);
        }
        title.At(rows, row: 0);

        if (settings.Count == 0)
        {
            Centered("This monitor doesn't accept settings from the PC").At(rows, row: 1);
            return Make.Card(rows, new Thickness(22, 16, 22, 16), border: 2);
        }

        int columns = monitor.IsEdge ? 2 : 1;
        // Rows share the card's full height, so the sliders spread out instead of huddling in the middle.
        var grid = new Grid { ColumnSpacing = 48, RowSpacing = 6 };
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
        int perColumn = (int)Math.Ceiling(settings.Count / (double)columns);
        for (int r = 0; r < perColumn; r++) grid.RowDefinitions.Add(new RowDefinition { Height = Make.Star });
        for (int i = 0; i < settings.Count; i++)
            SliderRow(monitor, settings[i]).At(grid, row: i % perColumn, column: i / perColumn);
        grid.At(rows, row: 1);

        return Make.Card(rows, new Thickness(22, 16, 22, 16), border: 2);
    }

    private Grid SliderRow(DdcMonitor monitor, Setting setting)
    {
        var row = Make.Columns(20, Make.Px(170), Make.Star, Make.Px(150));
        row.VerticalAlignment = VerticalAlignment.Center;
        var label = Make.Text(setting.Label, 30, Theme.TextSoft);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.At(row, column: 0);

        var value = Make.Text($"{setting.Current}", 44, bold: true, font: Theme.Display);
        var max = Make.Text($" / {setting.Max}", 22, Theme.TextMuted);
        max.VerticalAlignment = VerticalAlignment.Bottom; max.Margin = new Thickness(0, 0, 0, 8);
        var readout = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        readout.Children.Add(value); readout.Children.Add(max);
        readout.At(row, column: 2);

        var slider = new Slider
        {
            Minimum = 0, Maximum = setting.Max, Value = Math.Min(setting.Current, setting.Max), StepFrequency = 1,
            MinHeight = 56, VerticalAlignment = VerticalAlignment.Center, IsThumbToolTipEnabled = false,
        };
        slider.ValueChanged += (_, e) =>
        {
            uint v = (uint)Math.Round(e.NewValue);
            value.Text = v.ToString();
            if (!_refreshing) _pending[(monitor, setting.Code)] = v; // only the latest value per setting is sent
        };
        slider.At(row, column: 1);
        _controls[(monitor, setting.Code)] = (slider, value);
        return row;
    }

    private void ResetToFactory(DdcMonitor monitor, Button button)
    {
        button.IsEnabled = false;
        foreach (var key in _pending.Keys.Where(k => k.Monitor == monitor).ToList()) _pending.TryRemove(key, out _);
        _ = Task.Run(async () =>
        {
            monitor.Set(DdcMonitor.FactoryReset, 1);
            await Task.Delay(3000); // the monitor needs a moment before it reports its restored values
            var fresh = _controls.Keys.Where(k => k.Monitor == monitor).ToDictionary(k => k.Code, k => monitor.Get(k.Code));
            _ui.TryEnqueue(() =>
            {
                _refreshing = true;
                foreach (var (code, reading) in fresh)
                    if (reading is var (current, max) && _controls.TryGetValue((monitor, code), out var c))
                    {
                        c.Slider.Maximum = max;
                        c.Slider.Value = Math.Min(current, max);
                        c.Value.Text = current.ToString();
                    }
                _refreshing = false;
                button.IsEnabled = true;
            });
        });
    }

    /// <summary>Sends queued changes a few times a second, so dragging a slider doesn't flood the monitor.</summary>
    private async Task SendLoop()
    {
        while (_running)
        {
            foreach (var key in _pending.Keys.ToList())
                if (_pending.TryRemove(key, out uint v))
                    try { key.Monitor.Set(key.Code, v); } catch (Exception) { }
            await Task.Delay(70);
        }
    }
}
