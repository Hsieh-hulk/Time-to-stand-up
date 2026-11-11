; Time-to-stand-up installer (Inno Setup 6).
; Build with build/publish.ps1, which publishes the app and passes MyAppVersion / PublishDir.
; Manual build:  iscc /DMyAppVersion=1.0.2 /DPublishDir=..\publish installer\Time-to-stand-up.iss

#ifndef MyAppVersion
  #define MyAppVersion "1.0.2"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define MyAppName "Time-to-stand-up"
#define MyAppExeName "Time-to-stand-up.exe"
#define MyAppPublisher "hsieh_hulk"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
; Must match AutoStartManager.ValueName in src/Time-to-stand-up.App/Services/AutoStartManager.cs.
#define RunValueName "Time-to-stand-up"

; Traditional Chinese wizard text is used when Languages\ChineseTraditional.isl exists
; next to this script (Inno Setup unofficial translation); otherwise English only.
#define HasZhTw FileExists(AddBackslash(SourcePath) + "Languages\ChineseTraditional.isl")

[Setup]
AppId={{6659A1CD-49BC-4BD3-8E81-16BA48965602}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
; Per-user install: no admin prompt, goes to %LocalAppData%\Programs\Time-to-stand-up.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Time-to-stand-up
DisableDirPage=no
; Always start from DefaultDirName instead of the folder remembered from an earlier install.
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableReadyPage=no
OutputDir=..\dist
OutputBaseFilename=Time-to-stand-up-Setup-{#MyAppVersion}
SetupIconFile=..\src\Time-to-stand-up.App\Assets\Time-to-stand-up.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Always ask: many zh-TW users run an English Windows UI, so auto-detection picked English.
; Preselect by regional format (locale) rather than the display language.
ShowLanguageDialog=yes
LanguageDetectionMethod=locale

[Languages]
#if HasZhTw
Name: "zh_tw"; MessagesFile: "Languages\ChineseTraditional.isl"
#endif
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
#if HasZhTw
zh_tw.AutoStart=開機時自動啟動 Time-to-stand-up(建議)
zh_tw.StartupGroup=啟動選項:
zh_tw.DeleteSettings=要一併刪除 Time-to-stand-up 的設定檔嗎?%n%n(位於 %1)%n%n選「否」會保留設定,之後重新安裝會沿用。
#endif
en.AutoStart=Start Time-to-stand-up automatically when I sign in to Windows (recommended)
en.StartupGroup=Startup:
en.DeleteSettings=Also delete Time-to-stand-up settings?%n%n(Located in %1)%n%nChoose "No" to keep them for a future reinstall.

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"; GroupDescription: "{cm:StartupGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\src\Time-to-stand-up.App\Assets\Fonts\LICENSE.txt"; DestDir: "{app}"; DestName: "FONT-LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Same HKCU Run value the in-app "開機時自動啟動" checkbox reads and writes.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "{#RunValueName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "{#RunValueName}"; Flags: deletevalue; Tasks: not autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,Time-to-stand-up}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "StopTime-to-stand-up"

[Code]
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  { The tray app has no "close" request API; end it so its exe can be replaced. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  if ResultCode = 0 then
    Sleep(800);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  SettingsDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { The in-app checkbox may have added the Run value even if the installer task was unchecked. }
    RegDeleteValue(HKEY_CURRENT_USER, '{#RunKey}', '{#RunValueName}');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    SettingsDir := ExpandConstant('{userappdata}\Time-to-stand-up');
    if DirExists(SettingsDir) and not UninstallSilent() then
    begin
      if MsgBox(FmtMessage(CustomMessage('DeleteSettings'), [SettingsDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(SettingsDir, True, True, True);
    end;
  end;
end;
