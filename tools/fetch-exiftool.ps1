<#
.SYNOPSIS
  Downloads the official ExifTool packages that TaoExif bundles, verified against exiftool.org's SHA-256 list.

  third_party/exiftool/win   exiftool.exe + exiftool_files (Windows package with its own Perl)
  third_party/exiftool/unix  exiftool + lib (pure Perl; Linux and macOS ship perl)

.PARAMETER Version  A specific version (default: the current one from exiftool.org/ver.txt).
.PARAMETER Force    Download again even if the bundle already has that version.
#>
param([string]$Version, [switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Join-Path (Split-Path $PSScriptRoot -Parent) 'third_party/exiftool'
$base = 'https://exiftool.org'
$headers = @{ 'User-Agent' = 'TaoExif-build' }

if (-not $Version) { $Version = (Invoke-WebRequest "$base/ver.txt" -Headers $headers -UseBasicParsing).Content.Trim() }
$stamp = Join-Path $root 'VERSION'
if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $Version) `
    -and (Test-Path (Join-Path $root 'win/exiftool.exe')) -and (Test-Path (Join-Path $root 'unix/exiftool'))) {
  Write-Host "ExifTool $Version already bundled in $root"
  return
}

# per-version checksum list on exiftool.org; the packages themselves are hosted on SourceForge
$sums = (Invoke-WebRequest "$base/checksums-$Version.txt" -Headers $headers -UseBasicParsing).Content
function Get-Verified([string]$name, [string]$dest) {
  $m = [regex]::Match($sums, "SHA2-256\($([regex]::Escape($name))\)=\s*([0-9a-f]{64})")
  if (-not $m.Success) { throw "No SHA-256 for $name on exiftool.org (old version? try without -Version)" }
  Write-Host "Downloading $name"
  Invoke-WebRequest "https://sourceforge.net/projects/exiftool/files/$name/download" -Headers $headers -OutFile $dest -UseBasicParsing
  $hash = (Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne $m.Groups[1].Value) { Remove-Item $dest; throw "Checksum mismatch for $name" }
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("taoexif-exiftool-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
  # Windows: exiftool-<v>_64.zip -> exiftool-<v>_64/exiftool(-k).exe + exiftool_files
  $zip = Join-Path $tmp 'win.zip'
  Get-Verified "exiftool-${Version}_64.zip" $zip
  Expand-Archive $zip (Join-Path $tmp 'win') -Force
  $winSrc = Get-ChildItem (Join-Path $tmp 'win') -Recurse -Filter 'exiftool(-k).exe' | Select-Object -First 1
  if (-not $winSrc) { throw 'exiftool(-k).exe not found in the Windows package' }

  # Unix: Image-ExifTool-<v>.tar.gz -> exiftool script + lib/
  $tgz = Join-Path $tmp 'unix.tar.gz'
  Get-Verified "Image-ExifTool-$Version.tar.gz" $tgz
  New-Item -ItemType Directory (Join-Path $tmp 'unix') | Out-Null
  # Windows' own bsdtar: a GNU tar from Git/MSYS on PATH reads "C:" as a remote host
  $tar = if ($env:OS -eq 'Windows_NT') { Join-Path $env:SystemRoot 'System32/tar.exe' } else { 'tar' }
  & $tar -xzf $tgz -C (Join-Path $tmp 'unix')
  if ($LASTEXITCODE -ne 0) { throw 'tar failed' }
  $unixSrc = Join-Path $tmp "unix/Image-ExifTool-$Version"

  if (Test-Path $root) { Remove-Item $root -Recurse -Force }
  $win = Join-Path $root 'win'; $unix = Join-Path $root 'unix'
  New-Item -ItemType Directory $win, $unix | Out-Null

  # "(-k)" in the name makes exiftool wait for Enter, so it is renamed
  Copy-Item $winSrc.FullName (Join-Path $win 'exiftool.exe')
  Copy-Item (Join-Path $winSrc.DirectoryName 'exiftool_files') $win -Recurse

  Copy-Item (Join-Path $unixSrc 'exiftool') $unix
  Copy-Item (Join-Path $unixSrc 'lib') $unix -Recurse
  foreach ($doc in 'README', 'Changes') {
    if (Test-Path (Join-Path $unixSrc $doc)) { Copy-Item (Join-Path $unixSrc $doc) $root }
  }
  Set-Content $stamp $Version -NoNewline
  Write-Host "ExifTool $Version bundled in $root"
}
finally {
  Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
