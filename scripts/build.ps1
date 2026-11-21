param([switch]$SkipNative)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.tools\packages'
$env:DOTNET_NOLOGO = '1'
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$inno = Join-Path $root '.tools\inno\ISCC.exe'
if (!(Test-Path $inno)) { $inno = (Get-Command ISCC.exe -ErrorAction Stop).Source }
if (!$SkipNative) { & "$PSScriptRoot\prepare-native.ps1" }
& $dotnet publish src/LightMediaPlayer/LightMediaPlayer.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
New-Item -ItemType Directory -Force artifacts/release | Out-Null
Copy-Item artifacts/publish/LightMediaPlayer.exe artifacts/release/LightMediaPlayer-Portable-0.1.0.exe -Force
& $inno /Q installer/LightMediaPlayer.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Get-Item artifacts/release/LightMediaPlayer-Portable-0.1.0.exe,artifacts/release/LightMediaPlayer-Setup.exe | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content artifacts/release/SHA256SUMS.txt
Get-ChildItem artifacts/release | Select-Object Name,Length
