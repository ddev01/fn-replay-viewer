#ifndef MyAppVersion
  #define MyAppVersion "1.0.7"
#endif

#define MyAppName "Replay Viewer"
#define MyAppPublisher "Replay Viewer"
#define MyAppURL "https://github.com/ddev01/fn-replay-viewer"
#define MyAppExeName "ReplayViewer.exe"

[Setup]
AppId={{8F3C2A1B-9D4E-4B6A-A7C1-2E8F0D5B91C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\ReplayViewer
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=ReplayViewer-Setup-{#MyAppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ReplayViewer.SingleInstance
MinVersion=10.0.17763
UsePreviousAppDir=yes
ChangesAssociations=no

[Code]
procedure CleanLegacyBundle;
var
  AppDir: String;
  FindRec: TFindRec;
begin
  AppDir := ExpandConstant('{app}');
  if not DirExists(AppDir) then
    Exit;
  if FindFirst(AppDir + '\*.dll', FindRec) then
  try
    repeat
      DeleteFile(AppDir + '\' + FindRec.Name);
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
  DeleteFile(AppDir + '\createdump.exe');
  DeleteFile(AppDir + '\ReplayViewer.deps.json');
  DeleteFile(AppDir + '\ReplayViewer.runtimeconfig.json');
  DeleteFile(AppDir + '\ReplayViewer.dll');
  DeleteFile(AppDir + '\update.zip');
end;

procedure RemoveLegacyLogonTask;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "Replay Viewer" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CleanLegacyBundle;
  RemoveLegacyLogonTask;
  // [Registry] only writes the value when the task is selected, so clear a previous choice.
  if not WizardIsTaskSelected('autostart') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'ReplayViewer');
  Result := '';
end;

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "autostart"; Description: "Start Replay Viewer when I sign in"; GroupDescription: "Startup:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ReplayViewer"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--install"; StatusMsg: "Creating the Start Menu shortcut..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Replay Viewer"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; RunOnceId: "RemoveReplayViewerTask"; Flags: runhidden waituntilterminated
