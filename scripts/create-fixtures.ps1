$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root
$ffmpeg = (Get-ChildItem .tools/ffmpeg -Recurse -Filter ffmpeg.exe | Select-Object -First 1).FullName
if (!$ffmpeg) { throw 'Download FFmpeg into .tools/ffmpeg before generating fixtures.' }
New-Item -ItemType Directory -Force 'artifacts/test-media/한글 경로' | Out-Null
$dir = Join-Path $root 'artifacts/test-media/한글 경로'
$subtitle = "1`n00:00:00,000 --> 00:00:24,000`n한글 자막 테스트 · Light Media Player`n"
[IO.File]::WriteAllText((Join-Path $dir '영상2.srt'), $subtitle, [Text.UTF8Encoding]::new($true))
& $ffmpeg -hide_banner -loglevel error -y -f lavfi -i 'testsrc2=size=640x360:rate=24' -f lavfi -i 'sine=frequency=440:sample_rate=48000' -f lavfi -i 'sine=frequency=660:sample_rate=48000' -i (Join-Path $dir '영상2.srt') -map 0:v -map 1:a -map 2:a -map 3:s -t 24 -c:v libx264 -preset ultrafast -crf 26 -pix_fmt yuv420p -c:a aac -b:a 96k -af 'volume=0.1' -c:s srt -metadata:s:a:0 language=kor -metadata:s:a:1 language=eng -metadata:s:s:0 language=kor (Join-Path $dir '영상2.mkv')
if ($LASTEXITCODE -ne 0) { throw 'MKV fixture generation failed' }
& $ffmpeg -hide_banner -loglevel error -y -i (Join-Path $dir '영상2.mkv') -map 0:v -map 0:a -c copy (Join-Path $dir '영상10.mp4')
if ($LASTEXITCODE -ne 0) { throw 'MP4 fixture generation failed' }
Copy-Item (Join-Path $dir '영상2.srt') (Join-Path $dir '영상10.srt') -Force
& $ffmpeg -hide_banner -loglevel error -y -i (Join-Path $dir '영상2.mkv') -map 0:a:0 -c:a flac (Join-Path $dir '음악.flac')
'This is intentionally not an MP4 file.' | Set-Content (Join-Path $dir 'broken.mp4')
"#EXTM3U`n영상2.mkv`n영상10.mp4" | Set-Content (Join-Path $dir 'sample.m3u') -Encoding utf8
[Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
[IO.File]::WriteAllText((Join-Path $dir 'cp949.srt'), $subtitle, [Text.Encoding]::GetEncoding(949))
Write-Output $dir
