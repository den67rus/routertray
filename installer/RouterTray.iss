#define AppName "RouterTray"
#define AppExeName "RouterTray.exe"
#define AppId "{{6FEC1E8E-0DA0-4E5B-9A4B-0A3F5CF6E6A1}"

#define AppArch GetEnv("APP_ARCH")
#if AppArch == ""
  #define AppArch "win-x64"
#endif

#define SourceDir GetEnv("PUBLISH_DIR")
#if SourceDir == ""
  #define SourceDir "..\\bin\\Release\\net8.0-windows\\{#AppArch}\\publish"
#endif

#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion GetVersionNumbersString("{#SourceDir}\\{#AppExeName}")
#endif

#if AppArch == "win-x86"
  #define ArchitecturesAllowed "x86"
  #define ArchitecturesInstallIn64BitMode ""
#elif AppArch == "win-x64"
  #define ArchitecturesAllowed "x64os"
  #define ArchitecturesInstallIn64BitMode "x64os"
#elif AppArch == "win-arm64"
  #define ArchitecturesAllowed "arm64"
  #define ArchitecturesInstallIn64BitMode "arm64"
#else
  #error Unknown APP_ARCH: {#AppArch}
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\artifacts
OutputBaseFilename={#AppName}-setup-{#AppArch}
SetupIconFile=..\favicon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed={#ArchitecturesAllowed}
#if ArchitecturesInstallIn64BitMode != ""
ArchitecturesInstallIn64BitMode={#ArchitecturesInstallIn64BitMode}
#endif

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "appsettings.json"
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure RemoveAutoStartEntry;
var
  CurrentValue: string;
  ExpectedValue: string;
begin
  if not RegQueryStringValue(
    HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Run',
    '{#AppName}',
    CurrentValue) then
  begin
    Exit;
  end;

  ExpectedValue := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
  if CompareText(CurrentValue, ExpectedValue) = 0 then
  begin
    RegDeleteValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#AppName}');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveAutoStartEntry;
  end;
end;
