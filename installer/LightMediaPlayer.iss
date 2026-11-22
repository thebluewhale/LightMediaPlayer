#define AppName "Light Media Player"
#ifndef AppVersion
#define AppVersion "0.3.6"
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
DefaultDirName={localappdata}\Programs\LightMediaPlayer
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#ReleaseDir}
OutputBaseFilename=LightMediaPlayer-Setup
SetupIconFile=..\src\LightMediaPlayer\Assets\player.ico
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
Name: "fileassoc"; Description: "미디어 파일의 연결 프로그램 후보로 등록 (기본 앱은 Windows 설정에서 선택)"

[Files]
Source: "{#PublishDir}\LightMediaPlayer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\LightMediaPlayer.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\LightMediaPlayer.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\LightMediaPlayer.Media"; ValueType: string; ValueData: "Light Media Player 미디어"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\LightMediaPlayer.Media\DefaultIcon"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"",0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\LightMediaPlayer.Media\shell\open\command"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"" ""%1"""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\Applications\LightMediaPlayer.exe\shell\open\command"; ValueType: string; ValueData: """{app}\LightMediaPlayer.exe"" ""%1"""; Tasks: fileassoc
Root: HKCU; Subkey: "Software\LightMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\LightMediaPlayer\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "가벼운 동영상 및 음악 플레이어"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Light Media Player"; ValueData: "Software\LightMediaPlayer\Capabilities"; Flags: uninsdeletevalue; Tasks: fileassoc
#include "associations.iss"

[Run]
Filename: "{app}\LightMediaPlayer.exe"; Description: "Light Media Player 실행"; Flags: nowait postinstall skipifsilent unchecked
