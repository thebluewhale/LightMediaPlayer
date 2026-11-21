param([string]$NativePackage = "$PSScriptRoot\..\.tools\libvlc")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$generated = Join-Path $root 'src\LightMediaPlayer\Generated'
New-Item -ItemType Directory -Force $generated | Out-Null
$source = Join-Path $NativePackage 'build\x64'
if (!(Test-Path (Join-Path $source 'libvlc.dll'))) { throw 'Extract VideoLAN.LibVLC.Windows 3.0.24 into .tools/libvlc first.' }
$stage = Join-Path $root 'artifacts\native-stage'
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $source '*.dll') $stage -Force
foreach ($dir in @('plugins', 'hrtfs', 'lua')) { Copy-Item (Join-Path $source $dir) $stage -Recurse -Force }
$licenses = Join-Path $stage 'licenses'
New-Item -ItemType Directory -Force $licenses | Out-Null
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $licenses -Force
if (Test-Path (Join-Path $root '.tools\licenses')) { Copy-Item (Join-Path $root '.tools\licenses\*') $licenses -Recurse -Force }
$zip = Join-Path $generated 'native.zip'
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
(Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content (Join-Path $generated 'native.sha256') -NoNewline
Write-Output "Native payload: $((Get-Item $zip).Length) bytes"
