# PrismWave 单 exe 安装包构建脚本
# 用法: .\tools\build_installer.ps1 [-Version 1.0.7]
# 产物: artifacts\PrismWave-Setup-<版本>.exe
#
# 流程:
#   1. dotnet publish 框架依赖部署（用户自装 .NET 10 Desktop Runtime + Windows App Runtime 2.2，
#      setup.exe 向导内自动检测并下载安装 Windows App Runtime）
#   2. 剔除 Windows App SDK 2.2 自动捆绑的 AI 组件（onnxruntime/DirectML 等）
#   3. Inno Setup (ISCC.exe) 编译 tools\setup.iss 生成单 exe 安装包

param(
    [string]$Version = "1.0.7"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\PrismWave.WinUI\PrismWave.WinUI.csproj"
$payload = Join-Path $root "artifacts\installer-payload"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "PrismWave Setup Builder v$Version" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# ---------- 1. 框架依赖发布 ----------
Write-Host "`n[1/3] dotnet publish (framework-dependent .NET + WinAppSDK framework-dependent)..." -ForegroundColor Yellow

if (Test-Path $payload) {
    Remove-Item $payload -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Platform=x64 `
    -o $payload
if ($LASTEXITCODE -ne 0) {
    Write-Host "publish 失败" -ForegroundColor Red
    exit 1
}

# ---------- 2. 剔除 AI 组件 ----------
Write-Host "`n[2/3] 剔除未使用的 AI 组件..." -ForegroundColor Yellow

# Windows App SDK 2.2 依赖链自动带入的 Windows AI Platform 运行时，
# PrismWave 未使用任何 AI API，剔除后不影响运行（build 后需验证启动）。
$excludeExact = @(
    "onnxruntime.dll",
    "DirectML.dll",
    "Microsoft.ML.OnnxRuntime.dll"
)
$excludePatterns = @(
    "Microsoft.Windows.AI.*.dll"
)

$removedBytes = 0L
foreach ($name in $excludeExact) {
    $file = Join-Path $payload $name
    if (Test-Path $file) {
        $removedBytes += (Get-Item $file).Length
        Remove-Item $file -Force
        Write-Host "  已剔除: $name" -ForegroundColor Gray
    }
}
foreach ($pattern in $excludePatterns) {
    Get-ChildItem $payload -Filter $pattern -File -ErrorAction SilentlyContinue | ForEach-Object {
        $removedBytes += $_.Length
        Remove-Item $_.FullName -Force
        Write-Host "  已剔除: $($_.Name)" -ForegroundColor Gray
    }
}
Write-Host ("  共释放 {0:N1} MB" -f ($removedBytes / 1MB)) -ForegroundColor Green

$payloadSize = (Get-ChildItem $payload -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("  安装内容总大小: {0:N1} MB" -f ($payloadSize / 1MB)) -ForegroundColor Green

# ---------- 3. Inno Setup 编译 ----------
Write-Host "`n[3/3] Inno Setup 编译 setup.exe..." -ForegroundColor Yellow

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $null
foreach ($p in $isccCandidates) {
    if (Test-Path $p) { $iscc = $p; break }
}
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Host "未找到 Inno Setup 编译器 ISCC.exe" -ForegroundColor Red
    Write-Host "请先安装: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    exit 1
}
Write-Host "  ISCC: $iscc" -ForegroundColor Gray

$setupPath = Join-Path $root "artifacts\PrismWave-Setup-$Version.exe"
if (Test-Path $setupPath) {
    Remove-Item $setupPath -Force
}

& $iscc "/DMyAppVersion=$Version" (Join-Path $PSScriptRoot "setup.iss")
if ($LASTEXITCODE -ne 0) {
    Write-Host "ISCC 编译失败" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $setupPath)) {
    Write-Host "未生成 setup.exe（预期路径: $setupPath）" -ForegroundColor Red
    exit 1
}

$setupSize = (Get-Item $setupPath).Length
Write-Host "`n=========================================" -ForegroundColor Cyan
Write-Host "构建完成" -ForegroundColor Green
Write-Host "  $setupPath"
Write-Host ("  大小: {0:N1} MB" -f ($setupSize / 1MB))
Write-Host "=========================================" -ForegroundColor Cyan
