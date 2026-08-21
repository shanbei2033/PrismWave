; PrismWave 单 exe 安装包脚本（Inno Setup 6）
; 用 tools\build_installer.ps1 编译（自动传入版本号），
; 或手动：ISCC.exe /DMyAppVersion=1.0.7 setup.iss

#ifndef MyAppVersion
#define MyAppVersion "1.0.7"
#endif

#define MyAppName "PrismWave"
#define MyAppPublisher "PrismWave"
#define MyAppExeName "PrismWave.WinUI.exe"
#define DotNetDownloadUrl "https://dotnet.microsoft.com/download/dotnet/10.0"
#define WinAppRuntimeUrl "https://aka.ms/windowsappsdk/2.2/latest/windowsappruntimeinstall-x64.exe"

[Setup]
AppId={{8F1D9A42-3C5B-4E7A-9D2F-6A8B0C1D2E3F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts
OutputBaseFilename=PrismWave-Setup-{#MyAppVersion}
SetupIconFile=..\src\PrismWave.WinUI\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=admin

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\installer-payload\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  NeedRuntimeDownload: Boolean;
  DownloadPage: TDownloadWizardPage;

// 检测指定 dotnet 目录下是否存在 >= 10.0 的 Microsoft.WindowsDesktop.App 运行时
function HasDotNetDesktop10(basePath: string): Boolean;
var
  findRec: TFindRec;
  dotPos: Integer;
  major: Integer;
begin
  Result := False;
  if not DirExists(basePath) then
    exit;

  if FindFirst(AddBackslash(basePath) + '*', findRec) then
  try
    repeat
      if (findRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
      begin
        dotPos := Pos('.', findRec.Name);
        if dotPos > 1 then
        begin
          major := StrToIntDef(Copy(findRec.Name, 1, dotPos - 1), 0);
          if major >= 10 then
          begin
            Result := True;
            exit;
          end;
        end;
      end;
    until not FindNext(findRec);
  finally
    FindClose(findRec);
  end;
end;

function InitializeSetup(): Boolean;
var
  runtimePath: string;
  opened: Boolean;
  errorCode: Integer;
  findRec: TFindRec;
begin
  // 依次探测 x64 与 x86 安装位置下的 .NET Desktop Runtime
  runtimePath := ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App');
  Result := HasDotNetDesktop10(runtimePath);

  if not Result then
  begin
    runtimePath := ExpandConstant('{autopf32}\dotnet\shared\Microsoft.WindowsDesktop.App');
    Result := HasDotNetDesktop10(runtimePath);
  end;

  if not Result then
  begin
    if MsgBox(
        '未检测到 .NET 10 Desktop Runtime（x64）。' + #13#10 + #13#10 +
        'PrismWave 需要先安装它才能运行。' + #13#10 + #13#10 +
        '是否现在打开官方下载页面？',
        mbConfirmation, MB_YESNO) = IDYES then
    begin
      opened := ShellExec('open', '{#DotNetDownloadUrl}', '', '', SW_SHOWNORMAL, ewNoWait, errorCode);
      if not opened then
        MsgBox('无法打开浏览器，请手动访问：' + #13#10 + '{#DotNetDownloadUrl}', mbInformation, MB_OK);
    end;
    Result := False;
    exit;
  end;

  // 检测系统是否已安装 Windows App Runtime 2.x（WinAppSDK framework-dependent 部署依赖）
  NeedRuntimeDownload := True;
  try
    if FindFirst(ExpandConstant('{commonpf}\WindowsApps\Microsoft.WindowsAppRuntime.2_*'), findRec) then
    try
      repeat
        NeedRuntimeDownload := False;
      until not FindNext(findRec) or not NeedRuntimeDownload;
    finally
      FindClose(findRec);
    end;
  except
    // WindowsApps 目录枚举失败时保守地尝试下载安装
  end;

  if NeedRuntimeDownload then
  begin
    if MsgBox(
        '未检测到 Windows App Runtime 2.2。' + #13#10 + #13#10 +
        '安装向导将在下一步自动下载并安装它（约 40 MB，需要联网）。' + #13#10 + #13#10 +
        '是否继续？',
        mbConfirmation, MB_YESNO) <> IDYES then
      Result := False;
  end;
end;

procedure InitializeWizard();
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and NeedRuntimeDownload then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('{#WinAppRuntimeUrl}', 'WindowsAppRuntimeInstall.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox('下载 Windows App Runtime 失败，请检查网络后重试，' + #13#10 +
          '或手动下载安装：' + #13#10 + '{#WinAppRuntimeUrl}', mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  errorCode: Integer;
begin
  if (CurStep = ssInstall) and NeedRuntimeDownload and FileExists(ExpandConstant('{tmp}\WindowsAppRuntimeInstall.exe')) then
  begin
    // 静默安装 Windows App Runtime（/q），完成后再复制应用文件
    Exec(ExpandConstant('{tmp}\WindowsAppRuntimeInstall.exe'), '/q', '', SW_SHOW, ewWaitUntilTerminated, errorCode);
  end;
end;
