# 형식 지원 범위

POC 0.1.0 / Windows x64 / LibVLC 3.0.24.

| 상태 | 형식·조합 | 검증 내용 |
|---|---|---|
| 검증 완료 | MKV + H.264 + AAC 2개 트랙 + 내장 SRT | 실제 디코딩, 화면 출력 통계, 재생·탐색·트랙 전환 |
| 검증 완료 | MP4 + H.264 + AAC 2개 트랙 | 실제 디코딩, 화면 출력 통계, 재생·탐색·트랙 전환 |
| 검증 완료 | FLAC | 오디오 재생 및 오디오 전용 화면 상태 |
| 검증 완료 | 외부 SRT / UTF-8, CP949 | 인코딩 변환, 트랙 연결, 표시 전환 |
| 검증 완료 | 로컬 M3U | 상대 경로와 한글 파일명, 목록 읽기 |
| 구현, 별도 샘플 미검증 | PLS, ASX/WAX/WVX, XSPF | 로컬 파일 참조 읽기. 네트워크 참조와 중첩 목록 제외 |
| 엔진에 위임, 개별 미검증 | 기타 일반 영상·오디오·자막, HLS/DASH | 실제 코덱·구조·엔진 지원에 따라 달라짐 |
| 별도 대응 필요 가능 | DRM, 전용 형식, CDA/CUE/IFO/MPLS 등 디스크 관련 형식 | 단순 확장자 연결로 보장할 수 없음 |

자막은 영상이 재생 중인 창에서 열거나 드롭해야 합니다. 자막 파일만 탐색기에서 실행하면 새 창이 열리고 먼저 영상을 열라는 안내가 표시됩니다. IDX/SUB는 함께 있어야 하는 형식일 수 있습니다.

같은 폴더의 이전·다음 이동은 영상·오디오 후보만 대상으로 하며 자막, 목록, 알 수 없는 전용 확장자는 제외합니다. 폴더 끝에서 순환하지 않습니다. 재생 종료 후 자동으로 다음 파일을 열지 않습니다.

다음은 요청받아 파일 선택 필터 및 연결 프로그램 후보로 등록한 확장자입니다. `mp2` 중복은 제거했고 표준 재생목록 확장자인 `xspf`를 추가했습니다. `wspf`, `xss` 등을 임의로 다른 확장자로 변경하지 않았습니다.

```text
3g2 3gp 3gp2 3gpp aac ac3 aif aiff amr amv ape asf ass asx avi cda cue
divx dmskm dpg dpl dsf dts dtshd dvr-ms eac3 evo f4v flac flv idx ifo
k3g lmp4 m1a m1v m2a m2t m2ts m2v m3u m3u8 m4a m4b m4p m4v mka mkv
mod mov mp2 mp2v mp3 mp4 mpa mpc mpd mpeg mpg mpl mpls mpv2 mqv mts mxf
nsr nsv ogg ogm ogv opus pbf pls psb qt ra ram rm rmvb rpm rt sbv skm
smi srt ssa ssf sub sup tak tp tpr ts tta ttml usf vob vtt wav wax webm
wm wma wmp wmv wmx wtv wv wvx wspf xss xspf
```

모든 확장자의 파일을 실제로 확보해 검증한 것은 아닙니다. 재생할 수 없는 파일은 오류 상태로 표시하며, 미디어 스트림이 없는 파일을 정상 재생 완료로 처리하지 않습니다.
