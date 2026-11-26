# Dimmli · Windows monitor brightness control

Dimmli (formerly TrayLuminance) is an open-source brightness controller for
Windows 10 and 11. The panel follows the Windows light or dark app theme at launch.
Click the sun icon in the system tray to adjust each monitor independently.
It uses DDC/CI for compatible external monitors and Windows WMI for built-in
laptop displays.

[Download for Windows x64 or ARM64](https://github.com/misiek1205/traysuite/releases/latest) · [MIT license](../../LICENSE)

## Get started

1. Install Dimmli from TraySuite Hub, or extract the ZIP for your Windows architecture and run
   `Dimmli.exe --open`. The ZIP requires the matching .NET 10 Desktop Runtime.
2. Click the sun icon next to the clock and adjust the slider for the display you want to change.
3. Use **Start with Windows** in the icon's right-click menu or manage autostart in the Hub.

## Adjust displays together and save profiles

- Drag **All monitors** to add or subtract the same number of percentage
  points from every controllable display. Individual values stop at 0% or
  100%. The slider returns to the center when you release it.
- Set the individual sliders, then click **Save day** or **Save evening**.
  Click **Day** or **Evening** to restore the saved levels. Profiles are
  stored on this Windows account and match displays by their Windows
  device identifier. Saving updates the connected, identifiable monitors
  and keeps previously saved levels for disconnected displays.

## Global keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl` + `Alt` + `B` | Open or close the panel |
| `Ctrl` + `Alt` + `Page Up` / `Page Down` | Brighten or dim the controllable monitors by 5 percentage points |
| `Ctrl` + `Alt` + `1` / `2` | Apply the saved Day or Evening profile |
| `Ctrl` + `Alt` + `Shift` + `Page Up` / `Page Down` | Adjust shared brightness by one percentage point |

Shortcuts work while Dimmli is running in the notification area. Open
**Keys** in the panel to turn them off or check whether Windows made a
combination available. The setting persists across restarts.
Hold **Shift** while dragging a slider for finer 1-point movement, or
while using its arrow or Page Up/Page Down keys.

## Optional automation and calibration

- **Automation · PRO** in the tray menu offers fixed hours or local sunrise and
  sunset for the saved Day and Evening profiles. Enter approximate latitude
  and longitude for solar scheduling. Dimmli uses the Windows time zone and
  uses the fixed hours when a solar event is unavailable.
- The same dialog contains **Fullscreen boost** and its brightness level.
  It follows the foreground fullscreen window and restores the previous
  level when that window leaves fullscreen. A scheduled profile can update
  the level that will be restored. A manual brightness change to the
  boosted display takes priority until the fullscreen session ends.
- **Brightness calibration** is available by right-clicking a monitor card.
  Set an offset from −20 to +20 percentage points. When any monitor has an
  offset, shared adjustments use one reference level plus each monitor's
  offset, clamped to 0–100%. Individual sliders and profiles use each
  monitor's physical brightness percentage.

Both automation options require an active Pro license and are off by default. The schedule arms a timer for
its next transition, and fullscreen detection listens for foreground and
window events while enabled.

## Command line

```powershell
.\Dimmli.exe --list
.\Dimmli.exe --profile Evening
.\Dimmli.exe --brightness 50
.\Dimmli.exe --monitor 1 --brightness 80
```

Use `--list` for the current monitor numbers. Commands go to the tray
instance in the same Windows user session and start it if needed. They
report whether the change was accepted into the queue. To get an exit code
from the GUI executable in PowerShell scripts, use
`Start-Process .\Dimmli.exe -ArgumentList '--brightness 50' -Wait -PassThru`
and read its `ExitCode`. For Task Scheduler, run under the signed-in account
in its interactive session.

## Monitor names and tray actions

- Right-click a monitor card and choose **Rename monitor**. An empty name
  restores the original. Dimmli saves names for displays Windows can
  identify individually.
- Right-click the tray icon and choose **Check monitors** for a fresh
  brightness read. Copy the report to share the result. It includes monitor
  models and read methods and omits device identifiers and custom names.
- Right-click the tray icon and enable **Scroll over tray icon to adjust
  brightness** to change all controllable displays by five percentage
  points per wheel notch. This is off by default. If the icon is hidden,
  move it to the visible notification area for quick access.

## Display compatibility

- **External monitors** use DDC/CI over the display connection. Dimmli
  tries the Windows monitor brightness API and the MCCS luminance feature
  (VCP `0x10`). Enable DDC/CI in the monitor's own menu if it has that setting.
- **Built-in displays** use the Windows WMI brightness interface when the
  graphics driver exposes it.
- Some docks, adapters, KVM switches and graphics drivers block DDC/CI.
  Some monitor presets or HDR modes lock brightness. If a display is listed
  without a slider, try a direct DisplayPort or HDMI connection and verify
  that the monitor's brightness control is available in SDR. Click **Refresh**
  after changing the setup. Support depends on the hardware and connection.

## Build from source

Use the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build Apps/Dimmli/Dimmli.csproj -c Release
dotnet run --project Apps/Dimmli/Dimmli.csproj
```

## Test monitor compatibility

The public solution cross-builds from Linux with the .NET 10 SDK. Use Windows to exercise the
tray and popup, and a physical display for DDC/CI and hardware brightness.

To verify a physical monitor, connect it to a Windows PC and adjust each
slider in turn. Confirm that it changes only its own monitor. Reconnect a
monitor and check that the panel refreshes the list. Reports are easiest to
reproduce with the Windows version, monitor model, connection type and
DDC/CI and HDR settings.
