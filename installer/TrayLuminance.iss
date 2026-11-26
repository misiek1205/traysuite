#define StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{DBE5D352-0CB5-4FF0-9A8E-C8584C29D696}
AppName=TrayLuminance
AppVersion={#AppVersion}
AppPublisher=misiek1205
AppPublisherURL=https://github.com/misiek1205/trayluminance
AppUpdatesURL=https://github.com/misiek1205/trayluminance/releases/latest
DefaultDirName={localappdata}\Programs\TrayLuminance
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
SetupIconFile=..\src\TrayLuminance\Sun.ico
UninstallDisplayIcon={app}\TrayLuminance.exe
OutputBaseFilename=TrayLuminance-{#AppVersion}-win-{#AppArchitecture}-Setup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
english.StartupTitle=Startup
english.StartupDescription=Choose when TrayLuminance starts.
english.StartupHelp=TrayLuminance can start in the notification area when you sign in.
english.StartupOption=Start TrayLuminance with Windows
english.LaunchBrightness=Start TrayLuminance now
polish.StartupTitle=Autostart
polish.StartupDescription=Wybierz, kiedy ma się uruchamiać TrayLuminance.
polish.StartupHelp=TrayLuminance może uruchamiać się w tle i wyświetlać ikonę obok zegara po zalogowaniu.
polish.StartupOption=Uruchamiaj TrayLuminance z Windows
polish.LaunchBrightness=Uruchom teraz TrayLuminance

[Files]
Source: "{#AppFiles}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\TrayLuminance"; Filename: "{app}\TrayLuminance.exe"

[Registry]
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: string; ValueName: "Brightness"; ValueData: """{app}\TrayLuminance.exe"""; Check: StartWithWindows
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: none; ValueName: "Brightness"; Flags: deletevalue; Check: not StartWithWindows

[Run]
Filename: "{app}\TrayLuminance.exe"; Description: "{cm:LaunchBrightness}"; Flags: nowait postinstall skipifsilent

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
     (CompareText(StartupCommand, '"' + ExpandConstant('{app}\TrayLuminance.exe') + '"') = 0) then
    RegDeleteValue(HKCU, '{#StartupKey}', 'Brightness');
end;
