# Run from PowerShell 7. Downloads build tools into this workspace.
param([switch]$SkipInstallerCompiler)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force .tools,.tools/licenses | Out-Null
function Fetch-Verified([string]$Url, [string]$Destination, [string]$Hash) {
    if (!(Test-Path $Destination) -or (Get-FileHash $Destination -Algorithm SHA256).Hash -ne $Hash) {
        Invoke-WebRequest $Url -OutFile $Destination
    }
    if ((Get-FileHash $Destination -Algorithm SHA256).Hash -ne $Hash) { throw "Checksum mismatch: $Destination" }
}
Fetch-Verified 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.404/dotnet-sdk-10.0.404-win-x64.zip' '.tools/dotnet-sdk.zip' 'F94F02EB8451605A893164BBD9C14E7F00AC8914C370C322822E6AC91B106BCC'
if (!(Test-Path .tools/dotnet/dotnet.exe)) { Expand-Archive .tools/dotnet-sdk.zip .tools/dotnet -Force }
Fetch-Verified 'https://api.nuget.org/v3-flatcontainer/videolan.libvlc.windows/3.0.24/videolan.libvlc.windows.3.0.24.nupkg' '.tools/libvlc.zip' '3DA3F37EE2B2B10E5C788D830A3B513658C36521EA1B0F966ED86EB69F41EF3C'
if (!(Test-Path .tools/libvlc/build/x64/libvlc.dll)) { Expand-Archive .tools/libvlc.zip .tools/libvlc -Force }
$licenseSources = @{
    'VLC-COPYING.txt' = 'https://raw.githubusercontent.com/videolan/vlc/3.0.24/COPYING'
    'VLC-COPYING.LIB.txt' = 'https://raw.githubusercontent.com/videolan/vlc/3.0.24/COPYING.LIB'
    'LibVLCSharp-LICENSE.txt' = 'https://raw.githubusercontent.com/videolan/libvlcsharp/3.10.2/LICENSE'
    'DOTNET-THIRD-PARTY-NOTICES.txt' = 'https://raw.githubusercontent.com/dotnet/runtime/v10.0.15/THIRD-PARTY-NOTICES.TXT'
    'DOTNET-LICENSE.txt' = 'https://raw.githubusercontent.com/dotnet/runtime/v10.0.15/LICENSE.TXT'
}
foreach ($name in $licenseSources.Keys) { Invoke-WebRequest $licenseSources[$name] -OutFile (Join-Path '.tools/licenses' $name) }
if (!$SkipInstallerCompiler -and !(Test-Path .tools/inno/ISCC.exe)) {
    Fetch-Verified 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' '.tools/inno-setup.exe' '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0'
    if ((Get-AuthenticodeSignature .tools/inno-setup.exe).Status -ne 'Valid') { throw 'Invalid Inno Setup signature' }
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS /DIR="{0}" /MERGETASKS="!fileassoc"' -f (Join-Path $root '.tools/inno')
    $p = Start-Process -FilePath (Resolve-Path .tools/inno-setup.exe) -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Inno Setup installation failed: $($p.ExitCode)" }
}
Write-Output 'Build dependencies are ready. Run ./scripts/build.ps1.'
