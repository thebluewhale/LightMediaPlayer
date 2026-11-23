#define AppName "Light Media Player"
; Separate the machine-wide handler from older per-user installations.
#define RegisteredName "Light Media Player (x64)"
#define MediaProgId "LightMediaPlayer.x64.Media"
#define OpenClassId "{{35BE37DB-35E8-4327-95F2-104965F7A571}"
#ifndef AppVersion
#define AppVersion "0.3.10"
#endif
#ifndef PublishDir
#define PublishDir "..\artifacts\publish\v" + AppVersion
#endif
#ifndef ReleaseDir
#define ReleaseDir "..\artifacts\release\v" + AppVersion
#endif

[Setup]
AppId={{E2B968A7-5672-4636-B35F-9787AC7D518A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Light Media Player
DefaultDirName={commonpf32}\LightMediaPlayer
UsePreviousAppDir=no
DefaultGroupName={#AppName}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ReleaseDir}
OutputBaseFilename=LightMediaPlayer-Setup
SetupIconFile=..\src\LightMediaPlayer\Assets\player.ico
UninstallDisplayName={#RegisteredName}
UninstallDisplayIcon={app}\LightMediaPlayer.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
MinVersion=10.0

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#PublishDir}\LightMediaPlayer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\LightMediaPlayer.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\LightMediaPlayer.exe"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "Software\Classes\CLSID\{#OpenClassId}"; ValueType: string; ValueData: "Light Media Player Open"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\CLSID\{#OpenClassId}\LocalServer32"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"" --shell-open"
Root: HKLM; Subkey: "Software\Classes\{#MediaProgId}"; ValueType: string; ValueData: "Light Media Player 미디어"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#MediaProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"",0"
Root: HKLM; Subkey: "Software\Classes\{#MediaProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\{#MediaProgId}\shell\open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKLM; Subkey: "Software\Classes\{#MediaProgId}\shell\open\DropTarget"; ValueType: string; ValueName: "CLSID"; ValueData: "{#OpenClassId}"
Root: HKLM; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe\shell\open\command"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"" ""%1"""
Root: HKLM; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe\shell\open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"
Root: HKLM; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe\shell\open\DropTarget"; ValueType: string; ValueName: "CLSID"; ValueData: "{#OpenClassId}"
Root: HKLM; Subkey: "Software\LightMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#RegisteredName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\LightMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\LightMediaPlayer.exe"",0"
Root: HKLM; Subkey: "Software\LightMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "가벼운 동영상 및 음악 플레이어"
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#RegisteredName}"; ValueData: "Software\LightMediaPlayer\Capabilities"; Flags: uninsdeletevalue
#include "associations.iss"

[Run]
Filename: "{app}\LightMediaPlayer.exe"; Description: "Light Media Player 실행"; Flags: nowait postinstall skipifsilent unchecked
