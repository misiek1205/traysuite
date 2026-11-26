# Brightness

Adjust hardware brightness from the Windows notification area. One click on
the sun icon opens a panel with a slider for each controllable display.

[Po polsku](#po-polsku) · [Download](https://github.com/misiek1205/Brightness/releases) · [MIT license](LICENSE)

## Get started

1. Download the ZIP for your Windows PC (x64 for most Intel and AMD PCs, ARM64
   for Windows on ARM), extract it and run `Brightness.exe`. The portable
   builds include the .NET runtime and run without administrator rights.
2. If the sun is under the arrow next to the clock, drag it into the visible
   notification area. Click it to open the panel and move a slider to change
   the brightness of that display.
3. To keep the icon available after signing in, right-click the sun and choose
   **Start with Windows**. Keep the executable in a permanent folder if you
   turn this on.

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

The application runs on Windows 10/11. If you report a compatibility issue,
include your Windows version, monitor model, connection type, and whether
DDC/CI and HDR are enabled. For a multi-monitor test, adjust two sliders in
turn and confirm each changes only its own monitor. Reconnect a monitor and
verify that the panel refreshes the list.

## Po polsku

**Brightness** zmienia sprzętową jasność monitorów w Windows 10/11. Kliknij
słońce obok zegara, a zobaczysz osobny suwak dla każdego obsługiwanego ekranu.
Interfejs używa języka polskiego, gdy taki jest język systemu.

1. Pobierz [ZIP z wydania](https://github.com/misiek1205/Brightness/releases)
   (x64 dla typowego komputera z Intelem lub AMD, ARM64 dla Windows na ARM).
   Wypakuj go i uruchom `Brightness.exe`. Paczka zawiera środowisko .NET.
2. Jeśli ikona słońca jest pod strzałką obok zegara, przeciągnij ją do widocznej
   części paska. Kliknij ją i przesuń suwak wybranego monitora.
3. Jeśli chcesz uruchamiać aplikację przy logowaniu, kliknij słońce prawym
   przyciskiem i zaznacz **Uruchamiaj z Windows**. Przechowuj plik EXE
   w stałym folderze.

Monitory zewnętrzne korzystają z DDC/CI, a ekrany laptopów mogą korzystać
z interfejsu WMI. Jeśli zewnętrzny monitor nie ma suwaka, sprawdź **DDC/CI**
w jego menu ekranowym. Spróbuj też bezpośredniego połączenia DisplayPort lub
HDMI i trybu SDR. Niektóre stacje dokujące, przejściówki i tryby HDR ograniczają
regulację. Po zmianie konfiguracji kliknij **Odśwież**.
