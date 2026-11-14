#ifndef AppVersion
  #define AppVersion "0.9.0-beta"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\src\TempAutoCleaner\bin\Release\net48"
#endif
#ifndef PackageDir
  #define PackageDir "..\dist"
#endif
#ifndef InstallerId
  #define InstallerId "TempAutoCleaner"
#endif

[Setup]
AppId={#InstallerId}
AppName=Temp Auto Cleaner
AppVersion={#AppVersion}
AppPublisher=Temp Auto Cleaner contributors
DefaultDirName={localappdata}\Programs\TempAutoCleaner
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19045
CloseApplications=no
RestartApplications=no
DisableWelcomePage=no
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage
OutputDir={#PackageDir}
OutputBaseFilename=TempAutoCleaner-Setup-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\TempAutoCleaner.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,compiler:Languages\ChineseSimplified.isl"

[CustomMessages]
english.InstallNote=Setup installs for your Windows account and registers one task. Starting at your next sign-in, cleanup runs after 30 seconds. It removes eligible files from your local Temp folder only when both creation and last-write times are older than 24 hours. Setup does not run a cleanup.
chinesesimplified.InstallNote=安装程序仅为当前 Windows 用户安装，并注册一个计划任务。从下次登录开始，清理将在 30 秒后运行。仅清理当前用户 Temp 中创建时间和最后修改时间均超过 24 小时的合格文件。安装过程中不会执行清理。
english.FrameworkRequired=Install .NET Framework 4.8 or later before running Setup.
chinesesimplified.FrameworkRequired=请先安装 .NET Framework 4.8 或更新版本。
english.Busy=Temp Auto Cleaner or another setup is active. Wait for it to finish, then retry.
chinesesimplified.Busy=清理程序或另一个安装程序正在运行，请等待完成后重试。
english.TaskError=Windows Task Scheduler operation failed. Setup cannot continue.
chinesesimplified.TaskError=Windows 计划任务操作失败，无法继续。

[Messages]
english.ConfirmUninstall=Remove %1 and its installed components? The scheduled task will be removed and cleanup logs will be kept.
chinesesimplified.ConfirmUninstall=是否移除 %1 及其已安装组件？卸载会移除计划任务，并保留清理日志。

[Files]
Source: "{#PayloadDir}\TempAutoCleaner.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.zh-CN.md"; DestDir: "{app}"; Flags: ignoreversion

[Code]
#include "LoginTask.iss"

function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('FrameworkRequired'), mbError, MB_OK, IDOK);
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := CustomMessage('InstallNote');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  try
    if not AcquireCleanupMutex then
      Result := CustomMessage('Busy');
  except
    Log(GetExceptionMessage);
    Result := CustomMessage('TaskError');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { ssInstall errors abort Setup before Inno replaces any installed files. }
  if CurStep = ssInstall then
    RegisterLoginTask
  else if CurStep = ssPostInstall then
    TaskCommitted := True;
end;

procedure DeinitializeSetup;
begin
  try
    if not TaskCommitted then
      RestoreLoginTask;
  finally
    ReleaseCleanupMutex;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then begin
    if not AcquireCleanupMutex then
      RaiseException(CustomMessage('Busy'));
    RemoveLoginTask;
  end;
end;

procedure DeinitializeUninstall;
begin
  ReleaseCleanupMutex;
end;
