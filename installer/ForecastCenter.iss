#define MyAppName "Forecast Center (Public Preview)"
#define MyAppVersion "0.8.0"
#define MyAppPublisher "Forecast Center contributors"
#define MyAppExeName "ForecastCenter.Public.exe"

[Setup]
AppId={{E37B02AB-5198-48E6-9C55-ECF2295E8C43}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Forecast Center Public
DefaultGroupName=Forecast Center (Public Preview)
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release\installer
OutputBaseFilename=ForecastCenter-Public-Setup-{#MyAppVersion}-x64
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

[InstallDelete]
Type: filesandordirs; Name: "{app}\ForecastCenter.Public.exe.WebView2"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\ForecastCenter.Public.exe.WebView2"

[Icons]
Name: "{group}\Forecast Center (Public Preview)"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Forecast Center (Public Preview)"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
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
