# VrcDmaFish Setup Script - UTF8 Compatible Version
$ProgressPreference = 'SilentlyContinue'
$TargetDir = "bin/Debug/net8.0"

if (!(Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir
}

Write-Host "--- VrcDmaFish Environment Setup ---" -ForegroundColor Cyan
Write-Host "Preparing DMA and Driver libraries..." -ForegroundColor Cyan

# 下载最新的 MemProcFS
$Url = "https://github.com/ufrisk/MemProcFS/releases/download/v5.17.0/MemProcFS_v5.17.0_win_x64.zip"
$ZipFile = "memprocfs.zip"

Write-Host "Downloading drivers from GitHub..." -ForegroundColor Yellow
try {
    Invoke-WebRequest -Uri $Url -OutFile $ZipFile
} catch {
    Write-Host "Download failed! Please check your network." -ForegroundColor Red
    return
}

# 解压并移动核心 DLL
Write-Host "Extracting and deploying files..." -ForegroundColor Yellow
if (Test-Path "temp_mem") { Remove-Item -Recurse -Force "temp_mem" }
Expand-Archive -Path $ZipFile -DestinationPath "temp_mem" -Force

$FilesToCopy = @("vmm.dll", "leechcore.dll", "FTD3XX.dll", "FTD601.dll", "info.db")
foreach ($file in $FilesToCopy) {
    if (Test-Path "temp_mem/$file") {
        Copy-Item "temp_mem/$file" -Destination $TargetDir -Force
        Write-Host "[OK] Deployed: $file" -ForegroundColor Green
    }
}

# 清理
Remove-Item $ZipFile -Force
Remove-Item -Recurse -Force "temp_mem"

Write-Host ""
Write-Host "Done! Setup finished successfully." -ForegroundColor Cyan
Write-Host "You can now run 'dotnet run' to start the bot." -ForegroundColor Cyan
