# Dimmli · Windows monitor brightness control

Dimmli (formerly TrayLuminance) is an open-source brightness controller for
Windows 10 and 11. The panel follows the Windows light or dark app theme at launch.
Click the sun icon in the system tray to adjust each monitor independently.
It uses DDC/CI for compatible external monitors and Windows WMI for built-in
laptop displays.

[Download for Windows x64 or ARM64](https://github.com/misiek1205/dimmli/releases/latest) · [Po polsku](#po-polsku) · [MIT license](LICENSE)

## Get started

1. Download and run the installer for your PC (x64 for most Intel and AMD
   PCs, ARM64 for Windows on ARM). It installs for your Windows account and
   includes the .NET runtime. You can also extract the portable ZIP and run
   `Dimmli.exe`.
2. If the sun is under the arrow next to the clock, drag it into the visible
   notification area. Click it to open the panel and move a slider to change
   the brightness of that display.
3. The installer enables **Start Dimmli with Windows** by default.
   Change this later with **Start with Windows** in the icon's right-click
   menu. If you use the portable build, keep the executable in a permanent
   folder before enabling this setting.

To update an installed copy, choose **Exit** from the sun icon's right-click
menu and run the new installer. It keeps your current startup preference.
To uninstall, exit Dimmli and remove it from Windows **Installed apps**.

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

- **Automation** in the tray menu offers fixed hours or local sunrise and
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

Both automation options are off by default. The schedule arms a timer for
its next transition, and fullscreen detection listens for foreground and
window events while enabled.

## Command line

```powershell
.\Dimmli.exe --list
.\Dimmli.exe --profile "Wieczór"
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
dotnet build src/Dimmli/Dimmli.csproj -c Release
dotnet run --project src/Dimmli/Dimmli.csproj
```

To build the installer on Windows, install [Inno Setup 6.3+](https://jrsoftware.org/isdl.php)
and run `./installer/Build-Installer.ps1 -Runtime win-x64` in PowerShell 7.
Use `win-arm64` to build for Windows on ARM. The installer and portable
ZIP appear under `artifacts/<runtime>`.

The installer targets Windows 10 22H2 and Windows 11 on x64, and Windows 11
on ARM64.

## Test monitor compatibility

You can cross-build from Linux with the .NET 10 SDK. GitHub Actions verifies
installation, startup registration, upgrades and removal on Windows x64 and
ARM64. A Windows VM with a virtual display is suitable for checking the
installer, system tray and panel. Wine's monitor configuration APIs have
no DDC/CI implementation in the upstream DXVA2 library.

To verify a physical monitor, connect it to a Windows PC and adjust each
slider in turn. Confirm that it changes only its own monitor. Reconnect a
monitor and check that the panel refreshes the list. Reports are easiest to
reproduce with the Windows version, monitor model, connection type and
DDC/CI and HDR settings.

## Po polsku

**Dimmli** zmienia sprzętową jasność monitorów w Windows 10/11. Kliknij
słońce obok zegara, a zobaczysz osobny suwak dla każdego obsługiwanego ekranu.
Interfejs używa języka polskiego, gdy taki jest język systemu.

1. Pobierz [instalator](https://github.com/misiek1205/dimmli/releases/latest)
   (x64 dla komputerów z Intelem lub AMD, ARM64 dla Windows na ARM).
   Instalator zawiera środowisko .NET i instaluje aplikację na Twoim koncie.
   Możesz też wypakować przenośny ZIP i uruchomić `Dimmli.exe`.
2. Jeśli ikona słońca jest pod strzałką obok zegara, przeciągnij ją do widocznej
   części paska. Kliknij ją i przesuń suwak wybranego monitora.
3. Instalator domyślnie włącza uruchamianie w tle po zalogowaniu do Windows.
   Możesz zmienić tę opcję w instalatorze lub później, klikając słońce prawym
   przyciskiem i wybierając **Uruchamiaj z Windows**.

Przed instalacją nowszej wersji wybierz **Zakończ** z menu ikony słońca.
Aktualizacja zachowa ustawienie autostartu. Aplikację odinstalujesz przez
**Zainstalowane aplikacje** w ustawieniach Windows.

Suwak **Wszystkie monitory** zmienia jasność każdego obsługiwanego ekranu
o tę samą liczbę punktów procentowych, w granicach 0–100%. Po puszczeniu
suwak wraca na środek. Aby zapisać ustawienia poszczególnych monitorów,
ustaw ich suwaki, a potem kliknij **Zapisz dzień** lub **Zapisz wieczór**.
Przyciski **Dzień** i **Wieczór** wczytują zapisane poziomy. Przy zapisie
aplikacja zachowuje też wcześniejsze poziomy odłączonych monitorów.

Skróty globalne działają, gdy Dimmli jest w zasobniku. `Ctrl+Alt+B` otwiera
lub zamyka panel, `Ctrl+Alt+PgUp/PgDn` zmienia jasność o 5 punktów
procentowych, a `Ctrl+Alt+1/2` włącza zapisany profil Dzień lub Wieczór.
W zakładce **Skróty** można je wyłączyć i sprawdzić dostępność kombinacji.
Przytrzymaj **Shift** przy przeciąganiu suwaka lub dodaj go do skrótu
`Ctrl+Alt+PgUp/PgDn`, aby regulować o 1 punkt procentowy.

W menu ikony wybierz **Automatyzacja**, aby ustawić harmonogram profili
lub jasność dla pełnego ekranu. Tryb harmonogramu słonecznego używa
przybliżonych współrzędnych i strefy Windows. **Kalibrację jasności**
znajdziesz pod prawym przyciskiem na karcie monitora.

Kliknij kartę monitora prawym przyciskiem, aby nadać mu własną nazwę.
Puste pole przywraca nazwę oryginalną. W menu ikony Dimmli **Sprawdź
monitory** odczytuje jasność i umożliwia skopiowanie raportu. Opcja
**Reguluj jasność rolką nad ikoną** jest domyślnie wyłączona. Po jej
włączeniu jeden skok rolki zmienia jasność o 5 punktów procentowych.

Monitory zewnętrzne korzystają z DDC/CI, a ekrany laptopów mogą korzystać
z interfejsu WMI. Jeśli zewnętrzny monitor nie ma suwaka, sprawdź **DDC/CI**
w jego menu ekranowym. Spróbuj też bezpośredniego połączenia DisplayPort lub
HDMI i trybu SDR. Niektóre stacje dokujące, przejściówki i tryby HDR ograniczają
regulację. Po zmianie konfiguracji kliknij **Odśwież**.
