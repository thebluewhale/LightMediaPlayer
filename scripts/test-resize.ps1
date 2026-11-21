param([string]$Executable, [string]$Report)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'src/LightMediaPlayer/LightMediaPlayer.csproj') -Raw)).Project.PropertyGroup.Version
if (!$Executable) { $Executable = Join-Path $root "artifacts/release/v$version/LightMediaPlayer-Portable-$version.exe" }
if (!$Report) { $Report = Join-Path $root "artifacts/test-results/v$version/resize.json" }
$exe = (Resolve-Path $Executable).Path
$Report = [IO.Path]::GetFullPath($Report)
New-Item -ItemType Directory -Force (Split-Path $Report) | Out-Null
if (Test-Path $Report) { Remove-Item -LiteralPath $Report }
$media = Join-Path $root 'artifacts/test-media/한글 경로/영상2.mkv'
$arguments = '--self-test-report "{0}" "{1}" --resize-only' -f $Report, $media
$probe = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (!$probe.WaitForExit(60000)) { throw "Resize test timed out: $($probe.Id)" }
if (!(Test-Path $Report)) { throw "Missing resize report (exit $($probe.ExitCode)): $Report" }
$result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
$result.checks | Where-Object { !$_.passed } | Format-Table -AutoSize
if (!$result.passed -or !($result.checks | Where-Object name -eq 'resize_has_no_changing_clip_region')) { throw 'Resize checks failed or executable lacks the resize probe' }
Write-Output "All $($result.checks.Count) resize checks passed (process $($result.processId))."
