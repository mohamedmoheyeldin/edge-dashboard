using System.IO.MemoryMappedFiles;
using System.Text;

namespace EdgeDashboard.Sensors;

public enum SensorType { None, Temp, Volt, Fan, Current, Power, Clock, Usage, Other }

public readonly record struct Reading(string Sensor, string Label, SensorType Type, string Unit, double Value);

/// <summary>
/// Reads HWiNFO's shared memory ("Shared Memory Support" must be on in HWiNFO's settings).
/// Layout: a header, then fixed-size sensor records, then fixed-size reading records.
/// </summary>
public sealed class HwInfoReader : IDisposable
{
    private const string MapName = @"Global\HWiNFO_SENS_SM2";
    private static readonly Encoding Ansi = Encoding.Latin1;

    private MemoryMappedFile? _map;
    private MemoryMappedViewAccessor? _view;
    private byte[] _buffer = [];

    public IReadOnlyList<Reading> Readings { get; private set; } = [];
    public bool Connected => _view is not null;

    /// <summary>Refreshes <see cref="Readings"/>. Returns false when HWiNFO is not publishing.</summary>
    public bool Refresh()
    {
        try
        {
            if (_view is null)
            {
                _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }

            var view = _view;
            if (view.ReadUInt32(0) != 0x53695748) { Reset(); return false; } // "HWiS"; HWiNFO writes "DEAD" on exit
            uint sOff = view.ReadUInt32(20), sSize = view.ReadUInt32(24), sNum = view.ReadUInt32(28);
            uint rOff = view.ReadUInt32(32), rSize = view.ReadUInt32(36), rNum = view.ReadUInt32(40);
            if (sNum == 0 || rNum == 0 || sSize < 264 || rSize < 292) { Readings = []; return true; }

            long total = Math.Max(sOff + (long)sSize * sNum, rOff + (long)rSize * rNum);
            if (total > view.Capacity) { Reset(); return false; }
            if (_buffer.Length < total) _buffer = new byte[total];
            view.ReadArray(0, _buffer, 0, (int)total);

            var sensors = new string[sNum];
            for (int i = 0; i < sNum; i++) sensors[i] = Str((int)(sOff + i * sSize + 136), 128);

            var list = new Reading[rNum];
            for (int i = 0; i < rNum; i++)
            {
                int o = (int)(rOff + i * rSize);
                uint type = BitConverter.ToUInt32(_buffer, o);
                uint si = BitConverter.ToUInt32(_buffer, o + 4);
                list[i] = new Reading(
                    si < sNum ? sensors[si] : "",
                    Str(o + 140, 128),
                    type <= 8 ? (SensorType)type : SensorType.Other,
                    Str(o + 268, 16),
                    BitConverter.ToDouble(_buffer, o + 284));
            }
            Readings = list;
            return true;
        }
        catch (FileNotFoundException) { Reset(); return false; }
        catch (IOException) { Reset(); return false; }
        catch (UnauthorizedAccessException) { Reset(); return false; }
    }

    private string Str(int offset, int max)
    {
        int n = Array.IndexOf(_buffer, (byte)0, offset, max);
        return Ansi.GetString(_buffer, offset, n < 0 ? max : n - offset);
    }

    private void Reset()
    {
        _view?.Dispose(); _map?.Dispose();
        _view = null; _map = null;
        Readings = [];
    }

    public void Dispose() => Reset();
}

/// <summary>Convenience lookups over one refresh of readings.</summary>
public sealed class Snapshot(IReadOnlyList<Reading> readings)
{
    public IReadOnlyList<Reading> All => readings;

    /// <summary>First reading whose sensor name contains <paramref name="sensor"/> and whose label matches exactly.</summary>
    public double? Get(string sensor, string label, SensorType? type = null)
    {
        foreach (var r in readings)
            if (r.Label == label && (type is null || r.Type == type) && r.Sensor.Contains(sensor, StringComparison.OrdinalIgnoreCase))
                return r.Value;
        return null;
    }

    public IEnumerable<Reading> Where(string sensor, Func<Reading, bool> match) =>
        readings.Where(r => r.Sensor.Contains(sensor, StringComparison.OrdinalIgnoreCase) && match(r));
}
