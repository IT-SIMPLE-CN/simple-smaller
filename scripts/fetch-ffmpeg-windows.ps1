# 取随包的 ffmpeg / ffprobe（Windows）：用 BtbN/FFmpeg-Builds 的现成 GPL 静态构建（含 x264、zimg、Media Foundation），
# 按固定版本与 SHA-256 校验，装到 .local\ffmpeg\win-<arch>\bin。版本与 mac 的 build-ffmpeg-mac.sh（n9.0.2）对齐。
# BtbN 的自动构建标签只保留一段时间，失效时换成新的标签与校验和，并在 docs/codecs.md 记一笔。
# 用法：scripts\fetch-ffmpeg-windows.ps1 [-Arch arm64|x64]
param([string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }))
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$TAG = 'autobuild-2026-10-16-13-04'
$BUILDS = @{
  'arm64' = @{ Name = 'ffmpeg-n9.0.2-28-g669f5ab083-winarm64-gpl-9.0'; Sha = 'c7a1d5728aebda8652deca482dbd21b6cf310ad81c5846ef0059bada409dd347' }
  'x64'   = @{ Name = 'ffmpeg-n9.0.2-28-g669f5ab083-win64-gpl-9.0'; Sha = '191415610e7ec846f9f01f68cabe8f9d8a5f49c9bcc1379df00b025433b5844c' }
}

$Root = Split-Path -Parent $PSScriptRoot
$b = $BUILDS[$Arch]
$cache = "$Root\.local\downloads"
$zip = "$cache\$($b.Name).zip"
$out = "$Root\.local\ffmpeg\win-$Arch\bin"
New-Item -ItemType Directory -Force $cache, $out | Out-Null

if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne $b.Sha.ToUpper()) {
  Invoke-WebRequest "https://github.com/BtbN/FFmpeg-Builds/releases/download/$TAG/$($b.Name).zip" -OutFile $zip
  $got = (Get-FileHash $zip -Algorithm SHA256).Hash
  if ($got -ne $b.Sha.ToUpper()) { Remove-Item $zip; throw "ffmpeg 校验和不对：$got" }
}

$tmp = "$cache\$($b.Name)"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
Expand-Archive $zip -DestinationPath $cache
foreach ($exe in 'ffmpeg.exe', 'ffprobe.exe') { Copy-Item "$tmp\bin\$exe" "$out\$exe" -Force }
Copy-Item "$tmp\LICENSE.txt" "$Root\.local\ffmpeg\win-$Arch\LICENSE.txt" -Force
Remove-Item -Recurse -Force $tmp
& "$out\ffmpeg.exe" -hide_banner -version | Select-Object -First 1
"✓ ffmpeg 已装到 $out"
