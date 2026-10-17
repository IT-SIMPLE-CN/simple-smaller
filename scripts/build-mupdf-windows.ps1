# 按固定版本编译 MuPDF 静态库（AGPL），装到 .local\mupdf\win-<arch>。Windows 版，对应 build-mupdf-mac.sh。
# 用 MuPDF 自带的 VS 解决方案（工程写的是 VS 2019 工具集，按 VS 2022 的 v143 编）；和 mac 一样不带 OCR（tesseract / leptonica），不内置 Noto 兜底字体（TOFU）。
# 用法：scripts\build-mupdf-windows.ps1 [-Arch arm64|x64]，默认本机架构。
param([string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }))
$ErrorActionPreference = 'Stop'

$MUPDF_TAG = '1.28.5' # 与 build-mupdf-mac.sh 一致

$Root = Split-Path -Parent $PSScriptRoot
$Src = "$Root\.local\src\mupdf"
$Out = "$Root\.local\mupdf\win-$Arch"
$Log = "$Root\.local\build\mupdf-win-$Arch.log"
New-Item -ItemType Directory -Force (Split-Path $Log), "$Out\lib" | Out-Null

. "$PSScriptRoot\windows-env.ps1"
Enter-VsShell $Arch

# 源码目录不在、或不是要的那个版本，就删掉重新拉，不悄悄沿用旧源码。
$ErrorActionPreference = 'Continue'
if (Test-Path $Src) {
  $cur = git -C $Src describe --tags --exact-match 2>$null
  if ($cur -ne $MUPDF_TAG) { Remove-Item -Recurse -Force $Src }
}
if (-not (Test-Path $Src)) {
  git clone -q --depth 1 --branch $MUPDF_TAG --recurse-submodules --shallow-submodules https://github.com/ArtifexSoftware/mupdf $Src
  if ($LASTEXITCODE -ne 0) { throw '拉取 MuPDF 失败' }
}

$ErrorActionPreference = 'Stop'
# 解决方案的 Release 默认带 OCR，去掉这两个宏（只改本地源码副本）。
foreach ($proj in Get-ChildItem "$Src\platform\win32" -Filter '*.vcxproj') {
  $text = [IO.File]::ReadAllText($proj.FullName)
  $patched = $text.Replace('HAVE_TESSERACT;', '').Replace('HAVE_LEPTONICA;', '')
  if ($patched -ne $text) { [IO.File]::WriteAllText($proj.FullName, $patched) }
}

$platform = if ($Arch -eq 'x64') { 'x64' } else { 'ARM64' }
$env:CL = '/DTOFU /DTOFU_CJK'
$ErrorActionPreference = 'Continue'
msbuild "$Src\platform\win32\mupdf.sln" /t:libmupdf /p:Configuration=Release /p:Platform=$platform /p:PlatformToolset=v143 /m /v:minimal /nologo *> $Log
$code = $LASTEXITCODE
Remove-Item Env:CL
if ($code -ne 0) { Get-Content $Log -Tail 40; throw "MuPDF 编译失败，日志：$Log" }

# 统一成 build.rs 认的库名。
$built = "$Src\platform\win32\$platform\Release"
$names = @{
  'libmupdf.lib' = 'mupdf.lib'; 'libthirdparty.lib' = 'mupdf-third.lib'; 'libresources.lib' = 'mupdf-resources.lib'
  'libpkcs7.lib' = 'mupdf-pkcs7.lib'; 'libharfbuzz.lib' = 'mupdf-harfbuzz.lib'; 'libextract.lib' = 'mupdf-extract.lib'
  'libmubarcode.lib' = 'mupdf-barcode.lib'; 'libzxing.lib' = 'mupdf-zxing.lib'
}
Remove-Item "$Out\lib\*.lib" -ErrorAction SilentlyContinue
foreach ($k in $names.Keys) {
  if (Test-Path "$built\$k") { Copy-Item "$built\$k" "$Out\lib\$($names[$k])" }
}
if (Test-Path "$Out\include") { Remove-Item -Recurse -Force "$Out\include" }
Copy-Item -Recurse "$Src\include" "$Out\include"
"✓ mupdf $MUPDF_TAG ($platform)"
Get-ChildItem "$built" -Filter *.lib | ForEach-Object { '  built {0,-24} {1,10:N0} KB' -f $_.Name, ($_.Length / 1KB) }
Get-ChildItem "$Out\lib" | ForEach-Object { '  {0,-24} {1,10:N0} KB' -f $_.Name, ($_.Length / 1KB) }
