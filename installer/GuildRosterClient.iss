#ifndef MyAppVersion
  #define MyAppVersion "0.1.0-beta.1"
#endif
#ifndef MyAppFileVersion
  #define MyAppFileVersion "0.1.0.1"
#endif

#define MyAppName "FrostLabs Guild Roster Client"
#define MyAppExeName "GuildRosterClient.exe"
#define MyAppPublisher "Frostcanvas"
#define MyAppURL "https://github.com/Frostcanvas/Guild-Roster-Client"

[Setup]
AppId={{B08D30E1-94B9-4C47-94DD-8326EC0A7921}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\FrostLabs Guild Roster Client
DefaultGroupName=FrostLabs
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=..\publish\installer
OutputBaseFilename=GuildRosterClient-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
SetupLogging=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Uninstallable=yes
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppFileVersion}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppFileVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} {#MyAppVersion} Windows installer

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\FrostLabs Guild Roster Client"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\FrostLabs Guild Roster Client"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{autodesktop}\FrostLabs Guild Roster Client"; Filename: "{app}\{#MyAppExeName}"; Check: DesktopShortcutExists

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch FrostLabs Guild Roster Client"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait runasoriginaluser; Check: IsSilentInstall

[Code]
function DesktopShortcutExists: Boolean;
begin
  Result := FileExists(ExpandConstant('{autodesktop}\FrostLabs Guild Roster Client.lnk'));
end;

function IsSilentInstall: Boolean;
begin
  Result := WizardSilent;
end;
