# TraySuite

Minimalistyczne narzędzia w zasobniku Windows 10/11. Każde działa jako samodzielna aplikacja WinForms, z polskim i angielskim interfejsem oraz jasnym i ciemnym motywem zależnym od ustawień Windows przy uruchomieniu.

| Projekt | Funkcja |
| --- | --- |
| [Hub](Hub) | Instalacja, uruchamianie, aktualizacje, autostart i aktywacja Pro |
| [Dimmli](Apps/Dimmli/README.md) | Jasność monitorów, profile, offsety i CLI |
| [Awake](Apps/Awake) | Blokada uśpienia na określony czas lub do zakończenia procesu |
| [ClipPlain](Apps/ClipPlain) | Historia 10 wpisów tekstowych w pamięci i czyste wklejanie przez `Ctrl+Shift+V` |
| [Suite.Core](Suite.Core) | Kontrolki WinForms, zasobnik, skróty, autostart, IPC i lokalna licencja DPAPI |

## Kompilacja i uruchomienie

W katalogu zawierającym ten plik uruchom `dotnet build TraySuite.sln -c Release` z SDK .NET 10. Na Windows aplikacje wymagają Desktop Runtime .NET 10 dla swojej architektury. Dystrybucja obejmuje Windows 10 22H2 i Windows 11 na x64 oraz Windows 11 na ARM64.

Wypakuj całą paczkę modułu przed uruchomieniem. `--open` pokazuje panel, `--start` uruchamia sam zasobnik, a `--exit` zamyka uruchomioną instancję. Dimmli zachowuje uruchomienie do zasobnika po wywołaniu bez argumentów. Pozostałe narzędzia otwierają panel.

[Licencja MIT](LICENSE)
