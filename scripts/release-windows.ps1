# Windows 发版：两种架构各 publish 一次（自带 .NET 与 Windows App SDK），用 Velopack 打成安装程序与更新包。
# 产物在 .local\win-releases\<版本>\，再在 Mac 上跑 scripts/publish-windows.sh 发布。见 docs/release.md 的 Windows 一节。
# 先在 VM 里准备好：build-codecs-windows.ps1、build-mupdf-windows.ps1、fetch-misans.ps1（两种架构），Mac 上 build-ffmpeg-windows.sh all。
# 用法：scripts\release-windows.ps1 [-Arch x64,arm64] [-Version 0.9.6] [-UpdateTest]
#   -Version 只给验证更新流程用，正式发版读 version.txt。
#   -UpdateTest 打测试包：应用认环境变量 SMALLER_UPDATE_URL（从本地目录或网址取更新），产物放 .local\win-releases\update-test\<版本>，
#   不会被发布脚本取走。正式包不带这个开关。
param([string[]]$Arch = @('x64', 'arm64'), [string]$Version = '', [switch]$UpdateTest)
$ErrorActionPreference = 'Stop'

$VPK_VERSION = '1.2.161' # 与应用里的 Velopack 包同一版本
$Root = Split-Path -Parent $PSScriptRoot
$App = "$Root\apps\windows\SimpleSmaller"
if (-not $Version) { $Version = (Get-Content "$Root\apps\windows\version.txt" -Raw).Trim() }
$Notes = "$Root\apps\windows\release-notes\$Version.md"
if (-not (Test-Path $Notes)) { $Notes = (Get-ChildItem "$Root\apps\windows\release-notes\*.md" | Sort-Object Name | Select-Object -Last 1).FullName }
$Out = if ($UpdateTest) { "$Root\.local\win-releases\update-test\$Version" } else { "$Root\.local\win-releases\$Version" }
$TestFlag = if ($UpdateTest) { '-p:UpdateTest=true' } else { '-p:UpdateTest=false' }
if (-not $Notes) { throw "先写更新说明：apps\windows\release-notes\$Version.md" }
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:USERPROFILE\.dotnet\tools;$env:Path"

# 原生命令写到 stderr 的进度在 PowerShell 5.1 里会被当成错误，这里只看退出码。
$ErrorActionPreference = 'Continue'
function Assert-Ok([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What 失败（$LASTEXITCODE）" } }

# vpk 命令行（dotnet 全局工具），版本不对就重装。
$have = (dotnet tool list -g | Select-String '^vpk\s+(\S+)').Matches.Groups[1].Value
if ($have -ne $VPK_VERSION) {
  if ($have) { dotnet tool uninstall -g vpk | Out-Null }
  dotnet tool install -g vpk --version $VPK_VERSION | Out-Null
  Assert-Ok '安装 vpk'
}

& "$PSScriptRoot\fetch-misans.ps1"
if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force $Out | Out-Null

foreach ($a in $Arch) {
  $rid = "win-$a"
  $platform = if ($a -eq 'x64') { 'x64' } else { 'ARM64' }
  "▸ $rid：引擎"
  & "$PSScriptRoot\build-core-windows.ps1" -Arch $a | Select-Object -Last 1
  "▸ $rid：发布"
  $pub = "$Root\.local\win-publish\$rid"
  if (Test-Path $pub) { Remove-Item -Recurse -Force $pub }
  dotnet publish "$App\SimpleSmaller.csproj" -c Release -r $rid -p:Platform=$platform --self-contained -p:Version=$Version $TestFlag -o $pub -nologo -v q | Select-Object -Last 5
  Assert-Ok "dotnet publish $rid"
  foreach ($f in 'SimpleSmaller.exe', 'smaller_core.dll', 'ffmpeg.exe', 'ffprobe.exe', 'Assets\Fonts\MiSansVF.ttf', 'Assets\Fonts\MiSans-License.pdf') {
    if (-not (Test-Path "$pub\$f")) { throw "发布目录里缺 $f" }
  }
  # 许可证全文（AGPL 等要求随二进制附带，见 licenses\ 与 THIRD_PARTY_NOTICES.md）。
  foreach ($f in 'SimpleSmaller-AGPL-3.0.txt', 'FFmpeg-LICENSE.md', 'MuPDF-COPYING.txt', 'WindowsAppSDK-2.5.1-license.txt', 'dotnet-THIRD-PARTY-NOTICES.txt', 'rust-crates.txt') {
    if (-not (Test-Path "$pub\Assets\ThirdParty\$f")) { throw "发布目录里缺许可证 Assets\ThirdParty\$f" }
  }
  "▸ $rid：打包"
  # 只建开始菜单快捷方式（和 mac 一样不往桌面放东西）；不出便携版。
  vpk pack --packId SimpleSmaller --packVersion $Version --packDir $pub --mainExe SimpleSmaller.exe `
    --packTitle '简单小一点' --packAuthors 'IT-SIMPLE' --icon "$App\Assets\AppIcon.ico" `
    --channel $rid --runtime $rid --releaseNotes $Notes --delta None --outputDir $Out `
    --shortcuts StartMenuRoot --noPortable
  Assert-Ok "vpk pack $rid"
}

# 校验和（发布脚本核对用）。
Get-ChildItem $Out -File | Where-Object { $_.Name -ne 'SHA256SUMS' } | ForEach-Object {
  '{0}  {1}' -f (Get-FileHash $_.FullName).Hash.ToLower(), $_.Name
} | Set-Content "$Out\SHA256SUMS" -Encoding ascii
"✓ $Version 已打包到 $Out$(if ($UpdateTest) { '（测试包，认 SMALLER_UPDATE_URL，不要发布）' })"
Get-ChildItem $Out | ForEach-Object { '  {0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
