# Light Media Player

Windows x64용 동영상·음악 플레이어 POC. C# / WPF / .NET 10 / LibVLCSharp / LibVLC로 구현했습니다.

## 실행 파일

[GitHub Releases](https://github.com/thebluewhale/LightMediaPlayer/releases)에서 받습니다.

- `LightMediaPlayer-Portable-0.1.0.exe`: 설치 없이 실행하는 단일 EXE. .NET이나 VLC를 따로 설치할 필요가 없습니다.
- `LightMediaPlayer-Setup.exe`: 현재 사용자용 설치 프로그램. 관리자 권한 없이 설치하고 제거할 수 있습니다.
- `SHA256SUMS.txt`: 배포 파일의 SHA-256 체크섬.

포터블은 실행 시 포함된 네이티브 라이브러리를 사용자 캐시에 추출합니다. 실행 파일 하나만 이동해 사용할 수 있지만, 실행 후 캐시·설정·로그가 사용자 프로필에 남습니다. 파일 추출이 전혀 없는 EXE나 설정까지 USB에 보관하는 방식은 아닙니다.

설치본은 파일의 **연결 프로그램 후보**로 등록할 수 있습니다. Windows 설정 → 앱 → 기본 앱에서 Light Media Player를 선택하거나 탐색기의 **연결 프로그램**으로 지정하면 더블클릭으로 실행됩니다. 기존 기본 앱을 강제로 변경하지 않습니다.

## 구현된 기능

- 상단 제목 표시줄, 최소화·최대화·종료, 창 크기 조절.
- 영상 표시, 원본 화면 비율 유지, 오디오 전용 파일 표시.
- 재생·일시정지·정지와 상태에 따른 재생 버튼 전환. 정지 후 재생은 처음부터 시작.
- 진행 바 클릭·드래그, 시간 표시, 키보드 구간 탐색.
- 음량·음소거, 전체화면과 이전 창 크기 복원.
- 파일 열기, 드래그 앤 드롭, 명령줄 파일 경로 처리, 한글·공백 경로.
- **각 실행은 독립 프로세스**. 다른 파일을 탐색기에서 열어도 기존 영상은 계속 재생됩니다. 실행 중인 창에서 파일 열기를 사용하면 해당 창의 파일을 교체하며, 여러 파일을 선택하면 나머지는 새 프로세스로 실행합니다.
- 이전·다음 파일: 같은 폴더의 미디어를 자연 정렬 순서로 이동. 폴더 끝에서는 버튼 비활성화. 자동 다음 재생·순환은 하지 않습니다.
- 동일 이름의 자막 자동 로드, 외부 자막 열기·드롭, 자막 켜기·끄기.
- UTF-8, UTF-16, CP949 텍스트 자막 처리. 원본 자막 파일을 수정하지 않고 변환본을 캐시합니다.
- 오디오·자막 트랙 선택: 영상 우클릭 또는 하단 `…` 메뉴.
- 로딩·버퍼링·일시정지·종료·파일 오류 표시와 프로세스별 로그.
- 음량·음소거·창 크기·위치 기억. 동시에 여러 창을 닫으면 마지막으로 저장한 창의 설정을 사용합니다.
- 하드웨어 디코딩 우선, 재생 오류 시 소프트웨어 디코딩 재시도. 메뉴에서 수동으로 소프트웨어 디코딩을 선택할 수도 있습니다.
- 기본 로컬 M3U/PLS/ASX/XSPF 목록 읽기. 재생목록 내부의 로컬 미디어를 이전·다음으로 이동합니다.

**설정 버튼과 재생목록 확장 버튼은 배치만 했습니다.** 누르더라도 설정 창이나 재생목록 패널이 열리지 않습니다.

## 단축키

| 키 | 기능 |
|---|---|
| Space | 재생 / 일시정지 |
| S | 정지 |
| ← / → | 5초 뒤 / 앞 |
| Shift + ← / → | 30초 뒤 / 앞 |
| ↑ / ↓ | 음량 5% 조절 |
| M | 음소거 |
| Alt+Enter / F11 / 영상 더블클릭 | 전체화면 전환 |
| Esc | 전체화면 해제 |
| Ctrl+O | 파일 열기 |
| Ctrl+Shift+O | 자막 열기 |
| V | 자막 표시 / 숨김 |
| Page Up / Page Down | 이전 / 다음 파일 |
| Home | 처음으로 이동 |

## 파일 형식과 제한

사용자가 요청한 확장자 목록은 파일 선택 필터와 설치 프로그램의 연결 후보에 포함됩니다. **확장자 등록은 실제 디코딩 지원 보증이 아닙니다.** 실제 지원은 LibVLC 3.0.24의 컨테이너·코덱·자막 지원에 따릅니다. DRM, 전용 형식, 디스크의 부속 파일은 별도 지원이 필요할 수 있습니다.

검증된 조합과 미검증 영역은 [형식 지원 범위](docs/formats.md), 검증 내역은 [테스트 결과](docs/testing.md)를 참고하세요. 사용자 화면을 직접 캡처한 시각 검증과 실제 모든 GPU·모니터 환경 검증은 수행하지 못했습니다.

## 데이터 위치

- 설정: `%LOCALAPPDATA%\LightMediaPlayer\settings.json`
- 엔진: `%LOCALAPPDATA%\LightMediaPlayer\runtime\<payload hash>`
- 자막 변환본: `%LOCALAPPDATA%\LightMediaPlayer\subtitles`
- 로그: `%LOCALAPPDATA%\LightMediaPlayer\logs\player-<pid>.log`
- .NET 단일 파일용 네이티브 추출 캐시는 .NET 런타임의 사용자 임시 경로를 사용합니다.

설치 제거는 앱 파일과 앱의 연결 등록을 제거합니다. 설정·캐시는 유지합니다. 로그에는 열었던 파일 경로가 포함될 수 있습니다. 앱은 로그를 업로드하지 않습니다.

## 빌드

Windows x64, PowerShell 7, 인터넷 연결이 필요합니다. SDK 10.0.404, LibVLC 3.0.24, LibVLCSharp 3.10.2, Inno Setup 7.1.1을 사용합니다.

```powershell
./scripts/bootstrap.ps1
./scripts/build.ps1
```

의존성은 `.tools`에, 배포 파일은 `artifacts/release`에 생성합니다. 도구와 배포 바이너리는 Git에 포함하지 않습니다. Inno Setup을 상업적으로 사용하려면 해당 도구의 사용 조건을 확인하세요.

`scripts/make-icon.py`는 이미 포함된 아이콘을 다시 생성할 때만 필요하며 Python 표준 라이브러리를 사용합니다.

## 테스트

FFmpeg Windows 빌드를 `.tools/ffmpeg` 아래에 준비합니다. FFmpeg는 테스트 미디어 생성에만 사용하며 앱에는 포함하지 않습니다.

```powershell
./scripts/create-fixtures.ps1
./scripts/test.ps1
```

생성한 MP4·MKV를 두 프로세스로 동시에 실행하여 실제 LibVLC 재생·탐색·자막·트랙·오류·동시 재생을 검증합니다. 결과 JSON은 `artifacts/test-results`에 생성됩니다. 테스트는 사용자의 설정 파일을 덮어쓰지 않습니다.

## 코드 구조

- `src/LightMediaPlayer/MainWindow.xaml`: 화면.
- `MainWindow.xaml.cs`: 재생 생명주기, 입력, 메뉴, 자막, 전체화면.
- `MediaFiles.cs`, `PlaylistFiles.cs`: 형식 분류, 폴더 탐색, 로컬 목록 읽기.
- `PlayerSettings.cs`: 설정 로드·원자적 저장·손상 복구.
- `NativeRuntime.cs`: 단일 EXE의 엔진 추출. 추출 시에만 잠금을 사용하므로 다중 프로세스 재생을 제한하지 않습니다.
- `MainWindow.SelfTest.cs`: 명시적 테스트 인자로만 실행되는 통합 검사.
- `installer`: Inno Setup 설치 및 연결 등록.

[오픈소스 고지](THIRD-PARTY-NOTICES.txt). 호환되는 수정 LibVLC 배포본을 사용하려면 실행 파일 옆 `libvlc` 폴더에 DLL과 플러그인을 둘 수 있습니다.
