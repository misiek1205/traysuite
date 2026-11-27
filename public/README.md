# TraySuite

Standalone WinForms tools for Windows 10 and 11, installed and managed through TraySuite Hub. The tray apps offer English and Polish interfaces and use the Windows light or dark theme at launch.

| Project | Purpose |
| --- | --- |
| [Hub](Hub) | Install, launch, and update apps, manage startup, activate Pro, and restart Windows Explorer from Settings or the tray menu |
| [Dimmli](Apps/Dimmli/README.md) | Monitor brightness, profiles, calibration offsets, and a CLI |
| [Awake](Apps/Awake) | Prevents sleep for a set time or until a process exits |
| [ClipPlain](Apps/ClipPlain) | Keeps a 10-entry text history in memory and pastes plain text with `Ctrl+Shift+V` |
| [PinWin](Apps/PinWin) | Pins the foreground window with `Ctrl+Win+Space`, with a configurable shortcut and optional border |
| [PowerFlip](Apps/PowerFlip) | Switches power plans and toggles the active plan's 99% processor limit on battery and AC |
| [Suite.Core](Suite.Core) | Shared WinForms controls, system tray support, shortcuts, startup, IPC, and DPAPI-protected local licensing |

## Build and run

From the directory containing this file, run `dotnet build TraySuite.sln -c Release` with the .NET 10 SDK. On Windows, the apps require the .NET 10 Desktop Runtime for their architecture. Builds support Windows 10 22H2 and Windows 11 on x64, and Windows 11 on ARM64.

Extract the entire module package before launching it. For tray modules, `--open` shows the panel, `--start` starts the app in the system tray, and `--exit` closes the running instance. With no arguments, Dimmli starts in the tray and the other tools open their panels.

## On-demand tools

Install tools in TraySuite Hub and open them from the Hub or the **TraySuite** folder in the Start menu. Closing an on-demand window ends its process. The Hub filters by launch type and license tier independently.

- **HashVerify** streams SHA-256 checksums and can compare a supplied digest. MD5 is available for legacy checksums.
- **PortCheck** identifies owners of local TCP listeners and UDP endpoints. Ending a selected process requires confirmation and is restricted to eligible processes in the current session.
- **BatchRename** previews changes to file names and checks for conflicting destinations before applying them.
- **QuickView** previews images, PDFs, text, ZIP entries, and TrueType or OpenType fonts. Media playback uses the installed Microsoft Edge WebView2 Runtime and its supported codecs.
- **FocusDim** follows the active window on the desktop. Use its control window to change opacity, pause, or exit.
- **CleanLink** performs a best-effort overwrite before permanent deletion. It skips links and special files and cannot guarantee sanitization on flash storage or removal of copies.

File tools accept dropped files and paths on the command line. In Explorer, use **Send to → TraySuite <tool>**, under **Show more options** on Windows 11 when necessary. The Hub installs and removes these shortcuts with their modules.

Enable **Tool shortcuts while Hub runs** in the Hub settings to preview Explorer selections with **Ctrl+Alt+Space**, toggle FocusDim with **Ctrl+Alt+F**, and open DropShelf with **Ctrl+Alt+D**. This setting requires the Hub tray icon to remain enabled.

[MIT license](LICENSE)
