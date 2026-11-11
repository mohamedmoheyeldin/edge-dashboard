using System.Globalization;
using System.Runtime.InteropServices;
using EdgeDashboard.Audio;
using EdgeDashboard.Sensors;
using EdgeDashboard.Ui;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NAudio.CoreAudioApi;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace EdgeDashboard;

public sealed partial class MainWindow : Window
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    // Apps that draw frames but are not games (on top of NVIDIA FrameView's own exclusion list).
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude.exe", "icue.exe", "dwm.exe", "explorer.exe", "msedge.exe", "chrome.exe", "firefox.exe", "mailcraft.exe",
        "windowsterminal.exe", "steamwebhelper.exe", "steam.exe", "hwinfo64.exe", "code.exe", "edgedashboard.exe",
        "nvidia app.exe", "powertoys.exe", "applicationframehost.exe", "searchhost.exe", "textinputhost.exe",
    };

    private readonly HwInfoReader _hw = new();
    private readonly AudioControl _output = new(DataFlow.Render);
    private readonly AudioControl _mic = new(DataFlow.Capture);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    // CPU
    private readonly StatTile _cpuTemp = new("Temperature"), _cpuPower = new("Power"), _cpuVolt = new("Core voltage"), _cpuFan = new("CPU fan"), _cpuPeak = new("Peak core");
    private readonly GraphCard _cpuUsage = new("Usage", Theme.Amd, Theme.Hex("#8F3408"), fixedScale: true, v => $"{v:0}%");
    private readonly GraphCard _cpuClock = new("Clock speed", Theme.Amd, Theme.Hex("#8F3408"), fixedScale: false, v => $"{v / 1000:0.00} GHz");
    private readonly CoreBarsCard _ccd0 = new("3D Cache", 0), _ccd1 = new("Frequency", 8);

    // GPU
    private readonly StatTile _gpuCore = new("Core"), _gpuVram = new("VRAM"), _gpuHot = new("Hotspot"), _gpuPower = new("Power"), _gpuVolt = new("Core voltage");
    private readonly GraphCard _gpuUsage = new("Usage", Theme.Nvidia, Theme.Hex("#3F6600"), fixedScale: true, v => $"{v:0}%");
    private readonly GraphCard _gpuClock = new("Clock speed", Theme.Nvidia, Theme.Hex("#3F6600"), fixedScale: false, v => $"{v:N0} MHz");
    private readonly TextBlock _vramPercent = Make.Text("", 24, bold: true), _vramUsed = Make.Text("–", 44, bold: true, font: Theme.Display), _vramTotal = Make.Text("", 24, Theme.TextMuted);
    private readonly TextBlock _vramDedicated = Make.Text("–", 18, bold: true), _vramShared = Make.Text("–", 18, bold: true), _vramFree = Make.Text("–", 18, bold: true);
    private readonly TextBlock _vramClock = Make.Text("–", 26, bold: true, font: Theme.Display);
    private readonly Bar _vramBar = new(14);
    private readonly TextBlock _fanState = Make.Text("", 20, Theme.TextMuted);
    private readonly TextBlock[] _fanRpm = [Make.Text("–", 28, bold: true), Make.Text("–", 28, bold: true)];
    private readonly TextBlock[] _fanPct = [Make.Text("", 18, Theme.TextMuted), Make.Text("", 18, Theme.TextMuted)];
    private readonly Bar[] _fanBar = [new(10), new(10)];

    // Time and frame rate
    private readonly TextBlock _time = Make.Text("", 44, bold: true, font: Theme.Display), _ampm = Make.Text("", 22, Theme.TextSoft), _day = Make.Text("", 26, Theme.Accent, bold: true), _date = Make.Text("", 24, Theme.Hex("#E4E4E4"));
    private readonly TextBlock _weatherMain = Make.Text("–", 34, bold: true), _weatherSmall = Make.Text("", 20, Theme.TextMuted), _weatherSub = Make.Text("Finding the weather…", 18, Theme.TextMuted);
    private readonly TextBlock _weatherIcon = Make.Text("🌡️", 38, font: new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji"));
    private string _weatherLine = "Finding the weather…";
    private bool _sensorsDown;
    private DateTime _weatherRead = DateTime.MinValue;
    private readonly TextBlock _fps = Make.Text("–", 76, bold: true, font: Theme.Display), _fpsTag = Make.Text("Display", 18, bold: true);
    private readonly StatTile _fpsLow1 = new("1% low", 26), _fpsAvg = new("Average", 26), _fpsFrame = new("Frame", 26), _fpsPeakTile = new("Peak", 26), _fpsLowestTile = new("Lowest", 26), _fpsLatency = new("Latency", 26);
    private double _fpsPeak = double.NaN, _fpsLowest = double.NaN;
    private string _fpsApp = "";

    // System
    private readonly TextBlock _ramText = Make.Text("–", 24, bold: true), _ramRest = Make.Text("", 18, Theme.TextMuted), _ramPct = Make.Text("", 18, bold: true);
    private readonly TextBlock _swapText = Make.Text("–", 24, bold: true), _swapRest = Make.Text("", 18, Theme.TextMuted), _swapPct = Make.Text("", 18, bold: true);
    private readonly Bar _ramBar = new(6), _swapBar = new(6);
    private readonly TextBlock _down = Make.Text("–", 22, bold: true), _up = Make.Text("–", 22, bold: true);
    private readonly TextBlock _ssdC = Make.Text("–", 22, bold: true), _ssdD = Make.Text("–", 22, bold: true), _board = Make.Text("–", 22, bold: true), _vrm = Make.Text("–", 22, bold: true), _chipset = Make.Text("–", 22, bold: true), _dimm = Make.Text("–", 22, bold: true);

    // Audio
    private readonly AudioStrip _outStrip, _micStrip;

    public MainWindow()
    {
        InitializeComponent();
        _outStrip = new AudioStrip(_output, "Output", "\uE767");
        _micStrip = new AudioStrip(_mic, "Microphone", "\uE720");
        Build();
        PlaceOnEdgeDisplay();
        // A dashboard, not a program you switch to: keep it off the taskbar and out of Alt+Tab.
        AppWindow.IsShownInSwitchers = false;


        Backdrop.Background = Theme.Brush(Theme.Page);

        // No keyboard shortcut closes the dashboard; the only way is the two-tap button on the settings page.
        var displayPage = new DisplayPage(DisplayHost, Close);
        // "--display" opens straight on the screen-settings page (used for testing).
        if (Environment.GetCommandLineArgs().Contains("--display")) Pages.Loaded += (_, _) => Pages.SelectedIndex = 1;

        Closed += (_, _) => { _running = false; displayPage.Stop(); _timer.Stop(); _output.Dispose(); _mic.Dispose(); };
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => SlowTick();
        _timer.Start();
        SlowTick();
        _ = Task.Run(SensorLoop);
        if (Environment.GetCommandLineArgs().Contains("--frame-log")) StartFrameLog();
    }

    /// <summary>Diagnostic: measures the time between drawn frames for 30 seconds and writes a summary.</summary>
    private void StartFrameLog()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var gaps = new List<double>(4000);
        double last = -1; bool written = false;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += (_, _) =>
        {
            double now = watch.Elapsed.TotalMilliseconds;
            if (now < 8000) { last = now; return; } // skip start-up
            if (last >= 0) gaps.Add(now - last);
            last = now;
            if (!written && now > 38000)
            {
                written = true;
                gaps.Sort();
                string line = $"frames {gaps.Count} in 30 s ({gaps.Count / 30.0:0.0} fps); frame gap median {gaps[gaps.Count / 2]:0.0} ms, 99th percentile {gaps[(int)(gaps.Count * 0.99)]:0.0} ms, worst {gaps[^1]:0.0} ms; gaps over 25 ms: {gaps.Count(g => g > 25)}, over 50 ms: {gaps.Count(g => g > 50)}";
                try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "frame-log.txt"), line); } catch { }
            }
        };
    }

    private volatile bool _running = true;

    /// <summary>
    /// Reads HWiNFO on a background thread so the drawing thread never waits on it; only the finished
    /// readings are handed over for display.
    /// </summary>
    private async Task SensorLoop()
    {
        while (_running)
        {
            bool ok = _hw.Refresh();
            var readings = _hw.Readings;
            _output.Refresh(); // audio state is read here too, never on the drawing thread
            _mic.Refresh();
            var frames = PickGame(_fv.Read());
            DispatcherQueue.TryEnqueue(() => { ApplySensors(ok, readings); UpdateFps(frames); });
            await Task.Delay(500);
        }
        _hw.Dispose();
        _fv.Dispose();
    }

    private readonly FrameViewReader _fv = new();
    private int _fvLogLines;

    /// <summary>
    /// Chooses which of the applications FrameView is measuring counts as "the game": never the dashboard
    /// itself or a desktop app, never a nonsense reading; the window in front wins, otherwise the fastest.
    /// </summary>
    private FrameStats? PickGame(IReadOnlyList<FrameStats> all)
    {
        if (_fvLogLines < 60 && Environment.GetCommandLineArgs().Contains("--fv-log"))
        {
            _fvLogLines++;
            string line = $"{DateTime.Now:HH:mm:ss.f} [{_fv.Status}] " + string.Join(" | ", all.Select(a => $"{a.App}({a.Pid}) fps {a.Fps:0.0} low {a.Low1:0.0} lat {a.LatencyMs:0.0}"));
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "fv-log.txt"), line + Environment.NewLine); } catch { }
        }

        GetWindowThreadProcessId(GetForegroundWindow(), out uint front);
        FrameStats? best = null;
        foreach (var a in all)
        {
            if (a.Pid == (ulong)Environment.ProcessId || a.App.Length == 0 || NotGames.Contains(a.App) || a.Fps is < 1 or > 2000) continue;
            if (a.Pid == front) return a;
            if (best is null || a.Fps > best.Fps) best = a;
        }
        return best;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        Root.Padding = new Thickness(6);
        Root.ColumnSpacing = 8; Root.RowSpacing = 8;
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Px(620) });
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
        Root.RowDefinitions.Add(new RowDefinition { Height = Make.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = Make.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = Make.Star });
        Root.RowDefinitions.Add(new RowDefinition { Height = Make.Px(68) });

        BuildChipPanel("\uE950", Theme.Amd, "Ryzen 9 9950X3D", [_cpuTemp, _cpuPower, _cpuVolt, _cpuFan, _cpuPeak], _cpuUsage, _cpuClock, _ccd0, _ccd1)
            .At(Root, row: 0, column: 0, rowSpan: 3);
        BuildChipPanel("\uE7F4", Theme.Nvidia, "GeForce RTX 5080", [_gpuCore, _gpuVram, _gpuHot, _gpuPower, _gpuVolt], _gpuUsage, _gpuClock, BuildVideoMemoryCard(), BuildFansCard(), nameOnRight: true, below: BuildIgpuCard())
            .At(Root, row: 0, column: 2, rowSpan: 3);
        // The middle column gets the same kind of title line as the CPU and GPU sides, so its
        // boxes start level with theirs instead of at the very top.
        var middle = Make.Rows(8, Make.Auto, Make.Auto, Make.Auto, Make.Star);
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };
        title.Children.Add(Make.Icon("", 28, Theme.Accent));
        title.Children.Add(Make.Text("System", 24, bold: true));
        title.At(middle, row: 0);
        var time = BuildTimePanel();
        time.Height = 86; // same height as the CPU and GPU tile rows beside it
        time.At(middle, row: 1);
        BuildFpsPanel().At(middle, row: 2);
        BuildSystemPanel().At(middle, row: 3);
        middle.At(Root, row: 0, column: 1, rowSpan: 3);
        BuildAudioBar().At(Root, row: 3, column: 0, columnSpan: 3);
    }

    private static Border BuildChipPanel(string glyph, Color color, string name, StatTile[] tiles, GraphCard usage, GraphCard clock, FrameworkElement left, FrameworkElement right, bool nameOnRight = false, FrameworkElement? below = null)
    {
        var rows = Make.Rows(12, Make.Auto, Make.Auto, Make.Auto, Make.Auto, Make.Star);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        if (nameOnRight)
        {
            // mirrored: the name ends at the panel's right edge, icon outermost
            header.HorizontalAlignment = HorizontalAlignment.Right;
            header.Children.Add(Make.Text(name, 24, bold: true));
            header.Children.Add(Make.Icon(glyph, 28, color));
        }
        else
        {
            header.Children.Add(Make.Icon(glyph, 28, color));
            header.Children.Add(Make.Text(name, 24, bold: true));
        }
        header.At(rows, row: 0);

        var tileGrid = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < tiles.Length; i++)
        {
            tileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = Make.Star });
            tiles[i].At(tileGrid, column: i);
        }
        tileGrid.At(rows, row: 1);

        usage.At(rows, row: 2);
        clock.At(rows, row: 3);

        var bottom = Make.Columns(16, Make.Star, Make.Star);
        left.At(bottom, column: 0);
        right.At(bottom, column: 1);
        if (below is null) bottom.At(rows, row: 4);
        else
        {
            var stack = Make.Rows(12, Make.Star, Make.Auto);
            bottom.At(stack, row: 0);
            below.At(stack, row: 1);
            stack.At(rows, row: 4);
        }

        return Make.Panel(rows, new Thickness(24, 16, 24, 16));
    }

    // AMD integrated graphics (drives the Edge itself)
    private readonly TextBlock _igpuUsage = Make.Text("–", 24, bold: true), _igpuMem = Make.Text("–", 24, bold: true), _igpuClock = Make.Text("–", 24, bold: true), _igpuTemp = Make.Text("–", 24, bold: true), _igpuPower = Make.Text("–", 24, bold: true);

    private Border BuildIgpuCard()
    {
        var cols = Make.Columns(10, Make.Auto, Make.Star, Make.Star, Make.Star, Make.Star, Make.Star);
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        name.Children.Add(Make.Icon("", 24, Theme.Amd));
        name.Children.Add(Make.Text("Radeon iGPU", 22, bold: true));
        name.At(cols, column: 0);

        (string Label, TextBlock Value)[] stats = [("Usage", _igpuUsage), ("Memory", _igpuMem), ("Clock", _igpuClock), ("Temperature", _igpuTemp), ("Power", _igpuPower)];
        for (int i = 0; i < stats.Length; i++)
        {
            var label = Make.Text(stats[i].Label, 16, Theme.TextMuted);
            label.HorizontalAlignment = stats[i].Value.HorizontalAlignment = HorizontalAlignment.Center;
            var stat = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stat.Children.Add(label); stat.Children.Add(stats[i].Value);
            stat.At(cols, column: i + 1);
        }
        return Make.Card(cols, new Thickness(18, 5, 18, 7), border: 2);
    }

    private void UpdateIgpu(Snapshot s)
    {
        const string igpu = "iGPU [#1]";
        if (s.Get(igpu, "GPU D3D Usage") is double use) Glide(_igpuUsage, use, x => $"{x:0}%", rule: true);
        if (s.Get(igpu, "GPU D3D Memory Dedicated") is double ded && s.Get(igpu, "GPU D3D Memory Dynamic") is double dyn) Glide(_igpuMem, (ded + dyn) / 1024, x => $"{x:0.0} GB");
        if (s.Get(igpu, "GPU Clock") is double clock) Glide(_igpuClock, clock, x => x.ToString("N0", Us) + " MHz");
        TempText(_igpuTemp, s.Get(igpu, "GPU Temperature"));
        if (s.Get(igpu, "GPU Core Power (VDDCR_GFX)") is double w) Glide(_igpuPower, w, x => $"{x:0} W");
    }

    private Border BuildVideoMemoryCard()
    {
        var rows = Make.Rows(8, Make.Auto, Make.Auto, Make.Star, Make.Auto, Make.Auto);

        var header = Make.Columns(12, Make.Star, Make.Auto);
        Make.Text("Video memory", 26, bold: true).At(header, column: 0);
        _vramPercent.VerticalAlignment = VerticalAlignment.Center;
        _vramPercent.At(header, column: 1);
        header.At(rows, row: 0);

        var big = new StackPanel { Orientation = Orientation.Horizontal };
        _vramTotal.VerticalAlignment = VerticalAlignment.Bottom;
        _vramTotal.Margin = new Thickness(8, 0, 0, 6);
        big.Children.Add(_vramUsed); big.Children.Add(_vramTotal);

        // Memory speed (the memory clock) sits to the right of the big number.
        var speedValue = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var mhz = Make.Text("MHz", 18, Theme.TextMuted);
        mhz.VerticalAlignment = VerticalAlignment.Bottom; mhz.Margin = new Thickness(0, 0, 0, 3);
        speedValue.Children.Add(_vramClock); speedValue.Children.Add(mhz);
        var speedLabel = Make.Text("Speed", 18, Theme.TextMuted);
        speedLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var speed = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 4) };
        speed.Children.Add(speedLabel); speed.Children.Add(speedValue);

        var bigRow = Make.Columns(12, Make.Star, Make.Auto);
        big.At(bigRow, column: 0);
        speed.At(bigRow, column: 1);
        bigRow.At(rows, row: 1);

        _vramBar.At(rows, row: 3);

        var stats = Make.Columns(0, Make.Auto, Make.Star, Make.Auto, Make.Star, Make.Auto);
        Labelled("Dedicated", _vramDedicated, HorizontalAlignment.Left).At(stats, column: 0);
        Labelled("Shared", _vramShared, HorizontalAlignment.Center).At(stats, column: 2);
        Labelled("Free", _vramFree, HorizontalAlignment.Right).At(stats, column: 4);
        stats.At(rows, row: 4);

        return Make.Card(rows, new Thickness(18, 12, 18, 12), border: 2);
    }

    private static StackPanel Labelled(string label, TextBlock value, HorizontalAlignment align)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = align };
        var l = Make.Text(label, 18, Theme.TextMuted);
        l.VerticalAlignment = VerticalAlignment.Center;
        p.Children.Add(l); p.Children.Add(value);
        return p;
    }

    private Border BuildFansCard()
    {
        var rows = Make.Rows(8, Make.Auto, Make.Star, Make.Auto, Make.Auto, Make.Auto, Make.Auto);

        var header = Make.Columns(12, Make.Star, Make.Auto);
        Make.Text("Fans", 26, bold: true).At(header, column: 0);
        _fanState.VerticalAlignment = VerticalAlignment.Center;
        _fanState.At(header, column: 1);
        header.At(rows, row: 0);

        for (int i = 0; i < 2; i++)
        {
            var line = Make.Columns(8, Make.Star, Make.Auto, Make.Auto);
            var label = Make.Text($"Fan {i + 1}", 22, Theme.TextSoft);
            label.VerticalAlignment = VerticalAlignment.Bottom;
            label.At(line, column: 0);
            _fanRpm[i].At(line, column: 1);
            _fanPct[i].VerticalAlignment = VerticalAlignment.Bottom;
            _fanPct[i].Margin = new Thickness(0, 0, 0, 5);
            _fanPct[i].At(line, column: 2);
            line.At(rows, row: 2 + i * 2);
            _fanBar[i].At(rows, row: 3 + i * 2);
        }
        return Make.Card(rows, new Thickness(18, 12, 18, 12), border: 2);
    }

    private Border BuildTimePanel()
    {
        var cols = Make.Columns(8, Make.Auto, Make.Star);

        // the big digits carry empty space above and below; trim it so time and date fit the card
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -12, 0, -10) };
        _ampm.VerticalAlignment = VerticalAlignment.Bottom; _ampm.Margin = new Thickness(6, 0, 0, 7);
        _day.VerticalAlignment = VerticalAlignment.Bottom; _day.Margin = new Thickness(14, 0, 0, 5);
        line.Children.Add(_time); line.Children.Add(_ampm); line.Children.Add(_day);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(line); left.Children.Add(_date);
        Make.Card(left, new Thickness(18, 6, 18, 8)).At(cols, column: 0);

        var weather = Make.Columns(14, Make.Auto, Make.Star);
        _weatherIcon.VerticalAlignment = VerticalAlignment.Center;
        _weatherIcon.At(weather, column: 0);
        var temps = new StackPanel { Orientation = Orientation.Horizontal };
        _weatherSmall.VerticalAlignment = VerticalAlignment.Bottom; _weatherSmall.Margin = new Thickness(10, 0, 0, 5);
        temps.Children.Add(_weatherMain); temps.Children.Add(_weatherSmall);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(temps); text.Children.Add(_weatherSub);
        text.At(weather, column: 1);
        Make.Card(weather, new Thickness(16, 8, 16, 8)).At(cols, column: 1);

        return Make.Panel(cols, new Thickness(24, 10, 24, 10));
    }

    private Border BuildFpsPanel()
    {
        var cols = Make.Columns(12, Make.Auto, Make.Star);

        var top = Make.Columns(8, Make.Auto, Make.Auto, Make.Star, Make.Auto);
        var label = Make.Text("FPS", 18, Theme.TextMuted);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.At(top, column: 0);
        _fpsTag.VerticalAlignment = VerticalAlignment.Center;
        _fpsTag.At(top, column: 1);
        var reset = new Button
        {
            Width = 44, Height = 44, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
            Content = Make.Icon("\uE72C", 20, Theme.Text), Margin = new Thickness(16, 0, 0, 0),
        };
        ToolTipService.SetToolTip(reset, "Reset peak and lowest");
        reset.Click += (_, _) => { _fpsPeak = _fpsLowest = double.NaN; };
        reset.At(top, column: 3);

        var number = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -8, 0, 0) };
        var unit = Make.Text("fps", 22, Theme.TextMuted);
        unit.VerticalAlignment = VerticalAlignment.Bottom; unit.Margin = new Thickness(8, 0, 0, 14);
        number.Children.Add(_fps); number.Children.Add(unit);

        var left = new StackPanel { MinWidth = 180, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(top); left.Children.Add(number);
        Make.Card(left, new Thickness(18, 6, 18, 2)).At(cols, column: 0);

        var tiles = Make.Columns(12, Make.Star, Make.Star, Make.Star);
        tiles.RowSpacing = 8;
        tiles.RowDefinitions.Add(new RowDefinition { Height = Make.Star });
        tiles.RowDefinitions.Add(new RowDefinition { Height = Make.Star });
        _fpsLow1.At(tiles, 0, 0); _fpsAvg.At(tiles, 0, 1); _fpsFrame.At(tiles, 0, 2);
        _fpsPeakTile.At(tiles, 1, 0); _fpsLowestTile.At(tiles, 1, 1); _fpsLatency.At(tiles, 1, 2);
        tiles.At(cols, column: 1);

        return Make.Panel(cols, new Thickness(24, 8, 24, 8));
    }

    private Border BuildSystemPanel()
    {
        var rows = Make.Rows(6, Make.Auto, Make.Star);

        var ram = new StackPanel { Spacing = 4 };
        ram.Children.Add(MemoryLine("System RAM", _ramText, _ramRest, _ramPct)); ram.Children.Add(_ramBar);
        ram.Children.Add(MemoryLine("Virtual memory", _swapText, _swapRest, _swapPct)); ram.Children.Add(_swapBar);
        Make.Card(ram, new Thickness(16, 5, 16, 8)).At(rows, row: 0);

        var grid = Make.Columns(28, Make.Star, Make.Star);
        grid.RowSpacing = 1;
        for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = Make.Star });
        InfoRow("Download", _down, "\uE896", Theme.Accent).At(grid, 0, 0);
        InfoRow("Upload", _up, "\uE898", Theme.Hex("#C8A2FF")).At(grid, 0, 1);
        InfoRow("SSD C:", _ssdC).At(grid, 1, 0);
        InfoRow("SSD D:", _ssdD).At(grid, 1, 1);
        InfoRow("Motherboard", _board).At(grid, 2, 0);
        InfoRow("VRM", _vrm).At(grid, 2, 1);
        InfoRow("Chipset", _chipset).At(grid, 3, 0);
        InfoRow("RAM modules", _dimm).At(grid, 3, 1);
        Make.Card(grid, new Thickness(16, 8, 16, 10)).At(rows, row: 1);

        return Make.Panel(rows, new Thickness(24, 8, 24, 6));
    }

    private static Grid MemoryLine(string label, TextBlock used, TextBlock rest, TextBlock percent)
    {
        var g = Make.Columns(0, Make.Star, Make.Auto, Make.Auto, Make.Auto);
        var l = Make.Text(label, 20, Theme.TextSoft);
        l.VerticalAlignment = VerticalAlignment.Bottom;
        l.At(g, column: 0);
        used.At(g, column: 1);
        rest.VerticalAlignment = VerticalAlignment.Bottom; rest.Margin = new Thickness(5, 0, 0, 4);
        rest.At(g, column: 2);
        percent.VerticalAlignment = VerticalAlignment.Bottom; percent.Margin = new Thickness(12, 0, 0, 4);
        percent.MinWidth = 44; percent.TextAlignment = TextAlignment.Right;
        percent.At(g, column: 3);
        return g;
    }

    private static Grid InfoRow(string label, TextBlock value, string? glyph = null, Color? glyphColor = null)
    {
        var g = Make.Columns(8, Make.Auto, Make.Star, Make.Auto);
        if (glyph is not null)
        {
            var icon = Make.Icon(glyph, 18, glyphColor ?? Theme.TextSoft);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.At(g, column: 0);
        }
        var l = Make.Text(label, 20, Theme.TextSoft);
        l.VerticalAlignment = VerticalAlignment.Center;
        l.At(g, column: 1);
        value.VerticalAlignment = VerticalAlignment.Center;
        value.At(g, column: 2);
        return g;
    }

    private Border BuildAudioBar()
    {
        var cols = Make.Columns(24, Make.Star, Make.Px(1), Make.Star);
        _outStrip.At(cols, column: 0);
        new Border { Background = Theme.Brush(Theme.Hex("#454545")), Margin = new Thickness(0, 14, 0, 14) }.At(cols, column: 1);
        _micStrip.At(cols, column: 2);
        return Make.Panel(cols, new Thickness(20, 0, 20, 0));
    }

    // ---------------------------------------------------------------- window placement

    /// <summary>Opens borderless and full-screen on the 2560x720 Xeneon Edge; falls back to the widest-aspect display.</summary>
    private void PlaceOnEdgeDisplay()
    {
        try
        {
            var areas = DisplayArea.FindAll();
            DisplayArea? target = null;
            double bestAspect = 0;
            for (int i = 0; i < areas.Count; i++)
            {
                var b = areas[i].OuterBounds;
                if (b.Width == 2560 && b.Height == 720) { target = areas[i]; break; }
                double aspect = b.Height == 0 ? 0 : (double)b.Width / b.Height;
                if (aspect > bestAspect) { bestAspect = aspect; target = areas[i]; }
            }
            if (target is null) return;
            AppWindow.Move(new PointInt32(target.OuterBounds.X, target.OuterBounds.Y));
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        catch (Exception) { /* stay as a normal window if the display can't be resolved */ }
    }

    // ---------------------------------------------------------------- data

    /// <summary>Once a second: the clock, and the audio state (so changes made elsewhere show up here).</summary>
    private void SlowTick()
    {
        var now = DateTime.Now;
        _time.Text = now.ToString("h:mm", Us);
        _ampm.Text = now.ToString("tt", Us);
        _day.Text = now.ToString("dddd", Us);
        _date.Text = now.ToString("d MMMM yyyy", Us);

        _outStrip.Sync();
        _micStrip.Sync();

        if ((now - _weatherRead).TotalMinutes >= 15) { _weatherRead = now; UpdateWeather(); }
    }

    /// <summary>Reads the weather for Windows' location; on failure says why and tries again in a minute.</summary>
    private async void UpdateWeather()
    {
        var (weather, problem) = await Weather.WeatherService.GetAsync();
        if (!_running) return;
        if (weather is null)
        {
            _weatherLine = problem ?? "Weather is unavailable";
            _weatherRead = DateTime.Now.AddMinutes(-14);
        }
        else
        {
            _weatherIcon.Text = weather.Emoji;
            _weatherMain.Text = $"{weather.TempF:0}°F";
            _weatherSmall.Text = $"{weather.TempC:0}°C";
            _weatherLine = $"{weather.Condition} · High {weather.HighF:0}° · Low {weather.LowF:0}°".TrimStart(' ', '·');
        }
        if (!_sensorsDown) _weatherSub.Text = _weatherLine;
    }

    private void ApplySensors(bool connected, IReadOnlyList<Reading> readings)
    {
        if (!_running) return;
        if (!connected || readings.Count == 0)
        {
            _sensorsDown = true;
            _weatherSub.Text = "HWiNFO is not publishing sensors";
            return;
        }
        if (_sensorsDown) { _sensorsDown = false; _weatherSub.Text = _weatherLine; }

        var s = new Snapshot(readings);
        UpdateCpu(s, out bool cpuHasData);
        UpdateGpu(s);
        UpdateIgpu(s);
        UpdateSystem(s);
        _ = cpuHasData;
    }

    private static bool IsCpuBase(Reading r) => r.Sensor.StartsWith("CPU [#0]", StringComparison.Ordinal) && !r.Sensor.Contains("Enhanced") && !r.Sensor.Contains("C-State");

    private static int CoreIndex(string label)
    {
        // "Core 12 Clock (perf #2)" or "Core 3 T1 Usage"
        var parts = label.Split(' ');
        return parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : -1;
    }

    private bool _gameRunning;

    private void UpdateCpu(Snapshot s, out bool hasData)
    {
        Temp(_cpuTemp, s.Get("Enhanced", "CPU (Tctl/Tdie)"));
        Num(_cpuPower, s.Get("Enhanced", "CPU Package Power"), "0", "W");
        Num(_cpuVolt, s.Get("Enhanced", "CPU VDDCR_VDD Voltage (SVI3 TFN)"), "0.000", "V");
        Num(_cpuFan, s.Get("Nuvoton", "CPU", SensorType.Fan), "N0", "rpm");

        var clocks = new double[16]; var usage = new double[16];
        bool anyClock = false;
        foreach (var r in s.All)
        {
            if (!IsCpuBase(r) || !r.Label.StartsWith("Core ", StringComparison.Ordinal)) continue;
            int core = CoreIndex(r.Label);
            if (core is < 0 or > 15) continue;
            if (r.Type == SensorType.Clock && r.Label.Contains(" Clock (perf", StringComparison.Ordinal)) { clocks[core] = r.Value; anyClock = true; }
            else if (r.Type == SensorType.Usage && r.Label.EndsWith(" Usage", StringComparison.Ordinal)) usage[core] = Math.Max(usage[core], r.Value);
        }
        hasData = anyClock;

        double? total = s.Get("CPU [#0]", "Total CPU Usage");
        if (total is double t)
        {
            _cpuUsage.SetNumber(t, "0", "%", rule: true);
            _cpuUsage.Push(t);
        }
        if (anyClock)
        {
            double avg = clocks.Average(), peak = clocks.Max();
            _cpuClock.SetNumber(avg / 1000, "0.00", "GHz");
            _cpuClock.Push(avg);
            _cpuPeak.SetNumber(peak / 1000, "0.00", "GHz");
        }

        for (int i = 0; i < 8; i++) { _ccd0.SetCore(i, usage[i]); _ccd1.SetCore(i, usage[i + 8]); }
        double load0 = usage.Take(8).Average(), load1 = usage.Skip(8).Average();
        _ccd0.SetHeader(s.Get("Enhanced", "CPU CCD1 (Tdie)"), anyClock ? clocks.Take(8).Average() : null, _gameRunning && load0 >= load1);
        _ccd1.SetHeader(s.Get("Enhanced", "CPU CCD2 (Tdie)"), anyClock ? clocks.Skip(8).Average() : null, _gameRunning && load1 > load0);
    }

    private void UpdateGpu(Snapshot s)
    {
        const string gpu = "NVIDIA";
        Temp(_gpuCore, s.Get(gpu, "GPU Temperature"));
        Temp(_gpuVram, s.Get(gpu, "GPU Memory Junction Temperature"));
        Temp(_gpuHot, s.Get(gpu, "GPU Hot Spot Temperature"));
        Num(_gpuPower, s.Get(gpu, "GPU Power"), "0", "W");
        Num(_gpuVolt, s.Get(gpu, "GPU Core Voltage"), "0.000", "V");

        if (s.Get(gpu, "GPU Core Load") is double load)
        {
            _gpuUsage.SetNumber(load, "0", "%", rule: true);
            _gpuUsage.Push(load);
        }
        if (s.Get(gpu, "GPU Clock") is double clock)
        {
            _gpuClock.SetNumber(clock, "N0", "MHz");
            _gpuClock.Push(clock);
        }

        double? used = s.Get(gpu, "GPU Memory Allocated"), free = s.Get(gpu, "GPU Memory Available");
        if (used is double u && free is double f && u + f > 0)
        {
            double pct = u / (u + f) * 100;
            Glide(_vramUsed, u / 1024, x => $"{x:0.0}");
            _vramTotal.Text = $"/ {(u + f) / 1024:0} GB";
            Glide(_vramPercent, pct, x => $"{x:0}%", rule: true);
            _vramBar.SetRule(pct / 100);
            _vramFree.Text = $"{f / 1024:0.0} GB";
        }
        if (s.Get(gpu, "GPU Memory Clock") is double memClock) Glide(_vramClock, memClock, x => x.ToString("N0", Us));
        if (s.Get(gpu, "GPU D3D Memory Dedicated") is double d) _vramDedicated.Text = $"{d / 1024:0.0} GB";
        if (s.Get(gpu, "GPU D3D Memory Dynamic") is double dyn) _vramShared.Text = $"{dyn / 1024:0.0} GB";

        bool spinning = false;
        for (int i = 0; i < 2; i++)
        {
            double? rpm = s.Get(gpu, $"GPU Fan{i + 1}", SensorType.Fan), pct = s.Get(gpu, $"GPU Fan{i + 1}", SensorType.Other);
            if (rpm is double r) Glide(_fanRpm[i], r, x => x.ToString("N0", Us));
            _fanPct[i].Text = pct is double p ? $" rpm · {p:0}%" : " rpm";
            _fanBar[i].Set((pct ?? 0) / 100, Theme.Nvidia);
            spinning |= rpm > 0;
        }
        _fanState.Text = spinning ? "Spinning" : "Stopped";
    }

    private void UpdateSystem(Snapshot s)
    {
        if (s.Get("System", "Physical Memory Used") is double used && s.Get("System", "Physical Memory Available") is double avail)
        {
            double pct = used / (used + avail) * 100;
            Glide(_ramText, used / 1024, x => $"{x:0.0}");
            _ramRest.Text = $"/ {(used + avail) / 1024:0} GB";
            Glide(_ramPct, pct, x => $"{x:0}%", rule: true);
            _ramBar.SetRule(pct / 100);
        }
        if (s.Get("System", "Page File Used") is double swap && s.Get("System", "Page File Total") is double swapTotal && swapTotal > 0)
        {
            double pct = swap / swapTotal * 100;
            Glide(_swapText, swap / 1024, x => $"{x:0.0}");
            _swapRest.Text = $"/ {swapTotal / 1024:0} GB";
            Glide(_swapPct, pct, x => $"{x:0}%", rule: true);
            _swapBar.SetRule(pct / 100);
        }

        // HWiNFO reports KB/s; the dashboard shows megabits per second.
        if (s.Get("Network", "Current DL rate") is double dl) Glide(_down, dl, Mbps);
        if (s.Get("Network", "Current UP rate") is double ul) Glide(_up, ul, Mbps);

        TempText(_ssdC, s.Where("S.M.A.R.T.", r => r.Sensor.Contains("[C:]") && r.Label == "Drive Temperature").Select(r => (double?)r.Value).FirstOrDefault());
        TempText(_ssdD, s.Where("S.M.A.R.T.", r => r.Sensor.Contains("[D:]") && r.Label == "Drive Temperature").Select(r => (double?)r.Value).FirstOrDefault());
        TempText(_board, s.Get("Nuvoton", "Motherboard", SensorType.Temp));
        TempText(_vrm, s.Get("ASUS EC", "VRM", SensorType.Temp));
        TempText(_chipset, s.Get("AMD Chipset", "Chipset 1 (xHCI)"));
        var dimms = s.Where("DDR5 DIMM", r => r.Label == "SPD Hub Temperature").Select(r => r.Value).ToList();
        TempText(_dimm, dimms.Count > 0 ? dimms.Max() : null);
    }

    /// <summary>Frame rate box: the game NVIDIA FrameView is measuring, or the display's refresh rate when there is none.</summary>
    private void UpdateFps(FrameStats? stats)
    {
        if (!_running) return;
        string app = stats?.App ?? "";
        double? avg = stats?.Fps, low1 = stats?.Low1, frame = stats is null ? null : 1000 / stats.Fps;

        bool game = stats is not null;
        if (game && app != _fpsApp) { _fpsPeak = _fpsLowest = double.NaN; }
        _fpsApp = game ? app : "";
        _gameRunning = game;

        if (game && avg is double fps)
        {
            _fpsPeak = double.IsNaN(_fpsPeak) ? fps : Math.Max(_fpsPeak, fps);
            _fpsLowest = double.IsNaN(_fpsLowest) ? fps : Math.Min(_fpsLowest, fps);
            _fpsTag.Text = "Game";
            Glide(_fps, fps, x => $"{x:0}");
            if (low1 is double l) _fpsLow1.SetNumber(l, "0"); else _fpsLow1.Set("–");
            _fpsAvg.SetNumber(fps, "0");
            if (frame is double ft) _fpsFrame.SetNumber(ft, "0.0", "ms"); else _fpsFrame.Set("–");
            _fpsPeakTile.SetNumber(_fpsPeak, "0");
            _fpsLowestTile.SetNumber(_fpsLowest, "0");
            // PC latency only exists in games that report it (NVIDIA Reflex)
            if (stats!.LatencyMs is double ms) _fpsLatency.SetNumber(ms, "0", "ms"); else _fpsLatency.Set("–");
        }
        else
        {
            _fpsTag.Text = "Display";
            if (MaxRefreshRate() is int hz and > 0) Glide(_fps, hz, x => $"{x:0}");
            foreach (var tile in new[] { _fpsLow1, _fpsAvg, _fpsFrame, _fpsPeakTile, _fpsLowestTile, _fpsLatency }) tile.Set("–");
        }
    }

    // ---------------------------------------------------------------- small helpers

    private static void Temp(StatTile tile, double? value)
    {
        if (value is double v) tile.SetNumber(v, "0", "", "°C", rule: true); else tile.Set("–");
    }

    private static void Num(StatTile tile, double? value, string format, string unit)
    {
        if (value is double v) tile.SetNumber(v, format, unit); else tile.Set("–");
    }

    private void TempText(TextBlock text, double? value)
    {
        if (value is double v) Glide(text, v, x => $"{x:0}°C", rule: true);
    }

    private readonly Dictionary<TextBlock, Motion.Value> _glides = new();

    /// <summary>Counts a text block smoothly to a new value instead of replacing the number outright.</summary>
    private void Glide(TextBlock text, double value, Func<double, string> format, bool rule = false)
    {
        if (!_glides.TryGetValue(text, out var motion)) _glides[text] = motion = Motion.Number(text, format, rule);
        motion.To(value);
    }

    private static string Mbps(double kilobytesPerSecond)
    {
        double mbps = kilobytesPerSecond * 8 / 1000;
        return mbps >= 100 ? $"{mbps:0} Mbps" : mbps >= 10 ? $"{mbps:0.0} Mbps" : $"{mbps:0.00} Mbps";
    }

    private static int _refreshCache;
    private static DateTime _refreshRead = DateTime.MinValue;

    /// <summary>Highest refresh rate among the connected displays (the "no game" figure).</summary>
    private static int MaxRefreshRate()
    {
        if ((DateTime.Now - _refreshRead).TotalSeconds < 10) return _refreshCache;
        int best = 0;
        var mode = new byte[220];
        for (int i = 1; i <= 16; i++)
        {
            Array.Clear(mode);
            BitConverter.TryWriteBytes(mode.AsSpan(68), (short)220); // dmSize
            if (EnumDisplaySettingsW($@"\\.\DISPLAY{i}", -1, mode)) best = Math.Max(best, BitConverter.ToInt32(mode, 184)); // dmDisplayFrequency
        }
        _refreshRead = DateTime.Now;
        return _refreshCache = best;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, byte[] devMode);
}
