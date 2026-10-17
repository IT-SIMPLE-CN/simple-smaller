# Windows 构建共用：进入 VS 开发环境（MSVC、Windows SDK、VS 自带的 CMake 与 Ninja）。
# 用法：. scripts\windows-env.ps1; Enter-VsShell arm64|x64
# Launch-VsDevShell 不认 ARM64 主机，这里跑 vcvarsall.bat 再把环境变量搬进当前会话。
function Enter-VsShell([string]$Arch) {
  $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
  $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
  if (-not $vs) { throw '找不到带 C++ 生成工具的 Visual Studio，见 docs/build.md 的 Windows 一节' }
  $hostArch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'amd64' }
  $target = if ($Arch -eq 'x64') { 'amd64' } else { 'arm64' }
  $spec = if ($hostArch -eq $target) { $target } else { "${hostArch}_$target" }
  $vars = cmd.exe /c "`"$vs\VC\Auxiliary\Build\vcvarsall.bat`" $spec >nul && set"
  if ($LASTEXITCODE -ne 0) { throw "vcvarsall $spec 失败" }
  foreach ($line in $vars) {
    if ($line -match '^([^=]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] }
  }
}
