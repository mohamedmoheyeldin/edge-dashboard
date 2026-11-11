using EdgeDashboard.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace EdgeDashboard.Ui;

/// <summary>A thin horizontal bar: a fraction of its width filled with one colour.</summary>
public sealed class Bar : Grid
{
    private readonly Border _fill;
    private readonly ColumnDefinition _filled = new(), _rest = new();

    public Bar(double height)
    {
        Height = height;
        ColumnDefinitions.Add(_filled); ColumnDefinitions.Add(_rest);
        var track = new Border { Background = Theme.Brush(Theme.Track), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        Children.Add(track);
        _fill = new Border { CornerRadius = new CornerRadius(height / 2) };
        Children.Add(_fill);
    }

    private Motion.Value? _motion;
    private SolidColorBrush? _brush;
    private bool _rule;
    private Color _color;

    /// <summary>Fills to a fraction (0-1) in a fixed colour, gliding there.</summary>
    public void Set(double fraction, Color color) { _rule = false; _color = color; Go(fraction); }

    /// <summary>Fills to a fraction (0-1), coloured by the colour rule as it moves.</summary>
    public void SetRule(double fraction) { _rule = true; Go(fraction); }

    private void Go(double fraction)
    {
        _motion ??= Motion.Create(v =>
        {
            _filled.Width = new GridLength(v, GridUnitType.Star);
            _rest.Width = new GridLength(1 - v, GridUnitType.Star);
            Paint(_rule ? Theme.Rule(v * 100) : _color);
        }, tauMs: 320);
        _motion.To(double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1));
        if (!_rule) Paint(_color);
    }

    private void Paint(Color c)
    {
        if (_brush is null) { _brush = new SolidColorBrush(c); _fill.Background = _brush; }
        else if (_brush.Color != c) _brush.Color = c;
    }
}

/// <summary>One half of the audio bar: device button (tap to switch), mute button, volume slider, percentage.</summary>
public sealed class AudioStrip : Grid
{
    private readonly AudioControl _audio;
    private readonly TextBlock _name = Make.Text("", 20, bold: true);
    private readonly TextBlock _percent = Make.Text("", 30, bold: true, font: Theme.Display);
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, StepFrequency = 1, VerticalAlignment = VerticalAlignment.Center, IsThumbToolTipEnabled = false };
    private readonly Button _mute;
    private readonly FontIcon _muteIcon;
    private readonly string _glyph;
    private bool _syncing;

    public AudioStrip(AudioControl audio, string label, string glyph)
    {
        _audio = audio;
        _glyph = glyph;
        ColumnSpacing = 16;
        VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
        ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Px(96) });

        // Device button: shows the current device, opens a list to switch.
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(Make.Text(label, 14, Theme.TextMuted));
        names.Children.Add(_name);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        content.Children.Add(Make.Icon(glyph, 22, Theme.Accent));
        content.Children.Add(names);
        content.Children.Add(Make.Icon("", 12, Theme.TextSoft));
        var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top };
        flyout.Opening += (_, _) => FillDevices(flyout);
        var device = new Button { Height = 48, MaxWidth = 420, Padding = new Thickness(14, 0, 14, 0), CornerRadius = new CornerRadius(8), Content = content, Flyout = flyout };
        Grid.SetColumn(device, 0);
        Children.Add(device);

        _muteIcon = Make.Icon(glyph, 22, Theme.Text);
        _mute = new Button { Width = 48, Height = 48, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Content = _muteIcon };
        _mute.Click += (_, _) => _audio.ToggleMute();
        Grid.SetColumn(_mute, 1);
        Children.Add(_mute);

        // The built-in Slider handles touch drag, tap-to-jump, mouse and keyboard.
        _slider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _touched = Environment.TickCount64;
            _audio.SetVolume(e.NewValue);
            if (!_audio.Muted) _percent.Text = $"{e.NewValue:0}%";
        };
        Grid.SetColumn(_slider, 2);
        Children.Add(_slider);

        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _percent.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_percent, 3);
        Children.Add(_percent);
    }

    /// <summary>Pulls the current device, level and mute state from Windows (so outside changes show up here).</summary>
    private long _touched;
    private string _shownState = "";

    /// <summary>
    /// Shows the latest state read in the background (so changes made elsewhere appear here).
    /// Does nothing when nothing changed, and leaves the slider alone while it is being dragged.
    /// </summary>
    public void Sync()
    {
        bool recentlyTouched = Environment.TickCount64 - _touched < 1500;
        string state = $"{_audio.DeviceName}|{_audio.Volume}|{_audio.Muted}|{_audio.Available}|{recentlyTouched}";
        if (state == _shownState) return;
        _shownState = state;

        _syncing = true;
        _name.Text = _audio.DeviceName;
        if (!recentlyTouched && Math.Abs(_slider.Value - _audio.Volume) >= 1) _slider.Value = _audio.Volume;
        _slider.IsEnabled = _audio.Available;
        _syncing = false;

        bool muted = _audio.Muted;
        _percent.Text = muted ? "Muted" : $"{(recentlyTouched ? _slider.Value : _audio.Volume):0}%";
        _percent.FontSize = muted ? 24 : 30;
        _percent.Foreground = Theme.Brush(muted ? Theme.Hex("#FF99A4") : Theme.Text);
        _slider.Opacity = muted ? 0.45 : 1;
        _mute.Background = muted ? Theme.Brush(Theme.Danger) : null;
        _muteIcon.Glyph = muted ? "" : _glyph;
        ToolTipService.SetToolTip(_mute, muted ? "Unmute" : "Mute");
    }

    private async void FillDevices(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Looking for devices…", IsEnabled = false, FontSize = 20, MinHeight = 52, MinWidth = 380 });
        var devices = await _audio.ListDevicesAsync();
        flyout.Items.Clear();
        foreach (var d in devices)
        {
            var item = new ToggleMenuFlyoutItem { Text = d.Name, IsChecked = d.Id == _audio.DeviceId, FontSize = 20, MinHeight = 52, MinWidth = 380 };
            item.Click += (_, _) => _audio.SetDefault(d.Id);
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count == 0) flyout.Items.Add(new MenuFlyoutItem { Text = "No devices found", IsEnabled = false, FontSize = 20 });
    }
}
