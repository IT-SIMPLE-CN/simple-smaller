# 按固定版本从源码编译图片编码库（静态库），装到 .local\codecs\<arch>。Windows 版，对应 build-codecs-mac.sh。
# 用法：scripts\build-codecs-windows.ps1 [-Arch arm64|x64]，默认本机架构。
# 版本与 build-codecs-mac.sh 保持一致；更新时两边一起改，并在 docs/codecs.md 记录原因。
param([string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }))
$ErrorActionPreference = 'Stop'

$LIBWEBP_TAG = 'v1.6.0'
$AOM_TAG = 'v3.15.2'
$LIBAVIF_TAG = 'v1.4.2'
$JPEGLI_COMMIT = '031a0077f5799a6041004267fc12b956c1f52a20' # 上游没有发版，钉提交

$Root = Split-Path -Parent $PSScriptRoot
$Src = "$Root\.local\src"
$Build = "$Root\.local\build\win-$Arch"
$Prefix = "$Root\.local\codecs\win-$Arch"
New-Item -ItemType Directory -Force $Src, $Build, $Prefix | Out-Null

. "$PSScriptRoot\windows-env.ps1"
Enter-VsShell $Arch

$Common = @(
  '-G', 'Ninja',
  '-DCMAKE_BUILD_TYPE=Release',
  '-DBUILD_SHARED_LIBS=OFF',
  '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreadedDLL',
  "-DCMAKE_INSTALL_PREFIX=$Prefix",
  "-DCMAKE_PREFIX_PATH=$Prefix",
  '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW'
)

function Invoke-Native {
  param([string]$Log, [Parameter(ValueFromRemainingArguments)][string[]]$Cmd)
  $exe = $Cmd[0]; $rest = @($Cmd | Select-Object -Skip 1)
  & $exe @rest *> $Log
  if ($LASTEXITCODE -ne 0) { Get-Content $Log -Tail 30; throw "$cmd 失败，日志：$Log" }
}

# 源码目录不在、或不是要的那个版本（改了版本号），就删掉重新拉，不悄悄沿用旧源码。
function Get-Tag([string]$Name, [string]$Repo, [string]$Tag) {
  $dir = "$Src\$Name"
  if (Test-Path $dir) {
    $cur = git -C $dir describe --tags --exact-match 2>$null
    if ($cur -eq $Tag) { return }
    Remove-Item -Recurse -Force $dir
  }
  git clone -q --depth 1 --branch $Tag $Repo $dir
  if ($LASTEXITCODE -ne 0) { throw "拉取 $Name 失败" }
}

function Build-Lib([string]$Name, [string[]]$Extra) {
  Invoke-Native "$Build\$Name.configure.log" cmake -S "$Src\$Name" -B "$Build\$Name" @Common @Extra
  Invoke-Native "$Build\$Name.build.log" cmake --build "$Build\$Name"
  Invoke-Native "$Build\$Name.install.log" cmake --install "$Build\$Name"
  "✓ $Name"
}

# libwebp
Get-Tag libwebp https://chromium.googlesource.com/webm/libwebp $LIBWEBP_TAG
Build-Lib libwebp @(
  '-DWEBP_BUILD_ANIM_UTILS=OFF', '-DWEBP_BUILD_CWEBP=OFF', '-DWEBP_BUILD_DWEBP=OFF',
  '-DWEBP_BUILD_GIF2WEBP=OFF', '-DWEBP_BUILD_IMG2WEBP=OFF', '-DWEBP_BUILD_VWEBP=OFF',
  '-DWEBP_BUILD_WEBPINFO=OFF', '-DWEBP_BUILD_WEBPMUX=OFF', '-DWEBP_BUILD_EXTRAS=OFF')

# aom（AV1 编解码，供 libavif 用）。x64 的汇编优化要 nasm。
Get-Tag aom https://aomedia.googlesource.com/aom $AOM_TAG
$aomCpu = if ($Arch -eq 'x64') { 'x86_64' } else { 'arm64' }
Build-Lib aom @("-DAOM_TARGET_CPU=$aomCpu",
  '-DENABLE_DOCS=0', '-DENABLE_EXAMPLES=0', '-DENABLE_TESTDATA=0', '-DENABLE_TESTS=0', '-DENABLE_TOOLS=0')

# libavif
Get-Tag libavif https://github.com/AOMediaCodec/libavif $LIBAVIF_TAG
Build-Lib libavif @(
  '-DAVIF_CODEC_AOM=SYSTEM', '-DAVIF_BUILD_APPS=OFF', '-DAVIF_BUILD_TESTS=OFF',
  '-DAVIF_LIBYUV=OFF', '-DAVIF_LIBSHARPYUV=OFF', '-DAVIF_JPEG=OFF', '-DAVIF_ZLIBPNG=OFF', '-DAVIF_LIBXML2=OFF')

# jpegli：和 mac 一样直接用它自己的 jpegli_* 接口，单独编 jpegli-static。
if (-not (Test-Path "$Src\jpegli")) {
  git clone -q --recurse-submodules --shallow-submodules https://github.com/google/jpegli "$Src\jpegli"
}
git -C "$Src\jpegli" checkout -q $JPEGLI_COMMIT
git -C "$Src\jpegli" submodule update -q --init --recursive --depth 1
Build-Lib jpegli @(
  '-DBUILD_TESTING=OFF', '-DJPEGLI_ENABLE_TOOLS=OFF', '-DJPEGLI_ENABLE_DEVTOOLS=OFF',
  '-DJPEGLI_ENABLE_BENCHMARK=OFF', '-DJPEGLI_ENABLE_DOXYGEN=OFF', '-DJPEGLI_ENABLE_MANPAGES=OFF',
  '-DJPEGLI_ENABLE_JNI=OFF', '-DJPEGLI_ENABLE_SJPEG=OFF', '-DJPEGLI_ENABLE_OPENEXR=OFF',
  '-DJPEGLI_ENABLE_FUZZERS=OFF', '-DJPEGLI_INSTALL_JPEGLI_LIBJPEG=ON', '-DJPEGLI_STATIC=ON')
Invoke-Native "$Build\jpegli.static.log" cmake --build "$Build\jpegli" --target jpegli-static
New-Item -ItemType Directory -Force "$Prefix\include\jpegli" | Out-Null
foreach ($h in 'common.h', 'encode.h', 'decode.h', 'types.h') {
  Copy-Item "$Src\jpegli\lib\jpegli\$h" "$Prefix\include\jpegli\$h"
}
# 统一成 build.rs 认的库名（MSVC 下不带 lib 前缀、扩展名 .lib）。
$libs = @{
  'jpegli.lib'     = Get-ChildItem "$Build\jpegli" -Recurse -Filter 'jpegli-static.lib' | Select-Object -First 1
  'lcms2.lib'      = Get-ChildItem "$Build\jpegli" -Recurse -Filter '*lcms2*.lib' | Select-Object -First 1
}
foreach ($k in $libs.Keys) {
  if ($libs[$k]) { Copy-Item $libs[$k].FullName "$Prefix\lib\$k" -Force }
}
"✓ jpegli-static"
Get-ChildItem "$Prefix\lib" -Filter *.lib | ForEach-Object { '{0,-24} {1,8:N0} KB' -f $_.Name, ($_.Length / 1KB) }
"编码库已装到 $Prefix"
