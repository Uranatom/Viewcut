#ifndef AppVersion
  #error Define AppVersion when compiling.
#endif
#ifndef Payload
  #error Define Payload when compiling.
#endif
#ifndef SettingsDirectory
  #define SettingsDirectory "{localappdata}\Viewcut"
#endif

#define FileVerbRoot "Software\Classes\SystemFileAssociations\"
#define FolderVerb "Software\Classes\Directory\shell\Viewcut.Setup.CullFolder"
#define Mp4Verb FileVerbRoot + ".mp4\shell\Viewcut.Setup.CullFile"
#define MovVerb FileVerbRoot + ".mov\shell\Viewcut.Setup.CullFile"
#define InstallId "{D81B38D5-2C39-414B-91F0-8EA6C13FD2BF}"

[Setup]
AppId={{D81B38D5-2C39-414B-91F0-8EA6C13FD2BF}
AppName=Viewcut
AppVersion={#AppVersion}
AppPublisher=Viewcut
DefaultDirName={localappdata}\Programs\Viewcut
DefaultGroupName=Viewcut
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
SetupIconFile=Viewcut.ico
UninstallDisplayIcon={app}\Viewcut.exe
WizardStyle=modern dynamic
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AppMutex=Local\Viewcut.Running
ChangesAssociations=yes
OutputBaseFilename=Viewcut-{#AppVersion}-Setup-x64

[Languages]
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.DesktopIcon=创建桌面快捷方式
en.DesktopIcon=Create a desktop shortcut
zhcn.ExplorerMenu=在资源管理器中添加 Viewcut 粗筛右键菜单
en.ExplorerMenu=Add Viewcut commands to the File Explorer context menu
zhcn.OpenInViewcut=用 Viewcut 粗筛
en.OpenInViewcut=Review with Viewcut
zhcn.OpenFolder=粗筛此文件夹
en.OpenFolder=Review this folder in Viewcut
zhcn.RemoveViewcut=卸载 Viewcut
en.RemoveViewcut=Uninstall Viewcut
zhcn.StartViewcut=启动 Viewcut
en.StartViewcut=Launch Viewcut
zhcn.PurgePrompt=同时清除 Viewcut 保存的片段标记、导出队列和设置吗？已导出的视频和源视频会保留。
en.PurgePrompt=Also remove Viewcut's saved clip markers, export queue and settings? Source and exported videos will be kept.
zhcn.ExistingInstall=Viewcut 已安装在 %1。若要更改安装位置，请先卸载已有版本。
en.ExistingInstall=Viewcut is already installed in %1. Uninstall it before choosing a new location.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked
Name: "explorermenu"; Description: "{cm:ExplorerMenu}"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "ThirdParty.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Viewcut"; Filename: "{app}\Viewcut.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Viewcut.exe"
Name: "{group}\{cm:RemoveViewcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Viewcut"; Filename: "{app}\Viewcut.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Viewcut.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "{#Mp4Verb}"; ValueType: string; ValueData: "{cm:OpenInViewcut}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "{#Mp4Verb}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Viewcut.exe"",0"; Tasks: explorermenu
Root: HKCU; Subkey: "{#Mp4Verb}\command"; ValueType: string; ValueData: """{app}\Viewcut.exe"" --file ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "{#MovVerb}"; ValueType: string; ValueData: "{cm:OpenInViewcut}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "{#MovVerb}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Viewcut.exe"",0"; Tasks: explorermenu
Root: HKCU; Subkey: "{#MovVerb}\command"; ValueType: string; ValueData: """{app}\Viewcut.exe"" --file ""%1"""; Tasks: explorermenu
Root: HKCU; Subkey: "{#FolderVerb}"; ValueType: string; ValueData: "{cm:OpenFolder}"; Flags: uninsdeletekey; Tasks: explorermenu
Root: HKCU; Subkey: "{#FolderVerb}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\Viewcut.exe"",0"; Tasks: explorermenu
Root: HKCU; Subkey: "{#FolderVerb}\command"; ValueType: string; ValueData: """{app}\Viewcut.exe"" --folder ""%1"""; Tasks: explorermenu

[Run]
Filename: "{app}\Viewcut.exe"; Description: "{cm:StartViewcut}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PurgeSettings: Boolean;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousPath: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#InstallId}_is1',
    'InstallLocation', PreviousPath) and
    (CompareText(RemoveBackslashUnlessRoot(PreviousPath), RemoveBackslashUnlessRoot(WizardDirValue)) <> 0) then
    Result := FmtMessage(CustomMessage('ExistingInstall'), [PreviousPath]);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  I: Integer;
  SettingsPath: String;
begin
  if (CurUninstallStep = usPostUninstall) and PurgeSettings then begin
    SettingsPath := ExpandConstant('{#SettingsDirectory}');
    if not DeleteFile(SettingsPath + '\session.json') then
      Log('Viewcut session.json was absent or could not be deleted.');
    if not DeleteFile(SettingsPath + '\session.json.tmp') then
      Log('Viewcut session.json.tmp was absent or could not be deleted.');
    RemoveDir(SettingsPath);
    Exit;
  end;

  if CurUninstallStep <> usUninstall then Exit;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/PURGEDATA') = 0 then PurgeSettings := True;

  if not PurgeSettings and not UninstallSilent and
    (FileExists(ExpandConstant('{#SettingsDirectory}\session.json')) or
     FileExists(ExpandConstant('{#SettingsDirectory}\session.json.tmp'))) then
    PurgeSettings := MsgBox(CustomMessage('PurgePrompt'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;
