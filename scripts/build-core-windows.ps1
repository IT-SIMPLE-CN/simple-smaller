# 编译 Rust 引擎宿主（smaller-core.exe，独立进程，应用经标准输入输出调用它），供 apps/windows 使用。Windows 版，对应 build-core-mac.sh。
# Windows 应用不再在进程内加载引擎（不用 UniFFI 的 C# 绑定）：引擎静态链着 MuPDF 等 GPL 库，与应用自带的 Windows App SDK 分属两个程序。
# 用法：scripts\build-core-windows.ps1 [-Arch arm64|x64] [-Profile release|debug]
# 需先编好同架构的编码库与 MuPDF（build-codecs-windows.ps1、build-mupdf-windows.ps1）。
param(
  [string]$Arch = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }),
  [string]$Profile = 'release'
)
$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Triple = if ($Arch -eq 'x64') { 'x86_64-pc-windows-msvc' } else { 'aarch64-pc-windows-msvc' }
. "$PSScriptRoot\windows-env.ps1"
Enter-VsShell $Arch
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:Path"

# 原生命令写到 stderr 的进度在 PowerShell 5.1 里会被当成错误，这里只看退出码。
$ErrorActionPreference = 'Continue'
function Assert-Ok([string]$What) { if ($LASTEXITCODE -ne 0) { throw "$What 失败（$LASTEXITCODE）" } }

# 编译产物里不留本机路径（panic 信息、调试信息里的源码位置）：仓库、cargo 注册表、用户目录都映射成短名。
# 仓库路径可能带空格，RUSTFLAGS 按空格拆分会拆坏，用 CARGO_ENCODED_RUSTFLAGS（参数之间用 0x1f 分隔）。
# 同一路径匹配多条时后写的优先，所以用户目录写在最前面。
$cargoHome = if ($env:CARGO_HOME) { $env:CARGO_HOME } else { "$env:USERPROFILE\.cargo" }
$env:CARGO_ENCODED_RUSTFLAGS = @(
  "--remap-path-prefix=$env:USERPROFILE=~",
  "--remap-path-prefix=$cargoHome\registry\src=\cargo",
  "--remap-path-prefix=$cargoHome\git\checkouts=\cargo-git",
  "--remap-path-prefix=$Root=\smaller"
) -join [char]0x1f
Remove-Item Env:RUSTFLAGS -ErrorAction SilentlyContinue

Set-Location $Root
if ($Profile -eq 'release') { cargo build --release -p smaller-core-host --target $Triple } else { cargo build -p smaller-core-host --target $Triple }
Assert-Ok 'cargo build'
$hostExe = "$Root\target\$Triple\$Profile\smaller-core-host.exe"

$app = "$Root\apps\windows\SimpleSmaller"
New-Item -ItemType Directory -Force "$app\Native\$Arch" | Out-Null
Copy-Item $hostExe "$app\Native\$Arch\smaller-core.exe" -Force
# 早先进程内加载用的 DLL 与 C# 绑定已不用，留着会被误打包。
Remove-Item "$app\Native\$Arch\smaller_core.dll", "$app\Core\smaller_core.cs" -Force -ErrorAction SilentlyContinue
# ffmpeg / ffprobe 放在应用旁边（引擎按可执行文件旁边找）。它们在 macOS / Linux 上用 scripts/build-ffmpeg-windows.sh 交叉编译，
# 产物在那边仓库的 .local/ffmpeg/win-<arch>/bin，拷到这边仓库的同一位置即可。
# 也可以设环境变量 SMALLER_MAC_REPO 指向共享文件夹里的那份仓库：这边没有或那边重新编过（文件更新）时自动拷过来。
$ff = "$Root\.local\ffmpeg\win-$Arch\bin"
$sharedFf = if ($env:SMALLER_MAC_REPO) { "$env:SMALLER_MAC_REPO\.local\ffmpeg\win-$Arch\bin" } else { '' }
if ($sharedFf -and (Test-Path "$sharedFf\ffmpeg.exe") -and ($sharedFf -ne $ff)) {
  New-Item -ItemType Directory -Force $ff | Out-Null
  foreach ($exe in 'ffmpeg.exe', 'ffprobe.exe') {
    $from = Get-Item "$sharedFf\$exe"
    $to = Get-Item "$ff\$exe" -ErrorAction SilentlyContinue
    if (-not $to -or $to.Length -ne $from.Length -or $to.LastWriteTimeUtc -lt $from.LastWriteTimeUtc) { Copy-Item $from.FullName "$ff\$exe" -Force }
  }
}
if (Test-Path "$ff\ffmpeg.exe") { Copy-Item "$ff\ffmpeg.exe", "$ff\ffprobe.exe" "$app\Native\$Arch\" -Force } else { Write-Warning "没有 ffmpeg：先在 macOS / Linux 上运行 scripts/build-ffmpeg-windows.sh $Arch，或从 Releases 下载预编译包，放到 $ff" }
"引擎已就绪：$Profile $Arch"
