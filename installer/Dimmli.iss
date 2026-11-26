#define StartupKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{DBE5D352-0CB5-4FF0-9A8E-C8584C29D696}
AppName=Dimmli
AppVersion={#AppVersion}
AppPublisher=misiek1205
AppPublisherURL=https://github.com/misiek1205/dimmli
AppUpdatesURL=https://github.com/misiek1205/dimmli/releases/latest
DefaultDirName={localappdata}\Programs\Dimmli
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
SetupIconFile=..\src\Dimmli\Sun.ico
UninstallDisplayIcon={app}\Dimmli.exe
OutputBaseFilename=Dimmli-{#AppVersion}-win-{#AppArchitecture}-Setup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
english.StartupTitle=Startup
english.StartupDescription=Choose when Dimmli starts.
english.StartupHelp=Dimmli can start in the notification area when you sign in.
english.StartupOption=Start Dimmli with Windows
english.LaunchBrightness=Start Dimmli now
polish.StartupTitle=Autostart
polish.StartupDescription=Wybierz, kiedy ma się uruchamiać Dimmli.
polish.StartupHelp=Dimmli może uruchamiać się w tle i wyświetlać ikonę obok zegara po zalogowaniu.
polish.StartupOption=Uruchamiaj Dimmli z Windows
polish.LaunchBrightness=Uruchom teraz Dimmli

[Files]
Source: "{#AppFiles}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Dimmli"; Filename: "{app}\Dimmli.exe"

[Registry]
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: string; ValueName: "Brightness"; ValueData: """{app}\Dimmli.exe"""; Check: StartWithWindows
Root: HKCU; Subkey: "{#StartupKey}"; ValueType: none; ValueName: "Brightness"; Flags: deletevalue; Check: not StartWithWindows

[Run]
Filename: "{app}\Dimmli.exe"; Description: "{cm:LaunchBrightness}"; Flags: nowait postinstall skipifsilent

[Code]
var
  StartupPage: TInputOptionWizardPage;
  IsUpgrade: Boolean;

procedure InitializeWizard;
begin
  StartupPage := CreateInputOptionPage(wpSelectDir, CustomMessage('StartupTitle'),
    CustomMessage('StartupDescription'), CustomMessage('StartupHelp'), False, False);
  StartupPage.Add(CustomMessage('StartupOption'));
  IsUpgrade := GetPreviousData('Installed', '') <> '';
  StartupPage.Values[0] := not IsUpgrade or RegValueExists(HKCU, '{#StartupKey}', 'Brightness');
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'Installed', 'yes');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and IsUpgrade then
  begin
    DeleteFile(ExpandConstant('{app}\TrayLuminance.exe'));
    DeleteFile(ExpandConstant('{userprograms}\TrayLuminance.lnk'));
  end;
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
     (CompareText(StartupCommand, '"' + ExpandConstant('{app}\Dimmli.exe') + '"') = 0) then
    RegDeleteValue(HKCU, '{#StartupKey}', 'Brightness');
end;
