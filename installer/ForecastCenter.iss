#define MyAppName "Forecast Center"
#define MyAppVersion "0.8.0"
#define MyAppPublisher "Forecast Center contributors"
#define MyAppExeName "ForecastCenter.Public.exe"

[Setup]
AppId={{E37B02AB-5198-48E6-9C55-ECF2295E8C43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Forecast Center Public
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release\installer
OutputBaseFilename=ForecastCenter-Setup-{#MyAppVersion}-x64
SetupIconFile=..\src\ForecastCenter\Assets\forecast-center.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\release\Forecast Center Public\*"; DestDir: "{app}"; Excludes: "ForecastCenter.Public.exe.WebView2\*,*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dependencies\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[InstallDelete]
Type: filesandordirs; Name: "{app}\ForecastCenter.Public.exe.WebView2"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\ForecastCenter.Public.exe.WebView2"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: WebView2RuntimeNeeded
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Forecast Center"; Flags: nowait postinstall skipifsilent

[Code]
var
  UpgradePage: TOutputMsgWizardPage;
  InstalledVersion: String;

function GetInstalledVersion(): String;
var
  UninstallKey: String;
  InstalledExe: String;
begin
  UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
    '{E37B02AB-5198-48E6-9C55-ECF2295E8C43}_is1';

  if RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Result) then
    Exit;

  { Covers portable/manual installs in the standard install folder, as well as
    older installers whose uninstall entry is no longer present. }
  InstalledExe := ExpandConstant('{localappdata}\Programs\Forecast Center Public\{#MyAppExeName}');
  if not GetVersionNumbersString(InstalledExe, Result) then
    Result := '';
end;

function HasWebView2Version(RootKey: Integer; SubKey: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(RootKey, SubKey, 'pv', Version) and
    (Version <> '') and (Version <> '0.0.0.0');
end;

function WebView2RuntimeNeeded(): Boolean;
var
  ClientKey: String;
begin
  ClientKey := 'Software\Microsoft\EdgeUpdate\Clients\' +
    '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := not (
    HasWebView2Version(HKCU, ClientKey) or
    HasWebView2Version(HKLM, 'Software\WOW6432Node\Microsoft\EdgeUpdate\Clients\' +
      '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'));
end;

procedure InitializeWizard();
begin
  InstalledVersion := GetInstalledVersion();
  if InstalledVersion <> '' then
  begin
    UpgradePage := CreateOutputMsgPage(
      wpWelcome,
      'Upgrade Forecast Center',
      'Setup found an existing installation.',
      'Forecast Center ' + InstalledVersion + ' is currently installed.' + #13#10 + #13#10 +
      'Setup will upgrade it to Forecast Center {#MyAppVersion}.' + #13#10 + #13#10 +
      'Your saved locations, preferences, and cached weather data will be kept.');
  end;
end;
