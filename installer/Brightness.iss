#define StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{DBE5D352-0CB5-4FF0-9A8E-C8584C29D696}
AppName=Brightness
AppVersion={#AppVersion}
AppPublisher=misiek1205
AppPublisherURL=https://github.com/misiek1205/Brightness
AppUpdatesURL=https://github.com/misiek1205/Brightness/releases
DefaultDirName={localappdata}\Programs\Brightness
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AppArchitecture}
ArchitecturesInstallIn64BitMode={#AppArchitecture}
#if AppArchitecture == "arm64"
MinVersion=10.0.22000
#else
MinVersion=10.0.19045
#endif
AppMutex=Local\Brightness.Tray
SetupIconFile=..\src\Brightness\Sun.ico
UninstallDisplayIcon={app}\Brightness.exe
OutputBaseFilename=Brightness-{#AppVersion}-win-{#AppArchitecture}-Setup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
english.StartupTitle=Startup
english.StartupDescription=Choose when Brightness starts.
english.StartupHelp=Brightness can start in the notification area when you sign in.
english.StartupOption=Start Brightness with Windows
english.LaunchBrightness=Start Brightness now
polish.StartupTitle=Autostart
polish.StartupDescription=Wybierz, kiedy ma się uruchamiać Brightness.
polish.StartupHelp=Brightness może uruchamiać się w tle i wyświetlać ikonę obok zegara po zalogowaniu.
polish.StartupOption=Uruchamiaj Brightness z Windows
polish.LaunchBrightness=Uruchom teraz Brightness

[Files]
Source: "{#AppFiles}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Brightness"; Filename: "{app}\Brightness.exe"

[Registry]
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: string; ValueName: "Brightness"; ValueData: """{app}\Brightness.exe"""; Check: StartWithWindows
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: none; ValueName: "Brightness"; Flags: deletevalue; Check: not StartWithWindows

[Run]
Filename: "{app}\Brightness.exe"; Description: "{cm:LaunchBrightness}"; Flags: nowait postinstall skipifsilent

[Code]
var
  StartupPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  StartupPage := CreateInputOptionPage(wpSelectDir, CustomMessage('StartupTitle'),
    CustomMessage('StartupDescription'), CustomMessage('StartupHelp'), False, False);
  StartupPage.Add(CustomMessage('StartupOption'));
  StartupPage.Values[0] := (GetPreviousData('Installed', '') = '') or
    RegValueExists(HKCU, '{#StartupKey}', 'Brightness');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'Installed', 'yes');
end;

function StartWithWindows: Boolean;
begin
  Result := StartupPage.Values[0];
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if (CurUninstallStep = usPostUninstall) and
     RegQueryStringValue(HKCU, '{#StartupKey}', 'Brightness', StartupCommand) and
     (CompareText(StartupCommand, '"' + ExpandConstant('{app}\Brightness.exe') + '"') = 0) then
    RegDeleteValue(HKCU, '{#StartupKey}', 'Brightness');
end;
