# 取界面字体 MiSans（小米，免费商用，见 MiSans字体知识产权许可协议.pdf）。
# 协议要求：软件里注明使用了 MiSans；不得改编（所以不做子集化）；不得单独分发字体文件；副本里带上版权声明与协议。
# 因此字体不进 git 仓库，只在构建时下载官方包、按 SHA-256 校验，取可变字体与协议放进 Assets\Fonts，随安装包分发。
# 用法：scripts\fetch-misans.ps1
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$URL = 'https://hyperos.mi.com/font-download/MiSans.zip'
$ZIP_SHA = 'b6aa1fc827035922612df8edf36e5609bca1c5441e25cd57572204569b7b81d9'
$VF_SHA = '0ddef90648998900175cfdca9a6f087a2544c182f130b0ad4f7e94a03a115e79'
$LICENSE_URL = 'https://hyperos.mi.com/font-download/MiSans%E5%AD%97%E4%BD%93%E7%9F%A5%E8%AF%86%E4%BA%A7%E6%9D%83%E8%AE%B8%E5%8F%AF%E5%8D%8F%E8%AE%AE.pdf'

$Root = Split-Path -Parent $PSScriptRoot
$cache = "$Root\.local\downloads"
$out = "$Root\apps\windows\SimpleSmaller\Assets\Fonts"
New-Item -ItemType Directory -Force $cache, $out | Out-Null

if ((Test-Path "$out\MiSansVF.ttf") -and (Get-FileHash "$out\MiSansVF.ttf").Hash -eq $VF_SHA.ToUpper() -and (Test-Path "$out\MiSans-License.pdf")) {
  '✓ MiSans 已就绪'
  return
}
$zip = "$cache\MiSans.zip"
if (-not (Test-Path $zip) -or (Get-FileHash $zip).Hash -ne $ZIP_SHA.ToUpper()) {
  Invoke-WebRequest $URL -OutFile $zip
  $got = (Get-FileHash $zip).Hash
  if ($got -ne $ZIP_SHA.ToUpper()) { Remove-Item $zip; throw "MiSans 校验和不对：$got（官方包更新了的话，核对后改这里的 SHA）" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
  $entry = $archive.Entries | Where-Object { $_.Name -eq 'MiSansVF.ttf' -and $_.FullName -notlike '__MACOSX*' } | Select-Object -First 1
  if (-not $entry) { throw '官方包里没有 MiSansVF.ttf' }
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, "$out\MiSansVF.ttf", $true)
} finally { $archive.Dispose() }
if ((Get-FileHash "$out\MiSansVF.ttf").Hash -ne $VF_SHA.ToUpper()) { throw 'MiSansVF.ttf 校验和不对' }
Invoke-WebRequest $LICENSE_URL -OutFile "$out\MiSans-License.pdf"
'✓ MiSans 已装到 ' + $out
