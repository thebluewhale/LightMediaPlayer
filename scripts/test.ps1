param([string]$Executable, [string]$ResultsDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root 'src/LightMediaPlayer/LightMediaPlayer.csproj') -Raw)).Project.PropertyGroup.Version
if (!$Executable) { $Executable = Join-Path $root "artifacts/release/v$version/LightMediaPlayer-Portable-$version.exe" }
$exe = (Resolve-Path $Executable).Path
$media = Join-Path $root 'artifacts\test-media\한글 경로'
$reports = if ($ResultsDirectory) { [IO.Path]::GetFullPath($ResultsDirectory) } else { Join-Path $root "artifacts/test-results/v$version" }
New-Item -ItemType Directory -Force $reports | Out-Null
$jobs = @()
foreach ($pair in @(@('mkv', '영상2.mkv'), @('mp4', '영상10.mp4'))) {
    $report = Join-Path $reports "$($pair[0]).json"
    if (Test-Path $report) { Remove-Item -LiteralPath $report }
    $arguments = '--self-test-report "{0}" "{1}"' -f $report, (Join-Path $media $pair[1])
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $jobs += @{ Process = $process; Report = $report }
}
foreach ($job in $jobs) {
    if (!$job.Process.WaitForExit(120000)) { throw "Player test timed out: $($job.Process.Id)" }
    if (!(Test-Path $job.Report)) { throw "Missing report: $($job.Report)" }
}
$results = $jobs | ForEach-Object { Get-Content $_.Report -Raw | ConvertFrom-Json }
$results | ForEach-Object { Write-Output "Process $($_.processId): passed=$($_.passed)"; $_.checks | Where-Object { !$_.passed } | Format-Table -AutoSize }
if (($results.processId | Select-Object -Unique).Count -ne 2) { throw 'Expected distinct playback processes' }
$intervals = foreach ($result in $results) {
    $log = Join-Path $env:LOCALAPPDATA "LightMediaPlayer\logs\player-$($result.processId).log"
    $lines = Get-Content $log
    $start = ($lines | Where-Object { $_ -match 'CONCURRENCY_START' } | Select-Object -Last 1) -split ' '
    $end = ($lines | Where-Object { $_ -match 'CONCURRENCY_END' } | Select-Object -Last 1) -split ' '
    if (!$start[0] -or !$end[0]) { throw 'Missing playback concurrency interval' }
    @{ Start = [DateTimeOffset]::Parse($start[0]); End = [DateTimeOffset]::Parse($end[0]) }
}
$overlap = $intervals[0].Start -lt $intervals[1].End -and $intervals[1].Start -lt $intervals[0].End
@{ passed = $overlap; processIds = @($results.processId); intervals = $intervals } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $reports 'concurrency.json')
if (!$overlap) { throw 'Playback test intervals did not overlap' }
if ($results | Where-Object { !$_.passed }) { throw 'Playback integration checks failed' }
Write-Output 'All playback checks and simultaneous-process playback passed.'
