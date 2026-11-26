#define DesktopRoot "SOFTWARE\dotnet\Setup\InstalledVersions\" + AppArchitecture
#define RuntimeName "traysuite-desktop-runtime.exe"

[Setup]
AppId={{D43BDF43-85B7-4D8E-96F8-B59C99A8AC09}
AppName=TraySuite Hub
AppVersion={#AppVersion}
AppPublisher=misiek1205
AppPublisherURL=https://github.com/misiek1205/dimmli
AppUpdatesURL=https://github.com/misiek1205/dimmli/releases/latest
DefaultDirName={localappdata}\Programs\TraySuite Hub
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
#if AppArchitecture == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
MinVersion=10.0.22000
#else
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
#endif
AppMutex=Local\TraySuite.Hub
SetupIconFile=..\public\Apps\Dimmli\Sun.ico
UninstallDisplayIcon={app}\TraySuite.Hub.exe
OutputBaseFilename=TraySuite-Hub-win-{#AppArchitecture}-Setup
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[CustomMessages]
english.InstallRuntime=Installing Microsoft .NET 10 Desktop Runtime…
english.RestartRuntime=Restart Windows to finish installing the .NET runtime.
english.RuntimeError=The .NET 10 Desktop Runtime could not be installed.
english.LaunchHub=Open TraySuite Hub
polish.InstallRuntime=Instalowanie Microsoft .NET 10 Desktop Runtime…
polish.RestartRuntime=Uruchom ponownie Windows, aby dokończyć instalację środowiska .NET.
polish.RuntimeError=Nie udało się zainstalować .NET 10 Desktop Runtime.
polish.LaunchHub=Otwórz TraySuite Hub

[Files]
Source: "{#AppFiles}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\TraySuite Hub"; Filename: "{app}\TraySuite.Hub.exe"; Parameters: "--open"

[Run]
Filename: "{app}\TraySuite.Hub.exe"; Parameters: "--open"; Description: "{cm:LaunchHub}"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

function HasFramework(const Root, Framework: String): Boolean;
var
  Location, Version: String;
  FindRec: TFindRec;
begin
  Result := False;
  Location := AddBackslash(Root) + 'shared\' + Framework + '\';
  if FindFirst(Location + '10.0.*', FindRec) then
  begin
    try
      repeat
        Version := FindRec.Name;
        if (Copy(Version, 1, 5) = '10.0.') and
           (StrToIntDef(Copy(Version, 6, Length(Version)), -1) >= 0) and
           FileExists(Location + Version + '\' + Framework + '.deps.json') then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function HasDesktopRuntime: Boolean;
var
  Location: String;
begin
  if not RegQueryStringValue(HKLM64, '{#DesktopRoot}', 'InstallLocation', Location) or (Location = '') then
    Location := ExpandConstant('{pf64}\dotnet');
  Result := FileExists(AddBackslash(Location) + 'dotnet.exe') and
    HasFramework(Location, 'Microsoft.NETCore.App') and HasFramework(Location, 'Microsoft.WindowsDesktop.App');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Progress: TDownloadWizardPage;
  RuntimePath: String;
  ResultCode: Integer;
begin
  Result := '';
  if HasDesktopRuntime then
    Exit;
  Progress := CreateDownloadPage(CustomMessage('InstallRuntime'), '', nil);
  Progress.Clear;
  Progress.Add('{#RuntimeUrl}', '{#RuntimeName}', '{#RuntimeSha256}');
  Progress.Show;
  try
    try
      Progress.Download;
      RuntimePath := ExpandConstant('{tmp}\{#RuntimeName}');
      if not ShellExec('runas', RuntimePath, '/install /quiet /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode) then
        Result := CustomMessage('RuntimeError') + ' ' + SysErrorMessage(ResultCode)
      else if (ResultCode = 3010) or (ResultCode = 1641) then
      begin
        NeedsRestart := True;
        Result := CustomMessage('RestartRuntime');
      end
      else if (ResultCode <> 0) or not HasDesktopRuntime then
        Result := CustomMessage('RuntimeError') + ' (' + IntToStr(ResultCode) + ')';
    except
      Result := CustomMessage('RuntimeError') + ' ' + GetExceptionMessage;
    end;
  finally
    Progress.Hide;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current, OwnCommand: String;
begin
  if (CurUninstallStep = usPostUninstall) and RegQueryStringValue(HKCU, StartupKey, 'Hub', Current) then
  begin
    OwnCommand := '"' + ExpandConstant('{app}\TraySuite.Hub.exe') + '" --start';
    if CompareText(Current, OwnCommand) = 0 then RegDeleteValue(HKCU, StartupKey, 'Hub');
  end;
end;
