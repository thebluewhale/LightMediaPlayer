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
$version = ([xml](Get-Content src/LightMediaPlayer/LightMediaPlayer.csproj -Raw)).Project.PropertyGroup.Version
$publish = Join-Path $root "artifacts/publish/v$version"
$release = Join-Path $root "artifacts/release/v$version"
& $dotnet publish src/LightMediaPlayer/LightMediaPlayer.csproj -c Release -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
New-Item -ItemType Directory -Force $release | Out-Null
$portable = Join-Path $release "LightMediaPlayer-Portable-$version.exe"
Copy-Item (Join-Path $publish 'LightMediaPlayer.exe') $portable -Force
& $inno /Q "/DAppVersion=$version" "/DPublishDir=$publish" "/DReleaseDir=$release" installer/LightMediaPlayer.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Get-Item $portable,(Join-Path $release 'LightMediaPlayer-Setup.exe') | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } | Set-Content (Join-Path $release 'SHA256SUMS.txt')
Get-ChildItem $release | Select-Object Name,Length
