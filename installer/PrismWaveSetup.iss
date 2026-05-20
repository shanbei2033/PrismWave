#define MyAppName "PrismWave"
#define MyAppVersion "R401_Pre"
#define MyAppPublisher "shanbei2033"
#define MyAppURL "https://github.com/shanbei2033/PrismWave"
#define MyAppExeName "PrismWave.exe"
#define MySourceDir "..\app\build\windows\x64\runner\Release"
#define MyLicenseFile "..\LICENSE"
#define MyIconFile "..\app\windows\runner\resources\app_icon.ico"

[Setup]
AppId={{C3E57196-9792-4D0C-9D5A-BF97222C843A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={code:GetDefaultInstallDir}
DefaultGroupName={#MyAppName}
LicenseFile={#MyLicenseFile}
SetupIconFile={#MyIconFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
DisableDirPage=no
UsePreviousAppDir=no
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=PrismWave-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\prismwave_demo.exe"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "prismwave_demo.exe"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function GetDefaultInstallDir(Param: string): string;
begin
  if DirExists('D:\') then
    Result := 'D:\{#MyAppName}'
  else
    Result := 'C:\{#MyAppName}';
end;
