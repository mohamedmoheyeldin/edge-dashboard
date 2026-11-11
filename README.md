# Edge Dashboard

A native Windows (WinUI 3) hardware dashboard for the Corsair Xeneon Edge (2560×720 touch display).

![Dashboard on the Edge](docs/on-the-edge.png)

- CPU and GPU temperatures, power, voltage, usage and clock graphs, per-core load for both CCDs
- Frame rate, 1% low and PC latency for the running game (NVIDIA FrameView)
- Memory, network, drive and motherboard temperatures, AMD iGPU, weather, clock
- Output and microphone volume, mute and device switching by touch
- A second page (swipe) with brightness, contrast and colour controls for each monitor

## Needs

- [HWiNFO64](https://www.hwinfo.com/) running with **Shared Memory Support** turned on (all sensor values come from it)
- The NVIDIA app (it provides the FrameView SDK used for frame rates)
- Windows location turned on, for the weather

## Install

Run `EdgeDashboard-Setup-<version>.exe`. It installs for the current user without an admin prompt, adds a
Start menu entry, starts with Windows, and sets the app to use the power-saving GPU. Uninstall from
Settings > Apps.

The dashboard opens full-screen on the 2560×720 display. To close it, swipe to the Screen settings
page and tap **Close dashboard** twice (the second tap confirms). No key closes it by accident.

## Build

```powershell
dotnet build -c Release          # the app
.\installer\build.ps1            # the installer, into dist\ (needs Inno Setup 6)
```

## Release

Push a version tag and GitHub builds the installer and publishes it on the Releases page:

```powershell
git tag -a v1.0.1 -m "Edge Dashboard 1.0.1"
git push origin v1.0.1
```
