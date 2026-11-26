# TraySuite

Modułowe narzędzia dla Windows 10/11 w modelu Free i Pro. [Instrukcje Huba i darmowych aplikacji](public/README.md) · [Dokumentacja Dimmli](public/Apps/Dimmli/README.md)

- `public/` zawiera Hub, Suite.Core i darmowe moduły Dimmli, Awake oraz ClipPlain. `public/TraySuite.sln` buduje się samodzielnie po skopiowaniu tego katalogu do osobnego repozytorium.
- Główne `TraySuite.sln` obejmuje część publiczną. Źródła Sweep, Boost, Pulse i Volly są rozwijane w osobnym prywatnym repozytorium, które lokalnie można umieścić w ignorowanym katalogu `pro/`. Każda aplikacja używa współdzielonego Desktop Runtime .NET 10 dla swojej architektury.

## Budowanie paczek

Wymagane są .NET 10 SDK i Python 3.11 lub nowszy. Instalator Huba buduje się na Windows z Inno Setup 6.3+.

```powershell
dotnet build TraySuite.sln -c Release
python installer/build.py build --scope public --runtime win-x64
python installer/build.py build --scope public --runtime win-arm64
python installer/build.py build --scope pro --runtime win-x64
python installer/build.py build --scope pro --runtime win-arm64
python installer/build.py merge --scope public --inputs artifacts/public --output artifacts/release
python installer/build.py merge --scope pro --inputs artifacts/pro --output artifacts/private-release
```

Dodaj `--no-setup` do kompilacji publicznej na Linux lub kiedy potrzebujesz przenośnego Huba. ZIP-y, fragmenty manifestu i przenośny Hub pojawią się pod `artifacts/public/<runtime>`. Zbudowany Inno Setup sprawdza zgodną architekturę i wersję Desktop Runtime. Przy jego braku pobiera instalator Microsoftu z przypiętą sumą SHA‑256 i prosi o uprawnienia do uruchomienia instalatora systemowego.

Publiczny workflow buduje instalatory oraz paczki Free i scala `manifest.json`. Prywatny workflow uruchamia się ręcznie w prywatnym repozytorium i generuje paczki Pro oraz `manifest-pro.json`. Publiczny manifest ma puste `download_url` i `sha256` dla Pro. Przy składaniu publicznego manifestu możesz podać `--pro-metadata artifacts/private-release/manifest-pro.json`, aby opublikować wersje i rozmiary pakietów Pro. Linki i sumy pozostają w prywatnym manifeście.

Hub domyślnie korzysta z `manifest.json` i paczek Free z najnowszego wydania GitHub Releases repozytorium `misiek1205/dimmli`. Umieść tam publiczny manifest, dwa instalatory i sześć publicznych ZIP-ów. Pliki prywatnego wydania publikuj przez źródło pakietów Pro.

## Praca lokalna

Na Windows uruchom `TraySuite.Hub.exe --local C:\paczki`, gdy Hub jest zamknięty. Możesz też ustawić `TRAYSUITE_LOCAL_PACKAGES`. Katalog może zawierać wygenerowany manifest z ZIP-ami, ZIP-y z plikami `<App>-<runtime>.zip.sha256` lub pliki z `dotnet publish` w podkatalogach `<App>\<runtime>`. W przypadku manifestu i ZIP-ów Hub sprawdza SHA‑256. Lokalne pliki `.exe` mogą korzystać z bibliotek dołączonych obok nich. Aktywacja `DEV-PRO-TEST` działa w buildach Debug, także dla lokalnego `manifest-pro.json` i osobno publikowanych plików Pro Debug.

## Konfiguracja sprzedaży i pobierania Pro

| Zmienna przy budowaniu | Przeznaczenie |
| --- | --- |
| `TRAYSUITE_STORE_ID`, `TRAYSUITE_PRODUCT_ID` | Identyfikatory produktu w Lemon Squeezy sprawdzane przy aktywacji |
| `TRAYSUITE_CHECKOUT_URL` | HTTPS do zakupu klucza |
| `TRAYSUITE_DOWNLOADS_URL` | HTTPS do usługi wydającej linki do prywatnych pakietów |
| `TRAYSUITE_PRIVATE_PACKAGE_BASE` | Opcjonalna baza adresów w manifeście z prywatnego workflow |

Hub aktywuje klucz przez oficjalny endpoint Lemon Squeezy i przechowuje parę klucz–instancja zaszyfrowaną przez DPAPI dla użytkownika Windows. [Dokumentacja aktywacji](https://docs.lemonsqueezy.com/api/license-api/activate-license-key)

Oficjalna odpowiedź Lemon Squeezy zawiera stan licencji i metadane produktu. Hub akceptuje rozszerzenie `download_manifest_url` w odpowiedzi lub wysyła do skonfigurowanej usługi pobierania JSON z `license_key` i `instance_id`. Usługa weryfikuje instancję i identyfikatory produktu po stronie serwera, po czym zwraca `{"download_manifest_url":"https://…"}`. Adres prowadzi do manifestu z krótkotrwałymi adresami HTTPS paczek i ich SHA‑256. Konfiguracja zakupu i usługa pobierania stanowią osobne elementy wdrożenia.

Lemon Squeezy zamknęło rejestrację nowych sklepów 16 listopada 2026 r. i kończy obsługę transakcji 31 marca 2027 r. [Harmonogram dostawcy](https://docs.lemonsqueezy.com/migration)

## Integracje z Windowsem

- **Sweep** pokazuje wynik przed czyszczeniem. Uwzględnia pliki starsze niż 24 godziny, a dla Windows Update starsze niż 7 dni. Pomija punkty ponownej analizy, pliki chronione i pobrane aktualizacje, gdy działa usługa Windows Update.
- **Boost** próbuje włączyć najwyższą lub wysoką wydajność i przywraca wcześniejszy plan. Integracja Dimmli przywraca jasność po zwolnieniu trybu lub zakończeniu procesu Boost. Boost włącza systemowe „Nie przeszkadzać” przez nieudokumentowany interfejs COM powłoki i po grze przywraca poprzedni profil. Gdy interfejs jest niedostępny, przycisk reguł powiadomień otwiera ustawienia Windows. Boost prosi wybrane aplikacje o zamknięcie i po grze ponownie uruchamia te, które się zamknęły. [Reguły powiadomień Windows](https://support.microsoft.com/en-us/windows/experience/focus-stay-on-task-without-distractions-in-windows)
- **Pulse** próbuje odczytać temperatury przez WMI i NVIDIA NVML. Kreska oznacza brak dostępnego pomiaru. WMI LibreHardwareMonitor może udostępnić dodatkowe czujniki CPU i GPU.
- **Volly** miesza aktywne sesje Core Audio. Domyślne wyjście zmienisz listą w popupie lub przez `Ctrl+Alt+O`. Przełączanie używa interfejsu Windows `IPolicyConfig`. [Interfejsy Core Audio](https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-interfaces)

Workflow publiczny testuje instalację Huba oraz instalowanie, otwieranie i usuwanie darmowych modułów z jego popupu. Sprawdza też CLI Dimmli i usuwanie wpisu autostartu Huba. Pomiary sprzętu, schowek, zasobnik, jasność i audio wymagają testu w interaktywnej sesji Windows.
