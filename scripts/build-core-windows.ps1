# 编译 Rust 引擎（smaller_core.dll）并生成 C# 绑定，供 apps/windows 使用。Windows 版，对应 build-core-mac.sh。
# 用法：scripts\build-core-windows.ps1 [-Arch arm64|x64] [-Profile release|debug]
# 需先编好同架构的编码库与 MuPDF（build-codecs-windows.ps1、build-mupdf-windows.ps1）。
param(
  [string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
  [string]$Profile = 'release'
)
$ErrorActionPreference = 'Stop'

# 跟 uniffi 版本走，见根目录 Cargo.toml。
$BINDGEN_CS_TAG = 'v0.11.1+v0.31.0'

$Root = Split-Path -Parent $PSScriptRoot
$Triple = if ($Arch -eq 'x64') { 'x86_64-pc-windows-msvc' } else { 'aarch64-pc-windows-msvc' }
. "$PSScriptRoot\windows-env.ps1"
Enter-VsShell $Arch
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:Path"

# 原生命令写到 stderr 的进度在 PowerShell 5.1 里会被当成错误，这里只看退出码。
$ErrorActionPreference = 'Continue'
function Assert-Ok([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What 失败（$LASTEXITCODE）" } }

Set-Location $Root
if ($Profile -eq 'release') { cargo build --release -p smaller-core --target $Triple } else { cargo build -p smaller-core --target $Triple }
Assert-Ok 'cargo build' 
$dll = "$Root\target\$Triple\$Profile\smaller_core.dll"

# C# 绑定：uniffi-bindgen-cs 装在 .local\tools，版本不对就重装。
$tools = "$Root\.local\tools"
$bindgen = "$tools\bin\uniffi-bindgen-cs.exe"
$stamp = "$tools\bindgen-cs.tag"
if (-not (Test-Path $bindgen) -or (Get-Content $stamp -ErrorAction SilentlyContinue) -ne $BINDGEN_CS_TAG) {
  cargo install uniffi-bindgen-cs --git https://github.com/NordSecurity/uniffi-bindgen-cs --tag $BINDGEN_CS_TAG --root $tools --force
  Assert-Ok '安装 uniffi-bindgen-cs' 
  Set-Content $stamp $BINDGEN_CS_TAG
}
$out = "$Root\.local\bindings-cs"
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
& $bindgen --library $dll --out-dir $out
Assert-Ok '生成 C# 绑定' 

$app = "$Root\apps\windows\SimpleSmaller"
New-Item -ItemType Directory -Force "$app\Core", "$app\Native\$Arch" | Out-Null
Copy-Item "$out\smaller_core.cs" "$app\Core\smaller_core.cs" -Force
Copy-Item $dll "$app\Native\$Arch\smaller_core.dll" -Force
# ffmpeg / ffprobe 放在应用旁边（引擎按可执行文件旁边找），先跑 fetch-ffmpeg-windows.ps1。
$ff = "$Root\.local\ffmpeg\win-$Arch\bin"
if (Test-Path "$ff\ffmpeg.exe") { Copy-Item "$ff\ffmpeg.exe", "$ff\ffprobe.exe" "$app\Native\$Arch\" -Force } else { Write-Warning "没有 ffmpeg：先运行 scripts\fetch-ffmpeg-windows.ps1 -Arch $Arch" }
"引擎已就绪：$Profile $Arch"
