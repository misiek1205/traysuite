# TraySuite

Minimal system tray tools for Windows 10 and 11. Each tool runs as a standalone WinForms app with English and Polish interfaces and uses the Windows light or dark theme at launch.

| Project | Purpose |
| --- | --- |
| [Hub](Hub) | Install, launch, and update apps, manage startup, and activate Pro |
| [Dimmli](Apps/Dimmli/README.md) | Monitor brightness, profiles, calibration offsets, and a CLI |
| [Awake](Apps/Awake) | Prevents sleep for a set time or until a process exits |
| [ClipPlain](Apps/ClipPlain) | Keeps a 10-entry text history in memory and pastes plain text with `Ctrl+Shift+V` |
| [Suite.Core](Suite.Core) | Shared WinForms controls, system tray support, shortcuts, startup, IPC, and DPAPI-protected local licensing |

## Build and run

From the directory containing this file, run `dotnet build TraySuite.sln -c Release` with the .NET 10 SDK. On Windows, the apps require the .NET 10 Desktop Runtime for their architecture. Builds support Windows 10 22H2 and Windows 11 on x64, and Windows 11 on ARM64.

Extract the entire module package before launching it. `--open` shows the panel, `--start` starts the app in the system tray, and `--exit` closes the running instance. With no arguments, Dimmli starts in the tray and the other tools open their panels.

[MIT license](LICENSE)
