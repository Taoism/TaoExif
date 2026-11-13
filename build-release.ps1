<#
.SYNOPSIS
  Builds every TaoExif release package into .\release:
    TaoExif-<v>-setup-win-x64.exe    Windows installer (Inno Setup 6)
    TaoExif-<v>-win-x64.zip          Windows portable
    taoexif_<v>_amd64.deb            Ubuntu / Debian
    TaoExif-<v>-linux-x64.tar.gz     Linux portable
    TaoExif-<v>-macos-arm64.zip      macOS app, Apple silicon
    TaoExif-<v>-macos-x64.zip        macOS app, Intel
    SHA256SUMS.txt
  The Linux and macOS packages are made in WSL (packaging/package-unix.sh) for Unix permissions and dpkg-deb.
#>
param(
    [string]$Iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    [string]$WslDistro = 'Ubuntu'
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "TaoExif $Version" -ForegroundColor Cyan

& "$PSScriptRoot\tools\fetch-exiftool.ps1"

$release = Join-Path $PSScriptRoot 'release'
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
New-Item -ItemType Directory $release | Out-Null

foreach ($rid in 'win-x64', 'linux-x64', 'osx-arm64', 'osx-x64') {
    Write-Host "publish $rid"
    $out = Join-Path $PSScriptRoot "publish\$rid"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish src/TaoExif/TaoExif.csproj -c Release -r $rid --self-contained -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "publish $rid failed" }
}

# Windows installer + portable zip
& $Iscc /Q "/DAppVersion=$Version" "/DSourceDir=$PSScriptRoot\publish\win-x64" "/O$release" installer\taoexif-setup.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
$stage = Join-Path $env:TEMP "taoexif-zip-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$zipRoot = Join-Path $stage "TaoExif-$Version-win-x64"
Copy-Item publish\win-x64 $zipRoot -Recurse
Copy-Item LICENSE, README.md $zipRoot
Compress-Archive -Path $zipRoot -DestinationPath (Join-Path $release "TaoExif-$Version-win-x64.zip") -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# Linux + macOS (in WSL)
$wslRoot = (wsl.exe -d $WslDistro -- wslpath -a ($PSScriptRoot -replace '\\', '/')).Trim()
wsl.exe -d $WslDistro -- bash "$wslRoot/packaging/package-unix.sh" $Version $wslRoot
if ($LASTEXITCODE -ne 0) { throw 'package-unix.sh failed' }

# checksums
Push-Location $release
Get-ChildItem -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content SHA256SUMS.txt -Encoding ascii
Pop-Location

Get-ChildItem $release | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } -AutoSize
