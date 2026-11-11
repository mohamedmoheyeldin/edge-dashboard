using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace EdgeDashboard.Audio;

public sealed record AudioDevice(string Id, string Name);

/// <summary>
/// Volume, mute and default-device control for either playback or recording, via Windows Core Audio.
/// Every call into Windows happens on background threads; the screen only reads the cached values,
/// so audio never makes the display stutter.
/// </summary>
public sealed class AudioControl(DataFlow flow) : IDisposable
{
    private readonly object _gate = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;

    // Cached state, safe to read from the drawing thread.
    public volatile string DeviceName = "No device";
    public volatile string DeviceId = "";
    public volatile bool Muted;
    public volatile bool Available;
    private volatile int _volume;
    public double Volume => _volume;   // 0-100

    /// <summary>Re-reads the current default device and its level. Call from a background thread.</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                if (!_enumerator.HasDefaultAudioEndpoint(flow, Role.Multimedia)) { Clear(); return; }
                var current = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                if (_device is null || _device.ID != current.ID)
                {
                    _device?.Dispose();
                    _device = current;
                    DeviceId = current.ID;
                    DeviceName = ShortName(current.FriendlyName);
                }
                else current.Dispose();

                _volume = (int)Math.Round(_device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                Muted = _device.AudioEndpointVolume.Mute;
                Available = true;
            }
            catch (COMException) { Clear(); }
            catch (InvalidCastException) { Clear(); }
        }
    }

    public void SetVolume(double percent) => Task.Run(() =>
    {
        lock (_gate)
        {
            try
            {
                if (_device is null) return;
                _device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(percent / 100, 0, 1);
                _volume = (int)Math.Round(percent);
            }
            catch (COMException) { }
        }
    });

    public void ToggleMute() => Task.Run(() =>
    {
        lock (_gate)
        {
            try
            {
                if (_device is null) return;
                bool muted = !_device.AudioEndpointVolume.Mute;
                _device.AudioEndpointVolume.Mute = muted;
                Muted = muted;
            }
            catch (COMException) { }
        }
    });

    public Task<List<AudioDevice>> ListDevicesAsync() => Task.Run(() =>
    {
        lock (_gate)
        {
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                return _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
                    .Select(d => new AudioDevice(d.ID, ShortName(d.FriendlyName))).ToList();
            }
            catch (COMException) { return new List<AudioDevice>(); }
        }
    });

    /// <summary>
    /// Makes a device the Windows default. Windows has no documented API for this; the IPolicyConfig
    /// interface used here is the same one audio-switcher utilities rely on.
    /// </summary>
    public void SetDefault(string deviceId) => Task.Run(() =>
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { Role.Console, Role.Multimedia, Role.Communications })
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
        }
        catch (Exception) { }
        Refresh();
    });

    // "Speakers (Audeze Maxwell)" reads better as "Audeze Maxwell" in a narrow button.
    private static string ShortName(string friendly)
    {
        int open = friendly.IndexOf('('), close = friendly.LastIndexOf(')');
        return open >= 0 && close > open + 1 ? friendly[(open + 1)..close] : friendly;
    }

    private void Clear()
    {
        _device?.Dispose(); _device = null;
        DeviceName = "No device"; DeviceId = ""; _volume = 0; Muted = false; Available = false;
    }

    public void Dispose()
    {
        lock (_gate) { _device?.Dispose(); _device = null; _enumerator?.Dispose(); _enumerator = null; }
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // Only SetDefaultEndpoint is used; the earlier slots are declared so it lands at the right position.
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, Role role);
        [PreserveSig] int SetEndpointVisibility();
    }
}
