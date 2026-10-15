; Script generated for LocalSocialArchive Inno Setup Installer
; Modern, high-performance installer for Local Social Archive (WPF / .NET 8)

#define MyAppName "Local Social Archive"
#define MyAppVersion "2.4.1"
#define MyAppPublisher "Local Social Archive"
#define MyAppURL "https://github.com/naveenbatta96-creator/LocalSocialArchive"
#define MyAppExeName "LocalSocialArchive.exe"

[Setup]
; Unique application GUID
AppId={{9F32E52F-774A-480D-B0B2-16719E87340D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\LocalSocialArchive
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output folder & filename
OutputDir=dist\installer
OutputBaseFilename=LocalSocialArchive-Setup
SetupIconFile=app_icon.ico
UninstallDisplayIcon={app}\app_icon.ico
UninstallDisplayName={#MyAppName} v{#MyAppVersion}

; Compression & Performance
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

; Platform & Architecture
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; Permissions: allows standard per-user install or admin elevation
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Close running instance automatically before upgrading
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

; Version Info for Setup Executable
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoCopyright=Copyright (C) 2026 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Distribute all release files from the single-file / light publish folder (excludes debug PDBs)
Source: "dist\LocalSocialArchive-Light\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
; Application icon for shortcuts & uninstaller
Source: "app_icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu Shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; WorkingDir: "{app}"
; Start Menu Uninstall Shortcut
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; IconFilename: "{app}\app_icon.ico"
; Desktop Shortcut (Optional Task)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app_icon.ico"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Option to launch app after installation completes
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\app_icon.ico"
Type: dirifempty; Name: "{app}"

[Code]
// Application is packaged as Self-Contained with native .NET 8 runtime included.
// No external runtime prerequisites or downloads are required on the target computer.
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
