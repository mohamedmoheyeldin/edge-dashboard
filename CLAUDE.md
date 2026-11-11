# Edge Dashboard

Native Windows (WinUI 3, .NET 11, unpackaged) hardware dashboard for the Corsair Xeneon Edge, a
2560×720 touch display driven by the AMD integrated GPU. Most of the UI is built in C# under
`Ui/`. `README.md` describes what it shows and how it is installed.

## Rules
- **Do not install or run a new build on this PC.** Eldin installs releases himself. Build to
  check that it compiles; stop there unless he asks for more.
- **Do not push a version tag** (`v*`) unless Eldin asks: a tag makes GitHub build the installer
  and publish a release. Ordinary pushes to `main` do not trigger anything.
- The dashboard is touch-only and full-screen. Nothing may close it by accident: closing takes two
  taps on **Close dashboard**. Do not add key or gesture shortcuts that close or move it.
- Monitor sliders write to the monitor itself over DDC/CI, so a test changes the real picture
  settings of a real screen. Do not send DDC/CI commands on your own; ask first.
- Sensor names in `Sensors/HwInfoReader.cs` match this PC's hardware in HWiNFO. Do not rename or
  generalise them without checking the live sensor list.

## Layout
| Path | What |
|---|---|
| `App.xaml(.cs)`, `MainWindow.xaml(.cs)` | Start-up, the window, placement on the Edge display |
| `Ui/` | Cards, graphs, the audio strip, the screen-settings page, animation helpers |
| `Sensors/` | `HwInfoReader` (HWiNFO shared memory), `FrameViewReader` (NVIDIA FrameView) |
| `Audio/` | Volume, mute and device switching (NAudio WASAPI) |
| `Display/` | Monitor control over DDC/CI |
| `Weather/` | Weather from the Windows location |
| `installer/` | Inno Setup 6 script and `build.ps1` |

## Build
```powershell
dotnet build -c Release          # the app
.\installer\build.ps1            # the installer, into dist\ (Inno Setup 6 is installed)
```
There are no automated tests. After a change, build in Release and say plainly that it was
compiled but not run, unless Eldin ran it.

## Needs at run time
HWiNFO64 with Shared Memory Support on, the NVIDIA app (FrameView SDK), Windows location on.

## Conventions
- Comments say why, in plain words. Text on screen is short and plain.
- Commit straight to `main`, one logical change per commit, and push. Commits are SSH-signed
  automatically. The version lives in `EdgeDashboard.csproj`.
