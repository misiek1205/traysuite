# Brightness

Adjust hardware brightness from the Windows notification area. One click on
the sun icon opens a panel with a slider for each controllable display.

[Po polsku](#po-polsku) · [Download](https://github.com/misiek1205/Brightness/releases) · [MIT license](LICENSE)

## Get started

1. Download and run the installer for your PC (x64 for most Intel and AMD
   PCs, ARM64 for Windows on ARM). It installs for your Windows account and
   includes the .NET runtime. You can also extract the portable ZIP and run
   `Brightness.exe`.
2. If the sun is under the arrow next to the clock, drag it into the visible
   notification area. Click it to open the panel and move a slider to change
   the brightness of that display.
3. The installer enables **Start Brightness with Windows** by default.
   Change this later with **Start with Windows** in the icon's right-click
   menu. If you use the portable build, keep the executable in a permanent
   folder before enabling this setting.

To update an installed copy, choose **Exit** from the sun icon's right-click
menu and run the new installer. It keeps your current startup preference.
To uninstall, exit Brightness and remove it from Windows **Installed apps**.

## Display compatibility

- **External monitors** use DDC/CI over the display connection. Brightness
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
dotnet build src/Brightness/Brightness.csproj -c Release
dotnet run --project src/Brightness/Brightness.csproj
```

To build the installer on Windows, install [Inno Setup 6.3+](https://jrsoftware.org/isdl.php)
and run `./installer/Build-Installer.ps1 -Runtime win-x64` in PowerShell 7.
Use `win-arm64` to build for Windows on ARM. The installer and portable
ZIP appear under `artifacts/<runtime>`.

The installer targets Windows 10 22H2 and Windows 11 on x64, and Windows 11
on ARM64. If you report a compatibility issue,
include your Windows version, monitor model, connection type, and whether
DDC/CI and HDR are enabled. For a multi-monitor test, adjust two sliders in
turn and confirm each changes only its own monitor. Reconnect a monitor and
verify that the panel refreshes the list.

## Po polsku

**Brightness** zmienia sprzętową jasność monitorów w Windows 10/11. Kliknij
słońce obok zegara, a zobaczysz osobny suwak dla każdego obsługiwanego ekranu.
Interfejs używa języka polskiego, gdy taki jest język systemu.

1. Pobierz [instalator](https://github.com/misiek1205/Brightness/releases)
   (x64 dla komputerów z Intelem lub AMD, ARM64 dla Windows na ARM).
   Instalator zawiera środowisko .NET i instaluje aplikację na Twoim koncie.
   Możesz też wypakować przenośny ZIP i uruchomić `Brightness.exe`.
2. Jeśli ikona słońca jest pod strzałką obok zegara, przeciągnij ją do widocznej
   części paska. Kliknij ją i przesuń suwak wybranego monitora.
3. Instalator domyślnie włącza uruchamianie w tle po zalogowaniu do Windows.
   Możesz zmienić tę opcję w instalatorze lub później, klikając słońce prawym
   przyciskiem i wybierając **Uruchamiaj z Windows**.

Przed instalacją nowszej wersji wybierz **Zakończ** z menu ikony słońca.
Aktualizacja zachowa ustawienie autostartu. Aplikację odinstalujesz przez
**Zainstalowane aplikacje** w ustawieniach Windows.

Monitory zewnętrzne korzystają z DDC/CI, a ekrany laptopów mogą korzystać
z interfejsu WMI. Jeśli zewnętrzny monitor nie ma suwaka, sprawdź **DDC/CI**
w jego menu ekranowym. Spróbuj też bezpośredniego połączenia DisplayPort lub
HDMI i trybu SDR. Niektóre stacje dokujące, przejściówki i tryby HDR ograniczają
regulację. Po zmianie konfiguracji kliknij **Odśwież**.
