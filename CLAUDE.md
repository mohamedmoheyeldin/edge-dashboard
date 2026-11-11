# Edge Dashboard

Native Windows (WinUI 3, .NET 11, unpackaged) hardware dashboard for the Corsair Xeneon Edge, a
2560×720 touch display driven by the AMD integrated GPU. Most of the UI is built in C# under
`Ui/`. `README.md` describes what it shows and how it is installed.

## Rules
- **Installing is automatic too** (Eldin, 2026-11-11: "this rules apply to everything"). After a
  release is published, download its `EdgeDashboard-Setup-<version>.exe` from the GitHub release
  and install it on this PC (it installs per user, no admin prompt), then check that the
  dashboard is running again on the Edge display.
- **Releases are fully automatic** (Eldin, 2026-11-11). When a change that people will notice is
  finished and builds in Release, release it without asking: raise `<Version>` in
  `EdgeDashboard.csproj` (patch for fixes, minor for features), commit, push, then push the tag
  `v<version>` (`git tag -a v1.0.1 -m "Edge Dashboard 1.0.1"`, `git push origin v1.0.1`). The tag
  makes GitHub build the installer and publish the release; check with `gh run watch` that the
  run succeeded and fix it if it did not. Ordinary pushes to `main` do not trigger anything.
- The dashboard is touch-only and full-screen. Nothing may close it by accident: closing takes two
  taps on **Close dashboard**. Do not add key or gesture shortcuts that close or move it.
- Monitor sliders write to the monitor itself over DDC/CI, so a test changes the real picture
  settings of a real screen. Do not send DDC/CI commands on your own; ask first.
- Sensor names in `Sensors/HwInfoReader.cs` match this PC's hardware in HWiNFO. Do not rename or
  generalise them without checking the live sensor list.

- **While developing**, run the Release build straight from `bin\` to try a change; close the
  installed dashboard first and start it again afterwards. Only the published release gets
  installed. Afterwards leave nothing behind: no test copies running, no downloaded installers,
  no scratch files, `git status` clean.

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
There are no automated tests. After a change, build in Release; what was only compiled and not
seen running should be reported as such.

## Needs at run time
HWiNFO64 with Shared Memory Support on, the NVIDIA app (FrameView SDK), Windows location on.

## Conventions
- Comments say why, in plain words. Text on screen is short and plain.
- Commit straight to `main`, one logical change per commit, and push. Commits are SSH-signed
  automatically. The version lives in `EdgeDashboard.csproj`.
