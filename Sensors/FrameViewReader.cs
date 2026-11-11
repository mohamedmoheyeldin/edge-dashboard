using System.Runtime.InteropServices;
using System.Text;

namespace EdgeDashboard.Sensors;

/// <summary>What NVIDIA FrameView reports for one application that is drawing frames.</summary>
public sealed record FrameStats(ulong Pid, string App, double Fps, double? Low1, double? LatencyMs);

/// <summary>
/// Frame rate, 1% low and PC latency from NVIDIA's FrameView SDK (installed with the NVIDIA app).
/// The SDK is a C++ library; this wraps the handful of calls needed. All calls happen on the
/// sensor thread. If the SDK is missing or fails, <see cref="Available"/> stays false.
/// </summary>
public sealed unsafe class FrameViewReader : IDisposable
{
    private const string Library = "FvSDK_x64";
    private const string InstallFolder = @"C:\Program Files\NVIDIA Corporation\FrameViewSDK";

    private const uint AvgFps = 0x6C4B1348, FpsLow = 0x29CCDD99, PcLatency = 0x363AFFA0, ProcessName = 0xA98734CD;

    [StructLayout(LayoutKind.Sequential)]
    private struct Samples { public uint Version; public uint Type; public nuint Count; public void* Data; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FpsInfo { public uint Version; public ulong Pid; public ulong SwapChain; public double Value; }

    /// <summary>Shared layout of the average-FPS, 1%-low and PC-latency metrics.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FpsMetric { public uint BaseVersion; public ulong Timestamp; public uint Version; public FpsInfo Fps; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NameMetric { public uint BaseVersion; public ulong Timestamp; public uint Version; public ulong Pid; public fixed byte Name[256]; }

    // The library exports a single function, fv_QueryInterface(number), that hands out the real
    // functions by number. The numbers below were read from NVIDIA's own debug symbols (FvSDK_x64.pdb).
    private static delegate* unmanaged[Cdecl]<uint> FvSDK_Initialize, FvSDK_Shutdown;
    private static delegate* unmanaged[Cdecl]<IntPtr*, uint> FvSDK_CreateSessionRaw;
    private static delegate* unmanaged[Cdecl]<IntPtr, uint*, uint, uint> FvSDK_EnableMetricsRaw;
    private static delegate* unmanaged[Cdecl]<IntPtr, uint> FvSDK_StartSession, FvSDK_StopSession, FvSDK_DestroySession;
    private static delegate* unmanaged[Cdecl]<IntPtr, int, uint> FvSDK_SampleData;
    private static delegate* unmanaged[Cdecl]<IntPtr, Samples*, uint, uint> FvSDK_ReadData;
    private static bool _loaded;

    private static uint FvSDK_CreateSession(out IntPtr session) { IntPtr s = IntPtr.Zero; uint r = FvSDK_CreateSessionRaw(&s); session = s; return r; }

    private static uint FvSDK_EnableMetrics(IntPtr session, uint[] metrics, uint count) { fixed (uint* m = metrics) return FvSDK_EnableMetricsRaw(session, m, count); }

    private static void Load()
    {
        if (_loaded) return;
        IntPtr library = IntPtr.Zero;
        // The library lives in NVIDIA's folder, not next to the dashboard.
        foreach (string path in new[] { Path.Combine(InstallFolder, "bin", Library + ".dll"), Path.Combine(InstallFolder, @"SDK\Public_Release", Library + ".dll") })
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out library)) break;
        if (library == IntPtr.Zero) throw new DllNotFoundException("NVIDIA FrameView SDK is not installed");

        var query = (delegate* unmanaged[Cdecl]<uint, void*>)NativeLibrary.GetExport(library, "fv_QueryInterface");
        void* Get(uint number) { void* f = query(number); return f != null ? f : throw new EntryPointNotFoundException($"FrameView function {number}"); }

        FvSDK_CreateSessionRaw = (delegate* unmanaged[Cdecl]<IntPtr*, uint>)Get(0);
        FvSDK_DestroySession = (delegate* unmanaged[Cdecl]<IntPtr, uint>)Get(1);
        FvSDK_EnableMetricsRaw = (delegate* unmanaged[Cdecl]<IntPtr, uint*, uint, uint>)Get(2);
        FvSDK_SampleData = (delegate* unmanaged[Cdecl]<IntPtr, int, uint>)Get(3);
        FvSDK_ReadData = (delegate* unmanaged[Cdecl]<IntPtr, Samples*, uint, uint>)Get(4);
        FvSDK_StartSession = (delegate* unmanaged[Cdecl]<IntPtr, uint>)Get(5);
        FvSDK_StopSession = (delegate* unmanaged[Cdecl]<IntPtr, uint>)Get(6);
        FvSDK_Initialize = (delegate* unmanaged[Cdecl]<uint>)Get(7);
        FvSDK_Shutdown = (delegate* unmanaged[Cdecl]<uint>)Get(8);
        _loaded = true;
    }

    private IntPtr _session;
    private long _retryAt;
    private string _probe = "";
    private readonly Dictionary<ulong, string> _names = new();

    public bool Available { get; private set; }

    /// <summary>Last status codes, for the diagnostic log.</summary>
    public string Status { get; private set; } = "not started";

    private static uint Ver(int size, uint version) => (uint)size | (version << 16);

    private bool Open()
    {
        if (Available) return true;
        if (Environment.TickCount64 < _retryAt) return false;
        _retryAt = Environment.TickCount64 + 15_000; // if it fails, try again in a while (NVIDIA app may start later)
        try
        {
            Load();
            uint init = FvSDK_Initialize();
            uint create = FvSDK_CreateSession(out _session);
            uint enable = FvSDK_EnableMetrics(_session, [AvgFps, FpsLow, PcLatency, ProcessName], 4);
            uint start = FvSDK_StartSession(_session);
            Status = $"initialize {init:X}, create {create:X}, enable {enable:X}, start {start:X}";
            // 0x14 = "session already in progress", which is fine
            Available = init == 0 && create == 0 && start is 0 or 0x14 && _session != IntPtr.Zero;
        }
        catch (Exception e) { Status = e.GetType().Name + ": " + e.Message; Available = false; }
        return Available;
    }

    /// <summary>Returns every application FrameView is currently measuring (possibly none).</summary>
    public IReadOnlyList<FrameStats> Read()
    {
        if (!Open()) return [];
        try
        {
            uint sample = FvSDK_SampleData(_session, 0);

            var fps = ReadFps(AvgFps);
            var low = ReadFps(FpsLow);
            var latency = ReadFps(PcLatency);
            ReadNames();
            Status = $"sample {sample:X}; {_probe}; fps {fps.Count}, low {low.Count}, latency {latency.Count}, names {_names.Count}";

            var result = new List<FrameStats>();
            foreach (var (pid, value) in fps)
            {
                if (!(value > 0)) continue;
                result.Add(new FrameStats(pid, _names.GetValueOrDefault(pid, ""), value,
                    low.TryGetValue(pid, out double l) && l > 0 ? l : null,
                    latency.TryGetValue(pid, out double ms) && ms > 0 ? ms : null));
            }
            return result;
        }
        catch (Exception e) { Status = e.GetType().Name + ": " + e.Message; return []; }
    }

    /// <summary>Latest value per process for one of the FPS-shaped metrics.</summary>
    private Dictionary<ulong, double> ReadFps(uint type)
    {
        var values = new Dictionary<ulong, double>();
        var request = new Samples { Version = Ver(sizeof(Samples), 1), Type = type };
        uint status = FvSDK_ReadData(_session, &request, 1);
        int count = (int)request.Count;
        if (type == AvgFps) _probe = $"read {status:X} count {count}";
        // the first call answers "more data" (2) or "insufficient buffer" with the number of samples waiting
        if (count is <= 0 or > 4096) return values;

        var buffer = new FpsMetric[count];
        for (int i = 0; i < count; i++)
        {
            buffer[i].BaseVersion = Ver(16, 1);
            buffer[i].Version = Ver(sizeof(FpsMetric), 1);
            buffer[i].Fps.Version = Ver(sizeof(FpsInfo), 1);
        }
        fixed (FpsMetric* data = buffer)
        {
            request.Data = data;
            if (FvSDK_ReadData(_session, &request, 1) != 0) return values;
            int got = Math.Min(count, (int)request.Count);
            for (int i = 0; i < got; i++) values[data[i].Fps.Pid] = data[i].Fps.Value; // later samples replace earlier ones
        }
        return values;
    }

    private void ReadNames()
    {
        var request = new Samples { Version = Ver(sizeof(Samples), 1), Type = ProcessName };
        uint status = FvSDK_ReadData(_session, &request, 1);
        int count = (int)request.Count;
        _ = status;
        if (count is <= 0 or > 4096) return;

        var buffer = new NameMetric[count];
        for (int i = 0; i < count; i++) { buffer[i].BaseVersion = Ver(16, 1); buffer[i].Version = Ver(sizeof(NameMetric), 1); }
        fixed (NameMetric* data = buffer)
        {
            request.Data = data;
            if (FvSDK_ReadData(_session, &request, 1) != 0) return;
            int got = Math.Min(count, (int)request.Count);
            for (int i = 0; i < got; i++)
            {
                var bytes = new ReadOnlySpan<byte>(data[i].Name, 256);
                int end = bytes.IndexOf((byte)0);
                string name = Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
                if (name.Length > 0) _names[data[i].Pid] = Path.GetFileName(name);
            }
        }
    }

    public void Dispose()
    {
        if (!Available) return;
        Available = false;
        try { FvSDK_StopSession(_session); FvSDK_DestroySession(_session); FvSDK_Shutdown(); } catch (Exception) { }
    }
}
